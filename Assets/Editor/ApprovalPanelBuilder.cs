#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 生成作业票审批卡片 prefab。
///
/// 【不保存场景】和 <c>CertListToolbarBuilder</c> / <c>TankListPanelBuilder</c> 同一条规矩：
/// 存一次 SandTable.unity 会重编号全部 fileID，「只想改两行、得到七万行 diff」。
/// 本方法只产出 prefab 并把 YAML 补丁内容打出来，接线手工打补丁。
///
/// 【位置由宿主定，不由这里定】prefab 里的 CardLeft/CardTop 只是 Editor 里摆得下的位置。
/// 票面已经从右侧副屏搬到正前方顶替作业票列表，卡片跟着盖在票面上当模态用 ——
/// 实际坐标在 <c>JobTicketDetailPanel.EnsureApprovalPanel</c> 里实例化时重设。
///
/// 样式不自己定：字体从票面单元格的 TMP 上抄（字号 50 是票面的基准），
/// 免得审批卡片看起来不是这套 UI 的一部分。
/// </summary>
internal static class ApprovalPanelBuilder
{
    private const string ScenePath = "Assets/LZW/SandTable/SandTable.unity";
    private const string PrefabPath = "Assets/ArtWorks/ApprovalPanel.prefab";

    // 卡片在 Report 局部空间里的位置。票面内容 x 到 4189 为止，票面右边缘 5681。
    private const float CardLeft = 4260f;
    private const float CardTop = 1600f;
    private const float CardWidth = 1350f;
    private const float CardHeight = 1800f;

    private const float Pad = 40f;
    private const float RowHeight = 110f;
    private const float RowStep = 125f;
    private const float BodySize = 44f;
    private const float TitleSize = 60f;
    private const float LabelSize = 40f;

    /// <summary>短语槽位数，与 <see cref="PortalApprovalPhraseConfig.MaxPhrases"/> 对齐。</summary>
    private const int PhraseSlots = 6;

    /// <summary>签名槽位数，2 x 2。多于这个数的签名清单会显示不全，自检会盯住。</summary>
    private const int SignatureSlots = 4;

    private static readonly Color CardColor = new Color(0.05f, 0.09f, 0.16f, 0.94f);
    private static readonly Color ButtonColor = new Color(1f, 1f, 1f, 0.16f);
    private static readonly Color DimColor = new Color(1f, 1f, 1f, 0.65f);
    private static readonly Color AccentColor = new Color(0.76862746f, 0.84313726f, 1f, 1f);

