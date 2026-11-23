using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class P0AnnotationPanel : MonoBehaviour
{
    [Header("历史批注")]
    public List<P0AnnotationRow> rows = new List<P0AnnotationRow>();
    public TextMeshProUGUI pageText;    // 1 / 3　共 7 条
    public Button prevButton;
    public Button nextButton;

    [Header("编辑")]
    public TMP_InputField contentInput;
    public TextMeshProUGUI characterCountText;
    public TextMeshProUGUI statusText;
    public Button sendButton;
    public Button editLatestButton;
    public Button cancelButton;
    public List<Button> phraseButtons = new List<Button>();

    private IP0AnnotationSource source;
    private string hazardId;
    private P0Annotation editing;
    private bool closeConfirmationArmed;
    private int page;

    public bool IsOpen => gameObject.activeSelf;
    public bool IsEditingInput => contentInput != null && contentInput.isFocused;

    /// <summary>
    /// 关闭时的回调，由宿主设置。
    ///
    /// 本卡片和危险源详情卡是互斥两态：宿主面板根上挂着
    /// HorizontalLayoutGroup(spacing=570)，两张 1880 宽的卡同时激活的话，
    /// 后一张会被排到前一张右边 2450 单位 ≈ 2.1 米处——真机上要转身才看得见，
    /// 表现就是"点了批注没反应"。所以开批注要把详情卡收起来占它的位置，
    /// 关掉再放回去。卡片自己不知道该放出什么，只把这一下转交宿主。
    /// </summary>
    public System.Action onClosed;

    /// <summary>一页放几条，由接线的行数决定。照 <see cref="TankListPanel"/> 的做法。</summary>
    private int PageSize => rows == null ? 0 : rows.Count;

    private void Awake()
    {
        if (contentInput != null)
        {
            contentInput.characterLimit = P0AnnotationStore.MaximumLength;
            contentInput.onValueChanged.AddListener(_ => UpdateCharacterCount());
        }
        sendButton?.onClick.AddListener(Save);
        editLatestButton?.onClick.AddListener(EditLatestOwn);
        cancelButton?.onClick.AddListener(CancelOrClose);
        prevButton?.onClick.AddListener(() => Turn(-1));
        nextButton?.onClick.AddListener(() => Turn(1));
        BindPhrases();
    }

    public void Configure(IP0AnnotationSource dataSource)
    {
        source = dataSource;
    }

    public void Open(string targetHazardId)
    {
        if (source == null || string.IsNullOrWhiteSpace(targetHazardId)) return;
        hazardId = targetHazardId;
        editing = null;
        closeConfirmationArmed = false;
        page = 0;
        if (contentInput != null) contentInput.text = string.Empty;
        gameObject.SetActive(true);
        RefreshHistory();
        SetStatus("新增批注；演示数据仅保存到本机");

        // 「点了没反应」这类反馈，光看现象分不清是入口没触发还是卡片开在了视野外。
        // 两个入口（详情卡按钮、X 键快捷方式）都汇到这里，有这行就能一眼断定。
        Debug.Log($"[批注] 面板已打开，目标 {hazardId}");
    }

    public void CloseImmediately()
    {
        bool wasOpen = gameObject.activeSelf;
        editing = null;
        closeConfirmationArmed = false;
        CloseKeyboard();
        gameObject.SetActive(false);

        // 本来就没开就别回调：StartMockMode 初始化时会无条件调一次，
        // 那时候详情卡还没摆好，回调过去只会白白重排一次布局
        if (wasOpen && onClosed != null) onClosed();
    }

    private void Save()
    {
        try
        {
            if (editing == null)
                source.AddAnnotation(hazardId, contentInput?.text);
            else
                source.UpdateAnnotation(editing.annotationId, editing.version, contentInput?.text);
            editing = null;
            if (contentInput != null) contentInput.text = string.Empty;

            // 回第一页：P0AnnotationStore.Load 按 updatedAt 倒序，刚存的这条必然排在最前。
            // 停在原页码的话，用户点完发送什么都没看见，会以为没存上。
            page = 0;
            RefreshHistory();
            SetStatus("演示批注已保存到本机");
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
        }
    }

    private void EditLatestOwn()
    {
        P0Annotation own = source.GetAnnotations(hazardId)
            .FirstOrDefault(item => item.authorId == "mock-user-self");
        if (own == null)
        {
            SetStatus("暂无本人可编辑批注");
            return;
        }
        editing = own;
        if (contentInput != null)
        {
            contentInput.text = own.content;
            contentInput.ActivateInputField();
        }
        SetStatus("正在编辑本人批注");
    }

    private void CancelOrClose()
    {
        bool hasDraft = contentInput != null && !string.IsNullOrWhiteSpace(contentInput.text);
        if (hasDraft && !closeConfirmationArmed)
        {
            closeConfirmationArmed = true;
            SetStatus("有未保存内容，再次点击取消将放弃草稿");
            return;
        }
        CloseImmediately();
    }

    private void Turn(int delta)
    {
        int pages = PageCount(source.GetAnnotations(hazardId).Count);
        if (pages <= 1) return;
        page = Mathf.Clamp(page + delta, 0, pages - 1);
        RefreshHistory();
    }

    private int PageCount(int count)
    {
        if (PageSize <= 0) return 0;
        return (count + PageSize - 1) / PageSize;
    }

    private void RefreshHistory()
    {
        IReadOnlyList<P0Annotation> items = source.GetAnnotations(hazardId);

        int pages = PageCount(items.Count);
        page = pages <= 0 ? 0 : Mathf.Clamp(page, 0, pages - 1);

        for (int slot = 0; slot < PageSize; slot++)
        {
            P0AnnotationRow row = rows[slot];
            if (row == null) continue;

            int index = page * PageSize + slot;
            P0Annotation item = index < items.Count ? items[index] : null;
            if (item == null) row.Clear();
            else row.Bind(item, item.authorId == "mock-user-self");
        }

        if (pageText != null)
            pageText.text = items.Count == 0 ? "暂无批注"
                : pages <= 1 ? $"共 {items.Count} 条"
                : $"{page + 1} / {pages}　共 {items.Count} 条";
        if (prevButton != null) prevButton.gameObject.SetActive(pages > 1);
        if (nextButton != null) nextButton.gameObject.SetActive(pages > 1);

        if (editLatestButton != null)
            editLatestButton.gameObject.SetActive(items.Any(item => item.authorId == "mock-user-self"));
    }

    private void BindPhrases()
    {
        TextAsset asset = Resources.Load<TextAsset>("P0Mock/p0_annotation_phrases");
        PhraseDocument document = asset != null
            ? Newtonsoft.Json.JsonConvert.DeserializeObject<PhraseDocument>(asset.text)
            : null;
        List<string> phrases = document?.items ?? new List<string>();
        for (int index = 0; index < phraseButtons.Count; index++)
        {
            Button button = phraseButtons[index];
            if (button == null) continue;
            if (index >= phrases.Count)
            {
                button.gameObject.SetActive(false);
                continue;
            }
            string phrase = phrases[index];
            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.text = phrase;
            button.onClick.AddListener(() => AppendPhrase(phrase));
        }
    }

    private void AppendPhrase(string phrase)
    {
        if (contentInput == null) return;
        string separator = string.IsNullOrWhiteSpace(contentInput.text) ? string.Empty : "；";
        if (contentInput.text.Length + separator.Length + phrase.Length > P0AnnotationStore.MaximumLength)
        {
            SetStatus("批注内容不能超过 500 字");
            return;
        }
        contentInput.text += separator + phrase;
        contentInput.ActivateInputField();
    }

    private void UpdateCharacterCount()
    {
        if (characterCountText != null)
            characterCountText.text = $"{contentInput?.text?.Length ?? 0}/500";
        closeConfirmationArmed = false;
    }

    private void SetStatus(string message)
    {
        if (statusText != null) statusText.text = message ?? string.Empty;
    }

    private void CloseKeyboard()
    {
        contentInput?.DeactivateInputField();
        BNG.VRTextInputPro adapter = contentInput != null
            ? contentInput.GetComponent<BNG.VRTextInputPro>()
            : null;
        if (adapter?.AttachedKeyboard != null)
        {
            CanvasGroup group = adapter.AttachedKeyboard.GetComponent<CanvasGroup>();
            if (group != null) BNG.VRKeyboardPro.SetCGActive(group, false);
        }
    }

    /// <summary>
    /// 卡片一亮就把沙盘上的企业 / 储罐图标从射线里摘掉，见
    /// <see cref="SandTableMarkerRaycasts"/>：手柄射线会穿过本卡片先命中后面的沙盘图标，
    /// 点中企业就会触发换企业，把正在写的批注连草稿一起关掉。
    ///
    /// 【为什么挂在 OnEnable / OnDisable 上，而不是 Open / CloseImmediately 里】
    /// 关闭路径不止一条：面板自己的取消按钮、宿主换企业、宿主离场、对象被销毁。
    /// 只有 OnEnable / OnDisable 是 Unity 保证配对的，写在业务方法里一定会漏掉某条路径，
    /// 漏掉的后果是沙盘图标永久点不动——比现在这个 bug 更难查。
    /// </summary>
    private void OnEnable()
    {
        SandTableMarkerRaycasts.Suppress();
    }

    private void OnDisable()
    {
        SandTableMarkerRaycasts.Release();
        CloseKeyboard();
    }

    [Serializable]
    private sealed class PhraseDocument
    {
        public List<string> items = new List<string>();
    }
}
