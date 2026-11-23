using System;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 作业票 tab 的只读静态校验：场景接线 + 票面槽位形状。
///
/// 跑法：
/// <code>
/// Unity.exe -batchmode -nographics -projectPath &lt;repo&gt;/sandtable \
///   -executeMethod SpecialJobTicketChecks.Run -logFile -
/// </code>
///
/// 【为什么非要有这一步】票面是把服务端下发的字段铺进场景里那 30 个「标签+值」槽位的，
/// 槽位靠层级形状识别（<c>容器 &gt; 行 &gt; 单元格 &gt; 恰好两个 TMP</c>）。
/// 这个形状一旦被动过 —— 谁在单元格里加了个图标、或者改了嵌套层数 ——
/// 运行时只会少扫到几个槽位，然后**静默少显示几行**，不报任何错。
/// 真机上看是"这张票怎么没有风险辨识结果"，查起来极费劲。所以必须在这里守住。
///
/// 接线是手写 YAML 补丁打进去的（在编辑器里拖引用会重新序列化整个场景、产生 7 万行 diff），
/// 写错一个 fileID 同样不报错，也一并在这里验。只读打开场景，绝不保存。
/// </summary>
public static class SpecialJobTicketChecks
{
    /// <summary>场景里现有的槽位数。少了就是有人删了行，多了就是加了行 —— 两种都要知道。</summary>
    private const int ExpectedCells = 30;

    private static readonly List<string> failures = new List<string>();

    [MenuItem("Tools/特殊作业/作业票静态校验")]
    public static void RunFromMenu()
    {
        Run();
    }

    public static void Run()
    {
        failures.Clear();

        try
        {
            CheckSceneWiring();
            CheckSignatures();
            CheckPhrases();
        }
        catch (Exception e)
        {
            failures.Add("未捕获异常：" + e.Message);
        }

        if (failures.Count == 0)
        {
            Debug.Log("[作业票校验] 全部通过（未保存场景）");
        }
        else
        {
            foreach (string failure in failures) Debug.LogError("[作业票校验] " + failure);
            Debug.LogError("[作业票校验] 共 " + failures.Count + " 项失败");
        }

        if (Application.isBatchMode) EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
    }

