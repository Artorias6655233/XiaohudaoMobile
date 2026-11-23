using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class PatrolDetailPanel : MonoBehaviour
{
    public TextMeshProUGUI headerText;
    public TextMeshProUGUI itemText;
    public List<Button> itemButtons = new List<Button>();
    public InspectionAnnotationPanel annotationPanel;

    private PatrolInspectionService patrolService;
    private RectTransform listPanelRect;
    private System.Action returnToList;
    private PatrolRecord current;
    private PatrolInspectionItem selectedItem;
    private int generation;

    public string CurrentPatrolRecordId => selectedItem?.patrolRecordId;
    public bool IsOpen => gameObject.activeSelf;

    public static PatrolDetailPanel Create(
        Transform parent,
        PatrolInspectionService patrolService,
        InspectionAnnotationService annotationService)
    {
        RectTransform root = RuntimeWorldUi.Panel(parent, "RuntimePatrolDetailPanel",
            new Vector2(1200, 760), Vector2.zero);
        RuntimeWorldUi.Card(root, "HeaderBar", new Vector2(0.02f, 0.88f),
            new Vector2(0.98f, 0.99f), RuntimeWorldUi.HeaderColor);
        TextMeshProUGUI header = RuntimeWorldUi.Text(root, "Header", "运行效果检查详情",
            new Vector2(0.04f, 0.89f), new Vector2(0.96f, 0.98f), 44,
            TextAlignmentOptions.MidlineLeft);
        RuntimeWorldUi.Card(root, "InspectionNavigation", new Vector2(0.02f, 0.18f),
            new Vector2(0.34f, 0.86f), RuntimeWorldUi.CardColor);
        TextMeshProUGUI navigationTitle = RuntimeWorldUi.Text(root, "NavigationTitle", "检查条目",
            new Vector2(0.035f, 0.79f), new Vector2(0.325f, 0.85f), 30);
        navigationTitle.color = RuntimeWorldUi.MutedTextColor;
        RuntimeWorldUi.Card(root, "DetailCard", new Vector2(0.35f, 0.31f),
            new Vector2(0.98f, 0.86f), RuntimeWorldUi.CardColor);
        TextMeshProUGUI detailTitle = RuntimeWorldUi.Text(root, "DetailTitle", "巡查与检查信息",
            new Vector2(0.37f, 0.78f), new Vector2(0.96f, 0.85f), 30);
        detailTitle.color = RuntimeWorldUi.MutedTextColor;
        TextMeshProUGUI item = RuntimeWorldUi.Text(root, "Item", "请选择检查条目",
            new Vector2(0.37f, 0.34f), new Vector2(0.96f, 0.78f), 36);
        RuntimeWorldUi.Card(root, "MediaCard", new Vector2(0.35f, 0.18f),
            new Vector2(0.98f, 0.30f), RuntimeWorldUi.CardColor);
        TextMeshProUGUI mediaTitle = RuntimeWorldUi.Text(root, "MediaTitle", "媒体资料",
            new Vector2(0.37f, 0.245f), new Vector2(0.50f, 0.295f), 26);
        mediaTitle.color = RuntimeWorldUi.MutedTextColor;
        CreateMediaLabel(root, "ImageStatus", "图片  无", 0.51f, 0.65f);
        CreateMediaLabel(root, "AudioStatus", "语音  无", 0.66f, 0.80f);
        CreateMediaLabel(root, "VideoStatus", "视频  无", 0.81f, 0.96f);

        PatrolDetailPanel panel = root.gameObject.AddComponent<PatrolDetailPanel>();
        panel.patrolService = patrolService;
        panel.headerText = header;
        panel.itemText = item;
        for (int slot = 0; slot < 8; slot++)
        {
            int captured = slot;
            Button button = RuntimeWorldUi.Button(root, "Inspection" + slot,
                "检查条目 " + (slot + 1), new Vector2(0.035f, 0.71f - slot * 0.075f),
                new Vector2(0.325f, 0.78f - slot * 0.075f), 30);
            RuntimeWorldUi.StyleButton(button, RuntimeWorldUi.SecondaryColor);
            button.onClick.AddListener(() => panel.SelectItem(captured));
            panel.itemButtons.Add(button);
        }
        Button annotation = RuntimeWorldUi.Button(root, "Annotation", "查看/新增批注（X）",
            new Vector2(0.55f, 0.04f), new Vector2(0.82f, 0.16f), 32);
        RuntimeWorldUi.StyleButton(annotation, RuntimeWorldUi.PrimaryColor);
        Button back = RuntimeWorldUi.Button(root, "Back", "返回列表",
            new Vector2(0.83f, 0.04f), new Vector2(0.98f, 0.16f), 32);
        RuntimeWorldUi.StyleButton(back, RuntimeWorldUi.SecondaryColor);

        panel.annotationPanel = InspectionAnnotationPanel.Create(parent, annotationService);
        annotation.onClick.AddListener(panel.OpenAnnotation);
        back.onClick.AddListener(panel.Back);
        root.gameObject.SetActive(false);
        return panel;
    }

    public void ConfigureListView(RectTransform source, System.Action onReturnToList)
    {
        listPanelRect = source;
        returnToList = onReturnToList;
        MatchListPanel();
    }

    public void Open(PatrolRecord record)
    {
        if (record == null) return;
        current = record;
        selectedItem = record.companySafetyInspections != null &&
                       record.companySafetyInspections.Count > 0
            ? record.companySafetyInspections[0]
            : null;
        MatchListPanel();
        gameObject.SetActive(true);
        itemText.text = PatrolTransitionPresentation.BuildSummary(current, selectedItem);
        BindItems();
        StartCoroutine(LoadDetail(record.sid, ++generation));
    }

    private IEnumerator LoadDetail(string sid, int requestGeneration)
    {
        itemText.text = "正在加载巡查详情…";
        var result = new ServiceResult<PatrolRecord>();
        yield return patrolService.LoadDetail(sid, result);
        if (requestGeneration != generation) yield break;
        if (!result.ok)
        {
            itemText.text = result.error;
            BindItems();
            yield break;
        }
        current = result.value;
        BindItems();
        if (current.companySafetyInspections != null && current.companySafetyInspections.Count > 0)
            SelectItem(0);
        else itemText.text = PatrolTransitionPresentation.BuildSummary(current, null);
    }

    private void BindItems()
    {
        int count = current?.companySafetyInspections?.Count ?? 0;
        for (int i = 0; i < itemButtons.Count; i++)
        {
            Button button = itemButtons[i];
            bool visible = i < count;
            button.gameObject.SetActive(visible);
            if (!visible) continue;
            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                PatrolInspectionItem data = current.companySafetyInspections[i];
                label.text = $"{i + 1}. {(data.inspectionType == "aqyh" ? "安全隐患" : "巡查记录")}";
            }
            RuntimeWorldUi.StyleButton(button,
                selectedItem == current.companySafetyInspections[i]
                    ? RuntimeWorldUi.PrimaryColor
                    : RuntimeWorldUi.SecondaryColor);
        }
    }

    private void SelectItem(int itemIndex)
    {
        if (current?.companySafetyInspections == null ||
            itemIndex < 0 || itemIndex >= current.companySafetyInspections.Count) return;
        selectedItem = current.companySafetyInspections[itemIndex];
        itemText.text = PatrolTransitionPresentation.BuildSummary(current, selectedItem);
        UpdateItemSelection(itemIndex);
    }

    public void OpenAnnotation()
    {
        if (selectedItem != null)
            annotationPanel.Open(selectedItem.patrolRecordId, selectedItem.inspectionType);
    }

    public void Close()
    {
        generation++;
        annotationPanel?.CloseImmediately();
        current = null;
        selectedItem = null;
        gameObject.SetActive(false);
    }

    private void Back()
    {
        Close();
        returnToList?.Invoke();
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

    private void UpdateItemSelection(int selectedIndex)
    {
        for (int index = 0; index < itemButtons.Count; index++)
            RuntimeWorldUi.StyleButton(itemButtons[index],
                index == selectedIndex ? RuntimeWorldUi.PrimaryColor : RuntimeWorldUi.SecondaryColor);
    }

    private static void CreateMediaLabel(
        Transform parent, string name, string value, float left, float right)
    {
        TextMeshProUGUI label = RuntimeWorldUi.Text(parent, name, value,
            new Vector2(left, 0.19f), new Vector2(right, 0.245f), 26,
            TextAlignmentOptions.Center);
        label.color = RuntimeWorldUi.MutedTextColor;
    }
}
