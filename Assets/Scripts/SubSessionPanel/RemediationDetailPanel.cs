using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class RemediationDetailPanel : MonoBehaviour
{
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI bodyText;
    public TextMeshProUGUI rectificationText;
    public TextMeshProUGUI descriptionText;
    public TextMeshProUGUI latestSupervisionText;
    public Button backButton;
    public Button superviseButton;

    private RectTransform listPanelRect;
    private Action returnToList;
    private Action<DoubleDanger> openSupervision;
    private DoubleDanger currentDanger;
    private SupervisionService supervisionService;
    private int historyGeneration;

    public bool IsOpen => gameObject.activeSelf;

    public static RemediationDetailPanel Create(
        Transform parent, SupervisionService supervisionService = null)
    {
        RectTransform root = RuntimeWorldUi.Panel(parent, "RuntimeRemediationDetailPanel",
            new Vector2(1200, 760), Vector2.zero);

        RuntimeWorldUi.Card(root, "HeaderBar", new Vector2(0.02f, 0.88f),
            new Vector2(0.98f, 0.99f), RuntimeWorldUi.HeaderColor);
        TextMeshProUGUI title = RuntimeWorldUi.Text(root, "Title", "整改督办详情",
            new Vector2(0.045f, 0.895f), new Vector2(0.96f, 0.975f), 44,
            TextAlignmentOptions.MidlineLeft);

        RuntimeWorldUi.Card(root, "HazardCard", new Vector2(0.02f, 0.49f),
            new Vector2(0.49f, 0.86f), RuntimeWorldUi.CardColor);
        TextMeshProUGUI hazardTitle = RuntimeWorldUi.Text(root, "HazardTitle", "隐患信息",
            new Vector2(0.045f, 0.79f), new Vector2(0.46f, 0.85f), 30);
        hazardTitle.color = RuntimeWorldUi.MutedTextColor;
        TextMeshProUGUI body = RuntimeWorldUi.Text(root, "HazardBody", string.Empty,
            new Vector2(0.045f, 0.52f), new Vector2(0.46f, 0.79f), 32);

        RuntimeWorldUi.Card(root, "RectificationCard", new Vector2(0.50f, 0.49f),
            new Vector2(0.98f, 0.86f), RuntimeWorldUi.CardColor);
        TextMeshProUGUI rectificationTitle = RuntimeWorldUi.Text(root,
            "RectificationTitle", "整改信息", new Vector2(0.525f, 0.79f),
            new Vector2(0.95f, 0.85f), 30);
        rectificationTitle.color = RuntimeWorldUi.MutedTextColor;
        TextMeshProUGUI rectification = RuntimeWorldUi.Text(root, "RectificationBody",
            string.Empty, new Vector2(0.525f, 0.52f), new Vector2(0.95f, 0.79f), 32);

        RuntimeWorldUi.Card(root, "DescriptionCard", new Vector2(0.02f, 0.30f),
            new Vector2(0.98f, 0.47f), RuntimeWorldUi.CardColor);
        TextMeshProUGUI descriptionTitle = RuntimeWorldUi.Text(root,
            "DescriptionTitle", "隐患描述与整改要求", new Vector2(0.045f, 0.405f),
            new Vector2(0.95f, 0.46f), 30);
        descriptionTitle.color = RuntimeWorldUi.MutedTextColor;
        TextMeshProUGUI description = RuntimeWorldUi.Text(root, "DescriptionBody",
            string.Empty, new Vector2(0.045f, 0.315f), new Vector2(0.95f, 0.405f), 30);

        RuntimeWorldUi.Card(root, "LatestSupervisionCard", new Vector2(0.02f, 0.17f),
            new Vector2(0.98f, 0.29f), RuntimeWorldUi.CardColor);
        TextMeshProUGUI latestTitle = RuntimeWorldUi.Text(root,
            "LatestSupervisionTitle", "最新督办要求", new Vector2(0.045f, 0.245f),
            new Vector2(0.25f, 0.285f), 28);
        latestTitle.color = RuntimeWorldUi.MutedTextColor;
        TextMeshProUGUI latest = RuntimeWorldUi.Text(root, "LatestSupervision",
            "暂无督办要求", new Vector2(0.26f, 0.185f), new Vector2(0.95f, 0.285f), 29,
            TextAlignmentOptions.MidlineLeft);

        Button back = RuntimeWorldUi.Button(root, "Back", "返回列表",
            new Vector2(0.56f, 0.035f), new Vector2(0.75f, 0.16f), 34);
        RuntimeWorldUi.StyleButton(back, RuntimeWorldUi.SecondaryColor);
        Button supervise = RuntimeWorldUi.Button(root, "Supervise", "发起督办（X）",
            new Vector2(0.76f, 0.035f), new Vector2(0.98f, 0.16f), 34);
        RuntimeWorldUi.StyleButton(supervise, RuntimeWorldUi.PrimaryColor);

        RemediationDetailPanel panel = root.gameObject.AddComponent<RemediationDetailPanel>();
        panel.titleText = title;
        panel.bodyText = body;
        panel.rectificationText = rectification;
        panel.descriptionText = description;
        panel.latestSupervisionText = latest;
        panel.supervisionService = supervisionService;
        panel.backButton = back;
        panel.superviseButton = supervise;
        back.onClick.AddListener(panel.Back);
        supervise.onClick.AddListener(panel.Supervise);
        root.gameObject.SetActive(false);
        return panel;
    }

    public void ConfigureListView(
        RectTransform source, Action onReturnToList, Action<DoubleDanger> onOpenSupervision)
    {
        listPanelRect = source;
        returnToList = onReturnToList;
        openSupervision = onOpenSupervision;
        MatchListPanel();
    }

    public void Open(DoubleDanger danger)
    {
        if (danger == null) return;
        currentDanger = danger;
        MatchListPanel();
        titleText.text = "整改督办详情  ·  " + Value(danger.hiddenDangerName);
        bodyText.text =
            $"隐患名称：{Value(danger.hiddenDangerName)}\n" +
            $"隐患分类：{Value(danger.hiddenDangerType)}\n" +
            $"隐患来源：{Value(danger.hiddenDangerSource)}\n" +
            $"隐患位置：{Value(danger.hiddenDangerLocation)}\n" +
            $"隐患等级：{Value(danger.hiddenDangerLevel)}";

        VerifyInfo verify = danger.verifyInfo;
        rectificationText.text =
            $"整改责任人：{Value(verify?.hiddenDangerVerifyChangePerson)}\n" +
            $"整改部门：{Value(verify?.rectificationDept, danger.deptOrgCode)}\n" +
            $"整改期限：{Value(verify?.rectificationDeadline)}\n" +
            $"整改状态：{Status(danger.hiddenDangerStatus)}\n" +
            $"整改进度：{Progress(danger)}";
        descriptionText.text =
            $"隐患描述：{Value(danger.hiddenDangerDescription)}\n\n" +
            $"整改要求：{Value(verify?.hiddenDangerVerifyInfo)}";
        ShowLatestSupervision(null);
        gameObject.SetActive(true);
        if (supervisionService != null)
            StartCoroutine(RefreshLatestSupervision(danger.sid, ++historyGeneration));
    }

    public void ShowLatestSupervision(SupervisionRecord record)
    {
        if (latestSupervisionText == null) return;
        latestSupervisionText.text = record == null
            ? "暂无督办要求"
            : $"{Value(record.remindContent)}\n{Value(record.senderName)}  {Value(record.created)}";
    }

    private System.Collections.IEnumerator RefreshLatestSupervision(
        string dangerId, int requestGeneration)
    {
        var result = new ServiceResult<System.Collections.Generic.List<SupervisionRecord>>();
        yield return supervisionService.LoadHistory(dangerId, result);
        if (requestGeneration != historyGeneration || currentDanger?.sid != dangerId) yield break;
        if (!result.ok)
        {
            latestSupervisionText.text = "督办要求加载失败";
            yield break;
        }
        SupervisionRecord latest = result.value != null && result.value.Count > 0
            ? result.value[0]
            : null;
        ShowLatestSupervision(latest);
    }

    public void Close()
    {
        historyGeneration++;
        currentDanger = null;
        gameObject.SetActive(false);
    }

    private void Back()
    {
        Close();
        returnToList?.Invoke();
    }

    private void Supervise()
    {
        if (currentDanger != null) openSupervision?.Invoke(currentDanger);
    }

    private void MatchListPanel()
    {
        if (listPanelRect == null || listPanelRect.parent != transform.parent) return;
        RectTransform target = (RectTransform)transform;
        target.anchorMin = listPanelRect.anchorMin;
        target.anchorMax = listPanelRect.anchorMax;
        target.pivot = listPanelRect.pivot;
        target.anchoredPosition = listPanelRect.anchoredPosition;
        target.sizeDelta = listPanelRect.sizeDelta;
        target.localRotation = listPanelRect.localRotation;
        target.localScale = listPanelRect.localScale;
        target.SetSiblingIndex(listPanelRect.GetSiblingIndex() + 1);
    }

    private static string Progress(DoubleDanger danger)
    {
        int count = danger.changeInfos?.Count ?? 0;
        if (count == 0) return "暂无整改记录";
        ChangeInfo latest = danger.changeInfos[count - 1];
        return string.IsNullOrWhiteSpace(latest.hiddenDangerChangeInfo)
            ? "已提交整改记录"
            : latest.hiddenDangerChangeInfo;
    }

    private static string Status(string status)
    {
        if (status == "4") return "待整改";
        if (status == "5") return "整改中";
        return Value(status);
    }

    private static string Value(string primary, string fallback = null)
    {
        if (!string.IsNullOrWhiteSpace(primary)) return primary;
        return string.IsNullOrWhiteSpace(fallback) ? "无" : fallback;
    }
}
