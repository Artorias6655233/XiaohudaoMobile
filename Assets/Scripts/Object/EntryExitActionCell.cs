using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 出入园列表行最右侧的「操作」列：一个审核按钮 + 一个备注按钮。
///
/// 由 <c>Assets/Editor/EntryExitReviewBuilder.cs</c> 生成成 prefab，运行时由
/// <see cref="CarAccessRecordObject"/> / <see cref="PersonAccessRecordObject"/> 懒实例化到行下。
/// 行是虚拟滚动复用的，所以本组件【不缓存记录】，只记一个 key，显示内容一律现从
/// <see cref="EntryExitReviewStore"/> 读——这样第 3 行拿到第 40 行的数据这类串行问题不会发生。
/// </summary>
public class EntryExitActionCell : MonoBehaviour
{
    /// <summary>
    /// 操作列宽度。生成 prefab 的编辑器脚本和面板补表头都用这个值，改一处即可。
    ///
    /// 别随便调大：行原有 5 列占 2210，行的 HorizontalLayoutGroup spacing 是 100，
    /// 加上本列后行宽 = 2210 + 100 + 本值，而 Viewport 可用宽只有约 2630。
    /// </summary>
    public const float ColumnWidth = 280f;

    [Header("审核按钮")]
    public Button reviewButton;
    public Image reviewImage;
    public TextMeshProUGUI reviewLabel;

    [Header("备注按钮")]
    public Button remarkButton;
    public Image remarkImage;
    public TextMeshProUGUI remarkLabel;

    [Header("状态配色")]
    public Color pendingColor = new Color(0.20f, 0.34f, 0.52f, 0.85f);
    public Color approvedColor = new Color(0.11f, 0.62f, 0.45f, 0.95f);
    public Color rejectedColor = new Color(0.72f, 0.28f, 0.20f, 0.95f);
    public Color remarkIdleColor = new Color(0.18f, 0.26f, 0.38f, 0.75f);
    public Color remarkFilledColor = new Color(0.85f, 0.60f, 0.18f, 0.95f);

    /// <summary>选中该行时给行底图乘上的色调，让「卡片正在看这一条」在空间上看得出来。</summary>
    public Color rowSelectedTint = new Color(0.55f, 0.85f, 1f, 1f);

    /// <summary>
    /// 射线指着该行时的色调。刻意比 <see cref="rowSelectedTint"/> 弱：
    /// 悬停是"扫过"，选中是"停下来看"，两者同时存在时必须一眼分得出谁是谁。
    /// </summary>
    public Color rowHoverTint = new Color(0.78f, 0.92f, 1f, 1f);

    /// <summary>
    /// 已审批（通过或驳回）的行的色调，整行压暗表示"这条处置过了"。
    /// 优先级最低——正在看或正指着的那条仍按上面两种走，否则已处置的行会看不见高亮。
    /// </summary>
    public Color rowHandledTint = new Color(0.62f, 0.62f, 0.62f, 1f);

    /// <summary>行底图，由宿主行在实例化时塞进来（prefab 里没有）。允许为空。</summary>
    [NonSerialized] public Image rowBackground;

    private string key;
    private Action<object> onReview;
    private Action<object> onRemark;
    private object payload;

    private bool rowBaseColorCaptured;
    private Color rowBaseColor = Color.white;

    private EntryExitReviewState shownState = EntryExitReviewState.Pending;
    private bool shownStateValid;
    private Coroutine pulse;

    private void Awake()
    {
        if (reviewButton != null) reviewButton.onClick.AddListener(OnReviewClicked);
        if (remarkButton != null) remarkButton.onClick.AddListener(OnRemarkClicked);
    }

    private void OnEnable()
    {
        EntryExitReviewStore.OnChanged += OnStoreChanged;
        Refresh();
    }

    private void OnDisable()
    {
        EntryExitReviewStore.OnChanged -= OnStoreChanged;
        RestoreRowColor();
    }

    /// <summary>
    /// 绑一条记录。行复用时每次都会被调用，所以这里必须把 key / payload 全部重写一遍。
    /// </summary>
    /// <param name="key">存档 key，取不到稳定标识时传 null，按钮会置灰。</param>
    /// <param name="payload">回调要带回去的原始记录（CarAccessRecord / PersonAccessRecord）。</param>
    public void Bind(string key, object payload, Action<object> onReview, Action<object> onRemark)
    {
        // key 换了就不该继承上一条的「刚变过状态」，否则复用行会闪一次无意义的脉冲
        if (this.key != key) shownStateValid = false;

        this.key = key;
        this.payload = payload;
        this.onReview = onReview;
        this.onRemark = onRemark;

        Refresh();
    }