    private static void CheckSceneWiring()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
            "Assets/LZW/SandTable/SandTable.unity",
            UnityEditor.SceneManagement.OpenSceneMode.Single);

        SpecialJob_JobTicket host = FindInScene<SpecialJob_JobTicket>();
        Check(host != null, "场景里找不到 SpecialJob_JobTicket");
        if (host == null) return;

        Check(host.jobTicketObjectPrefab != null, "没接行模板 jobTicketObjectPrefab");
        Check(host.detailPanel != null, "没接 detailPanel");

        // listContainer 留空是这个工程的既定约定，不是漏接：VirtualScrollList 会退到
        // 行模板的父节点（见它构造函数的注释）。所以这里验的是**兜底能不能落地**，
        // 而不是字段有没有值 —— 两者都为空才是真的坏了，那时行会建到场景根上。
        Check(host.listContainer != null ||
              (host.jobTicketObjectPrefab != null && host.jobTicketObjectPrefab.transform.parent != null),
            "listContainer 没接、行模板也没有父节点，虚拟滚动会把行建到场景根节点上");

        if (host.jobTicketObjectPrefab != null)
        {
            // 行模板按子节点下标取 8 列（序号/企业/类型/时间/编码/负责人/地点/状态）
            Check(host.jobTicketObjectPrefab.transform.childCount >= 8,
                "行模板的列数少于 8，JobTicketObject 按下标取列会越界");
        }

        JobTicketDetailPanel detail = host.detailPanel;
        if (detail == null) return;

        Check(detail.fieldGridRoot != null,
            "详情面板没接 fieldGridRoot —— 票面会整页空白（脚本里会打 Error，但真机上没人看 log）");
        Check(detail.passButton != null, "详情面板没接 passButton");
        Check(detail.rejectButton != null, "详情面板没接 rejectButton");

        Check(host.listPanelRoot != null,
            "没接 listPanelRoot —— 打开票面时列表不会让位，两块面板会叠在同一个位置上");

        CheckPanelPlacement(host, detail);
        CheckButtonPlacement(host, detail);
        CheckApprovalPanel(detail);

        if (detail.fieldGridRoot == null) return;

        CheckCells(detail.fieldGridRoot);
    }

    /// <summary>
    /// 票面必须盖在作业票列表那一块上。
    ///
    /// 【为什么要验这个】票面原先摆在列表右边约 4000 单位处。这套 UI 是围着人摆的世界空间
    /// 画布，列表本身就占满视野，票面于是整个落在视野之外 —— 现象是"点了详情什么都没发生"，
    /// 而日志里票面渲染得好好的，两个审批按钮也是"点不到"而不是"点了没反应"。
    /// 这一项就是拦住那种改回去的改动：两块面板必须重叠，不是并排。
    ///
    /// 【为什么现在可以在世界空间比】票面原来带 20 度 Y 旋转，轴对齐包围盒和真实矩形对不上，
    /// 所以旧的按钮校验只敢在同一个父空间里比。现在票面的旋转已经去掉，和列表面板一样是
    /// 正对用户的平面，两者又同属一块画布，包围盒比较是准的。
    /// </summary>
    private static void CheckPanelPlacement(SpecialJob_JobTicket host, JobTicketDetailPanel detail)
    {
        RectTransform panel = detail.fieldGridRoot as RectTransform;
        RectTransform list = host.listPanelRoot as RectTransform;
        if (panel == null || list == null) return;

        Transform canvas = panel.GetComponentInParent<Canvas>() != null
            ? panel.GetComponentInParent<Canvas>().transform : null;
        if (canvas == null)
        {
            failures.Add("票面不在任何 Canvas 下，位置无法校验");
            return;
        }

        Check(Quaternion.Angle(panel.localRotation, Quaternion.identity) < 0.5f,
            "票面带着旋转（" + panel.localRotation.eulerAngles +
            "）—— 那是当年斜挂在右侧副屏时留下的，正对用户时要清掉");

        Rect content = RectIn(panel, canvas);
        Rect listRect = RectIn(list, canvas);

        // 【为什么是"包住"而不是"相交"】票面和列表现在用的是同一张底图、同一个尺寸和位置，
        // 点开票面看起来就该是这块面板换了内容、框一动不动。只要求相交的话，
        // 票面挪偏半块也照样通过，而那正是最难看的一种错位。
        Check(Contains(content, listRect),
            "票面 " + Describe(content) + " 没有盖住作业票列表 " + Describe(listRect) +
            " —— 两块面板的框对不齐，切换时会看见框在跳");

        Debug.Log("[作业票校验] 票面 " + Describe(content) + " 盖住列表 " + Describe(listRect));
    }

    /// <summary>
    /// 审批卡片的接线与容量。
    ///
    /// 卡片的槽位数是写死在 prefab 里的，而短语和签名的条数来自配置文件 ——
    /// 两边对不上的表现是「少显示几条」，真机上没人会发现，所以在这儿拦住。
    /// </summary>
    private static void CheckApprovalPanel(JobTicketDetailPanel detail)
    {
        Check(detail.approvalPrefab != null,
            "详情面板没接 approvalPrefab —— 点通过/不通过会打 Error 然后什么都不发生");
        Check(detail.approvalParent != null, "详情面板没接 approvalParent");

        ApprovalPanel panel = detail.approvalPrefab;
        if (panel == null) return;

        Check(panel.submitButton != null, "审批卡片没有提交按钮");
        Check(panel.backButton != null, "审批卡片没有返回按钮");
        Check(panel.statusText != null, "审批卡片没有状态文本，失败原因将无处显示");

        int phraseSlots = panel.phraseButtons != null ? panel.phraseButtons.Length : 0;
        int signatureSlots = panel.signatureButtons != null ? panel.signatureButtons.Length : 0;

        Check(panel.phraseLabels != null && panel.phraseLabels.Length == phraseSlots,
            "审批卡片的短语按钮和文字数量对不上（" + phraseSlots + " vs " +
            (panel.phraseLabels != null ? panel.phraseLabels.Length : 0) + "）");
        Check(panel.signatureImages != null && panel.signatureImages.Length == signatureSlots,
            "审批卡片的签名按钮和缩略图数量对不上");

        PortalApprovalPhraseConfig.EnsureLoaded();
        int need = Mathf.Max(PortalApprovalPhraseConfig.PassPhrases.Length,
                             PortalApprovalPhraseConfig.RejectPhrases.Length);
        Check(phraseSlots >= need,
            "审批卡片只有 " + phraseSlots + " 个短语槽位，配置里最多有 " + need + " 条，会显示不全");

        PortalSignatureConfig.EnsureLoaded();
        int signatures = PortalSignatureConfig.Options != null ? PortalSignatureConfig.Options.Count : 0;
        Check(signatureSlots >= signatures,
            "审批卡片只有 " + signatureSlots + " 个签名槽位，清单里有 " + signatures + " 张，会显示不全");

        Debug.Log("[作业票校验] 审批卡片槽位：短语 " + phraseSlots + "（需 " + need + "）、" +
                  "签名 " + signatureSlots + "（需 " + signatures + "）");
    }

    /// <summary>
    /// 审批短语配置。
    ///
    /// 服务端下发的 audit 按钮带 opinionRequired=true，意见是必填的 ——
    /// 某一组短语空了，那个结论就永远提交不了，而界面上只表现为「提交按钮一直灰着」。
    /// </summary>
    private static void CheckPhrases()
    {
        PortalApprovalPhraseConfig.EnsureLoaded();

        Check(PortalApprovalPhraseConfig.PassPhrases.Length > 0,
            "「通过」没有可选的审批意见，该结论无法提交（意见是服务端必填项）");
        Check(PortalApprovalPhraseConfig.RejectPhrases.Length > 0,
            "「不通过」没有可选的审批意见，该结论无法提交");

        Debug.Log("[作业票校验] 审批短语：通过 " + PortalApprovalPhraseConfig.PassPhrases.Length +
                  " 条、不通过 " + PortalApprovalPhraseConfig.RejectPhrases.Length + " 条");
    }

    /// <summary>
    /// 数槽位，并把"形状不对"的单元格点名报出来。
    ///
    /// 运行时的扫描规则是「恰好两个 TMP 才算一个槽位」，这里用同一条规则数，
    /// 数出来的就是真机上实际能用的槽位数。
    /// </summary>
    private static void CheckCells(Transform root)
    {
        int usable = 0;
        List<string> malformed = new List<string>();

        for (int r = 0; r < root.childCount; r++)
        {
            Transform row = root.GetChild(r);
            for (int c = 0; c < row.childCount; c++)
            {
                Transform cell = row.GetChild(c);
                int texts = cell.GetComponentsInChildren<TextMeshProUGUI>(true).Length;
                if (texts == 2) usable++;
                else malformed.Add($"{row.name}/{cell.name} 有 {texts} 个 TMP");
            }
        }

        Check(malformed.Count == 0,
            "这些单元格不是「恰好两个 TMP」的形状，运行时会被跳过、票面静默少几行：" +
            string.Join("；", malformed));

        Check(usable == ExpectedCells,
            $"可用槽位 {usable} 个，预期 {ExpectedCells} 个。" +
            "多了少了都要确认票面是不是会截断（截断时最后一格会写「还有 N 项未显示」）");

        // 动火作业实测基础信息段有 20 个带标签的字段，加上表头和当前环节信息，
        // 最少要 25 格才不至于一上来就截断。低于这个数说明场景被削过头了。
        Check(usable >= 25, $"槽位只有 {usable} 个，动火票光基础信息就有 20 个字段，必然截断");

        Debug.Log($"[作业票校验] 票面槽位 {usable} 个，形状全部正确");
    }

    /// <summary>
    /// 审批按钮的位置。
    ///
    /// 【为什么要验这个】两个按钮挂在 <c>Report</c> 下，而 <c>Report</c> 的 sizeDelta 是 0x0 ——
    /// 这种父节点下 anchoredPosition 就是相对父原点的绝对偏移，anchor 怎么设都不起作用。
    /// 场景原始状态两个按钮的 anchoredPosition 都是 (0,0)，于是它们：
    ///   1. 彼此完全重合（只点得到上面那个）；
    ///   2. 一起落在**作业票列表**那片区域，打开票面时浮在列表上面，看起来像两个面板互相覆盖。
    ///
    /// 【为什么拿列表那一块当边界，而不是票面自己的矩形】票面的矩形现在是那张底图，
    /// 比可视区大一圈（左右各溢出约 680、上下各约 148），按钮落在溢出的那一圈里
    /// 照样"在票面内"，但用户根本看不见。可视区就是列表那一块，以它为准。
    ///
    /// 【返回按钮不在这儿验】它是 <c>JobTicketDetailPanel.EnsureChrome</c> 运行时造的
    /// （场景里那两个是纯图片按钮，文字烘在贴图里，复制一份只会得到写着"不通过"的返回键），
    /// 编辑器下不存在。它和这两个按钮共用中线 y=443，横坐标 -873 是一组常量里的一个。
    /// </summary>
    private static void CheckButtonPlacement(SpecialJob_JobTicket host, JobTicketDetailPanel detail)
    {
        RectTransform pass = detail.passButton != null
            ? detail.passButton.transform as RectTransform : null;
        RectTransform reject = detail.rejectButton != null
            ? detail.rejectButton.transform as RectTransform : null;
        RectTransform list = host.listPanelRoot as RectTransform;

        if (pass == null || reject == null || list == null) return;

        Canvas canvas = pass.GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            failures.Add("审批按钮不在任何 Canvas 下，位置无法校验");
            return;
        }

        Rect passRect = RectIn(pass, canvas.transform);
        Rect rejectRect = RectIn(reject, canvas.transform);
        Rect visible = RectIn(list, canvas.transform);

        Check(!passRect.Overlaps(rejectRect),
            "通过/驳回两个按钮重叠了（通过 " + Describe(passRect) + "，驳回 " + Describe(rejectRect) +
            "）—— 只有一个点得到");

        Check(Contains(visible, passRect),
            "「通过」按钮不在可视区里（按钮 " + Describe(passRect) + "，可视区 " + Describe(visible) +
            "）—— 真机上会看不见或者点不到");
        Check(Contains(visible, rejectRect),
            "「驳回」按钮不在可视区里（按钮 " + Describe(rejectRect) + "，可视区 " + Describe(visible) +
            "）—— 真机上会看不见或者点不到");

        if (failures.Count == 0)
        {
            Debug.Log("[作业票校验] 审批按钮位置正常：通过 " + Describe(passRect) +
                      "，驳回 " + Describe(rejectRect) + "，都在可视区 " + Describe(visible) + " 内");
            return;
        }

        SuggestButtonPositions(pass, reject, visible);
    }

    /// <summary>
    /// 把 <paramref name="rt"/> 的实际四角换算到 <paramref name="parent"/> 的本地空间。
    ///
    /// 【为什么不直接比 anchoredPosition】<c>Report</c> 运行时被 ContentSizeFitter 撑出尺寸后，
    /// anchor 不同的子节点参考点就不同了（票面 Image 锚左上、按钮锚左下），
    /// 直接比 anchoredPosition 会差一个 Report 的高度。
    /// <c>GetWorldCorners</c> 给的是考虑了 anchor/pivot/size/旋转之后的真实四角，
    /// 再统一转回父空间，谁锚在哪都不影响结果。
    /// </summary>
    private static Rect RectIn(RectTransform rt, Transform parent)
    {
        Vector3[] corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        Vector3 a = parent.InverseTransformPoint(corners[0]);   // 左下
        Vector3 b = parent.InverseTransformPoint(corners[2]);   // 右上
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                               Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    /// <summary>outer 是否完整包住 inner（Rect.Contains 不接受矩形，只好自己写）。</summary>
    private static bool Contains(Rect outer, Rect inner)
    {
        return inner.xMin >= outer.xMin && inner.xMax <= outer.xMax
            && inner.yMin >= outer.yMin && inner.yMax <= outer.yMax;
    }

    /// <summary>
    /// 位置不对时，直接把该填的 anchoredPosition 算出来贴在日志里。
    ///
    /// 目标矩形是在画布空间里算的，而要填进 YAML 的是按钮在 <c>Report</c> 里的
    /// anchoredPosition。两者之间只差一个平移（按钮没有旋转也没有缩放），
    /// 所以拿"按钮现在在画布上的中心"减去"它现在的 anchoredPosition"就得到那个平移量 ——
    /// 不用去推 Report 到底被布局摆到哪儿了（那条路推错过一次，x 轴方向反了）。
    /// </summary>
    private static void SuggestButtonPositions(RectTransform pass, RectTransform reject, Rect visible)
    {
        Canvas canvas = pass.GetComponentInParent<Canvas>();
        Rect passRect = RectIn(pass, canvas.transform);
        Vector2 shift = passRect.center - pass.anchoredPosition;   // 画布空间 - 局部空间

        float gap = passRect.width * 0.45f;
        float y = visible.yMin + passRect.height * 0.5f + visible.height * 0.06f;
        float right = visible.xMax - visible.width * 0.05f;

        Line("rejectButton", right - reject.rect.width * 0.5f, y, shift);
        Line("passButton", right - reject.rect.width - gap - pass.rect.width * 0.5f, y, shift);

        Debug.LogError("[作业票校验] 上面两行就是 SandTable.unity 里该填的值，" +
                       "用 YAML 补丁改，不要在编辑器里拖（保存场景会重编号全部 fileID）");
    }

    private static void Line(string label, float canvasX, float canvasY, Vector2 shift)
    {
        // anchoredPosition 对应的是 pivot 点，pivot 0.5 时就是中心
        Debug.LogError("[作业票校验] " + label + " 应填  m_AnchoredPosition: {x: " +
                       Mathf.RoundToInt(canvasX - shift.x) + ", y: " +
                       Mathf.RoundToInt(canvasY - shift.y) + "}");
    }

    private static string Describe(Rect r)
    {
        return "x[" + Mathf.RoundToInt(r.xMin) + "," + Mathf.RoundToInt(r.xMax) +
               "] y[" + Mathf.RoundToInt(r.yMin) + "," + Mathf.RoundToInt(r.yMax) + "]";
    }

    /// <summary>
    /// 签名库文件夹。
    ///
    /// 【为什么不再验清单】上一版靠 <c>approval_signatures.json</c> 列出每张签名，
    /// 加一张得同时改 json 和放图，对不上就表现成「可选签名少了一张」。现在改成扫文件夹，
    /// 没有清单也就没有对不上的可能 —— 这里只需要保证**文件夹在**、图能被 Unity 认成贴图。
    ///
    /// 【空是合法的】图由现场后补（放进随包目录，或 adb push 到设备的 signatures 目录），
    /// 所以一张都没有不算失败，只提醒一句。
    /// </summary>
    private static void CheckSignatures()
    {
        string folder = "Assets/Resources/" + PortalSignatureConfig.ResourceFolder;

        if (!AssetDatabase.IsValidFolder(folder))
        {
            failures.Add("签名库目录不存在：" + folder + "（PortalSignatureConfig 会扫这个目录）");
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
        if (guids.Length == 0)
        {
            Debug.LogWarning("[作业票校验] 签名库 " + folder +
                             " 里还没有图，审批时签名一项会自动免填。把签名 PNG 放进去即可");
            return;
        }

        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string name = System.IO.Path.GetFileNameWithoutExtension(path);

            // 文件名就是 id，也是界面上显示的签名人 —— 重名会让本地存档记错人
            Check(seen.Add(name), "签名库里有同名文件：" + name + "（文件名就是签名人标识，不能重）");

            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null) continue;

            bool pot = IsPowerOfTwo(tex.width) && IsPowerOfTwo(tex.height);
            Check(pot, "签名图 " + path + " 是 " + tex.width + "x" + tex.height +
                  "，不是 2 的幂 —— Unity 会自动缩放到最近的 POT，签名会被拉变形（建议 512x256）");
        }

        Debug.Log("[作业票校验] 签名库 " + guids.Length + " 张：" + folder);
    }

    private static bool IsPowerOfTwo(int value)
    {
        return value > 0 && (value & (value - 1)) == 0;
    }

    /// <summary>面板默认是关掉的，<c>FindObjectOfType</c> 找不到，只能从全部已加载对象里挑。</summary>
    private static T FindInScene<T>() where T : MonoBehaviour
    {
        foreach (T candidate in Resources.FindObjectsOfTypeAll<T>())
        {
            if (candidate.gameObject.scene.IsValid()) return candidate;
        }
        return null;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) failures.Add(message);
    }
}
