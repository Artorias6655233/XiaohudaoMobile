#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class P0MonitoringPanelBuilder
{
    private const string TemplatePath = "Assets/ArtWorks/TankMonitorPanel.prefab";
    private const string PrefabPath = "Assets/ArtWorks/P0MonitoringPanel.prefab";

    public static void Build()
    {
        GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePath);
        TextMeshProUGUI templateText = template != null
            ? template.GetComponentInChildren<TextMeshProUGUI>(true)
            : null;
        Image templateImage = template != null ? template.GetComponent<Image>() : null;
        if (templateText == null || templateImage == null)
            throw new System.InvalidOperationException("P0 监测卡片模板不可用");

        var root = new GameObject("P0MonitoringPanel", typeof(RectTransform), typeof(Image));
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(1880f, 1250f);
        Image background = root.GetComponent<Image>();
        background.sprite = templateImage.sprite;
        background.color = templateImage.color;
        background.raycastTarget = false;

        P0TankAreaMonitorPanel panel = root.AddComponent<P0TankAreaMonitorPanel>();
        panel.titleText = Text(root, "Title", "罐区监测预警", 90, -60, 1500, 90, 72, templateText);
        panel.sourceBadgeText = Text(root, "SourceBadge", "演示数据", 1500, -70, 280, 70, 48, templateText);
        panel.companyText = Text(root, "Company", "企业", 90, -180, 1600, 70, 54, templateText);
        panel.areaText = Text(root, "Area", "罐区", 90, -270, 1600, 70, 54, templateText);

        Text(root, "HeaderName", "参数", 90, -390, 230, 60, 44, templateText);
        Text(root, "HeaderValue", "实时值", 340, -390, 260, 60, 44, templateText);
        Text(root, "HeaderUnit", "单位", 620, -390, 160, 60, 44, templateText);
        Text(root, "HeaderThreshold", "报警阈值", 800, -390, 700, 60, 44, templateText);
        Text(root, "HeaderState", "状态", 1520, -390, 240, 60, 44, templateText);

        panel.liquidLevelRow = Row(root, "LiquidLevel", "液位", -490, templateText);
        panel.temperatureRow = Row(root, "Temperature", "温度", -680, templateText);
        panel.concentrationRow = Row(root, "Concentration", "浓度", -870, templateText);
        panel.footerText = Text(root, "Footer", string.Empty, 90, -1080, 1690, 90, 42, templateText);

        root.SetActive(false);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(
            Directory.GetParent(Application.dataPath).FullName, PrefabPath)));
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log("P0MonitoringPanelBuilder: prefab generated");
    }

    private static P0SensorRow Row(
        GameObject parent,
        string name,
        string displayName,
        float y,
        TextMeshProUGUI template)
    {
        var root = new GameObject("Row_" + name, typeof(RectTransform));
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.SetParent(parent.transform, false);
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(0, y);
        rect.sizeDelta = new Vector2(1880, 150);

        P0SensorRow row = root.AddComponent<P0SensorRow>();
        row.nameText = Text(root, "Name", displayName, 90, 0, 230, 130, 52, template);
        row.valueText = Text(root, "Value", "--", 340, 0, 260, 130, 58, template);
        row.unitText = Text(root, "Unit", string.Empty, 620, 0, 160, 130, 48, template);
        row.thresholdText = Text(root, "Threshold", string.Empty, 800, 0, 700, 130, 42, template);
        row.statusText = Text(root, "Status", string.Empty, 1520, 0, 240, 130, 48, template);
        return row;
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
}
#endif
