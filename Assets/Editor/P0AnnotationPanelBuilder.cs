#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class P0AnnotationPanelBuilder
{
    private const string TemplatePath = "Assets/ArtWorks/TankMonitorPanel.prefab";
    private const string KeyboardPath = "Assets/LZW/ExternalScene/Prefabs/VR Keyboard Variant.prefab";
    private const string PrefabPath = "Assets/ArtWorks/P0AnnotationPanel.prefab";

    /// <summary>
    /// 历史区排版。原先是一个 1690×430 的文本框把所有批注拼成一段，超出直接省略号
    /// 截断；改成固定 <see cref="RowsPerPage"/> 行 + 翻页，条数再多也不会丢内容。
    /// 行高吃掉的 400px 由面板整体加高补上（1500 → <see cref="PanelHeight"/>）。
    /// </summary>
    private const int RowsPerPage = 3;
    private const float FirstRowY = -180f;
    private const float RowHeight = 240f;
    private const float RowStep = 250f;
    private const float RowWidth = 1690f;
    private const float PagingY = -940f;
    private const float PanelWidth = 1880f;
    private const float PanelHeight = 1800f;

    public static void Build()
    {
        GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePath);
        TextMeshProUGUI templateText = template?.GetComponentInChildren<TextMeshProUGUI>(true);
        Image templateImage = template?.GetComponent<Image>();
        GameObject keyboardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(KeyboardPath);
        if (templateText == null || templateImage == null || keyboardPrefab == null)
            throw new System.InvalidOperationException("P0 批注面板模板或 VR Keyboard Variant 不可用");

        var root = new GameObject("P0AnnotationPanel", typeof(RectTransform), typeof(Image));
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        Image background = root.GetComponent<Image>();
        background.sprite = templateImage.sprite;
        background.color = templateImage.color;
        P0AnnotationPanel panel = root.AddComponent<P0AnnotationPanel>();

        Text(root, "Title", "重大危险源批注", 90, -60, 1500, 90, 72, templateText);
        Text(root, "SourceBadge", "演示数据 · 保存到本机", 1240, -70, 540, 70, 42, templateText);

        panel.rows = new List<P0AnnotationRow>();
        for (int index = 0; index < RowsPerPage; index++)
            panel.rows.Add(Row(root, "History" + index, FirstRowY - index * RowStep, templateText));

        // 只有一页时面板会把这两个按钮收起来（见 P0AnnotationPanel.RefreshHistory），
        // 所以页码文本要能独立站住，不能依赖按钮占位。
        panel.prevButton = Button(root, "PrevPage", "上一页", 90, PagingY, 200, 70, templateText);
        panel.pageText = Text(root, "PageText", "暂无批注", 320, PagingY, 1140, 70, 40, templateText);
        panel.pageText.alignment = TextAlignmentOptions.Center;
        panel.nextButton = Button(root, "NextPage", "下一页", 1580, PagingY, 200, 70, templateText);

        panel.contentInput = Input(root, templateText);
        panel.characterCountText = Text(root, "CharacterCount", "0/500", 1490, -1460, 290, 60, 38, templateText);
        panel.statusText = Text(root, "Status", string.Empty, 90, -1560, 1000, 80, 38, templateText);
        panel.sendButton = Button(root, "Send", "发送", 1120, -1660, 200, 90, templateText);
        panel.editLatestButton = Button(root, "EditLatest", "编辑本人最近批注", 90, -1660, 420, 90, templateText);
        panel.cancelButton = Button(root, "Cancel", "取消", 1360, -1660, 200, 90, templateText);

        string[] phrases = { "短语1", "短语2", "短语3", "短语4", "短语5" };
        panel.phraseButtons = new List<Button>();
        for (int index = 0; index < phrases.Length; index++)
        {
            panel.phraseButtons.Add(Button(root, "Phrase" + index, phrases[index],
                90 + index % 3 * 540, -1320 - index / 3 * 100, 500, 80, templateText));
        }

        GameObject keyboard = (GameObject)PrefabUtility.InstantiatePrefab(keyboardPrefab);
        keyboard.name = "VR Keyboard Variant";
        keyboard.transform.SetParent(root.transform, false);
        // 面板加高了 300，键盘跟着往下挪同样的量，保持原来「贴在面板下沿外侧」的位置
        keyboard.transform.localPosition = new Vector3(0, -1200, 0);
        BNG.VRKeyboardPro keyboardComponent = keyboard.GetComponentInChildren<BNG.VRKeyboardPro>(true);
        BNG.VRTextInputPro adapter = panel.contentInput.gameObject.AddComponent<BNG.VRTextInputPro>();
        adapter.AttachedKeyboard = keyboardComponent;
        adapter.AttachToVRKeyboard = true;
        adapter.ActivateKeyboardOnSelect = true;
        adapter.DeactivateKeyboardOnDeselect = true;

        root.SetActive(false);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(
            Directory.GetParent(Application.dataPath).FullName, PrefabPath)));
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log($"P0AnnotationPanelBuilder: prefab generated，{RowsPerPage} 条/页");
    }

    /// <summary>一条历史批注：底色块 + 作者时间行 + 正文。</summary>
    private static P0AnnotationRow Row(GameObject parent, string name, float y, TextMeshProUGUI template)
    {
        var item = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(P0AnnotationRow));
        RectTransform rect = item.GetComponent<RectTransform>();
        rect.SetParent(parent.transform, false);
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(90, y);
        rect.sizeDelta = new Vector2(RowWidth, RowHeight);

        P0AnnotationRow row = item.GetComponent<P0AnnotationRow>();
        row.background = item.GetComponent<Image>();
        row.background.color = new Color(1f, 1f, 1f, 0.07f);
        row.background.raycastTarget = false;
        row.headerText = Text(item, "Header", string.Empty, 20, -10, RowWidth - 40, 55, 36, template);
        row.bodyText = Text(item, "Body", string.Empty, 20, -70, RowWidth - 40, 160, 36, template);
        // 正文要换行，否则长批注在一行里向右溢出看不见；超过 4 行仍然省略号收尾
        row.bodyText.enableWordWrapping = true;
        return row;
    }

    private static TMP_InputField Input(GameObject parent, TextMeshProUGUI template)
    {
        var root = new GameObject("ContentInput", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.SetParent(parent.transform, false);
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(90, -1050);
        rect.sizeDelta = new Vector2(1690, 230);
        root.GetComponent<Image>().color = new Color(0, 0, 0, 0.45f);

        TextMeshProUGUI inputText = Text(root, "Text", string.Empty, 25, -20, 1640, 190, 42, template);
        inputText.enableWordWrapping = true;
        inputText.overflowMode = TextOverflowModes.ScrollRect;
        TextMeshProUGUI placeholder = Text(root, "Placeholder", "请输入批注内容", 25, -20, 1640, 190, 42, template);
        placeholder.color = new Color(template.color.r, template.color.g, template.color.b, 0.45f);

        TMP_InputField input = root.GetComponent<TMP_InputField>();
        input.textComponent = inputText;
        input.placeholder = placeholder;
        input.lineType = TMP_InputField.LineType.MultiLineNewline;
        input.characterLimit = P0AnnotationStore.MaximumLength;
        return input;
    }

    private static Button Button(GameObject parent, string name, string label,
        float x, float y, float width, float height, TextMeshProUGUI template)
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
        TextMeshProUGUI text = Text(item, "Label", label, 0, 0, width, height, 34, template);
        text.alignment = TextAlignmentOptions.Center;
        return item.GetComponent<Button>();
    }

    private static TextMeshProUGUI Text(GameObject parent, string name, string content,
        float x, float y, float width, float height, float size, TextMeshProUGUI template)
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
