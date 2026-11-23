using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 竖向列表的虚拟滚动。
///
/// 原来各面板切 tab 时会把整份数据实例化成行——第三方单位 1000 条、摄像头 499 个、
/// 巡检 347 个、罐区 264 个。每行 8~9 个子节点（TMP + Image），1000 行就是近万个
/// CanvasRenderer 常驻，Canvas 任何一次 dirty 都要遍历全部；按企业过滤还靠逐行
/// SetActive，每换一个企业又是一千次激活状态翻转。<see cref="FrameBudget"/> 只是把
/// 这笔开销摊到多帧，总量一点没少。
///
/// 这里换成 web 那套做法：只实例化「可视区放得下的行数 + <see cref="Overscan"/>」，
/// 滚动时把行挪到新位置并重新绑数据。常驻行数从 1000 降到约 20，且不再随数据量增长。
///
/// 前提（当前 10 个列表面板都满足，Prepare 会校验并在不满足时告警）：
///   1. 行高固定，从模板行的 RectTransform 读；
///   2. 行的 anchor 与 pivot 都在左上 (0,1)，这样 anchoredPosition.y = -index * step；
///   3. Content 上的 VerticalLayoutGroup / ContentSizeFitter 由本类接管并禁用——
///      虚拟滚动必须自己算 Content 高度和行位置，留着这两个组件会和手动定位打架。
///
/// 用法：
///   private VirtualScrollList&lt;ThirdParty, ThirdPartyObject&gt; rows;
///   rows = new VirtualScrollList&lt;ThirdParty, ThirdPartyObject&gt;(
///       thirdPartyObjectPrefab, thirdPartyContainer,
///       (row, item, i) =&gt; { row.no = i + 1; row.thirdPartyDetails = item; });
///   rows.SetData(filtered);   // 传「过滤后」的数据，不要再逐行 SetActive
///
/// 注意：bind 回调会在同一个行实例上被反复调用（这就是复用的意义），所以回调里必须把
/// 每个用到的字段都重新写一遍，包括 OnDetailClick 这类引用字段。任何「只在创建时设一次」
/// 的假设在复用后都会串行——第 3 行会拿着第 40 行的回调。
/// </summary>
public class VirtualScrollList<TData, TRow> where TRow : Component
{
    /// <summary>
    /// 可视区之外多留的行数。滚动到一行只露出一半时，这几行负责把边界补上，
    /// 否则顶/底会闪一条空白。2 行足够，再多只是白占。
    /// </summary>
    private const int Overscan = 2;

    /// <summary>
    /// 读不到 viewport 高度时按多少行兜底。世界空间 Canvas 在首帧 layout 还没算完时
    /// rect.height 可能是 0，这时候如果按 0 算池大小，列表会整个不显示。
    /// </summary>
    private const int FallbackVisibleRows = 12;

    private readonly TRow template;
    private readonly RectTransform content;
    private readonly Action<TRow, TData, int> bind;
    private readonly List<TRow> pool = new List<TRow>();

    private ScrollRect scrollRect;
    private IList<TData> data;

    private float rowHeight;
    private float step;        // rowHeight + spacing
    private float padTop;
    private float padBottom;

    private int firstBound = -1;
    private bool prepared;
    private bool listening;

    /// <summary>行的父容器，面板的 Show/Hide 拿它开关整个列表。</summary>
    public Transform Container => content;

    /// <summary>当前数据条数（不是行实例数）。</summary>
    public int Count => data != null ? data.Count : 0;

    /// <summary>当前池里的行实例数，用于日志核对「真的只建了这么多行」。</summary>
    public int RowCount => pool.Count;

    /// <param name="template">
    /// 行模板。就是各面板 Inspector 里那个 xxxObjectPrefab——它指向 Content 下那个
    /// 关掉的 Label 行，本类只拿它当 Instantiate 的源，不会把它显示出来。
    /// </param>
    /// <param name="container">
    /// 行的父容器（Content）。传 null 时退回 template.transform.parent，
    /// 和各面板原有的 fallback 保持一致——场景里 container 字段大多是空的。
    /// </param>
    /// <param name="bind">把第 index 条数据写进行实例。index 是数据下标，从 0 起。</param>
    public VirtualScrollList(TRow template, Transform container, Action<TRow, TData, int> bind)
    {
        this.template = template;
        this.bind = bind;

        Transform parent = container != null
            ? container
            : (template != null ? template.transform.parent : null);
        content = parent as RectTransform;

        if (this.template == null)
        {
            Debug.LogError("[VirtualScrollList] 模板行为空，列表建不起来。检查面板 Inspector 上的 xxxObjectPrefab 是否掉了引用。");
        }
        else if (content == null)
        {
            Debug.LogError($"[VirtualScrollList] 找不到 Content（{this.template.name} 的父级不是 RectTransform），列表建不起来。");
        }
    }

