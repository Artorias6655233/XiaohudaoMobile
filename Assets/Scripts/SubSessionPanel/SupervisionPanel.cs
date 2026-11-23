using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class SupervisionPanel : MonoBehaviour
{
    public TMP_InputField contentInput;
    public TextMeshProUGUI dangerSummaryText;
    public TextMeshProUGUI historyText;
    public TextMeshProUGUI statusText;
    public Button sendButton;
    public Button closeButton;
    public List<Button> phraseButtons = new List<Button>();

    private static readonly string[] QuickPhrases =
    {
        "请立即落实整改措施，并在整改期限内完成闭环。",
        "整改进度滞后，请加快整改并及时反馈进展。",
        "整改措施不完善，请补充完善后重新提交。",
        "整改期限临近，请确保按期完成整改。"
    };

    private SupervisionService service;
    private DoubleDanger current;
    private bool sending;
    private string clientRequestId;
    private bool closeConfirmationArmed;

    public bool IsOpen => gameObject.activeSelf;
    public bool IsEditingInput => contentInput != null && contentInput.isFocused;
    public event Action<SupervisionRecord> Sent;

    public static SupervisionPanel Create(Transform parent, SupervisionService service)
    {
        RectTransform root = RuntimeWorldUi.Panel(parent, "RuntimeSupervisionPanel",
            new Vector2(1300, 900), Vector2.zero);
        RuntimeWorldUi.Card(root, "HeaderBar", new Vector2(0.02f, 0.89f),
            new Vector2(0.98f, 0.99f), RuntimeWorldUi.HeaderColor);
        RuntimeWorldUi.Text(root, "Title", "整改督办（Y 发送）",
            new Vector2(0.04f, 0.90f), new Vector2(0.96f, 0.98f), 40,
            TextAlignmentOptions.MidlineLeft);

        RectTransform summaryCard = RuntimeWorldUi.Card(root, "SummaryCard",
            new Vector2(0.02f, 0.63f), new Vector2(0.46f, 0.87f), RuntimeWorldUi.CardColor);
        TextMeshProUGUI summaryTitle = RuntimeWorldUi.Text(summaryCard, "SummaryTitle", "当前隐患",
            new Vector2(0.03f, 0.76f), new Vector2(0.97f, 0.97f), 28);
        summaryTitle.color = RuntimeWorldUi.MutedTextColor;
        TextMeshProUGUI summary = RuntimeWorldUi.Text(summaryCard, "DangerSummary", "",
            new Vector2(0.03f, 0.04f), new Vector2(0.97f, 0.76f), 27);

        RectTransform historyCard = RuntimeWorldUi.Card(root, "HistoryCard",
            new Vector2(0.02f, 0.17f), new Vector2(0.46f, 0.62f), RuntimeWorldUi.CardColor);
        historyCard.gameObject.AddComponent<RectMask2D>();
        TextMeshProUGUI historyTitle = RuntimeWorldUi.Text(historyCard, "HistoryTitle", "督办历史",
            new Vector2(0.03f, 0.84f), new Vector2(0.97f, 0.97f), 28);
        historyTitle.color = RuntimeWorldUi.MutedTextColor;
        TextMeshProUGUI history = RuntimeWorldUi.Text(historyCard, "History", "暂无督办记录",
            new Vector2(0.03f, 0.03f), new Vector2(0.97f, 0.84f), 24);
        history.overflowMode = TextOverflowModes.Truncate;

        RectTransform editorCard = RuntimeWorldUi.Card(root, "EditorCard",
            new Vector2(0.47f, 0.17f), new Vector2(0.98f, 0.87f), RuntimeWorldUi.CardColor);
        TextMeshProUGUI editorTitle = RuntimeWorldUi.Text(editorCard, "EditorTitle", "下达整改要求",
            new Vector2(0.04f, 0.88f), new Vector2(0.96f, 0.98f), 30);
        editorTitle.color = RuntimeWorldUi.MutedTextColor;
        TMP_InputField input = RuntimeWorldUi.Input(editorCard, "Content",
            new Vector2(0.04f, 0.61f), new Vector2(0.96f, 0.87f));
        TextMeshProUGUI phraseTitle = RuntimeWorldUi.Text(editorCard, "PhraseTitle",
            "快捷督办要求（点击后可继续编辑）", new Vector2(0.04f, 0.52f),
            new Vector2(0.96f, 0.60f), 26);
        phraseTitle.color = RuntimeWorldUi.MutedTextColor;
        var phrases = new List<Button>();
        for (int index = 0; index < QuickPhrases.Length; index++)
        {
            int row = index / 2;
            int column = index % 2;
            float left = 0.04f + column * 0.46f;
            float top = 0.51f - row * 0.22f;
            Button phrase = RuntimeWorldUi.Button(editorCard, "Phrase" + index,
                QuickPhrases[index], new Vector2(left, top - 0.19f),
                new Vector2(left + 0.44f, top), 22);
            RuntimeWorldUi.StyleButton(phrase, RuntimeWorldUi.SecondaryColor);
            phrases.Add(phrase);
        }
        TextMeshProUGUI status = RuntimeWorldUi.Text(root, "Status", "",
            new Vector2(0.03f, 0.105f), new Vector2(0.97f, 0.16f), 24,
            TextAlignmentOptions.Center);
        status.color = RuntimeWorldUi.AccentColor;
        Button send = RuntimeWorldUi.Button(root, "Send", "发送督办",
            new Vector2(0.72f, 0.015f), new Vector2(0.97f, 0.10f), 30);
        RuntimeWorldUi.StyleButton(send, RuntimeWorldUi.PrimaryColor);
        Button close = RuntimeWorldUi.Button(root, "Close", "关闭",
            new Vector2(0.53f, 0.015f), new Vector2(0.71f, 0.10f), 30);
        RuntimeWorldUi.StyleButton(close, RuntimeWorldUi.SecondaryColor);

        SupervisionPanel panel = root.gameObject.AddComponent<SupervisionPanel>();
        panel.service = service;
        panel.historyText = history;
        panel.dangerSummaryText = summary;
        panel.contentInput = input;
        panel.statusText = status;
        panel.sendButton = send;
        panel.closeButton = close;
        panel.phraseButtons = phrases;
        for (int index = 0; index < phrases.Count; index++)
        {
            int captured = index;
            phrases[index].onClick.AddListener(() => panel.SelectPhrase(captured));
        }
        input.onValueChanged.AddListener(_ =>
        {
            panel.closeConfirmationArmed = false;
            panel.ResetPhraseSelection();
        });
        send.onClick.AddListener(panel.Submit);
        close.onClick.AddListener(panel.CancelOrClose);
        root.gameObject.SetActive(false);
        return panel;
    }

    public void Open(DoubleDanger danger)
    {
        if (danger == null) return;
        current = danger;
        clientRequestId = null;
        sending = false;
        closeConfirmationArmed = false;
        if (contentInput != null) contentInput.text = string.Empty;
        ResetPhraseSelection();
        if (dangerSummaryText != null)
        {
            ChangeInfo latest = danger.changeInfos != null && danger.changeInfos.Count > 0
                ? danger.changeInfos[danger.changeInfos.Count - 1]
                : null;
            dangerSummaryText.text =
                $"隐患：{danger.hiddenDangerName}\n" +
                $"整改责任人：{danger.verifyInfo?.hiddenDangerVerifyChangePerson ?? "未指定"}    " +
                $"整改期限：{danger.verifyInfo?.rectificationDeadline ?? "未指定"}\n" +
                $"整改进度：{latest?.hiddenDangerChangeInfo ?? "暂无整改反馈"}";
        }
        gameObject.SetActive(true);
        StartCoroutine(RefreshHistory());
    }

    public void Submit()
    {
        if (sending || current == null) return;
        if (clientRequestId == null) clientRequestId = Guid.NewGuid().ToString("N");
        StartCoroutine(SendCoroutine());
    }

    private IEnumerator SendCoroutine()
    {
        sending = true;
        if (sendButton != null) sendButton.interactable = false;
        SetStatus("正在发送…");
        var result = new ServiceResult<SupervisionRecord>();
        yield return service.Send(current, contentInput?.text, clientRequestId, result);
        sending = false;
        if (sendButton != null) sendButton.interactable = true;
        if (!result.ok)
        {
            SetStatus(result.error);
            yield break;
        }
        CompleteSuccessfulSend(result.value, result.isMock);
        yield return RefreshHistory();
    }

    private void CompleteSuccessfulSend(SupervisionRecord record, bool isMock)
    {
        clientRequestId = null;
        if (contentInput != null) contentInput.text = string.Empty;
        SetStatus(isMock ? "演示督办已保存到本机，未实际发送" : "督办通知已下达");
        if (record != null) Sent?.Invoke(record);
    }

    private IEnumerator RefreshHistory()
    {
        if (current == null) yield break;
        var result = new ServiceResult<List<SupervisionRecord>>();
        yield return service.LoadHistory(current.sid, result);
        if (!result.ok)
        {
            if (historyText != null) historyText.text = result.error;
            yield break;
        }
        var builder = new StringBuilder();
        foreach (SupervisionRecord item in result.value)
        {
            builder.Append(item.created).Append("  ")
                .Append(item.senderName).Append(" → ").Append(item.recipientName).AppendLine()
                .AppendLine(item.remindContent).AppendLine();
        }
        if (historyText != null)
            historyText.text = builder.Length == 0 ? "暂无督办记录" : builder.ToString();
    }

    private void CancelOrClose()
    {
        if (contentInput != null && !string.IsNullOrWhiteSpace(contentInput.text) &&
            !closeConfirmationArmed)
        {
            closeConfirmationArmed = true;
            SetStatus("有未保存内容，再次点击关闭将放弃草稿");
            return;
        }
        CloseImmediately();
    }

    private void SelectPhrase(int index)
    {
        if (index < 0 || index >= QuickPhrases.Length || contentInput == null) return;
        contentInput.text = QuickPhrases[index];
        contentInput.DeactivateInputField();
        for (int buttonIndex = 0; buttonIndex < phraseButtons.Count; buttonIndex++)
            RuntimeWorldUi.StyleButton(phraseButtons[buttonIndex],
                buttonIndex == index ? RuntimeWorldUi.AccentColor : RuntimeWorldUi.SecondaryColor);
        SetStatus("已选择快捷短语，可按 Y 发送");
    }

    private void ResetPhraseSelection()
    {
        foreach (Button button in phraseButtons)
            RuntimeWorldUi.StyleButton(button, RuntimeWorldUi.SecondaryColor);
    }

    public void CloseImmediately()
    {
        if (contentInput != null) contentInput.DeactivateInputField();
        current = null;
        gameObject.SetActive(false);
    }

    private void SetStatus(string value)
    {
        if (statusText != null) statusText.text = value ?? string.Empty;
    }

    private void OnEnable() { SandTableMarkerRaycasts.Suppress(); }
    private void OnDisable() { SandTableMarkerRaycasts.Release(); }
}
