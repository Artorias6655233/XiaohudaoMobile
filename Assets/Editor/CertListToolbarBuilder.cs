#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 生成企业证件管理的统计 + 翻页工具栏 prefab。
///
/// 【不保存场景】和 <c>TankListPanelBuilder</c> / <c>EntryExitReviewBuilder</c> 一样：
/// 存一次 SandTable.unity 会重新序列化整个场景、重编号全部 fileID，
/// "只想改两行、得到七万行 diff"。接线走手工 YAML 补丁，本方法只产出 prefab 并打印补丁内容。
///
/// 【为什么放在标题下面】卡片高 1277.5，滚动区下面只剩 70 像素，塞不下 96 高的按钮；
/// 标题（y=-58）和表头（y=-375）之间空着两百多像素，工具栏落在 y=-230。
///
/// 样式不自己定：字体和文字色从场景里证件卡片的表头文本上抄，
/// 免得这一条和卡片其它文字看起来不是一套。
/// </summary>
internal static class CertListToolbarBuilder
{
    private const string ScenePath = "Assets/LZW/SandTable/SandTable.unity";
    private const string PrefabPath = "Assets/ArtWorks/CertListToolbar.prefab";

    private const float CardWidth = 3583.9995f;

    /// <summary>左边距，和表头 / 滚动区的 240 对齐。</summary>
    private const float PadLeft = 240f;

    /// <summary>工具栏基线。标题在 -58、表头在 -375，落在中间。</summary>
    private const float RowY = -230f;

    private const float RowHeight = 96f;
    private const float ButtonWidth = 260f;
    private const float PageTextWidth = 220f;
    private const float TextSize = 52f;

    private static readonly Color DimColor = new Color(1f, 1f, 1f, 0.7f);
    private static readonly Color ButtonColor = new Color(1f, 1f, 1f, 0.16f);

