using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 出入园审核卡片：并排浮在出入园列表面板右侧的一块 MR 卡片。
///
/// 结构照 <see cref="CameraAlarmDetailPanel"/>（panelRoot + Show/Hide + 等一帧再强制重排），
/// 布局由 <c>Assets/Editor/EntryExitReviewBuilder.cs</c> 生成成 prefab。字段全部允许为空——
/// 接线漏一个不该让整块卡片炸掉。
///
/// 备注【只能点选常用短语】，没有自由文本输入。原因是实测结论，不是偷懒：
/// 这台 SXR_1 上眼镜里的画面只来自 OpenXR 运行时那个合成窗口
/// （dumpsys window 里唯一 isReadyForDisplay()=true 的），系统 IME 是普通 2D 窗口，
/// 起得来但进不了合成链（厂商那个 VR 输入法只服务 xr2d 类应用）。所以 TouchScreenKeyboard
/// 这条路整个删掉了，别再往回加——加了也是个看不见还没法取消的隐形键盘。
///
/// 短语来自 <see cref="EntryExitPhraseConfig"/>（配置文件），分「通过原因 / 驳回原因」两组，
/// 一次只显示一组，由卡片上那两个 tab 切换。数量不固定，所以短语按钮和两个 tab 都是运行时
/// 按 <see cref="chipTemplate"/> 克隆出来的，不是 prefab 里摆死的——
/// 【不要改回去生成 prefab】：重跑 EntryExitReviewBuilder 会换掉 prefab 根的 fileID，
/// 打断场景里 ClosedPark_EntryExitList 上那两个引用，而场景是不能重新保存的。
///
/// 审批做成两条有差异的流程：
///   通过   —— 允许空备注，点两下确认即可；
///   不通过 —— 必须先选一条【驳回原因组】里的短语，否则按钮置灰；同样要点两下确认。
/// 二次确认复用原来那三个按钮（卡片横向只剩 61.5px，塞不下第四个按钮）：
/// 第一下把按钮文案改成「再按一次确认」、把「撤销」临时变成「取消」，3 秒不点自动复原。
///
/// 所有改动只写 <see cref="EntryExitReviewStore"/>，即【只存本机，不回传生产环境】。
///
/// 行高亮不走回调：卡片打开/关闭时调 <see cref="EntryExitReviewStore.Select"/>，
/// 行内的 <see cref="EntryExitActionCell"/> 订阅同一条广播自己点亮/复原。
/// </summary>
public class EntryExitReviewCard : MonoBehaviour
{
    /// <summary>备注里多条短语的分隔符。切分和拼接都用它，别换成英文逗号。</summary>
    private const char Separator = '，';

    /// <summary>二次确认的等待时长（秒）。太短会来不及看清，太长会让人以为卡住了。</summary>
    private const float ConfirmWindow = 3f;

    public GameObject panelRoot;
    public TextMeshProUGUI titleText;

    [Header("摘要（label/value 一一对应，多出来的整行隐藏）")]
    public TextMeshProUGUI[] summaryLabels;
    public TextMeshProUGUI[] summaryValues;

    [Header("审核")]
    public Button approveButton;
    public Image approveImage;
    public Button rejectButton;
    public Image rejectImage;
    public Button revokeButton;
    public Image revokeImage;

    [Header("备注（短语按钮和分组 tab 运行时按模板克隆）")]
    [Tooltip("短语按钮模板，prefab 里是 inactive 的，本体不显示")]
    public Button chipTemplate;
    [Tooltip("短语网格的原点，克隆出来的按钮挂在它下面")]
    public RectTransform chipArea;
    public int chipColumns = 4;
    public float chipColStep = 430f;
    public float chipRowStep = 100f;

    [Header("分组 tab（相对 chipArea 的本地坐标，正 y 表示在网格上方）")]
    [Tooltip("两个 tab 的横向位置。默认值让它们右对齐到 chipArea 右边，不压到「常用短语」那行标题")]
    public float tabApproveX = 850f;
    public float tabRejectX = 1280f;
    public float tabY = 100f;

    [Tooltip("当前备注全文，只读展示")]
    public TextMeshProUGUI remarkValueText;
    public Button remarkClearButton;
    public Image remarkFocusFrame;