    /// <summary>
    /// 换一份数据并刷新显示。传进来的应该是「已经过滤好的」列表——
    /// 虚拟滚动下过滤必须在数据层做，逐行 SetActive 那套已经没有意义了。
    /// </summary>
    public void SetData(IList<TData> newData)
    {
        data = newData;

        if (!Prepare())
        {
            return;
        }

        int count = Count;

        // Content 高度得自己算：ContentSizeFitter 已经被关掉，没人再撑它了。
        // count 行之间有 count-1 个间距，所以要把多算的那一个 spacing 减回去。
        content.sizeDelta = new Vector2(
            content.sizeDelta.x,
            count > 0 ? padTop + count * step - (step - rowHeight) + padBottom : 0f);

        // 回到顶部。原先每次进面板都是整份重建，天然从第一行看起；
        // 这里不归零的话会保留上一次的滚动位置，和以前的表现不一致。
        // velocity 也得清：ScrollRect 的惯性存在自己身上，光挪 Content
        // 下一帧就被残余速度带着继续滑。
        if (scrollRect != null)
        {
            scrollRect.velocity = Vector2.zero;
        }
        content.anchoredPosition = new Vector2(content.anchoredPosition.x, 0f);

        EnsurePool();

        // 强制重绑：数据换了，但 first 可能还是 0，光靠 Refresh 的「没变就跳过」会漏刷。
        firstBound = -1;
        Refresh();
    }

    /// <summary>
    /// 数据没换、但每行要显示的内容变了，按现有数据把可见行重绑一遍。
    ///
    /// 和 <see cref="SetData"/> 的区别是**不动滚动位置**：SetData 会把列表拉回顶部
    /// （换了一份数据，从头看才对），而这里是原地刷新 ——
    /// 用户在第 180 行处置完一张票，列表不该跳回第 1 行。
    /// </summary>
    public void Rebind()
    {
        if (data == null) return;

        firstBound = -1;   // 绕开 Refresh 里那个"首行没变就跳过"的判断
        Refresh();
    }

