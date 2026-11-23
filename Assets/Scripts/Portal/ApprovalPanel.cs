using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 审批卡片：结论已经由票面上的「通过 / 不通过」给出，这里补齐意见和签名，然后确认。
///
/// 【全程本机】确认之后整份结果写进 <see cref="JobTicketApprovalStore"/>，
/// **不回传门户**。所以不看账号权限，任何一张票都能操作。
///
/// 【为什么点按钮不直接记】结论、意见、签名是一组，缺一份就不成其为一次处置；
/// 而且中间要有一步能反悔。所以票面那两个按钮只负责给出结论，其余在这里补齐。
///
/// 【签名是选图不是笔迹】从签名库（<see cref="PortalSignatureConfig"/>，一个文件夹）
/// 里挑一张贴上去，象征签字。卡片上那句话就是这么写的，
/// 免得用户以为门户端能看到这张签名。
///
/// 卡片由 <c>ApprovalPanelBuilder</c> 生成 prefab，宿主 <see cref="JobTicketDetailPanel"/>
/// 按需实例化一份。放在票面右侧那块空白上 —— 票面内容只占左边一半宽。
/// </summary>
public class ApprovalPanel : MonoBehaviour
{
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI conclusionText;
    public TextMeshProUGUI statusText;

    public Button[] phraseButtons;
    public TextMeshProUGUI[] phraseLabels;

    public Button[] signatureButtons;
    public RawImage[] signatureImages;
    public TextMeshProUGUI[] signatureLabels;

    public Button submitButton;
    public Button backButton;

    /// <summary>选中项的底色。未选中用 <see cref="idleColor"/>。</summary>
    public Color pickedColor = new Color(0.30f, 0.62f, 1f, 0.85f);
    public Color idleColor = new Color(1f, 1f, 1f, 0.16f);

    private PortalApprovalDraft.Draft draft;
    private string ticketSid;
    private string stageName;
    private Coroutine submitting;

    /// <summary>提交成功时回调 true；用户点返回或提交失败关闭时回调 false。</summary>
    private Action<bool> onClosed;

    private void Awake()
    {
        if (submitButton != null) submitButton.onClick.AddListener(OnSubmit);
        if (backButton != null) backButton.onClick.AddListener(OnBack);
        BindPickers();
        gameObject.SetActive(false);
    }

    private void BindPickers()
    {
        if (phraseButtons != null)
        {
            for (int i = 0; i < phraseButtons.Length; i++)
            {
                int index = i;                       // 闭包要捕获副本，不能直接用 i
                if (phraseButtons[i] != null) phraseButtons[i].onClick.AddListener(() => PickPhrase(index));
            }
        }

        if (signatureButtons == null) return;
        for (int i = 0; i < signatureButtons.Length; i++)
        {
            int index = i;
            if (signatureButtons[i] != null) signatureButtons[i].onClick.AddListener(() => PickSignature(index));
        }
    }

    /// <summary>打开卡片。<paramref name="passed"/> 是票面上那两个按钮给出的结论。</summary>
    public void Open(PortalTicketDetail ticketDetail, string sid, bool passed, Action<bool> closed)
    {
        ticketSid = sid;
        onClosed = closed;

        PortalApprovalPhraseConfig.EnsureLoaded();
        PortalSignatureConfig.EnsureLoaded();

        draft = PortalApprovalDraft.Get(sid, ticketDetail != null ? ticketDetail.TaskInsId : null);
        draft.Passed = passed;
        stageName = ticketDetail != null ? ticketDetail.TaskName : string.Empty;

        gameObject.SetActive(true);

        if (titleText != null) titleText.text = "本机处置 · " + stageName;
        if (conclusionText != null) conclusionText.text = "结论：" + (passed ? "通过" : "不通过");

        RenderPhrases(passed);
        RenderSignatures();
        RefreshState();
    }