    [Header("底部")]
    public TextMeshProUGUI footerText;
    public TextMeshProUGUI noticeText;
    public Button closeButton;

    [Header("配色")]
    public Color actionIdleColor = new Color(0.18f, 0.26f, 0.38f, 0.75f);
    public Color approvedColor = new Color(0.11f, 0.62f, 0.45f, 0.95f);
    public Color rejectedColor = new Color(0.72f, 0.28f, 0.20f, 0.95f);
    public Color pendingColor = new Color(0.20f, 0.34f, 0.52f, 0.90f);
    public Color chipIdleColor = new Color(0.16f, 0.22f, 0.32f, 0.70f);
    public Color chipOnColor = new Color(0.85f, 0.60f, 0.18f, 0.95f);
    [Tooltip("等待二次确认时按钮的颜色，刻意比落库后的状态色更亮")]
    public Color confirmColor = new Color(0.95f, 0.72f, 0.20f, 1f);
    [Tooltip("未选中的分组 tab 的颜色")]
    public Color tabIdleColor = new Color(0.14f, 0.18f, 0.26f, 0.60f);
    [Tooltip("选中的分组 tab 的颜色")]
    public Color tabOnColor = new Color(0.28f, 0.42f, 0.60f, 0.95f);

    /// <summary>短语分组。下标要和 <see cref="PhrasesOf"/> 对上。</summary>
    private enum PhraseGroup { Approve = 0, Reject = 1 }

    private readonly List<Button> chipButtons = new List<Button>();
    private readonly List<Image> chipImages = new List<Image>();
    private readonly List<TextMeshProUGUI> chipLabels = new List<TextMeshProUGUI>();
    private string[] builtPhrases;
    private PhraseGroup builtGroup;
    private bool chipsBuilt;

    private PhraseGroup currentGroup = PhraseGroup.Reject;
    private Button tabApprove;
    private Button tabReject;
    private Image tabApproveImage;
    private Image tabRejectImage;

    /// <summary>快捷键选中的短语下标，-1 表示没有焦点。</summary>
    private int focusedChip = -1;

    private string currentKey;
    private Coroutine focusPulse;
    private bool wired;

    // --- 二次确认 ---
    private bool confirming;
    private EntryExitReviewState confirmTarget;
    private Coroutine confirmTimer;

    // 动作按钮的原始文案在 prefab 里，运行时改完要还原，所以第一次接线时抄一份
    private TextMeshProUGUI approveLabel;
    private TextMeshProUGUI rejectLabel;
    private TextMeshProUGUI revokeLabel;
    private string approveText = "通过";
    private string rejectText = "不通过";
    private string revokeText = "撤销";

    /// <summary>卡片当前是否开着。快捷键据此决定是吞掉按键还是放行。</summary>
    public bool IsOpen => panelRoot != null ? panelRoot.activeSelf : gameObject.activeSelf;

    private void Start()
    {
        // 这里【不要】调 Hide()：prefab 根本来就是存成 inactive 的，Instantiate 出来不跑
        // Awake/Start；等 ShowCar/ShowPerson 把它打开时 Start 才第一次执行，那时候再 Hide
        // 就会把刚打开的卡片又关掉。收起由调用方负责。
        WireOnce();
    }

    private void WireOnce()
    {
        if (wired) return;
        wired = true;

        if (approveButton != null)
        {
            approveLabel = approveButton.GetComponentInChildren<TextMeshProUGUI>(true);
            approveButton.onClick.AddListener(() => OnActionClicked(EntryExitReviewState.Approved));
        }
        if (rejectButton != null)
        {
            rejectLabel = rejectButton.GetComponentInChildren<TextMeshProUGUI>(true);
            rejectButton.onClick.AddListener(() => OnActionClicked(EntryExitReviewState.Rejected));
        }
        if (revokeButton != null)
        {
            revokeLabel = revokeButton.GetComponentInChildren<TextMeshProUGUI>(true);
            revokeButton.onClick.AddListener(OnRevokeClicked);
        }

        // prefab 里通过/驳回的文案是「通过」「驳回」，这里统一成产品口径的「通过」「不通过」。
        // 改文案不需要重生成 prefab —— 重跑 builder 会换掉 prefab 根 fileID，代价太大。
        if (approveLabel != null) approveLabel.text = approveText;
        if (rejectLabel != null) rejectLabel.text = rejectText;
        if (revokeLabel != null) revokeText = revokeLabel.text;

        if (closeButton != null) closeButton.onClick.AddListener(Hide);
        if (remarkClearButton != null) remarkClearButton.onClick.AddListener(ClearRemark);

        if (noticeText != null) noticeText.text = "仅存本机，不回传生产环境";
        if (titleText != null) titleText.text = "出入园审核";
    }

