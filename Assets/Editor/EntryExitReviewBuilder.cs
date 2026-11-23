#if UNITY_EDITOR
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 生成出入园审核相关的两个 prefab 的一次性编辑器工具：
///   Assets/ArtWorks/EntryExitActionCell.prefab   行最右侧的「操作」列（审核 / 备注）
///   Assets/ArtWorks/EntryExitReviewCard.prefab   并排浮在面板右侧的审核卡片
///
/// 做法完全照 <see cref="TankMonitorPanelBuilder"/>：规整的表格/按钮阵列手摆太慢也容易漏接
/// 引用，用代码生成一次，产出的是普通 prefab 资产，之后照常在 Inspector 里改。
///
/// 【本脚本绝不保存场景】。Unity 保存 SandTable.unity 会重新序列化整个场景并重编号所有
/// fileID（实测只想改两行，得到 7 万行 diff）。所以接线走手工 YAML 补丁，见 Build() 末尾的日志。
///
/// 样式不自己定：字体和文字颜色从场景现有元素抄，卡片底图沿用罐区监测卡片那张干净角框。
/// </summary>
internal static class EntryExitReviewBuilder
{
    private const string ScenePath = "Assets/LZW/SandTable/SandTable.unity";
    private const string CellPrefabPath = "Assets/ArtWorks/EntryExitActionCell.prefab";
    private const string CardPrefabPath = "Assets/ArtWorks/EntryExitReviewCard.prefab";

    /// <summary>
    /// 卡片底图：四个角标、框内干净的通用框，和罐区监测卡片用的是同一张。
    /// 原生 4715 x 4231（宽高比 1.1144），spriteBorder 全 0 不能九宫格，
    /// 所以卡片必须按原生比例做，否则 Simple 拉伸会把四个角标拉扁。
    /// </summary>
    private const string BackgroundSpritePath =
        "Assets/ArtWorks/UI/双重预防机制-整改督办_slices/Group 1321316901@2x.png";

    // --- 卡片尺寸与栅格（单位是卡片本地像素，宿主 Canvas 会整体缩放到世界空间） ---
    private const float CardWidth = 1881.5f;
    private const float CardHeight = 1688f;

    // 角标臂长原生 158px，卡片缩放约 0.399 → 约 63 单位，留 90 的内边距足够避开
    private const float Pad = 90f;
    private const float RightEdge = CardWidth - Pad;   // 1791.5

    private const float TitleSize = 76f;
    private const float SectionSize = 52f;
    private const float LabelSize = 48f;
    private const float ValueSize = 52f;
    private const float ActionSize = 56f;
    private const float ChipSize = 40f;
    private const float FootSize = 44f;

    // 摘要区：5 行 label + value
    private const int SummaryRows = 5;
    private const float SummaryTop = -200f;
    private const float SummaryStep = 90f;
    private const float SummaryLabelW = 280f;
    private const float SummaryValueX = 390f;
    private const float SummaryValueW = RightEdge - SummaryValueX;   // 1401.5

    // 审核区：三个大按钮
    private const float ActionY = -760f;
    private const float ActionH = 130f;
    private const float ActionW = 520f;
    private const float ActionStep = 560f;

    // 备注区：短语网格最多 4 列 x 3 行（上限见 EntryExitPhraseConfig.MaxPhrases）。
    // 按钮是运行时按模板克隆的，这里只定网格原点和步长。
    private const float ChipAreaY = -1010f;
    private const float ChipAreaW = 1710f;
    private const float ChipAreaH = 290f;
    private const float ChipRowStep = 100f;
    private const float ChipH = 84f;
    private const float ChipW = 400f;
    private const float ChipColStep = 430f;
    private const int ChipCols = 4;

    // 备注全文只读展示 + 清空。没有自由输入：系统 IME 进不了 XR 合成链，
    // 详见 EntryExitReviewCard 的类注释。
    private const float RemarkY = -1320f;
    private const float RemarkH = 100f;
    private const float RemarkW = 1480f;
    private const float ClearX = 1590f;
    private const float ClearW = 200f;

    private const float FooterY = -1480f;

    // --- 操作列（行内） ---
    private const float CellHeight = 100f;
    private const float CellButtonW = 130f;
    private const float CellButtonH = 76f;
    private const float CellButtonGap = 20f;
    private const float CellLabelSize = 38f;

    private static readonly Color DimColor = new Color(1f, 1f, 1f, 0.7f);
    private static readonly Color NoticeColor = new Color(1f, 0.72f, 0.25f, 1f);
    private static readonly Color IdleColor = new Color(0.18f, 0.26f, 0.38f, 0.75f);

