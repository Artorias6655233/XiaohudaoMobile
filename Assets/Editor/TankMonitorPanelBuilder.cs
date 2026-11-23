#if UNITY_EDITOR
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 生成罐区监测卡片（TankMonitorPanel）prefab 的一次性编辑器工具。
///
/// 这套工程的 UI 一贯在场景/prefab 里手摆，这里破例用代码生成，只因为卡片是规整的
/// 四行表格 + 表头，手摆 30 多个 TMP 又慢又容易漏接引用。产出的是普通 prefab 资产，
/// 之后照常在 Inspector 里改，改完这个脚本就可以删。
///
/// 样式不自己定，全部从场景里已有的负责人卡片（MajorHazard_Management.responsiblePersonPrefab
/// 指向的 personpanel）上抄：底图 sprite、字体资产、字号、文字颜色、卡片宽度。
/// </summary>
internal static class TankMonitorPanelBuilder
{
    private const string ScenePath = "Assets/LZW/SandTable/SandTable.unity";
    private const string PrefabPath = "Assets/ArtWorks/TankMonitorPanel.prefab";

    /// <summary>
    /// 卡片底图：只有四个角标、框内完全干净的通用框。
    ///
    /// 【不要】再用负责人卡片那张 组织-模块背景框-指标@2x —— 它把标题「管理员信息」和
    /// 企业名称 / 包保范围 / 手机号 / 人员类型 / 职责 五个标签【烘死在图里】了，
    /// 拿来当底图会和本卡片的表格叠在一起。这张是 CameraAlarmDetail 在用的那张。
    /// </summary>
    private const string BackgroundSpritePath =
        "Assets/ArtWorks/UI/双重预防机制-整改督办_slices/Group 1321316901@2x.png";

    // --- 卡片尺寸与栅格（单位是卡片本地像素，宿主 Canvas 会整体缩放到世界空间） ---
    //
    // 底图原生 4715 x 4231（宽高比 1.1144），spriteBorder 全 0 不能九宫格，
    // 所以卡片必须按原生比例做，否则 Simple 拉伸会把四个角标拉扁。
    // 1881.5 与负责人卡片同宽，高度由比例反推：1881.5 / 1.1144 ≈ 1688。
    private const float CardWidth = 1881.5f;
    private const float CardHeight = 1688f;

    // 角标臂长原生 158px，卡片缩放约 0.399 → 约 63 单位，留 90 的内边距足够避开
    private const float PadLeft = 90f;

    private const float PanelTitleSize = 76f;   // 罐区实时监测
    private const float TitleSize = 64f;        // 罐名
    private const float BodySize = 52f;         // 基础信息
    private const float HeaderSize = 52f;       // 表头
    private const float RowSize = 64f;          // 参数行数值
    private const float ThresholdSize = 42f;    // 阈值那列最长，单独调小
    private const float StatusSize = 58f;       // "高高报" 3 字要塞进 200 宽
    private const float FootSize = 48f;

    // 表格列：x 起点 / 宽度（右边界依次 340 / 710 / 885 / 1580 / 1800，卡片宽 1881.5）
    private const float ColNameX = 90f, ColNameW = 250f;
    private const float ColValueX = 350f, ColValueW = 360f;
    private const float ColUnitX = 725f, ColUnitW = 160f;
    private const float ColThresholdX = 900f, ColThresholdW = 680f;
    private const float ColStatusX = 1600f, ColStatusW = 200f;

    private const float TableHeaderY = -580f;
    private const float FirstRowY = -700f;
    private const float RowStep = 160f;
    private const float RowHeight = 130f;
    private const float FooterY = -1450f;

    private static readonly Color DimColor = new Color(1f, 1f, 1f, 0.7f);

    [MenuItem("Tools/罐区监测/生成 TankMonitorPanel 卡片")]
    public static void Build()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        MajorHazard_Management host = FindHost();
        if (host == null)
        {
            Debug.LogError("[卡片生成] 场景里找不到 MajorHazard_Management");
            return;
        }

        GameObject template = host.responsiblePersonPrefab;
        if (template == null)
        {
            Debug.LogError("[卡片生成] responsiblePersonPrefab 是空的，抄不到样式");
            return;
        }