    // ------------------------------------------------------------------ 短语分组

    private static string[] PhrasesOf(PhraseGroup group)
    {
        string[] phrases = group == PhraseGroup.Approve
            ? EntryExitPhraseConfig.ApprovePhrases
            : EntryExitPhraseConfig.RejectPhrases;

        return phrases ?? new string[0];
    }

    /// <summary>
    /// 建两个分组 tab。它们也是克隆 chipTemplate 来的，样式自然和短语按钮一致，
    /// 位置放在网格上方那一行（「常用短语」标题的右侧空白区），不占用任何新的纵向空间。
    /// </summary>
    private void EnsureTabs()
    {
        if (tabApprove != null || chipTemplate == null || chipArea == null) return;

        tabApprove = MakeTab("TabApprove", "通过原因", tabApproveX, out tabApproveImage);
        tabReject = MakeTab("TabReject", "驳回原因", tabRejectX, out tabRejectImage);

        tabApprove.onClick.AddListener(() => SwitchGroup(PhraseGroup.Approve));
        tabReject.onClick.AddListener(() => SwitchGroup(PhraseGroup.Reject));
    }

    private Button MakeTab(string name, string content, float x, out Image image)
    {
        Button tab = Instantiate(chipTemplate, chipArea);
        tab.name = name;

        RectTransform rt = tab.transform as RectTransform;
        if (rt != null) rt.anchoredPosition = new Vector2(x, tabY);

        TextMeshProUGUI label = tab.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null) label.text = content;

