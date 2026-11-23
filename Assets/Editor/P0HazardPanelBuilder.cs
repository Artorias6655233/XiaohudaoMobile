#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class P0HazardPanelBuilder
{
    private const string TemplatePath = "Assets/ArtWorks/TankMonitorPanel.prefab";
    private const string PrefabPath = "Assets/ArtWorks/P0HazardDetailPanel.prefab";

    public static void Build()
    {
        GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePath);
        TextMeshProUGUI templateText = template?.GetComponentInChildren<TextMeshProUGUI>(true);
        Image templateImage = template?.GetComponent<Image>();
        if (templateText == null || templateImage == null)
            throw new System.InvalidOperationException("P0 危险源详情模板不可用");

        var root = new GameObject("P0HazardDetailPanel", typeof(RectTransform), typeof(Image));
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(1880, 1300);
        Image background = root.GetComponent<Image>();
        background.sprite = templateImage.sprite;
        background.color = templateImage.color;
        background.raycastTarget = false;

        P0HazardDetailPanel panel = root.AddComponent<P0HazardDetailPanel>();
        panel.titleText = Text(root, "Title", "重大危险源管控", 90, -70, 1400, 90, 72, templateText);
        panel.sourceBadgeText = Text(root, "SourceBadge", "演示数据", 1500, -80, 280, 70, 48, templateText);
        panel.statisticsText = Text(root, "Statistics", "总数 0", 90, -200, 1690, 80, 52, templateText);
        panel.summaryText = Text(root, "Summary", string.Empty, 90, -330, 1690, 380, 48, templateText);
        panel.responsiblePeopleText = Text(root, "ResponsiblePeople", string.Empty, 90, -740, 1690, 330, 46, templateText);
        panel.hintText = Text(root, "Hint", "请选择企业", 90, -1120, 1690, 90, 42, templateText);
        panel.annotationButton = Button(root, "AnnotationButton", "新增/查看批注", 1420, -1110, 360, 90, templateText);

        panel.summaryText.enableWordWrapping = true;
        panel.summaryText.overflowMode = TextOverflowModes.Overflow;
        panel.responsiblePeopleText.enableWordWrapping = true;
        panel.responsiblePeopleText.overflowMode = TextOverflowModes.Ellipsis;
        root.SetActive(false);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(
            Directory.GetParent(Application.dataPath).FullName, PrefabPath)));
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log("P0HazardPanelBuilder: prefab generated");
    }

    private static TextMeshProUGUI Text(
        GameObject parent,
        string name,
        string content,
        float x,
        float y,
        float width,
        float height,
        float size,
        TextMeshProUGUI template)
    {
        var item = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform rect = item.GetComponent<RectTransform>();
        rect.SetParent(parent.transform, false);
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
        TextMeshProUGUI text = item.GetComponent<TextMeshProUGUI>();
        text.font = template.font;
        text.color = template.color;
        text.fontSize = size;
        text.text = content;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return text;
    }

    private static Button Button(
        GameObject parent,
        string name,
        string label,
        float x,
        float y,
        float width,
        float height,
        TextMeshProUGUI template)
    {
        var item = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        RectTransform rect = item.GetComponent<RectTransform>();
        rect.SetParent(parent.transform, false);
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
        item.GetComponent<Image>().color = new Color(0.1f, 0.55f, 0.75f, 0.9f);
        TextMeshProUGUI text = Text(item, "Label", label, 0, 0, width, height, 40, template);
        text.alignment = TextAlignmentOptions.Center;
        return item.GetComponent<Button>();
    }
}
#endif