        TextMeshProUGUI templateText = template.GetComponentInChildren<TextMeshProUGUI>(true);
        if (templateText == null)
        {
            Debug.LogError("[卡片生成] 模板上找不到 TextMeshProUGUI，抄不到字体");
            return;
        }

        // 字体和文字颜色照抄负责人卡片；底图【不抄】，见 BackgroundSpritePath 的说明
        TMP_FontAsset font = templateText.font;
        Color textColor = templateText.color;

        Sprite bg = AssetDatabase.LoadAssetAtPath<Sprite>(BackgroundSpritePath);
        if (bg == null)
        {
            Debug.LogError($"[卡片生成] 底图加载失败：{BackgroundSpritePath}");
            return;
        }

        Debug.Log($"[卡片生成] 底图={bg.name}（{bg.rect.width}x{bg.rect.height}），" +
            $"字体={font?.name}，颜色={textColor}");

        // --- 建卡片（先挂到场景里，存成 prefab 后再删掉这个临时实例） ---
        GameObject root = BuildCard(host.transform, bg, font, textColor);

        System.IO.Directory.CreateDirectory(
            System.IO.Path.GetDirectoryName(FullPath(PrefabPath)));

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        if (prefab == null)
        {
            Debug.LogError($"[卡片生成] 存 prefab 失败：{PrefabPath}");
            return;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[卡片生成] 已生成 {PrefabPath}");

        // 这里【刻意不接线、也不保存场景】。
        //
        // 踩过的坑：用 EditorSceneManager.SaveScene 存 SandTable.unity 会让 Unity
        // 重新序列化整个场景，把所有 fileID 重编号 —— 只想改两行，实际产生了
        // 73586 增 / 73584 删 的 diff，既没法评审也必然和别人的分支冲突。
        //
        // 所以接线走手工 YAML 补丁，只动一行（fileID/guid 以实际生成的为准）：
        //   MajorHazard_Management 那个 MonoBehaviour 块里，
        //   responsiblePersonPrefab 下面加一行 tankMonitorPrefab: {fileID: <prefab 根>, guid: <prefab guid>, type: 3}
        //
        // 卡片与负责人卡片【左右并排】，靠宿主已有的 HorizontalLayoutGroup 自动落位，不用改布局组。
        // 若哪天要改成上下排列：把同一个 GameObject 上 HorizontalLayoutGroup 的 m_Script guid
        // 30649d3a9faa99c48a7b1166b86bf2a0 换成 VerticalLayoutGroup 的
        // 59f8146938fff824cb5fd77236b75775 即可（两者序列化字段完全一致）。
        //
        // 在编辑器里手动拖引用当然也行，但保存场景时同样会触发上面那个重编号，
        // 提交前记得确认 SandTable.unity 的 diff 规模。
        Debug.Log("[卡片生成] prefab 已就绪；接线请走 YAML 补丁，不要在这里保存场景（会重编号整个场景的 fileID）");

        // 供参考：打印接线需要的两个值
        Debug.Log($"[卡片生成] prefab guid = {AssetDatabase.AssetPathToGUID(PrefabPath)}，" +
            $"根 GameObject fileID 见 {PrefabPath} 里 m_Father 为 0 的那个 RectTransform 对应的 GameObject");
    }