    /// <summary>清空显示（数据置空、行全部收起）。面板离场时用。</summary>
    public void Clear()
    {
        data = null;
        firstBound = -1;

        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i] != null)
            {
                pool[i].gameObject.SetActive(false);
            }
        }

        if (content != null)
        {
            content.sizeDelta = new Vector2(content.sizeDelta.x, 0f);
        }
    }

    /// <summary>
    /// 一次性准备：读行高/间距、关掉会和手动定位打架的布局组件、挂滚动监听。
    /// 放在 SetData 里做而不是构造函数里，是因为构造时 Canvas 的 layout 还没跑，
    /// 那时候读 viewport 高度大概率是 0。
    /// </summary>
    private bool Prepare()
    {
        if (prepared)
        {
            return true;
        }
        if (template == null || content == null)
        {
            return false;
        }

        RectTransform rowRect = template.transform as RectTransform;
        if (rowRect == null)
        {
            Debug.LogError($"[VirtualScrollList] 模板行 {template.name} 没有 RectTransform。");
            return false;
        }

        rowHeight = rowRect.rect.height;
        if (rowHeight <= 1f)
        {
            Debug.LogError($"[VirtualScrollList] 模板行 {template.name} 的高度是 {rowHeight}，无法按固定行高做虚拟滚动。");
            return false;
        }

        // 行必须锚在左上：下面用 anchoredPosition.y = -(padTop + index * step) 定位，
        // 换成居中或上下拉伸锚点这个公式就不成立了。
        if (!Mathf.Approximately(rowRect.pivot.y, 1f) ||
            !Mathf.Approximately(rowRect.anchorMin.y, 1f) ||
            !Mathf.Approximately(rowRect.anchorMax.y, 1f))
        {
            Debug.LogWarning($"[VirtualScrollList] 模板行 {template.name} 的 anchor/pivot 不在顶部" +
                $"（pivot.y={rowRect.pivot.y}, anchorMin.y={rowRect.anchorMin.y}, anchorMax.y={rowRect.anchorMax.y}），行位置可能整体偏移。");
        }

        // 先把间距读出来再关组件——关掉之后这些值还在组件上，但顺序写反了容易看漏。
        float spacing = 0f;
        VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
        if (layout != null)
        {
            spacing = layout.spacing;
            padTop = layout.padding.top;
            padBottom = layout.padding.bottom;
            layout.enabled = false;
        }

        // ContentSizeFitter 会按子元素撑 Content 高度，而虚拟滚动下子元素只有 20 个，
        // 留着它 Content 就只有 20 行高，滚动直接失效。
        ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
        if (fitter != null)
        {
            fitter.enabled = false;
        }

        step = rowHeight + spacing;

        scrollRect = content.GetComponentInParent<ScrollRect>();
        if (scrollRect == null)
        {
            Debug.LogWarning($"[VirtualScrollList] {content.name} 上方找不到 ScrollRect，" +
                "列表能显示但不会随滚动重绑（等于只看得到第一屏）。");
        }
        else if (!listening)
        {
            scrollRect.onValueChanged.AddListener(OnScrolled);
            listening = true;
        }

        prepared = true;
        return true;
    }

    /// <summary>按可视区高度把池补足。池只增不减——面板会反复进出，留着比来回销毁划算。</summary>
    private void EnsurePool()
    {
        int want = Mathf.Min(VisibleRowCount() + Overscan, Mathf.Max(Count, 1));

        while (pool.Count < want)
        {
            TRow row = UnityEngine.Object.Instantiate(template, content);
            row.gameObject.SetActive(false);
            pool.Add(row);
        }
    }

    private int VisibleRowCount()
    {
        RectTransform viewport = scrollRect != null && scrollRect.viewport != null
            ? scrollRect.viewport
            : content.parent as RectTransform;

        float h = viewport != null ? viewport.rect.height : 0f;
        if (h <= 1f)
        {
            return FallbackVisibleRows;
        }

        return Mathf.CeilToInt(h / step);
    }

    private void OnScrolled(Vector2 _)
    {
        Refresh();
    }

    /// <summary>
    /// 把池里的行摆到当前可视区并绑上对应数据。只在首行下标变化时才真干活，
    /// 否则每一次滚动回调（一帧可能好几次）都要重绑二十行。
    /// </summary>
    private void Refresh()
    {
        if (!prepared || content == null)
        {
            return;
        }

        int count = Count;
        if (count == 0)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] != null)
                {
                    pool[i].gameObject.SetActive(false);
                }
            }
            firstBound = 0;
            return;
        }

        // Content 的 pivot 在顶部，往下滚时 anchoredPosition.y 从 0 递增。
        // MovementType 是 Elastic，回弹时会越过两端，所以要 clamp。
        float offset = content.anchoredPosition.y - padTop;
        int first = Mathf.FloorToInt(offset / step);
        // 上界是 count-1，不是 count-pool.Count。按后者夹的话滚到底部时 first 会被
        // 往回压几行，那几行落在 viewport 下方，顶上就空出一条——底部越接近越明显。
        // 尾部行数不足由下面的 index >= count 收掉。
        first = Mathf.Clamp(first, 0, Mathf.Max(0, count - 1));

        if (first == firstBound)
        {
            return;
        }
        firstBound = first;

        for (int i = 0; i < pool.Count; i++)
        {
            TRow row = pool[i];
            if (row == null)
            {
                continue;
            }

            int index = first + i;
            if (index >= count)
            {
                row.gameObject.SetActive(false);
                continue;
            }

            RectTransform rt = row.transform as RectTransform;
            if (rt != null)
            {
                rt.anchoredPosition = new Vector2(
                    rt.anchoredPosition.x,
                    -(padTop + index * step));
            }

            // 先绑再显示：行 setter 里有 LayoutRebuilder.ForceRebuildLayoutImmediate，
            // 对着一个刚 SetActive 的对象跑会多一次无谓的重建。
            bind(row, data[index], index);

            if (!row.gameObject.activeSelf)
            {
                row.gameObject.SetActive(true);
            }
        }
    }
}