    [MenuItem("Tools/特殊作业/生成审批卡片")]
    public static void Build()
    {
        if (!TryReadStyle(out TMP_FontAsset font)) return;

        GameObject root = new GameObject("approvalpanel", typeof(RectTransform));
        try
        {
            RectTransform rt = (RectTransform)root.transform;
            // 宿主 Report 的 rect 是个零尺寸的点，两个审批按钮也是这么挂的：
            // 锚点固定在那个点上，anchoredPosition 就是局部坐标。
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(CardLeft, CardTop);
            rt.sizeDelta = new Vector2(CardWidth, CardHeight);
            rt.localScale = Vector3.one;

            Image background = root.AddComponent<Image>();
            background.color = CardColor;
            background.raycastTarget = true;   // 吃掉射线，免得穿过卡片点到后面的企业图标

            ApprovalPanel panel = root.AddComponent<ApprovalPanel>();

            float inner = CardWidth - Pad * 2f;
            float y = -30f;

            panel.titleText = Text(root, "Title", Pad, y, inner, 80f, TitleSize,
                TextAlignmentOptions.Left, Color.white, font, "审批");
            y -= 85f;

            panel.conclusionText = Text(root, "Conclusion", Pad, y, inner, 70f, BodySize + 6f,
                TextAlignmentOptions.Left, AccentColor, font, "结论：通过");
            y -= 80f;

            Text(root, "PhraseTitle", Pad, y, inner, 60f, LabelSize,
                TextAlignmentOptions.Left, DimColor, font, "审批意见（必选）");
            y -= 70f;

            List<Button> phraseButtons = new List<Button>();
            List<TextMeshProUGUI> phraseLabels = new List<TextMeshProUGUI>();
            for (int i = 0; i < PhraseSlots; i++)
            {
                Button b = MakeButton(root, "Phrase" + i, Pad, y, inner, RowHeight,
                    string.Empty, font, out TextMeshProUGUI caption);
                phraseButtons.Add(b);
                phraseLabels.Add(caption);
                y -= RowStep;
            }

            y -= 15f;
            Text(root, "SignatureTitle", Pad, y, inner, 60f, LabelSize,
                TextAlignmentOptions.Left, DimColor, font, "电子签名（本地留痕，不上传门户）");
            y -= 70f;

            List<Button> signatureButtons = new List<Button>();
            List<RawImage> signatureImages = new List<RawImage>();
            List<TextMeshProUGUI> signatureLabels = new List<TextMeshProUGUI>();
            float half = (inner - 20f) / 2f;
            for (int i = 0; i < SignatureSlots; i++)
            {
                float x = Pad + (i % 2) * (half + 20f);
                float rowY = y - (i / 2) * 180f;
                Button b = MakeSignature(root, "Signature" + i, x, rowY, half, 170f, font,
                    out RawImage preview, out TextMeshProUGUI caption);
                signatureButtons.Add(b);
                signatureImages.Add(preview);
                signatureLabels.Add(caption);
            }

            y -= 180f * (SignatureSlots / 2) + 15f;

            panel.statusText = Text(root, "Status", Pad, y, inner, 120f, LabelSize,
                TextAlignmentOptions.TopLeft, DimColor, font, "请选择审批意见和签名", wrap: true);
            y -= 130f;

            panel.submitButton = MakeButton(root, "Submit", Pad, y, half, RowHeight,
                "确认提交", font, out TextMeshProUGUI _);
            panel.backButton = MakeButton(root, "Back", Pad + half + 20f, y, half, RowHeight,
                "返回", font, out TextMeshProUGUI _2);

            panel.phraseButtons = phraseButtons.ToArray();
            panel.phraseLabels = phraseLabels.ToArray();
            panel.signatureButtons = signatureButtons.ToArray();
            panel.signatureImages = signatureImages.ToArray();
            panel.signatureLabels = signatureLabels.ToArray();

            float used = -y + RowHeight + 30f;
            if (used > CardHeight)
            {
                Debug.LogWarning($"[审批卡片] 内容高 {used:F0} 超出卡片 {CardHeight}，底部会被裁掉");
            }

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            if (saved == null)
            {
                Debug.LogError("[审批卡片] 存 prefab 失败：" + PrefabPath);
                return;
            }

            string guid = AssetDatabase.AssetPathToGUID(PrefabPath);

            // 必须取【组件】的 fileID，不是根 GameObject 的：宿主字段类型是 ApprovalPanel，
            // 指到 GameObject 上 Unity 会静默解析成 null，表现是「卡片不出现」而 YAML 看着没问题。
            long fileId = LocalFileId(saved.GetComponent<ApprovalPanel>());

            Debug.Log($"[审批卡片] 已生成 {PrefabPath}，guid = {guid}，组件 fileID = {fileId}");
            Debug.Log("[审批卡片] 接线走 YAML 补丁，往 JobTicketDetailPanel 那个 MonoBehaviour 块" +
                "（SandTable.unity 里的 &1147969362）加两行：\n" +
                $"  approvalPrefab: {{fileID: {fileId}, guid: {guid}, type: 3}}\n" +
                "  approvalParent: {fileID: 1147969361}   # Report 的 RectTransform\n" +
                "不要在编辑器里拖引用——会重新序列化整个场景。");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>从票面单元格的 TMP 上抄字体。只读打开场景，绝不 SaveScene。</summary>
    private static bool TryReadStyle(out TMP_FontAsset font)
    {
        font = null;

        UnityEngine.SceneManagement.Scene scene =
            UnityEditor.SceneManagement.EditorSceneManager.GetSceneByPath(ScenePath);
        if (!scene.isLoaded)
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
        }

        // 面板默认是关掉的，FindObjectOfType 找不到，必须用 FindObjectsOfTypeAll
        JobTicketDetailPanel host = null;
        foreach (JobTicketDetailPanel candidate in Resources.FindObjectsOfTypeAll<JobTicketDetailPanel>())
        {
            if (candidate.gameObject.scene.IsValid()) { host = candidate; break; }
        }

        if (host == null)
        {
            Debug.LogError("[审批卡片] 场景里找不到 JobTicketDetailPanel，抄不到样式");
            return false;
        }

        TextMeshProUGUI sample = host.GetComponentInChildren<TextMeshProUGUI>(true);
        if (sample == null)
        {
            Debug.LogError("[审批卡片] 票面下找不到 TMP，抄不到字体");
            return false;
        }

        font = sample.font;
        Debug.Log($"[审批卡片] 字体抄自「{sample.gameObject.name}」：{font?.name}");
        return font != null;
    }

    private static long LocalFileId(Object target)
    {
        return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
            target, out string _, out long localId) ? localId : 0L;
    }