    private static GameObject BuildCard(Transform parent, Sprite bg, TMP_FontAsset font, Color textColor)
    {
        GameObject root = new GameObject("tankmonitorpanel", typeof(RectTransform));
        RectTransform rt = (RectTransform)root.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(CardWidth, CardHeight);
        rt.localScale = Vector3.one;

        Image image = root.AddComponent<Image>();
        image.sprite = bg;
        image.type = Image.Type.Simple;
        image.color = Color.white;
        image.raycastTarget = false;   // 纯展示，不吃射线

        TankMonitorPanel panel = root.AddComponent<TankMonitorPanel>();

        // --- 卡片标题（底图是干净角框，没有烘死的标题，必须自己加） ---
        Text(root, "PanelTitle", PadLeft, -70f, 1000f, 96f,
            PanelTitleSize, TextAlignmentOptions.Left, textColor, font, "罐区实时监测");

        // --- 顶部基础信息 ---
        panel.tankNameText = Text(root, "TankName", PadLeft, -200f, CardWidth - PadLeft * 2f, 84f,
            TitleSize, TextAlignmentOptions.Left, textColor, font, "储罐名称");

        Label(root, "L_DeviceCode", PadLeft, -320f, 250f, "设备编码", font);
        panel.deviceCodeText = Text(root, "DeviceCode", PadLeft + 260f, -320f, 600f, 64f,
            BodySize, TextAlignmentOptions.Left, textColor, font, "-");

        Label(root, "L_TankType", 1010f, -320f, 160f, "罐型", font);
        panel.tankTypeText = Text(root, "TankType", 1180f, -320f, 620f, 64f,
            BodySize, TextAlignmentOptions.Left, textColor, font, "-");

        Label(root, "L_TankVol", PadLeft, -415f, 250f, "设计容积", font);
        panel.tankVolText = Text(root, "TankVol", PadLeft + 260f, -415f, 600f, 64f,
            BodySize, TextAlignmentOptions.Left, textColor, font, "-");

        Label(root, "L_LiquidTop", 1010f, -415f, 250f, "液位上限", font);
        panel.liquidTopText = Text(root, "LiquidTop", 1270f, -415f, 530f, 64f,
            BodySize, TextAlignmentOptions.Left, textColor, font, "-");

        // --- 表头 ---
        Header(root, "H_Name", ColNameX, ColNameW, "参数", TextAlignmentOptions.Left, font);
        Header(root, "H_Value", ColValueX, ColValueW, "实时值", TextAlignmentOptions.Right, font);
        Header(root, "H_Unit", ColUnitX, ColUnitW, "单位", TextAlignmentOptions.Left, font);
        Header(root, "H_Threshold", ColThresholdX, ColThresholdW, "报警阈值", TextAlignmentOptions.Left, font);
        Header(root, "H_Status", ColStatusX, ColStatusW, "状态", TextAlignmentOptions.Left, font);

        // --- 四个参数行（顺序必须与 TankMonitorService.Params 一致） ---
        panel.liquidLevelRow = Row(root, "Row_LiquidLevel", FirstRowY - RowStep * 0f, "液位", textColor, font);
        panel.storageRow = Row(root, "Row_Storage", FirstRowY - RowStep * 1f, "储量", textColor, font);
        panel.temperatureRow = Row(root, "Row_Temperature", FirstRowY - RowStep * 2f, "温度", textColor, font);
        panel.pressureRow = Row(root, "Row_Pressure", FirstRowY - RowStep * 3f, "压力", textColor, font);

        // --- 底部 ---
        Label(root, "L_UpdateTime", PadLeft, FooterY, 250f, "更新时间", font);
        panel.updateTimeText = Text(root, "UpdateTime", PadLeft + 260f, FooterY, 700f, 60f,
            FootSize, TextAlignmentOptions.Left, DimColor, font, "-");

        panel.statusHintText = Text(root, "StatusHint", 1060f, FooterY, CardWidth - 1060f - PadLeft, 60f,
            FootSize, TextAlignmentOptions.Right, new Color(1f, 0.8f, 0.3f), font, "");

        // 场景里的模板默认是关的；prefab 根也存成 inactive，
        // Instantiate 出来先是隐藏的，等 ShowLoading 再打开，避免闪一帧空卡片。
        root.SetActive(false);
        return root;
    }

    private static TankMonitorRow Row(GameObject card, string name, float y,
        string displayName, Color textColor, TMP_FontAsset font)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(card.transform, false);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(CardWidth, RowHeight);
        rt.localScale = Vector3.one;

        TankMonitorRow row = go.AddComponent<TankMonitorRow>();

