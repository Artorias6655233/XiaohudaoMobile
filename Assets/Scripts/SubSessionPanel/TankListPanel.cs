using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 储罐列表卡片：列出一家企业的全部储罐，选一行去看详情。
///
/// 【独立成卡而不是并进 TankMonitorPanel】：它要和管理员信息卡片并排，
/// 而罐体详情卡片是另一张、位置也不同。挤在一张卡里就只能二选一地切态，
/// 做不到"列表和详情同时看得见"。
///
/// 结构照 TankMonitorPanel —— 纯 MonoBehaviour，不继承 SubSessionPanel、
/// 不参与 SubSessionType 注册，由宿主面板持引用调用。
/// 所有字段允许为空：场景接线漏一个不该让整块面板炸掉。
/// </summary>
public class TankListPanel : MonoBehaviour
{
    public GameObject panelRoot;

    public TextMeshProUGUI titleText;   // 企业名
    public TextMeshProUGUI hintText;    // 共 30 个储罐，选一个查看实时数据
    public TextMeshProUGUI pageText;    // 1 / 4
    public Button prevButton;
    public Button nextButton;
    public List<TankListRow> rows = new List<TankListRow>();

    private readonly List<Tank> tanks = new List<Tank>();
    private string title;
    private string selectedKey;
    private Action<Tank> pick;
    private Action<Tank> annotate;
    private int page;

    /// <summary>Show/Hide 是否已经被宿主驱动过，说明见 Start。</summary>
    private bool driven;

    /// <summary>一页放几行，由接线的行数决定。</summary>
    private int PageSize => rows == null ? 0 : rows.Count;

    private int PageCount
    {
        get
        {
            if (PageSize <= 0) return 0;
            return (tanks.Count + PageSize - 1) / PageSize;
        }
    }

    private void Awake()
    {
        if (prevButton != null) prevButton.onClick.AddListener(() => Turn(-1));
        if (nextButton != null) nextButton.onClick.AddListener(() => Turn(1));
    }

    private void Start()
    {
        // 卡片是运行时 Instantiate 出来的，Start 会晚于宿主的 Show 执行；
        // 无条件 Hide 会把刚撑起来的卡片又关掉。只在还没被驱动过时才默认隐藏。
        if (!driven) Hide();
    }

    /// <param name="selectedKey">高亮哪一行（<see cref="Tank.MarkerKey"/>），并翻到它所在那页。</param>
    /// <param name="onPick">选中某行时回调，宿主拿去驱动 Session.tankID。</param>
    /// <param name="onAnnotate">每行「批注」按钮的回调；传 null 表示本页不提供批注，整列隐藏。</param>
    public void Show(string companyName, IList<Tank> companyTanks, string selectedKey,
        Action<Tank> onPick, Action<Tank> onAnnotate = null)
    {
        SetVisible(true);

        title = companyName;
        pick = onPick;
        annotate = onAnnotate;
        this.selectedKey = selectedKey;

        tanks.Clear();
        if (companyTanks != null) tanks.AddRange(companyTanks);

        // 翻到选中项那页，而不是一律回第一页——从详情切回来时不该丢掉位置
        page = 0;
        if (PageSize > 0 && !string.IsNullOrEmpty(selectedKey))
        {
            for (int i = 0; i < tanks.Count; i++)
            {
                if (tanks[i] != null && tanks[i].MarkerKey == selectedKey)
                {
                    page = i / PageSize;
                    break;
                }
            }
        }

        Render();
    }

    /// <summary>
    /// 空态：标题照常写企业名，正文写传进来的提示，一行都不列。
    ///
    /// 宿主用它来说"该企业无罐体数据"。不复用罐体详情卡——那张卡和「监测预警」
    /// 是同一个 prefab，长得一模一样，在这里弹出来像是切错了页面。
    /// </summary>
    public void ShowEmpty(string companyName, string message)
    {
        Show(companyName, null, null, null, null);
        // Show 内部的 Render 会按"没有罐"写默认文案，这里覆盖成宿主指定的那句
        SetText(hintText, message);
    }

    /// <summary>只换高亮，不重置分页——用户点了一行，列表不该跳回第一页。</summary>
    public void SetSelected(string key)
    {
        selectedKey = key;
        Render();
    }

    public void Hide()
    {
        SetVisible(false);
    }

    private void Turn(int delta)
    {
        int count = PageCount;
        if (count <= 1) return;

        page = Mathf.Clamp(page + delta, 0, count - 1);
        Render();
    }

    private void Render()
    {
        SetText(titleText, title);
        SetText(hintText, tanks.Count == 0
            ? "该企业暂无可定位储罐"
            : $"共 {tanks.Count} 个储罐，选一个查看实时数据");

        int pages = PageCount;
        SetText(pageText, pages <= 1 ? string.Empty : $"{page + 1} / {pages}");
        if (prevButton != null) prevButton.gameObject.SetActive(pages > 1);
        if (nextButton != null) nextButton.gameObject.SetActive(pages > 1);

        for (int slot = 0; slot < PageSize; slot++)
        {
            TankListRow row = rows[slot];
            if (row == null) continue;

            int index = page * PageSize + slot;
            Tank tank = index < tanks.Count ? tanks[index] : null;
            if (tank == null) row.Clear();
            else row.Bind(tank, tank.MarkerKey == selectedKey, pick, annotate);
        }

        Rebuild();
    }

    private void SetVisible(bool visible)
    {
        driven = true;

        // panelRoot 是子物体时，自身没激活的话它也显示不出来
        if (visible && panelRoot != null && !gameObject.activeSelf) gameObject.SetActive(true);

        if (panelRoot != null) panelRoot.SetActive(visible);
        else gameObject.SetActive(visible);
    }

    private void Rebuild()
    {
        if (gameObject.activeInHierarchy) StartCoroutine(RebuildLayout());
    }

    private IEnumerator RebuildLayout()
    {
        // 等 TMP 把 mesh 更新完再重排，否则按旧尺寸算
        yield return new WaitForEndOfFrame();

        RectTransform target = panelRoot != null
            ? panelRoot.GetComponent<RectTransform>()
            : GetComponent<RectTransform>();

        if (target != null) LayoutRebuilder.ForceRebuildLayoutImmediate(target);
    }

    private static void SetText(TextMeshProUGUI textComp, string value)
    {
        if (textComp != null) textComp.text = value ?? string.Empty;
    }
}