    private static Button MakeButton(GameObject parent, string name, float x, float y, float w,
        float h, string caption, TMP_FontAsset font, out TextMeshProUGUI captionText)
    {
        GameObject go = Rect(parent, name, x, y, w, h);

        Image image = go.AddComponent<Image>();
        image.color = ButtonColor;
        image.raycastTarget = true;

        Button button = go.AddComponent<Button>();
        button.targetGraphic = image;

        captionText = Text(go, "Caption", 20f, 0f, w - 40f, h, BodySize,
            TextAlignmentOptions.Left, Color.white, font, caption);
        return button;
    }

    /// <summary>签名槽：左边一张缩略图，右边一行说明。</summary>
    private static Button MakeSignature(GameObject parent, string name, float x, float y,
        float w, float h, TMP_FontAsset font, out RawImage preview, out TextMeshProUGUI caption)
    {
        GameObject go = Rect(parent, name, x, y, w, h);

        Image image = go.AddComponent<Image>();
        image.color = ButtonColor;
        image.raycastTarget = true;

        Button button = go.AddComponent<Button>();
        button.targetGraphic = image;

        // 签名图是 512x256（2:1，必须是 2 的幂，否则 Unity 会缩到最近的 POT 把字拉变形），
        // 缩略图按同样的比例摆，不然预览和真图看起来不是一个东西。
        GameObject previewGo = Rect(go, "Preview", 16f, -20f, 260f, 130f);
        preview = previewGo.AddComponent<RawImage>();
        preview.raycastTarget = false;

        caption = Text(go, "Caption", 290f, 0f, w - 300f, h, LabelSize,
            TextAlignmentOptions.Left, Color.white, font, string.Empty);
        return button;
    }

    private static GameObject Rect(GameObject parent, string name, float x, float y,
        float w, float h)
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
        return go;
    }

    private static TextMeshProUGUI Text(GameObject parent, string name, float x, float y,
        float w, float h, float size, TextAlignmentOptions align, Color color,
        TMP_FontAsset font, string content, bool wrap = false)
    {
        GameObject go = Rect(parent, name, x, y, w, h);

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font = font;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = align;
        tmp.text = content;
        tmp.enableWordWrapping = wrap;
        tmp.overflowMode = wrap ? TextOverflowModes.Truncate : TextOverflowModes.Ellipsis;
        tmp.enableAutoSizing = false;
        tmp.raycastTarget = false;   // 文字不吃射线，点击交给按钮自己的 Image
        return tmp;
    }
}
#endif