        // 行内子物体的 x 相对行原点（行占满整卡宽度），所以直接用列常量
        row.nameText = Text(go, "Name", ColNameX, 0f, ColNameW, RowHeight,
            RowSize, TextAlignmentOptions.Left, textColor, font, displayName);
        row.valueText = Text(go, "Value", ColValueX, 0f, ColValueW, RowHeight,
            RowSize, TextAlignmentOptions.Right, textColor, font, "--");
        row.unitText = Text(go, "Unit", ColUnitX, 0f, ColUnitW, RowHeight,
            RowSize, TextAlignmentOptions.Left, DimColor, font, "");
        // 阈值那列内容最长（形如「低低 -10 / 低 -5 / 高 75 / 高高 80」），字号单独调小
        row.thresholdText = Text(go, "Threshold", ColThresholdX, 0f, ColThresholdW, RowHeight,
            ThresholdSize, TextAlignmentOptions.Left, DimColor, font, "");
        row.statusText = Text(go, "Status", ColStatusX, 0f, ColStatusW, RowHeight,
            StatusSize, TextAlignmentOptions.Left, textColor, font, "");

        return row;
    }

    private static void Label(GameObject parent, string name, float x, float y, float w,
        string content, TMP_FontAsset font)
    {
        Text(parent, name, x, y, w, 64f, BodySize, TextAlignmentOptions.Left, DimColor, font, content);
    }

    private static void Header(GameObject parent, string name, float x, float w,
        string content, TextAlignmentOptions align, TMP_FontAsset font)
    {
        Text(parent, name, x, TableHeaderY, w, 64f, HeaderSize, align, DimColor, font, content);
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

        // 表格要求列宽固定：不换行、超出打省略号，也不要 ContentSizeFitter
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.enableAutoSizing = false;
        tmp.raycastTarget = false;

        return tmp;
    }

    private static MajorHazard_Management FindHost()
    {
        // 面板在场景里默认关闭，FindObjectOfType 找不到，得连隐藏的一起找
        foreach (MajorHazard_Management m in Resources.FindObjectsOfTypeAll<MajorHazard_Management>())
        {
            if (m.gameObject.scene.IsValid()) return m;
        }

        return null;
    }

    private static string FullPath(string assetPath)
    {
        return System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(Application.dataPath), assetPath);
    }

    /// <summary>
    /// 只读校验：场景接线、prefab 上的引用是否都接齐了。不保存任何文件。
    /// </summary>
    [MenuItem("Tools/罐区监测/校验接线（只读）")]
    public static void Verify()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("===== 校验 =====");

        MajorHazard_Management host = FindHost();
        sb.AppendLine($"host: {(host != null ? "OK" : "找不到")}");
        if (host == null) { Debug.LogError(sb.ToString()); return; }

        sb.AppendLine($"host.responsiblePersonPrefab: {(host.responsiblePersonPrefab != null ? host.responsiblePersonPrefab.name : "null")}");
        sb.AppendLine($"host.tankMonitorPrefab: {(host.tankMonitorPrefab != null ? host.tankMonitorPrefab.name : "null")}");
        sb.AppendLine($"host 布局: Vertical={host.GetComponent<VerticalLayoutGroup>() != null}, Horizontal={host.GetComponent<HorizontalLayoutGroup>() != null}");

        VerticalLayoutGroup v = host.GetComponent<VerticalLayoutGroup>();
        if (v != null)
        {
            sb.AppendLine($"  spacing={v.spacing} align={v.childAlignment} ctrlW={v.childControlWidth} ctrlH={v.childControlHeight} expW={v.childForceExpandWidth} expH={v.childForceExpandHeight} padding=({v.padding.left},{v.padding.right},{v.padding.top},{v.padding.bottom})");
        }

        GameObject prefab = host.tankMonitorPrefab;
        if (prefab == null) { Debug.LogError(sb.ToString()); return; }

        sb.AppendLine($"prefab 路径: {AssetDatabase.GetAssetPath(prefab)}  activeSelf={prefab.activeSelf}");

        Image bg = prefab.GetComponent<Image>();
        sb.AppendLine($"底图 sprite: {(bg != null && bg.sprite != null ? bg.sprite.name : "null")}");

        TankMonitorPanel p = prefab.GetComponent<TankMonitorPanel>();
        if (p == null) { sb.AppendLine("!! prefab 上没有 TankMonitorPanel"); Debug.LogError(sb.ToString()); return; }

        int missing = 0;
        missing += Check(sb, "tankNameText", p.tankNameText);
        missing += Check(sb, "deviceCodeText", p.deviceCodeText);
        missing += Check(sb, "tankTypeText", p.tankTypeText);
        missing += Check(sb, "tankVolText", p.tankVolText);
        missing += Check(sb, "liquidTopText", p.liquidTopText);
        missing += Check(sb, "updateTimeText", p.updateTimeText);
        missing += Check(sb, "statusHintText", p.statusHintText);
        missing += CheckRow(sb, "liquidLevelRow", p.liquidLevelRow);
        missing += CheckRow(sb, "storageRow", p.storageRow);
        missing += CheckRow(sb, "temperatureRow", p.temperatureRow);
        missing += CheckRow(sb, "pressureRow", p.pressureRow);
        sb.AppendLine($"panelRoot: {(p.panelRoot != null ? p.panelRoot.name : "null（按设计退化为控制自身）")}");

        sb.AppendLine(missing == 0 ? "== 全部引用已接齐 ==" : $"== 有 {missing} 处没接上 ==");

        // 字体核对：漏接字体的 TMP 在真机上是空白
        int noFont = 0;
        foreach (TextMeshProUGUI t in prefab.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (t.font == null) { noFont++; sb.AppendLine($"  !! 无字体: {t.name}"); }
        }
        sb.AppendLine($"TMP 总数={prefab.GetComponentsInChildren<TextMeshProUGUI>(true).Length}，无字体={noFont}");

        if (missing == 0 && noFont == 0) Debug.Log(sb.ToString());
        else Debug.LogError(sb.ToString());
    }

    private static int Check(StringBuilder sb, string field, Object value)
    {
        if (value == null) { sb.AppendLine($"  !! {field} = null"); return 1; }
        sb.AppendLine($"  {field} = {value.name}");
        return 0;
    }

    private static int CheckRow(StringBuilder sb, string field, TankMonitorRow row)
    {
        if (row == null) { sb.AppendLine($"  !! {field} = null"); return 1; }

        int n = 0;
        n += row.nameText == null ? 1 : 0;
        n += row.valueText == null ? 1 : 0;
        n += row.unitText == null ? 1 : 0;
        n += row.thresholdText == null ? 1 : 0;
        n += row.statusText == null ? 1 : 0;
        sb.AppendLine($"  {field} = {row.name}（5 个 TMP 里缺 {n} 个，name=\"{(row.nameText != null ? row.nameText.text : "?")}\"）");
        return n;
    }

    // ------------------------------------------------------------------ 调试用

    [MenuItem("Tools/罐区监测/导出负责人卡片样式（只读）")]
    public static void DumpTemplate()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        MajorHazard_Management host = FindHost();
        if (host == null || host.responsiblePersonPrefab == null)
        {
            Debug.LogError("[卡片生成] 找不到宿主或模板");
            return;
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("===== 负责人卡片模板 =====");
        Dump(host.responsiblePersonPrefab.transform, 0, sb);
        Debug.Log(sb.ToString());
    }

    private static void Dump(Transform t, int depth, StringBuilder sb)
    {
        string pad = new string(' ', depth * 2);
        sb.AppendLine($"{pad}{t.name}  (active={t.gameObject.activeSelf})");

        RectTransform rt = t as RectTransform;
        if (rt != null)
        {
            sb.AppendLine($"{pad}  rect: anchorMin={rt.anchorMin} anchorMax={rt.anchorMax} " +
                $"pivot={rt.pivot} pos={rt.anchoredPosition} size={rt.sizeDelta}");
        }

        foreach (Component c in t.GetComponents<Component>())
        {
            if (c == null || c is Transform) continue;

            sb.Append($"{pad}  [{c.GetType().Name}]");

            Image img = c as Image;
            if (img != null)
            {
                sb.Append($" sprite={(img.sprite != null ? img.sprite.name : "null")} color={img.color}");
            }

            TextMeshProUGUI tmp = c as TextMeshProUGUI;
            if (tmp != null)
            {
                sb.Append($" font={(tmp.font != null ? tmp.font.name : "null")} size={tmp.fontSize}" +
                    $" color={tmp.color} align={tmp.alignment} text=\"{tmp.text}\"");
            }

            sb.AppendLine();
        }

        for (int i = 0; i < t.childCount; i++)
        {
            Dump(t.GetChild(i), depth + 1, sb);
        }
    }
}
#endif
