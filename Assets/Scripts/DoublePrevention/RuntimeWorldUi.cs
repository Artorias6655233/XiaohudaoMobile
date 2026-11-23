using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static class RuntimeWorldUi
{
    public static readonly Color PanelColor = new Color(0.035f, 0.075f, 0.13f, 0.96f);
    public static readonly Color HeaderColor = new Color(0.055f, 0.22f, 0.32f, 0.98f);
    public static readonly Color CardColor = new Color(0.055f, 0.13f, 0.20f, 0.92f);
    public static readonly Color CardBorderColor = new Color(0.10f, 0.55f, 0.75f, 0.34f);
    public static readonly Color PrimaryColor = new Color(0.10f, 0.55f, 0.75f, 0.95f);
    public static readonly Color SecondaryColor = new Color(0.16f, 0.24f, 0.34f, 0.92f);
    public static readonly Color AccentColor = new Color(0.85f, 0.60f, 0.18f, 0.95f);
    public static readonly Color DestructiveColor = new Color(0.58f, 0.20f, 0.20f, 0.92f);
    public static readonly Color MutedTextColor = new Color(0.62f, 0.74f, 0.82f, 1f);

    public static RectTransform Panel(Transform parent, string name, Vector2 size, Vector2 position)
    {
        var root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        root.transform.SetParent(parent, false);
        var rect = root.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        root.GetComponent<LayoutElement>().ignoreLayout = true;
        root.GetComponent<Image>().color = PanelColor;
        return rect;
    }

    public static RectTransform Card(
        Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = new Vector2(8, 8);
        rect.offsetMax = new Vector2(-8, -8);
        go.GetComponent<Image>().color = color;
        return rect;
    }

    public static TextMeshProUGUI Text(
        Transform parent, string name, string value, Vector2 anchorMin, Vector2 anchorMax,
        float fontSize = 28, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = new Vector2(16, 10);
        rect.offsetMax = new Vector2(-16, -10);
        var text = go.GetComponent<TextMeshProUGUI>();
        text.text = value ?? string.Empty;
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = alignment;
        text.enableWordWrapping = true;
        return text;
    }

    public static Button Button(
        Transform parent, string name, string label, Vector2 anchorMin, Vector2 anchorMax,
        float fontSize = 26)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = new Vector2(8, 8);
        rect.offsetMax = new Vector2(-8, -8);
        go.GetComponent<Image>().color = new Color(0.08f, 0.42f, 0.72f, 1f);
        Text(go.transform, "Label", label, Vector2.zero, Vector2.one, fontSize,
            TextAlignmentOptions.Center);
        return go.GetComponent<Button>();
    }

    public static void StyleButton(Button button, Color normalColor)
    {
        if (button == null) return;
        Image image = button.GetComponent<Image>();
        if (image != null) image.color = normalColor;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.82f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(0.78f, 0.86f, 0.92f, 1f);
        colors.disabledColor = new Color(0.45f, 0.50f, 0.56f, 0.55f);
        button.colors = colors;
    }

    public static TMP_InputField Input(
        Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
    {
        var root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
        root.transform.SetParent(parent, false);
        var rect = root.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = new Vector2(16, 10);
        rect.offsetMax = new Vector2(-16, -10);
        Image image = root.GetComponent<Image>();
        image.color = new Color(0.08f, 0.20f, 0.29f, 0.96f);

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(root.transform, false);
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(12, 8);
        viewportRect.offsetMax = new Vector2(-12, -8);

        TextMeshProUGUI text = Text(viewport.transform, "Text", string.Empty,
            Vector2.zero, Vector2.one, 26);
        text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
        text.fontSize = 30;

        TMP_InputField input = root.GetComponent<TMP_InputField>();
        input.targetGraphic = image;
        input.textViewport = viewportRect;
        input.textComponent = text;
        input.lineType = TMP_InputField.LineType.MultiLineNewline;
        input.characterLimit = DoublePreventionRules.MaximumContentLength;
        return input;
    }
}