    public void Close()
    {
        if (submitting != null)
        {
            StopCoroutine(submitting);
            submitting = null;
        }

        gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------ 渲染

    private void RenderPhrases(bool passed)
    {
        string[] phrases = PortalApprovalPhraseConfig.For(passed);
        if (phraseButtons == null) return;

        for (int i = 0; i < phraseButtons.Length; i++)
        {
            bool has = i < phrases.Length;
            if (phraseButtons[i] != null) phraseButtons[i].gameObject.SetActive(has);
            if (has && phraseLabels != null && i < phraseLabels.Length && phraseLabels[i] != null)
            {
                phraseLabels[i].text = phrases[i];
            }
        }

        // 换了结论，上一条意见就未必还合适，清掉重选
        draft.Opinion = string.Empty;
    }

    private void RenderSignatures()
    {
        var options = PortalSignatureConfig.Options;
        if (signatureButtons == null) return;

        if (PortalSignatureConfig.IsEmpty)
        {
            // 一张图都没有时，把第一个槽位留着当提示位。
            // 否则签名那一栏是整片空白，看起来像"没有签名功能"——用户已经这么理解过一次了。
            ShowEmptySignatureSlot();
            return;
        }

        for (int i = 0; i < signatureButtons.Length; i++)
        {
            bool has = options != null && i < options.Count;
            if (signatureButtons[i] != null) signatureButtons[i].gameObject.SetActive(has);
            if (!has) continue;

            if (signatureImages != null && i < signatureImages.Length && signatureImages[i] != null)
            {
                signatureImages[i].texture = options[i].Texture;
                signatureImages[i].enabled = options[i].Texture != null;
            }

            if (signatureLabels != null && i < signatureLabels.Length && signatureLabels[i] != null)
            {
                signatureLabels[i].text = options[i].Label ?? string.Empty;
            }
        }
    }

    /// <summary>签名库空着时的占位提示，占第一个槽位，不可点。</summary>
    private void ShowEmptySignatureSlot()
    {
        for (int i = 0; i < signatureButtons.Length; i++)
        {
            bool first = i == 0;
            if (signatureButtons[i] != null)
            {
                signatureButtons[i].gameObject.SetActive(first);
                signatureButtons[i].interactable = false;
            }
            if (signatureImages != null && i < signatureImages.Length && signatureImages[i] != null)
            {
                signatureImages[i].enabled = false;
            }
            if (first && signatureLabels != null && signatureLabels.Length > 0 && signatureLabels[0] != null)
            {
                signatureLabels[0].text = "签名库为空，本次免签";
            }
        }
    }

    private void PickPhrase(int index)
    {
        if (submitting != null) return;
        string[] phrases = PortalApprovalPhraseConfig.For(draft.Passed ?? true);
        if (index < 0 || index >= phrases.Length) return;

        draft.Opinion = phrases[index];
        RefreshState();
    }

    private void PickSignature(int index)
    {
        if (submitting != null) return;
        var options = PortalSignatureConfig.Options;
        if (options == null || index < 0 || index >= options.Count) return;

        draft.SignatureId = options[index].Id;
        RefreshState();
    }

    /// <summary>刷新选中态、提交按钮可用性和状态文案。</summary>
    private void RefreshState()
    {
        string[] phrases = PortalApprovalPhraseConfig.For(draft.Passed ?? true);
        if (phraseButtons != null)
        {
            for (int i = 0; i < phraseButtons.Length; i++)
            {
                bool picked = i < phrases.Length && string.Equals(phrases[i], draft.Opinion, StringComparison.Ordinal);
                Tint(phraseButtons[i], picked);
            }
        }

        var options = PortalSignatureConfig.Options;
        if (signatureButtons != null && options != null)
        {
            for (int i = 0; i < signatureButtons.Length; i++)
            {
                bool picked = i < options.Count &&
                              string.Equals(options[i].Id, draft.SignatureId, StringComparison.Ordinal);
                Tint(signatureButtons[i], picked);
            }
        }

        bool ready = draft.IsComplete;
        if (submitButton != null) submitButton.interactable = ready && submitting == null;
        if (statusText != null && submitting == null)
        {
            statusText.text = ready ? "可以确认。结果只记在这台眼镜上，不回传门户。" : draft.MissingHint;
        }
    }

    private void Tint(Button button, bool picked)
    {
        if (button == null) return;
        Image image = button.targetGraphic as Image;
        if (image != null) image.color = picked ? pickedColor : idleColor;
    }

    // ------------------------------------------------------------------ 提交

    private void OnSubmit()
    {
        if (submitting != null || !draft.IsComplete) return;
        submitting = StartCoroutine(SubmitRoutine());
    }

    /// <summary>
    /// 写本机存档。
    ///
    /// 【为什么还是协程】写盘是同步的，本来一行就够。留着协程是因为宿主那边的回调时序
    /// （提交完成 → 票面改文案 → 3 秒回列表）是按"确认是个异步动作"搭的，
    /// 而且以后若要接回门户提交，改这一个方法就行，外面不用动。
    /// </summary>
    private IEnumerator SubmitRoutine()
    {
        SetBusy(true, "正在记录…");
        yield return null;                     // 让"正在记录"这一帧画出来

        JobTicketApprovalStore.Save(ticketSid, draft.Passed == true,
                                    draft.Opinion, draft.SignatureId, stageName);

        submitting = null;
        PortalApprovalDraft.Clear(ticketSid);
        SetBusy(false, "已记录在本机");
        Close();
        onClosed?.Invoke(true);
    }

    /// <summary>提交期间锁住所有选择，避免改了内容却提交了旧的。</summary>
    private void SetBusy(bool busy, string message)
    {
        if (statusText != null) statusText.text = message;

        SetGroup(phraseButtons, !busy);
        SetGroup(signatureButtons, !busy);
        if (submitButton != null)
        {
            submitButton.interactable = !busy && draft != null && draft.IsComplete;
        }
        if (backButton != null) backButton.interactable = !busy;

        if (!busy) RefreshState();
    }

    private static void SetGroup(Button[] buttons, bool interactable)
    {
        if (buttons == null) return;
        foreach (Button button in buttons)
        {
            if (button != null) button.interactable = interactable;
        }
    }

    private void OnBack()
    {
        if (submitting != null) return;
        Close();
        onClosed?.Invoke(false);
    }
}