    private void OnStoreChanged(string changedKey)
    {
        // 选中态切换时会用旧 key 和新 key 各广播一次，两次都可能不是自己；
        // 一屏就二十来个格子，无脑刷新比精确判断划算，也不会漏掉行高亮的复原。
        Refresh();
    }

    public void Refresh()
    {
        bool usable = !string.IsNullOrEmpty(key);

        if (reviewButton != null) reviewButton.interactable = usable;
        if (remarkButton != null) remarkButton.interactable = usable;

        EntryExitReviewState state = usable ? EntryExitReviewStore.StateOf(key) : EntryExitReviewState.Pending;
        bool hasRemark = usable && !string.IsNullOrEmpty(EntryExitReviewStore.RemarkOf(key));

        if (reviewLabel != null) reviewLabel.text = LabelOf(state);
        if (reviewImage != null) reviewImage.color = ColorOf(state);

        if (remarkLabel != null) remarkLabel.text = "备注";
        if (remarkImage != null) remarkImage.color = hasRemark ? remarkFilledColor : remarkIdleColor;

        ApplyRowTint(usable, state);

        // 状态真变了才脉冲：滚动重绑、备注改动都不该跟着抖
        if (shownStateValid && state != shownState)
        {
            PlayPulse();
        }
        shownState = state;
        shownStateValid = usable;
    }

    private static string LabelOf(EntryExitReviewState state)
    {
        switch (state)
        {
            case EntryExitReviewState.Approved: return "已通过";
            case EntryExitReviewState.Rejected: return "已驳回";
            default: return "审核";
        }
    }

    private Color ColorOf(EntryExitReviewState state)
    {
        switch (state)
        {
            case EntryExitReviewState.Approved: return approvedColor;
            case EntryExitReviewState.Rejected: return rejectedColor;
            default: return pendingColor;
        }
    }

    /// <summary>
    /// 行底图四态，按优先级取第一个命中的：
    ///   1 选中（卡片正在看这条） 2 悬停（射线正指着） 3 已处置（通过/驳回） 4 常态。
    ///
    /// 顺序不能反：已处置的行数量会越来越多，要是它盖过悬停和选中，
    /// 审到后半程就再也看不出射线指着谁了。
    /// </summary>
    private void ApplyRowTint(bool usable, EntryExitReviewState state)
    {
        if (rowBackground == null) return;

        if (!rowBaseColorCaptured)
        {
            rowBaseColor = rowBackground.color;
            rowBaseColorCaptured = true;
        }

        if (!usable)
        {
            rowBackground.color = rowBaseColor;
            return;
        }

        Color tint;
        if (key == EntryExitReviewStore.SelectedKey) tint = rowSelectedTint;
        else if (key == EntryExitReviewStore.HoveredKey) tint = rowHoverTint;
        else if (state != EntryExitReviewState.Pending) tint = rowHandledTint;
        else
        {
            rowBackground.color = rowBaseColor;
            return;
        }

        rowBackground.color = new Color(
            rowBaseColor.r * tint.r,
            rowBaseColor.g * tint.g,
            rowBaseColor.b * tint.b,
            rowBaseColor.a * tint.a);
    }

    private void RestoreRowColor()
    {
        if (rowBackground != null && rowBaseColorCaptured)
        {
            rowBackground.color = rowBaseColor;
        }
    }

    private void OnReviewClicked()
    {
        if (string.IsNullOrEmpty(key)) return;
        if (onReview != null) onReview(payload);
    }

    private void OnRemarkClicked()
    {
        if (string.IsNullOrEmpty(key)) return;
        if (onRemark != null) onRemark(payload);
    }

    // ------------------------------------------------------------------ 反馈

    private void PlayPulse()
    {
        if (reviewButton == null || !gameObject.activeInHierarchy) return;

        if (pulse != null) StopCoroutine(pulse);
        pulse = StartCoroutine(PulseRoutine(reviewButton.transform));
    }

    /// <summary>
    /// 一次约 0.15s 的 1.0 → 1.12 → 1.0 缩放脉冲。
    /// MR 里没有触感之外的"按下去了"提示，状态块自己抖一下是最直观的确认。
    /// 行的 HorizontalLayoutGroup 关掉了 ChildScaleWidth，缩放不会牵动排版。
    /// </summary>
    private IEnumerator PulseRoutine(Transform target)
    {
        const float half = 0.075f;
        const float peak = 1.12f;

        for (float t = 0f; t < half; t += Time.deltaTime)
        {
            target.localScale = Vector3.one * Mathf.Lerp(1f, peak, t / half);
            yield return null;
        }
        for (float t = 0f; t < half; t += Time.deltaTime)
        {
            target.localScale = Vector3.one * Mathf.Lerp(peak, 1f, t / half);
            yield return null;
        }

        target.localScale = Vector3.one;
        pulse = null;
    }
}