    [MenuItem("Tools/出入园审核/生成 prefab")]
    public static void Build()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        ClosedPark_EntryExitList host = FindHost();
        if (host == null)
        {
            Debug.LogError("[出入园审核] 场景里找不到 ClosedPark_EntryExitList");
            return;
        }

        // 字体和文字颜色照抄表头上已有的 TMP，保证和列表一套观感
        TMP_FontAsset font = null;
        Color textColor = Color.white;

        TextMeshProUGUI sample = host.GetComponentInChildren<TextMeshProUGUI>(true);
        if (sample != null)
        {
            font = sample.font;
            textColor = sample.color;
        }

        if (font == null)
        {
            Debug.LogError("[出入园审核] 面板下找不到带字体的 TextMeshProUGUI，抄不到字体");
            return;
        }

        Sprite bg = AssetDatabase.LoadAssetAtPath<Sprite>(BackgroundSpritePath);
        if (bg == null)
        {
            Debug.LogError($"[出入园审核] 底图加载失败：{BackgroundSpritePath}");
            return;
        }

        Debug.Log($"[出入园审核] 字体={font.name}，文字色={textColor}，底图={bg.name}（{bg.rect.width}x{bg.rect.height}）");

        System.IO.Directory.CreateDirectory(
            System.IO.Path.GetDirectoryName(FullPath(CardPrefabPath)));

        // --- 先建到场景里，存成 prefab 后再删掉临时实例 ---
        GameObject cellRoot = BuildCell(host.transform, font, textColor);
        GameObject cellPrefab = PrefabUtility.SaveAsPrefabAsset(cellRoot, CellPrefabPath);
        Object.DestroyImmediate(cellRoot);

        GameObject cardRoot = BuildCard(host.transform, bg, font, textColor);
        GameObject cardPrefab = PrefabUtility.SaveAsPrefabAsset(cardRoot, CardPrefabPath);
        Object.DestroyImmediate(cardRoot);

        if (cellPrefab == null || cardPrefab == null)
        {
            Debug.LogError("[出入园审核] 存 prefab 失败");
            return;
        }

        AssetDatabase.SaveAssets();