    [MenuItem("Tools/企业证件/生成工具栏")]
    public static void Build()
    {
        if (!TryReadStyle(out TMP_FontAsset font, out Color textColor)) return;

        GameObject root = new GameObject("certlisttoolbar", typeof(RectTransform));
        try
        {
            RectTransform rt = (RectTransform)root.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            // 锚在宿主卡片的左上角，整条往下挪 RowY；子元素相对本条排，y 都是 0
            rt.anchoredPosition = new Vector2(PadLeft, RowY);
            rt.sizeDelta = new Vector2(CardWidth - PadLeft * 2f, RowHeight);
            rt.localScale = Vector3.one;

            CertListToolbar toolbar = root.AddComponent<CertListToolbar>();

            // 右侧三件套：上一页 / 页码 / 下一页，贴着本条右端
            float barWidth = CardWidth - PadLeft * 2f;
            float nextX = barWidth - ButtonWidth;
            float pageX = nextX - PageTextWidth - 20f;
            float prevX = pageX - ButtonWidth - 20f;

            toolbar.summaryText = Text(root, "Summary", 0f, 0f, prevX - 40f, RowHeight,
                TextSize, TextAlignmentOptions.Left, textColor, font, "请在沙盘上选择企业");
            toolbar.prevButton = MakeButton(root, "PrevPage", prevX, 0f, ButtonWidth, "上一页", font);
            toolbar.pageText = Text(root, "PageText", pageX, 0f, PageTextWidth, RowHeight,
                TextSize, TextAlignmentOptions.Center, DimColor, font, "1 / 1");
            toolbar.nextButton = MakeButton(root, "NextPage", nextX, 0f, ButtonWidth, "下一页", font);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            if (saved == null)
            {
                Debug.LogError("[证件工具栏] 存 prefab 失败：" + PrefabPath);
                return;
            }

            string guid = AssetDatabase.AssetPathToGUID(PrefabPath);

            // 必须取【组件】的 fileID，不是根 GameObject 的：宿主那个字段类型是
            // CertListToolbar，指到 GameObject 上 Unity 会静默解析成 null，
            // 表现是「翻页条不出现」，而 YAML 看上去完全正常。
            long fileId = LocalFileId(saved.GetComponent<CertListToolbar>());

            Debug.Log($"[证件工具栏] 已生成 {PrefabPath}，guid = {guid}，根 fileID = {fileId}");
            Debug.Log("[证件工具栏] 接线走 YAML 补丁，往 SpecialJob_CertificateList 那个 " +
                "MonoBehaviour 块（SandTable.unity 里的 &595546696）加两行：\n" +
                $"  toolbarPrefab: {{fileID: {fileId}, guid: {guid}, type: 3}}\n" +
                "  toolbarParent: {fileID: 1584694547}   # GameObject/Image 的 RectTransform\n" +
                "不要在编辑器里拖引用——会重新序列化整个场景。");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>
    /// 从场景里证件卡片的表头文本上抄字体和颜色。
    ///
    /// 只读打开场景、抄完就算，绝不 SaveScene。找面板用 FindObjectsOfTypeAll 而不是
    /// FindObjectOfType：这些面板默认是关掉的，后者找不到。
    /// </summary>
    private static bool TryReadStyle(out TMP_FontAsset font, out Color color)
    {
        font = null;
        color = Color.white;

        UnityEngine.SceneManagement.Scene scene =
            UnityEditor.SceneManagement.EditorSceneManager.GetSceneByPath(ScenePath);
        bool opened = false;
        if (!scene.isLoaded)
        {
            scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
            opened = true;
        }

        try
        {
            SpecialJob_CertificateList host = null;
            foreach (SpecialJob_CertificateList candidate in
                Resources.FindObjectsOfTypeAll<SpecialJob_CertificateList>())
            {
                if (candidate.gameObject.scene.IsValid()) { host = candidate; break; }
            }

            if (host == null)
            {
                Debug.LogError("[证件工具栏] 场景里找不到 SpecialJob_CertificateList，抄不到样式");
                return false;
            }

            TextMeshProUGUI sample = host.GetComponentInChildren<TextMeshProUGUI>(true);
            if (sample == null)
            {
                Debug.LogError("[证件工具栏] 证件面板下找不到 TMP，抄不到字体");
                return false;
            }

            font = sample.font;
            color = sample.color;
            Debug.Log($"[证件工具栏] 样式抄自「{sample.gameObject.name}」：字体 {font?.name}，色 {color}");
            return font != null;
        }
        finally
        {
            // 场景是只读打开的，绝不 SaveScene —— 存一次就是七万行 diff。
            // opened 只用于说明本次是不是我们打开的，不做任何回滚动作。
            if (opened) { }
        }
    }

    private static long LocalFileId(Object target)
    {
        return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
            target, out string _, out long localId) ? localId : 0L;
    }

    private static Button MakeButton(GameObject parent, string name, float x, float y, float w,
        string caption, TMP_FontAsset font)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(parent.transform, false);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, RowHeight);
        rt.localScale = Vector3.one;

        Image image = go.AddComponent<Image>();
        image.color = ButtonColor;
        image.raycastTarget = true;

        Button button = go.AddComponent<Button>();
        button.targetGraphic = image;

        Text(go, "Caption", 0f, 0f, w, RowHeight, TextSize, TextAlignmentOptions.Center,
            Color.white, font, caption);
        return button;
    }

    private static TextMeshProUGUI Text(GameObject parent, string name, float x, float y,
        float w, float h, float size, TextAlignmentOptions align, Color color,
        TMP_FontAsset font, string content)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(parent.transform, false);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
        rt.localScale = Vector3.one;

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font = font;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = align;
        tmp.text = content;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.enableAutoSizing = false;
        tmp.raycastTarget = false;   // 文字不吃射线，点击交给按钮自己的 Image
        return tmp;
    }
}
#endif