        image = tab.GetComponent<Image>();
        tab.gameObject.SetActive(true);
        return tab;
    }

    private void SwitchGroup(PhraseGroup group)
    {
        if (currentGroup == group && chipsBuilt) return;

        currentGroup = group;
        focusedChip = -1;
        CancelConfirm();   // 换组等于改主意，之前那次待确认作废
        EnsureChips();
        Refresh();
    }

    /// <summary>
    /// 按当前组的短语建按钮。组或配置没变就不动它，平时打开卡片不重建。
    ///
    /// 建完顺手用字体自己的字表核一遍：配置是可以现场改的，改进去一个 TMP 图集里没有的字
    /// 在真机上就是豆腐块，且看不出是配置的问题，所以缺字必须在日志里点名。
    /// </summary>
    private void EnsureChips()
    {
        EntryExitPhraseConfig.Ensure();
        EnsureTabs();

        string[] phrases = PhrasesOf(currentGroup);

        if (chipsBuilt && builtGroup == currentGroup && SameAs(phrases)) return;

        if (chipTemplate == null || chipArea == null)
        {
            Debug.LogError("[出入园审核] 未接 chipTemplate / chipArea，短语按钮建不出来");
            builtPhrases = phrases;
            builtGroup = currentGroup;
            chipsBuilt = true;
            return;
        }

        for (int i = 0; i < chipButtons.Count; i++)
        {
            if (chipButtons[i] != null) Destroy(chipButtons[i].gameObject);
        }
        chipButtons.Clear();
        chipImages.Clear();
        chipLabels.Clear();

        for (int i = 0; i < phrases.Length; i++)
        {
            Button chip = Instantiate(chipTemplate, chipArea);
            chip.name = $"Chip{i}";

            RectTransform rt = chip.transform as RectTransform;
            if (rt != null)
            {
                rt.anchoredPosition = new Vector2(
                    (i % chipColumns) * chipColStep,
                    -(i / chipColumns) * chipRowStep);
            }

            TextMeshProUGUI label = chip.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.text = phrases[i];

            int index = i;   // 闭包捕获循环变量，必须先拷一份
            chip.onClick.AddListener(() => ToggleChip(index));
            chip.gameObject.SetActive(true);

            chipButtons.Add(chip);
            chipImages.Add(chip.GetComponent<Image>());
            chipLabels.Add(label);
        }

        builtPhrases = phrases;
        builtGroup = currentGroup;
        chipsBuilt = true;

        Debug.Log($"[出入园审核] {(currentGroup == PhraseGroup.Approve ? "通过" : "驳回")}组短语按钮建好 {phrases.Length} 个");
        WarnMissingGlyphs(phrases);
    }

    private bool SameAs(string[] phrases)
    {
        if (builtPhrases == null || builtPhrases.Length != phrases.Length) return false;
        for (int i = 0; i < phrases.Length; i++)
        {
            if (builtPhrases[i] != phrases[i]) return false;
        }
        return true;
    }

    private void WarnMissingGlyphs(string[] phrases)
    {
        TMP_FontAsset font = null;
        for (int i = 0; i < chipLabels.Count && font == null; i++)
        {
            if (chipLabels[i] != null) font = chipLabels[i].font;
        }
        if (font == null) return;

        for (int i = 0; i < phrases.Length; i++)
        {
            List<char> missing;
            if (!font.HasCharacters(phrases[i], out missing) && missing != null && missing.Count > 0)
            {
                Debug.LogError($"[出入园审核] 短语「{phrases[i]}」有 {missing.Count} 个字不在字体图集里，" +
                    $"真机会显示豆腐块：{new string(missing.ToArray())}。" +
                    "请把这些字补进 Assets/Fonts/charset_used.txt 并重生成 TMP 图集。");
            }
        }
    }

    // ------------------------------------------------------------------ 打开 / 关闭

    public void ShowCar(CarAccessRecord record, bool focusRemark)
    {
        if (record == null) { Hide(); return; }

        WireOnce();
        CancelConfirm();   // 复用卡片时别继承上一条的待确认态
        currentKey = EntryExitReviewStore.KeyFor(record);

        SetSummary(
            new[] { "车牌号码", "车辆类型", "车辆联系人", "车辆联系电话", "所属企业" },
            new[] { record.CARNUM, record.CARTYPE, record.CARLIANXR, record.CARPHONENUM, record.ENTERPRISENAME });

        Open(focusRemark);
    }

    public void ShowPerson(PersonAccessRecord record, bool focusRemark)
    {
        if (record == null) { Hide(); return; }

        WireOnce();
        CancelConfirm();
        currentKey = EntryExitReviewStore.KeyFor(record);

        SetSummary(
            new[] { "所属企业", "人员名称", "联系电话", "证件号码" },
            new[] { record.ENTERPRISENAME, record.PERSONNAME, record.PHONE, record.IDCARDNUM });

        Open(focusRemark);
    }

    public void Hide()
    {
        CancelConfirm();
        currentKey = null;
        focusedChip = -1;
        EntryExitReviewStore.Select(null);

        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    private void Open(bool focusRemark)
    {
        if (panelRoot != null)
        {
            panelRoot.SetActive(true);
        }
        else
        {
            // 没接 panelRoot 时退化成显示自身，与 CheckDetailPanel / CameraAlarmDetailPanel 的兜底一致
            gameObject.SetActive(true);
        }

        // 打开时按当前结论选默认组：已通过的看通过原因，其余都看驳回原因
        // ——「不通过」要求先填驳回原因，默认停在那一组能少点一次 tab。
        currentGroup = EntryExitReviewStore.StateOf(currentKey) == EntryExitReviewState.Approved
            ? PhraseGroup.Approve
            : PhraseGroup.Reject;
        focusedChip = -1;

        EnsureChips();
        EntryExitReviewStore.Select(currentKey);
        Refresh();

        if (!gameObject.activeInHierarchy) return;

        StartCoroutine(RebuildLayout());

        if (focusRemark && remarkFocusFrame != null)
        {
            if (focusPulse != null) StopCoroutine(focusPulse);
            focusPulse = StartCoroutine(FocusPulse());
        }
    }

    // ------------------------------------------------------------------ 内容

    private void SetSummary(string[] labels, string[] values)
    {
        int n = summaryLabels != null ? summaryLabels.Length : 0;

        for (int i = 0; i < n; i++)
        {
            bool used = i < labels.Length;

            if (summaryLabels[i] != null)
            {
                summaryLabels[i].gameObject.SetActive(used);
                if (used) summaryLabels[i].text = labels[i];
            }

            if (summaryValues != null && i < summaryValues.Length && summaryValues[i] != null)
            {
                summaryValues[i].gameObject.SetActive(used);
                if (used)
                {
                    string v = i < values.Length ? values[i] : null;
                    summaryValues[i].text = string.IsNullOrEmpty(v) ? "未填写" : v;
                }
            }
        }
    }

    private void Refresh()
    {
        bool usable = !string.IsNullOrEmpty(currentKey);

        EntryExitReviewState state = usable
            ? EntryExitReviewStore.StateOf(currentKey)
            : EntryExitReviewState.Pending;

        string remark = usable ? EntryExitReviewStore.RemarkOf(currentKey) : string.Empty;
        bool hasRejectReason = usable && HasRejectReason(remark);

        RefreshActions(usable, state, hasRejectReason);

        SetInteractable(remarkClearButton, usable);

        if (remarkValueText != null)
        {
            remarkValueText.text = string.IsNullOrEmpty(remark) ? "未填写备注" : remark;
        }

        RefreshTabs();
        RefreshChips(remark, usable);
        RefreshFooter(state, usable, hasRejectReason);
    }

    /// <summary>备注里是否至少有一条【驳回原因组】的短语。这是「不通过」能不能点的唯一依据。</summary>
    private static bool HasRejectReason(string remark)
    {
        List<string> segments = Split(remark);
        if (segments.Count == 0) return false;

        string[] reasons = PhrasesOf(PhraseGroup.Reject);
        for (int i = 0; i < reasons.Length; i++)
        {
            if (segments.Contains(reasons[i])) return true;
        }

        return false;
    }

    private void RefreshActions(bool usable, EntryExitReviewState state, bool hasRejectReason)
    {
        if (confirming)
        {
            // 待确认时整行只剩两个语义：确认（那个按钮）和取消（原撤销位）
            SetLabel(approveLabel, confirmTarget == EntryExitReviewState.Approved ? "再按一次确认" : approveText);
            SetLabel(rejectLabel, confirmTarget == EntryExitReviewState.Rejected ? "再按一次确认" : rejectText);
            SetLabel(revokeLabel, "取消");

            SetActionColor(approveImage, confirmTarget == EntryExitReviewState.Approved, confirmColor);
            SetActionColor(rejectImage, confirmTarget == EntryExitReviewState.Rejected, confirmColor);
            SetActionColor(revokeImage, true, pendingColor);

            // 等确认时只允许"确认那一个"和"取消"，另一个动作按钮先关掉，免得误触直接跳到另一条流程
            SetInteractable(approveButton, confirmTarget == EntryExitReviewState.Approved);
            SetInteractable(rejectButton, confirmTarget == EntryExitReviewState.Rejected);
            SetInteractable(revokeButton, true);
            return;
        }

        SetLabel(approveLabel, approveText);
        SetLabel(rejectLabel, rejectText);
        SetLabel(revokeLabel, revokeText);

        SetActionColor(approveImage, state == EntryExitReviewState.Approved, approvedColor);
        SetActionColor(rejectImage, state == EntryExitReviewState.Rejected, rejectedColor);
        SetActionColor(revokeImage, state == EntryExitReviewState.Pending, pendingColor);

        SetInteractable(approveButton, usable);
        // 【不通过必须先填驳回原因】——没选原因时按钮置灰，footer 里说明为什么
        SetInteractable(rejectButton, usable && hasRejectReason);
        SetInteractable(revokeButton, usable);
    }

    private static void SetLabel(TextMeshProUGUI label, string text)
    {
        if (label != null) label.text = text;
    }

    private void SetActionColor(Image image, bool active, Color activeColor)
    {
        if (image != null) image.color = active ? activeColor : actionIdleColor;
    }

    private static void SetInteractable(Selectable target, bool value)
    {
        if (target != null) target.interactable = value;
    }

    private void RefreshTabs()
    {
        if (tabApproveImage != null)
        {
            tabApproveImage.color = currentGroup == PhraseGroup.Approve ? tabOnColor : tabIdleColor;
        }
        if (tabRejectImage != null)
        {
            tabRejectImage.color = currentGroup == PhraseGroup.Reject ? tabOnColor : tabIdleColor;
        }
    }

    /// <summary>
    /// 刷新当前组的短语按钮。
    ///
    /// 只看当前组：备注里属于另一组的短语照样留在存档里，切组【不会】清掉它们，
    /// 所以一条记录同时带着通过类和驳回类备注是允许的，由人自己负责。
    /// </summary>
    private void RefreshChips(string remark, bool usable)
    {
        List<string> segments = Split(remark);
        string[] phrases = builtPhrases ?? new string[0];

        for (int i = 0; i < chipButtons.Count; i++)
        {
            if (chipButtons[i] != null)
            {
                chipButtons[i].interactable = usable;

                // 快捷键的焦点靠轻微放大表示。行的布局组关掉了 ChildScaleWidth，缩放不牵动排版
                chipButtons[i].transform.localScale = Vector3.one * (i == focusedChip ? 1.06f : 1f);
            }

            if (i < chipImages.Count && chipImages[i] != null && i < phrases.Length)
            {
                chipImages[i].color = segments.Contains(phrases[i]) ? chipOnColor : chipIdleColor;
            }
        }
    }

    private void RefreshFooter(EntryExitReviewState state, bool usable, bool hasRejectReason)
    {
        if (footerText == null) return;

        if (!usable)
        {
            footerText.text = "本机记录";
            return;
        }

        if (confirming)
        {
            footerText.text = confirmTarget == EntryExitReviewState.Approved
                ? "再按一次「通过」确认，或按「取消」放弃"
                : "再按一次「不通过」确认，或按「取消」放弃";
            return;
        }

        if (state == EntryExitReviewState.Pending && !hasRejectReason)
        {
            footerText.text = "待审核，本机记录。不通过需先选择驳回原因";
            return;
        }

        string time = EntryExitReviewStore.TimeOf(currentKey);
        string stateText = state == EntryExitReviewState.Approved ? "已通过"
            : state == EntryExitReviewState.Rejected ? "已驳回"
            : "待审核";

        footerText.text = string.IsNullOrEmpty(time)
            ? stateText + "，本机记录"
            : stateText + "，本机记录，审核时间：" + time;
    }

    // ------------------------------------------------------------------ 审批（两步确认）

    private void OnActionClicked(EntryExitReviewState target)
    {
        if (string.IsNullOrEmpty(currentKey)) return;

        // 第二下：同一个目标，落库
        if (confirming && confirmTarget == target)
        {
            CancelConfirm();
            Apply(target);
            return;
        }

        // 「不通过」的前置条件在这里再挡一次：按钮虽然置了灰，但快捷键也走这条路
        if (target == EntryExitReviewState.Rejected &&
            !HasRejectReason(EntryExitReviewStore.RemarkOf(currentKey)))
        {
            Debug.Log("[出入园审核] 未选驳回原因，不通过被挡下");
            Refresh();
            return;
        }

        BeginConfirm(target);
    }

    private void BeginConfirm(EntryExitReviewState target)
    {
        confirming = true;
        confirmTarget = target;

        if (confirmTimer != null) StopCoroutine(confirmTimer);
        if (gameObject.activeInHierarchy) confirmTimer = StartCoroutine(ConfirmCountdown());

        Buzz();
        Refresh();
    }

    /// <summary>超时自动放弃。MR 里误触很常见，让待确认态一直挂着比直接落库更危险。</summary>
    private IEnumerator ConfirmCountdown()
    {
        yield return new WaitForSeconds(ConfirmWindow);

        confirmTimer = null;
        if (!confirming) yield break;

        CancelConfirm();
        Refresh();
    }

    private void CancelConfirm()
    {
        if (confirmTimer != null)
        {
            StopCoroutine(confirmTimer);
            confirmTimer = null;
        }

        confirming = false;
    }

    private void OnRevokeClicked()
    {
        // 待确认时「撤销」这个位置显示的是「取消」，点它就是放弃本次确认
        if (confirming)
        {
            CancelConfirm();
            Refresh();
            return;
        }

        // 撤销回待审核是个后悔操作，本身不具破坏性，不再要二次确认
        Apply(EntryExitReviewState.Pending);
    }

    private void Apply(EntryExitReviewState state)
    {
        if (string.IsNullOrEmpty(currentKey)) return;

        EntryExitReviewStore.SetState(currentKey, state);
        Buzz();
        Refresh();
    }

    // ------------------------------------------------------------------ 备注

    private void ToggleChip(int index)
    {
        if (string.IsNullOrEmpty(currentKey)) return;

        string[] phrases = builtPhrases ?? new string[0];
        if (index < 0 || index >= phrases.Length) return;

        // 改备注等于改判断依据，之前那次待确认作废
        CancelConfirm();

        string phrase = phrases[index];
        List<string> segments = Split(EntryExitReviewStore.RemarkOf(currentKey));

        if (segments.Contains(phrase))
        {
            segments.Remove(phrase);
        }
        else
        {
            segments.Add(phrase);
        }

        EntryExitReviewStore.SetRemark(currentKey, string.Join(Separator.ToString(), segments.ToArray()));
        Buzz();
        Refresh();
    }

    private void ClearRemark()
    {
        if (string.IsNullOrEmpty(currentKey)) return;

        CancelConfirm();
        EntryExitReviewStore.SetRemark(currentKey, string.Empty);
        Refresh();
    }

    private static List<string> Split(string remark)
    {
        List<string> result = new List<string>();
        if (string.IsNullOrEmpty(remark)) return result;

        foreach (string part in remark.Split(Separator))
        {
            string trimmed = part.Trim();
            if (trimmed.Length > 0) result.Add(trimmed);
        }

        return result;
    }

    // ------------------------------------------------------------------ 快捷键入口

    /// <summary>
    /// 焦点移到下一条短语（Y 键）。没有可选短语时返回 false，让调用方去做别的事。
    /// 走到末尾会绕回第一条——列表里最多 12 条，绕回比"到头了不动"更符合预期。
    /// </summary>
    public bool FocusNextChip()
    {
        if (!IsOpen || chipButtons.Count == 0) return false;

        focusedChip = (focusedChip + 1) % chipButtons.Count;
        Refresh();
        return true;
    }

    /// <summary>切换当前焦点短语的选中状态（X 键）。没有焦点时返回 false。</summary>
    public bool ToggleFocusedChip()
    {
        if (!IsOpen || focusedChip < 0 || focusedChip >= chipButtons.Count) return false;

        ToggleChip(focusedChip);
        return true;
    }

    // ------------------------------------------------------------------ 反馈

    /// <summary>右手手柄一次短震。手柄没接 / 不支持震动时静默跳过。</summary>
    private static void Buzz()
    {
        BNG.InputBridge bridge = BNG.InputBridge.Instance;
        if (bridge == null) return;

        bridge.VibrateController(0.3f, 0.5f, 0.04f, BNG.ControllerHand.Right);
    }

    /// <summary>从行内「备注」按钮或 X 键进来时，让备注区闪两下，指明该看哪儿。</summary>
    private IEnumerator FocusPulse()
    {
        Color baseColor = remarkFocusFrame.color;
        // 框本身平时几乎透明；闪到 0.35 已经很显眼，再高会盖住短语文字
        Color hot = new Color(0.85f, 0.60f, 0.18f, 0.35f);

        for (int i = 0; i < 2; i++)
        {
            remarkFocusFrame.color = hot;
            yield return new WaitForSeconds(0.12f);
            remarkFocusFrame.color = baseColor;
            yield return new WaitForSeconds(0.12f);
        }

        focusPulse = null;
    }

    private IEnumerator RebuildLayout()
    {
        // 等 TMP 把 mesh 更新完再重排，否则按旧尺寸算
        yield return new WaitForEndOfFrame();

        RectTransform target = panelRoot != null
            ? panelRoot.GetComponent<RectTransform>()
            : GetComponent<RectTransform>();

        if (target != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(target);
        }
    }
}