        // 这里【刻意不接线、也不保存场景】：保存 SandTable.unity 会重编号全场景 fileID。
        // 接线走手工 YAML 补丁，在 ClosedPark_EntryExitList 那个 MonoBehaviour 块里加两行。
        Debug.Log(
            $"[出入园审核] 已生成：\n  {CellPrefabPath}  guid={AssetDatabase.AssetPathToGUID(CellPrefabPath)}\n" +
            $"  {CardPrefabPath}  guid={AssetDatabase.AssetPathToGUID(CardPrefabPath)}\n" +
            "接线请走 YAML 补丁（不要在编辑器里保存场景）：在 ClosedPark_EntryExitList 的 MonoBehaviour 块末尾加\n" +
            "  actionCellPrefab: {fileID: <cell prefab 根 GameObject 的 fileID>, guid: <cell guid>, type: 3}\n" +
            "  reviewCardPrefab: {fileID: <card prefab 根 GameObject 的 fileID>, guid: <card guid>, type: 3}\n" +
            "fileID 取 prefab 文件里 m_Father 为 0 的那个 RectTransform 对应的 GameObject。");
    }

    // ------------------------------------------------------------------ 操作列

    private static GameObject BuildCell(Transform parent, TMP_FontAsset font, Color textColor)
    {
        GameObject root = NewRect("entryexitactioncell", parent, 0f, 0f,
            EntryExitActionCell.ColumnWidth, CellHeight);

        EntryExitActionCell cell = root.AddComponent<EntryExitActionCell>();

        float y = -(CellHeight - CellButtonH) * 0.5f;

        Image reviewImage;
        TextMeshProUGUI reviewLabel;
        cell.reviewButton = Chip(root, "ReviewButton", 0f, y, CellButtonW, CellButtonH,
            "审核", CellLabelSize, textColor, font, cell.pendingColor, out reviewImage, out reviewLabel);
        cell.reviewImage = reviewImage;
        cell.reviewLabel = reviewLabel;

        Image remarkImage;
        TextMeshProUGUI remarkLabel;
        cell.remarkButton = Chip(root, "RemarkButton", CellButtonW + CellButtonGap, y, CellButtonW, CellButtonH,
            "备注", CellLabelSize, textColor, font, cell.remarkIdleColor, out remarkImage, out remarkLabel);
        cell.remarkImage = remarkImage;
        cell.remarkLabel = remarkLabel;

        // 操作列跟着行一起显示，prefab 根保持 active
        return root;
    }

    // ------------------------------------------------------------------ 审核卡片

    private static GameObject BuildCard(Transform parent, Sprite bg, TMP_FontAsset font, Color textColor)
    {
        GameObject root = NewRect("entryexitreviewcard", parent, 0f, 0f, CardWidth, CardHeight);

        Image image = root.AddComponent<Image>();
        image.sprite = bg;
        image.type = Image.Type.Simple;
        image.color = Color.white;
        image.raycastTarget = false;   // 底图不吃射线，避免挡住后面的按钮

        EntryExitReviewCard card = root.AddComponent<EntryExitReviewCard>();

        // --- 标题 + 关闭 ---
        card.titleText = Text(root, "Title", Pad, -70f, 1000f, 96f,
            TitleSize, TextAlignmentOptions.Left, textColor, font, "出入园审核");

        Image closeImage;
        TextMeshProUGUI closeLabel;
        card.closeButton = Chip(root, "CloseButton", RightEdge - 200f, -60f, 200f, 90f,
            "关闭", LabelSize, textColor, font, IdleColor, out closeImage, out closeLabel);

        // --- 摘要 ---
        card.summaryLabels = new TextMeshProUGUI[SummaryRows];
        card.summaryValues = new TextMeshProUGUI[SummaryRows];
        for (int i = 0; i < SummaryRows; i++)
        {
            float y = SummaryTop - i * SummaryStep;
            card.summaryLabels[i] = Text(root, $"SummaryLabel{i}", Pad, y, SummaryLabelW, 70f,
                LabelSize, TextAlignmentOptions.Left, DimColor, font, "-");
            card.summaryValues[i] = Text(root, $"SummaryValue{i}", SummaryValueX, y, SummaryValueW, 70f,
                ValueSize, TextAlignmentOptions.Left, textColor, font, "-");
        }

        // --- 审核结论 ---
        Text(root, "ActionSection", Pad, -680f, 500f, 64f,
            SectionSize, TextAlignmentOptions.Left, DimColor, font, "审核结论");

        Image approveImage, rejectImage, revokeImage;
        TextMeshProUGUI approveLabel, rejectLabel, revokeLabel;

        card.approveButton = Chip(root, "ApproveButton", Pad, ActionY, ActionW, ActionH,
            "通过", ActionSize, textColor, font, IdleColor, out approveImage, out approveLabel);
        card.approveImage = approveImage;

        card.rejectButton = Chip(root, "RejectButton", Pad + ActionStep, ActionY, ActionW, ActionH,
            "驳回", ActionSize, textColor, font, IdleColor, out rejectImage, out rejectLabel);
        card.rejectImage = rejectImage;

        card.revokeButton = Chip(root, "RevokeButton", Pad + ActionStep * 2f, ActionY, ActionW, ActionH,
            "撤销", ActionSize, textColor, font, IdleColor, out revokeImage, out revokeLabel);
        card.revokeImage = revokeImage;

        // --- 备注 ---
        Text(root, "RemarkSection", Pad, -940f, 500f, 64f,
            SectionSize, TextAlignmentOptions.Left, DimColor, font, "常用短语");

        // 高亮框要压在短语和备注全文【下面】，所以先建。平时几乎透明，从行内「备注」进来时闪两下。
        GameObject frame = NewRect("RemarkFocusFrame", root.transform, Pad - 20f, -950f, ChipAreaW, 470f);
        card.remarkFocusFrame = frame.AddComponent<Image>();
        card.remarkFocusFrame.color = new Color(0.85f, 0.60f, 0.18f, 0.05f);
        card.remarkFocusFrame.raycastTarget = false;

        // 短语按钮的数量来自配置文件，prefab 里只放一个 inactive 模板，运行时克隆
        GameObject area = NewRect("ChipArea", root.transform, Pad, ChipAreaY, ChipAreaW, ChipAreaH);
        card.chipArea = (RectTransform)area.transform;
        card.chipColumns = ChipCols;
        card.chipColStep = ChipColStep;
        card.chipRowStep = ChipRowStep;

        Image chipImage;
        TextMeshProUGUI chipLabel;
        card.chipTemplate = Chip(area, "ChipTemplate", 0f, 0f, ChipW, ChipH,
            "短语", ChipSize, textColor, font, card.chipIdleColor, out chipImage, out chipLabel);
        card.chipTemplate.gameObject.SetActive(false);

        card.remarkValueText = Text(root, "RemarkValue", Pad, RemarkY, RemarkW, RemarkH,
            ChipSize, TextAlignmentOptions.TopLeft, textColor, font, "未填写备注");
        // 备注是多条短语拼起来的，一行放不下，这里要换行而不是打省略号
        card.remarkValueText.enableWordWrapping = true;
        card.remarkValueText.overflowMode = TextOverflowModes.Truncate;

        Image clearImage;
        TextMeshProUGUI clearLabel;
        card.remarkClearButton = Chip(root, "ClearButton", ClearX, RemarkY, ClearW, RemarkH,
            "清空", LabelSize, textColor, font, IdleColor, out clearImage, out clearLabel);

        // --- 底部 ---
        card.footerText = Text(root, "Footer", Pad, FooterY, 1000f, 60f,
            FootSize, TextAlignmentOptions.Left, DimColor, font, "本机记录");

        card.noticeText = Text(root, "Notice", 1100f, FooterY, RightEdge - 1100f, 60f,
            FootSize, TextAlignmentOptions.Right, NoticeColor, font, "仅存本机，不回传生产环境");

        // prefab 根存成 inactive：Instantiate 出来先是隐藏的，等 ShowCar/ShowPerson 再打开，
        // 免得闪一帧空卡片，也免得它在关着的时候就把面板的 ContentSizeFitter 撑开。
        root.SetActive(false);
        return root;
    }

    // ------------------------------------------------------------------ 零件

    private static GameObject NewRect(string name, Transform parent, float x, float y, float w, float h)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
        rt.localScale = Vector3.one;
        return go;
    }

    /// <summary>
    /// 一个「色块 + 居中文字」的按钮。
    ///
    /// 用 ColorTint 而不是场景里那些按钮的 SpriteSwap：SpriteSwap 要成对切图，
    /// 这些新按钮没有对应美术资源。ColorTint 的高亮值给到 1.4，射线扫过时亮得足够明显。
    /// 状态色由运行时代码写 <see cref="Image.color"/>，和这里的 tint 是相乘关系，互不冲突。
    /// </summary>
    private static Button Chip(GameObject parent, string name, float x, float y, float w, float h,
        string content, float size, Color textColor, TMP_FontAsset font, Color fill,
        out Image image, out TextMeshProUGUI label)
    {
        GameObject go = NewRect(name, parent.transform, x, y, w, h);

        image = go.AddComponent<Image>();
        image.color = fill;
        image.raycastTarget = true;

        Button button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;

        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.4f, 1.4f, 1.4f, 1f);
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        colors.selectedColor = Color.white;
        colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        Navigation nav = button.navigation;
        nav.mode = Navigation.Mode.None;
        button.navigation = nav;

        label = Text(go, "Label", 0f, 0f, w, h, size, TextAlignmentOptions.Center, textColor, font, content);
        label.raycastTarget = false;   // 文字挡住射线的话按钮就点不到了

        return button;
    }

    private static TextMeshProUGUI Text(GameObject parent, string name, float x, float y,
        float w, float h, float size, TextAlignmentOptions align, Color color,
        TMP_FontAsset font, string content)
    {
        GameObject go = NewRect(name, parent.transform, x, y, w, h);

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font = font;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = align;
        tmp.text = content;

        // 列宽固定：不换行、超出打省略号，也不要 ContentSizeFitter
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.enableAutoSizing = false;
        tmp.raycastTarget = false;

        return tmp;
    }

    private static ClosedPark_EntryExitList FindHost()
    {
        // 面板在场景里默认关闭，FindObjectOfType 找不到，得连隐藏的一起找
        foreach (ClosedPark_EntryExitList p in Resources.FindObjectsOfTypeAll<ClosedPark_EntryExitList>())
        {
            if (p.gameObject.scene.IsValid()) return p;
        }

        return null;
    }

    private static string FullPath(string assetPath)
    {
        return System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(Application.dataPath), assetPath);
    }

    // ------------------------------------------------------------------ 只读校验

    /// <summary>只读校验：场景接线、两个 prefab 上的引用是否都接齐了。不保存任何文件。</summary>
    [MenuItem("Tools/出入园审核/校验接线（只读）")]
    public static void Verify()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("===== 出入园审核 校验 =====");

        ClosedPark_EntryExitList host = FindHost();
        sb.AppendLine($"host: {(host != null ? "OK" : "找不到")}");
        if (host == null) { Debug.LogError(sb.ToString()); return; }

        int missing = 0;
        missing += Check(sb, "host.actionCellPrefab", host.actionCellPrefab);
        missing += Check(sb, "host.reviewCardPrefab", host.reviewCardPrefab);
        missing += Check(sb, "host.carLabel", host.carLabel);
        missing += Check(sb, "host.personLabel", host.personLabel);

        if (host.actionCellPrefab != null)
        {
            EntryExitActionCell cell = host.actionCellPrefab;
            sb.AppendLine($"cell 路径: {AssetDatabase.GetAssetPath(cell)}");
            missing += Check(sb, "  cell.reviewButton", cell.reviewButton);
            missing += Check(sb, "  cell.reviewImage", cell.reviewImage);
            missing += Check(sb, "  cell.reviewLabel", cell.reviewLabel);
            missing += Check(sb, "  cell.remarkButton", cell.remarkButton);
            missing += Check(sb, "  cell.remarkImage", cell.remarkImage);
            missing += Check(sb, "  cell.remarkLabel", cell.remarkLabel);
            missing += CheckFonts(sb, "cell", cell.gameObject);
        }

        if (host.reviewCardPrefab != null)
        {
            EntryExitReviewCard card = host.reviewCardPrefab;
            sb.AppendLine($"card 路径: {AssetDatabase.GetAssetPath(card)}  activeSelf={card.gameObject.activeSelf}");
            missing += Check(sb, "  card.titleText", card.titleText);
            missing += Check(sb, "  card.approveButton", card.approveButton);
            missing += Check(sb, "  card.rejectButton", card.rejectButton);
            missing += Check(sb, "  card.revokeButton", card.revokeButton);
            missing += Check(sb, "  card.approveImage", card.approveImage);
            missing += Check(sb, "  card.rejectImage", card.rejectImage);
            missing += Check(sb, "  card.revokeImage", card.revokeImage);
            missing += Check(sb, "  card.chipTemplate", card.chipTemplate);
            missing += Check(sb, "  card.chipArea", card.chipArea);
            missing += Check(sb, "  card.remarkValueText", card.remarkValueText);
            missing += Check(sb, "  card.remarkClearButton", card.remarkClearButton);
            missing += Check(sb, "  card.remarkFocusFrame", card.remarkFocusFrame);
            missing += Check(sb, "  card.footerText", card.footerText);
            missing += Check(sb, "  card.noticeText", card.noticeText);
            missing += Check(sb, "  card.closeButton", card.closeButton);
            missing += CheckArray(sb, "  card.summaryLabels", card.summaryLabels, SummaryRows);
            missing += CheckArray(sb, "  card.summaryValues", card.summaryValues, SummaryRows);
            missing += CheckFonts(sb, "card", card.gameObject);
        }

        sb.AppendLine(missing == 0 ? "== 全部引用已接齐 ==" : $"== 有 {missing} 处没接上 ==");

        if (missing == 0) Debug.Log(sb.ToString());
        else Debug.LogError(sb.ToString());
    }

    private static int Check(StringBuilder sb, string field, Object value)
    {
        if (value == null) { sb.AppendLine($"  !! {field} = null"); return 1; }
        sb.AppendLine($"  {field} = {value.name}");
        return 0;
    }

    private static int CheckArray<T>(StringBuilder sb, string field, T[] array, int expected)
        where T : Object
    {
        if (array == null) { sb.AppendLine($"  !! {field} = null"); return 1; }

        int n = 0;
        for (int i = 0; i < array.Length; i++)
        {
            if (array[i] == null) n++;
        }

        if (array.Length != expected) { sb.AppendLine($"  !! {field} 长度 {array.Length}，应为 {expected}"); n++; }
        else sb.AppendLine($"  {field} 长度 {array.Length}，空 {n} 个");

        return n;
    }

    /// <summary>漏接字体的 TMP 在真机上是空白，必须查。</summary>
    private static int CheckFonts(StringBuilder sb, string tag, GameObject root)
    {
        TextMeshProUGUI[] all = root.GetComponentsInChildren<TextMeshProUGUI>(true);
        int noFont = 0;
        foreach (TextMeshProUGUI t in all)
        {
            if (t.font == null) { noFont++; sb.AppendLine($"  !! {tag} 无字体: {t.name}"); }
        }
        sb.AppendLine($"  {tag} TMP 总数={all.Length}，无字体={noFont}");
        return noFont;
    }
}
#endif
