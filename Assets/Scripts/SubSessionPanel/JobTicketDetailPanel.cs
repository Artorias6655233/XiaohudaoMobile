using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 作业票票面（只读）。
///
/// 【为什么改成通用槽位而不是 27 个固定字段】
/// 原先面板把 27 个中文标签写死在场景里，脚本按字段名一一对应赋值。换成门户数据源之后
/// 这套行不通了，有三个原因：
///
///   1. 那 27 个字段里有 17 个（作废原因、打卡状态、经纬度…）门户根本不返回 ——
///      照原样接下去，票面上会有 17 行永久空白；
///   2. 票面字段**随作业类型变**：动火作业 20 个字段，盲板抽堵 24 个，而且几乎不重叠
///      （动火有「动火作业级别」，盲板有「介质/温度/压力」）。写死一套必然只对一类；
///   3. 中文标签只存在于服务端的表单定义里（<see cref="PortalFormDefinition"/>）。
///      接口返回的字段名是 SQL 列名，客户端自己编标签早晚和门户 Web 端对不上。
///
/// 所以改成：场景里那 30 个「标签+值」单元格当成通用槽位，内容全部由服务端下发的
/// 表单定义驱动。场景只多接一个容器引用，没有新建 prefab，也没有重排层级。
///
/// 【版式：定尺卡片，不再让卡片跟着内容长】2026-09-23 改。旧版是「卡片高度随行数变、
/// 按钮贴着最后一行走」，三个后果：
///
///   1. 背景是一张 7182x5410 的 HUD 底图（四角括号 + 左上角烘死的标题），高度从 2700
///      缩到 700 时整张图被纵向压扁，角标和标题都变形；
///   2. 每个单元格都挂着 ContentSizeFitter，宽度随文字长短走，行与行之间值的起点全不一样，
///      第二列更是被第一列的长度顶得忽左忽右 —— 整页没有一条对齐线；
///   3. 值也带 Fitter，长文本单行无限伸长（永远不换行），能把第二列整个顶出画布。
///
/// 现在：卡片换成作业票列表那张底图，**尺寸和位置逐像素相同**（画布 4297.5 x 1573.8，
/// 中心 x=0、上沿 y=841.9），点开票面就像是同一块面板换了内容，框不动、只有内容变；
/// 两列定宽定距，标签列 460、值列 780，所有行的值从同一条竖线起头；
/// 一屏 15 行 x 2 列 = 30 格全铺开，装不下的在状态行注明还剩几项。
/// 按钮固定在底部栏里，不再跟着内容跑。
///
/// 【审批全程本机，所有票都能处置】通过 / 不通过 / 签名的结果写进
/// <see cref="JobTicketApprovalStore"/>，**不回传门户**，所以不看账号权限。
/// 按钮点开的是 <see cref="ApprovalPanel"/>，在那里补意见和签名再确认 ——
/// 结论、意见、签名是一组，而且中间要有一步能反悔。
///
/// 【为什么不再按门户权限置灰】2026-09-23 真机实测：用户打开的每一张票，服务端给的按钮
/// 都是 <c>[submit/toclose]</c> 或者空，从来没给过 <c>audit</c>，于是这两个按钮一直是灰的。
/// 在眼镜里，灰按钮和"这功能坏了"完全分不出来。既然结果不回传，卡着也没有意义。
///
/// 【票面顶替列表显示】票面和作业票列表原先并排摆在同一块画布上，票面在列表右边约 4000
/// 单位处 —— 列表本身就占满了视野，票面整个落在视野外，两个审批按钮自然也点不到。
/// 现在票面搬到列表那个位置，由宿主 <see cref="SpecialJob_JobTicket"/> 在打开票面时把列表
/// 藏掉、关闭票面时放回来（<see cref="OnBackAction"/>）。
/// </summary>
public class JobTicketDetailPanel : MonoBehaviour
{
    public GameObject panelRoot;
    public Button passButton;
    public Button rejectButton;

    /// <summary>
    /// 装「标签+值」单元格的容器，同时也是卡片本身（底图 + VerticalLayoutGroup 都在它身上）。
    /// 层级约定（场景里本来就是这个形状）：<c>容器 &gt; 行 &gt; 单元格 &gt; [标签TMP, 值TMP]</c>。
    /// 实测 15 行 x 2 格 = 30 个槽位，全部用来显示。
    /// </summary>
    public Transform fieldGridRoot;

    /// <summary>审批卡片 prefab，由 <c>ApprovalPanelBuilder</c> 生成。</summary>
    public ApprovalPanel approvalPrefab;

    /// <summary>审批卡片挂哪儿。接 Report 的 RectTransform，和两个审批按钮同级。</summary>
    public Transform approvalParent;

    public Action<JobTicket> OnPassAction;

    /// <summary>票面关掉了，请宿主把列表放回来。提交完成自动返回时也走这一条。</summary>
    public Action OnBackAction;

    // ------------------------------------------------------------------ 版式
    //
    // 【这一组常量和场景里那份 YAML 补丁是一体的】卡片的尺寸、位置、内边距、两个审批按钮的
    // 坐标写在 SandTable.unity 里；行高、列宽、字号写在这里。改一边必须同时改另一边，
    // 否则表现是"内容溢出卡片"或者"卡片下半截空着"，而不会报任何错。
    //
    // 场景侧的值（&269360982 / &269360984 / 两个按钮）：
    //   卡片 m_SizeDelta 4313.96 x 1579.82、m_AnchoredPosition (282, 1764.9)。
    //   这两个数是「画布上 4297.5 x 1573.8」除以卡片自己的 localScale 0.9961833 得来的 ——
    //   底图和作业票列表那张是同一张、同尺寸，所以两块面板的框严丝合缝地重合。
    //   内边距 左831/右831/上404/下358：底图比可视区大一圈（左右各溢出 681.4、上下各 148.5），
    //   先把那一圈让出来，再在可视区里内缩 150 / 上 255 / 下 210（下面这一截留给按钮栏）。
    //   行距 18。

    /// <summary>
    /// 一屏显示几行 —— 场景里有几行就用几行，不留余量。
    ///
    /// 【为什么不翻页】一度做过 11 行 + 翻页。票面是一张票的全貌，翻页把它切成两半，
    /// 用户得记着上一页写了什么再去对下一页，比挤一点难受得多；而且审批时最关心的
    /// 「本机处置」和「基础信息」本来就应该同屏。
    ///
    /// 卡片可视区高 1277.9 是定死的（要和作业票列表严丝合缝地重合），扣掉标题栏 255 和
    /// 按钮栏 210 之后只剩 817.8：15 行 x 46 + 14 x 9 = 816，正好铺满。
    /// 行高和字号是按这个数倒推的，改任何一个都要重算。
    /// </summary>
    private const int VisibleRows = 15;

    private const float RowHeight = 46f;
    private const float RowWidth = 2651.5f;

    /// <summary>一列（标签 + 值）的总宽。两列 + 列间距 110 = 2651.5，等于内容区宽度。</summary>
    private const float ColumnWidth = 1270.75f;
    private const float ColumnGap = 110f;

    /// <summary>
    /// 标签列宽。定宽是这次改版的关键：只有标签定宽，所有行的值才会从同一条竖线起头。
    /// 460 在字号 46 下约合 10 个汉字，够放「作业负责人审批意见」这类最长的表单标签。
    /// </summary>
    private const float LabelWidth = 460f;
    private const float ValueWidth = 780f;
    private const float CellSpacing = 30f;

    private const float LabelSize = 42f;
    private const float ValueSize = 42f;
    private const float HeaderSize = 46f;

    /// <summary>标题字号抄作业票列表那块面板的（71.8），两块面板切换时标题不会跳大小。</summary>
    private const float TitleSize = 71.8f;
    private const float StatusSize = 42f;
    private const float ButtonTextSize = 52f;

    private static readonly Color LabelColor = new Color(1f, 1f, 1f, 0.72f);
    private static readonly Color ValueColor = new Color(0.76862746f, 0.84313726f, 1f, 1f);
    private static readonly Color StatusColor = new Color(1f, 1f, 1f, 0.65f);

    /// <summary>运行时造的按钮底色，和审批卡片上那些按钮取同一个值（ApprovalPanelBuilder）。</summary>
    private static readonly Color ButtonColor = new Color(1f, 1f, 1f, 0.16f);

    // 底部按钮栏（Report 局部空间）。票面可视区（= 作业票列表那一块）左 -1188、右 1752、
    // 底 339、顶 1617；Report 原点在画布 (-282, -923) 处，这两个偏移是实测出来的，推不出来。
    // 通过 / 不通过 是场景里的美术按钮（414.75 x 188.25），坐标写在 YAML 里；
    // 下面这几个是运行时造的，和它们共用一条中线。
    private const float ButtonY = 443f;
    private const float FlatButtonHeight = 132f;
    private const float BackButtonX = -873f;
    private const float BackButtonWidth = 330f;

    // 标题栏（Report 局部空间）
    private const float TitleX = -238f;
    private const float TitleY = 1524f;
    private const float TitleWidth = 1600f;

    /// <summary>
    /// 状态行没有铺满整行宽，右边那截留给签名印章。
    /// 1850 宽在字号 42 下约能放 44 个字，最长的那句状态文案（40 字）一行放得下。
    /// </summary>
    private const float StatusX = -113f;
    private const float StatusY = 1424f;
    private const float StatusWidth = 1850f;

    /// <summary>
    /// 签名印章的位置和最大尺寸（Report 局部空间）。
    ///
    /// 落在标题右侧那块空白上（标题文字到 562 为止，印章从 842 起），和真实票据把章盖在
    /// 抬头旁边是一个意思；不压任何一行内容。
    /// 印章按自己的宽高比缩进这个框里，**不拉伸** —— 印章一变形就不像印章了。
    /// </summary>
    private const float StampX = 1222f;
    private const float StampY = 1495f;
    private const float StampMaxWidth = 760f;
    private const float StampMaxHeight = 243f;

    /// <summary>提交成功后停留多久再自动回列表。</summary>
    private const float DoneDwellSeconds = 3f;

    /// <summary>
    /// 审批卡片左上角（Report 局部空间）。
    ///
    /// 卡片按 <see cref="ApprovalCardScale"/> 缩放后是 1012.5 x 1350，横向对准票面可视区中心
    /// （Report 局部 x = 282），上沿与可视区上沿齐平 —— 当成盖在票面上的模态卡片。
    /// 坐标不是推出来的是量出来的：根画布上挂着 VerticalLayoutGroup，
    /// 光看 anchoredPosition 推不出任何一个节点的实际位置。
    /// </summary>
    private const float ApprovalCardLeft = -224.25f;
    private const float ApprovalCardTop = 1617f;

    /// <summary>
    /// 审批卡片的缩放。
    ///
    /// 卡片 prefab 是 1350x1800，而这块画布本身只有 2941x1389 —— 原样放进去，
    /// 下面 400 多单位（约 0.35 m）掉在视野下边缘之外，"确认提交"按钮够不着。
    /// 0.75 之后高 1350，正好落在画布里；字号 44 缩到 33，和票面单元格的 46 是一个量级。
    /// </summary>
    private const float ApprovalCardScale = 0.75f;

    /// <summary>一个槽位：左标签右值，外加它们所在的单元格（换标签宽度时要用）。</summary>
    private struct Cell
    {
        public RectTransform Root;
        public TextMeshProUGUI Label;
        public TextMeshProUGUI Value;
    }

    /// <summary>票面上的一行内容。分段标题和普通字段的排版不一样，用 <see cref="IsHeader"/> 分。</summary>
    private struct Line
    {
        public string Label;
        public string Value;
        public bool IsHeader;
    }

    private readonly List<Cell> cells = new List<Cell>();
    private readonly List<RectTransform> rows = new List<RectTransform>();

    /// <summary>当前这张票要显示的全部内容；超出 30 格的部分显示不出来，只在状态行报个数。</summary>
    private readonly List<Line> lines = new List<Line>();

    private bool cellsScanned;
    private bool chromeBuilt;

    private JobTicket currentTicket;
    private PortalTicketDetail currentDetail;

    /// <summary>票面没拉成功时服务端那句话。点审批按钮时要原样转述，不能只说"不可审批"。</summary>
    private string detailError;
    private Coroutine loading;
    private Coroutine autoBack;
    private ApprovalPanel approvalPanel;

    private Button backButton;
    private TextMeshProUGUI titleText;
    private TextMeshProUGUI statusText;
    private RawImage signatureStamp;

    private void Start()
    {
        if (passButton != null) passButton.onClick.AddListener(OnPass);
        if (rejectButton != null) rejectButton.onClick.AddListener(OnReject);

        EnsureCells();
        ApplySkin();
        EnsureChrome();
        Hide();
    }

    /// <summary>
    /// 打开一张票：先把已知的列表信息铺上，再去拉票面。
    ///
    /// 【为什么先铺再拉】三步调用要几个往返，期间面板不能是空白或者上一张票的内容。
    /// 先用列表那一行（作业类型、单位、申请时间…）填出一个可读的骨架，
    /// 拉到票面再整体换掉。
    /// </summary>
    public void Show(JobTicket ticket)
    {
        currentTicket = ticket;
        currentDetail = null;                 // 上一张票的审批上下文绝不能留到这一张上
        detailError = null;
        CancelAutoBack();                     // 上一张票的「3 秒后回列表」不能把这一张也关掉
        if (approvalPanel != null) approvalPanel.Close();
        SetButtonsVisible(true);

        if (panelRoot != null) panelRoot.SetActive(true);
        else gameObject.SetActive(true);

        SetButtonsEnabled(false);

        if (ticket == null)
        {
            RenderMessage("没有选中作业票");
            return;
        }

        // 签名库是懒加载的。空着的时候每次都重扫一遍：现场 adb push 完图不用重启应用。
        // 一旦扫到图就不再重扫 —— 重扫会重新 new Texture2D，反复扫会攒一堆没人释放的贴图。
        PortalSignatureConfig.EnsureLoaded();
        if (PortalSignatureConfig.IsEmpty) PortalSignatureConfig.Reload();

        Render(Compose(ticket, null), "正在加载票面…");

        if (loading != null) StopCoroutine(loading);
        loading = StartCoroutine(LoadDetail(ticket));
    }

    private IEnumerator LoadDetail(JobTicket ticket)
    {
        PortalJobTicketDetailService.Outcome outcome = new PortalJobTicketDetailService.Outcome();
        yield return PortalJobTicketDetailService.Fetch(ticket, outcome);

        loading = null;

        // 拉的过程中用户可能已经点开了另一张票，这一份就过期了，丢掉
        if (currentTicket != ticket) yield break;

        if (!outcome.Ok || outcome.Detail == null)
        {
            // 失败时保留列表那份骨架，只把原因写在状态行 —— 总比整页空白强。
            // 按钮照样放开：处置是本机的，不依赖票面细节（实测有些票服务端根本没有流程记录）。
            detailError = outcome.Error ?? "票面加载失败";
            Render(Compose(ticket, null), detailError + "。仍可做本机处置");
            SetButtonsEnabled(true);
            yield break;
        }

        RenderDetail(outcome.Detail, ticket);
    }

    // ------------------------------------------------------------------ 渲染

    /// <summary>
    /// 把票面铺进槽位。
    ///
    /// 顺序刻意是「当前环节 → 基础信息 → 其余」：审批人先要知道轮到哪一环、前一环写了什么，
    /// 再看票的内容。服务端给的段顺序是表单编辑器里的顺序，不是审批时的关注顺序。
    /// </summary>
    private void RenderDetail(PortalTicketDetail detail, JobTicket ticket)
    {
        currentDetail = detail;
        Render(Compose(ticket, detail), NoteFor(ticket));
        SetButtonsEnabled(true);
    }

    /// <summary>
    /// 拼出票面要显示的全部内容：本机处置结果在最前，然后才是票本身。
    ///
    /// 【为什么处置结果排在最前】它是这台眼镜上唯一的留痕 —— 结果不回传门户，
    /// 服务端那边永远是"没动过"。翻到第二页才看得见的话，同一张票会被反复处置，
    /// 而且没人能发现。排在第一屏第一段，一打开就知道"这张我处置过了、当时写的是什么"。
    ///
    /// <paramref name="detail"/> 为 null 表示票面还没拉到（或者拉失败了），
    /// 用列表那一行的信息凑骨架 —— 实测有相当一部分票服务端会回"流程记录未找到"，
    /// 那些票同样处置得了，也同样要能回看处置结果。
    /// </summary>
    private List<Line> Compose(JobTicket ticket, PortalTicketDetail detail)
    {
        List<Line> items = new List<Line>();
        items.AddRange(LocalApproval(ticket));

        if (detail == null) items.AddRange(FromListRow(ticket));
        else items.AddRange(FromDetail(ticket, detail));

        return items;
    }

    /// <summary>
    /// 本机处置那一段。没处置过就返回空，一行都不占。
    ///
    /// 这里的「电子签名」一行给的是文字（签名图的文件名，也就是签名人姓名），
    /// 图本身盖在票面右上角，见 <see cref="ShowStamp"/> —— 两者都要：
    /// 图被删掉或改过名时章盖不出来，那一行文字仍然说得清是谁签的。
    /// </summary>
    private static List<Line> LocalApproval(JobTicket ticket)
    {
        List<Line> items = new List<Line>();

        JobTicketApprovalEntry entry = ticket != null ? JobTicketApprovalStore.Get(ticket.sid) : null;
        if (entry == null) return items;

        items.Add(Head("本机处置"));
        items.Add(Pair("处置结论", entry.passed ? "通过" : "不通过"));
        items.Add(Pair("审批意见", string.IsNullOrEmpty(entry.opinion) ? "未填写" : entry.opinion));
        items.Add(Pair("电子签名", string.IsNullOrEmpty(entry.signatureId)
            ? "无（签名库为空，当时免签）" : entry.signatureId));
        items.Add(Pair("处置时间", entry.time));
        if (!string.IsNullOrEmpty(entry.taskName)) items.Add(Pair("处置环节", entry.taskName));

        return items;
    }

    private static List<Line> FromDetail(JobTicket ticket, PortalTicketDetail detail)
    {
        List<Line> items = new List<Line>();

        items.Add(Head("作业票"));
        items.Add(Pair("名称", ticket.jobTicket));
        items.Add(Pair("作业类型", ticket.jobTicketType));
        items.Add(Pair("申请单位", ticket.applyingUnit));
        items.Add(Pair("当前环节", detail.TaskName));

        foreach (PortalTicketDetail.Section section in detail.Sections)
        {
            if (section.IsAttachment)
            {
                // 附件本体是任务 5 的事，这里只说明有几个，免得用户以为票面缺了东西
                items.Add(Pair(section.Title, section.Attachments.Count + " 个附件"));
                continue;
            }

            if (section.Rows.Count > 0)
            {
                // 多行段（安全措施实测 10 行 x 4 字段）铺开就是 40 格，把整个票面挤没了。
                // 先给一行汇总，明细留给后续的专门列表。
                items.Add(Pair(section.Title, "共 " + section.Rows.Count + " 条"));
                continue;
            }

            if (section.Entries.Count == 0) continue;

            items.Add(Head(section.Title));
            foreach (PortalTicketDetail.Entry entry in section.Entries)
            {
                items.Add(Pair(entry.Label, entry.Value));
            }
        }

        return items;
    }

    /// <summary>
    /// 换一张票（或换一次状态文案）。
    ///
    /// 【状态文案为什么不占槽位】旧版把它写进最后一个单元格、标签硬编码成"说明"，
    /// 看起来就像票上多了一个叫「说明」的字段。它说的是"这张票在本机的处置状态"，
    /// 是整页的事，所以挪到标题下面那行去了。
    /// </summary>
    private void Render(List<Line> items, string status)
    {
        lines.Clear();
        if (items != null) lines.AddRange(items);

        ShowStamp(currentTicket);

        int overflow = RenderLines();
        if (statusText != null)
        {
            string text = status ?? string.Empty;
            if (overflow > 0) text += "（票面还有 " + overflow + " 项这一屏放不下）";
            statusText.text = text;
        }
    }

    /// <summary>
    /// 把这张票的签名图盖在右上角。没处置过、没选签名、或者签名图已经不在签名库里，就不盖。
    ///
    /// 【为什么不放进单元格里】票面那 30 个槽位是「恰好两个 TMP」的固定形状，
    /// 运行时和编辑器自检都按这个形状认槽位（<see cref="EnsureCells"/> /
    /// <c>SpecialJobTicketChecks.CheckCells</c>）。往格子里塞图，值那一格还只有 780 宽、58 高，
    /// 印章缩到这个尺寸就是一小条红痕，看不出盖了什么。
    ///
    /// 【签名图可能找不到】存档里记的是图的文件名。图是现场自己放的，可能被删掉、改名，
    /// 或者当时处置用的是设备目录那份而这次跑的是随包那份。找不到就只是不盖章 ——
    /// 「电子签名」那一行的文字还在，谁签的仍然看得见。
    /// </summary>
    private void ShowStamp(JobTicket ticket)
    {
        if (signatureStamp == null) return;

        JobTicketApprovalEntry entry = ticket != null ? JobTicketApprovalStore.Get(ticket.sid) : null;
        PortalSignatureConfig.Option option = entry != null && !string.IsNullOrEmpty(entry.signatureId)
            ? PortalSignatureConfig.Find(entry.signatureId)
            : null;

        Texture2D texture = option != null ? option.Texture : null;
        if (texture == null || texture.width <= 0 || texture.height <= 0)
        {
            signatureStamp.gameObject.SetActive(false);
            return;
        }

        float scale = Mathf.Min(StampMaxWidth / texture.width, StampMaxHeight / texture.height);
        RectTransform rt = (RectTransform)signatureStamp.transform;
        rt.sizeDelta = new Vector2(texture.width * scale, texture.height * scale);

        signatureStamp.texture = texture;
        signatureStamp.gameObject.SetActive(true);
    }

    /// <summary>
    /// 一次铺满 30 个槽位。装不下的部分只能截断（票面不翻页），
    /// 但**必须说出来**：默默少几行的话，用户看到的是一张缺字段的票，没法判断是票上没有
    /// 还是屏幕放不下。剩几项写在状态行里。
    /// </summary>
    /// <returns>没显示出来的条数，0 表示全显示了。</returns>
    private int RenderLines()
    {
        EnsureCells();
        if (cells.Count == 0)
        {
            Debug.LogError("[票面] 没有找到任何「标签+值」槽位，检查 fieldGridRoot 是否接线");
            return 0;
        }

        for (int i = 0; i < cells.Count; i++)
        {
            if (i < lines.Count) Write(cells[i], lines[i]);
            else Write(cells[i], new Line());
        }

        // 【为什么要手动排版】分段标题那一格的标签是整格宽、普通字段是 460 宽，换一张票时同一个
        // 单元格可能从一种变成另一种。子节点改了尺寸，父布局组是不知道的 ——
        // 平时靠 ContentSizeFitter 把布局标脏，而那些 Fitter 正是这次改版关掉的。
        // 不排这一次，表头行的值会停在上一张票的横坐标上。
        RectTransform panel = fieldGridRoot as RectTransform;
        if (panel != null) LayoutRebuilder.ForceRebuildLayoutImmediate(panel);

        return Mathf.Max(0, lines.Count - cells.Count);
    }

    /// <summary>
    /// 一格的内容与排版。
    ///
    /// 分段标题（<see cref="Line.IsHeader"/>）占满整格：段名可能有十来个字，
    /// 挤在 460 宽的标签列里会被省略号截掉，而段名恰恰是定位内容用的。
    /// </summary>
    private static void Write(Cell cell, Line line)
    {
        if (cell.Label != null)
        {
            cell.Label.text = line.Label ?? string.Empty;
            cell.Label.fontSize = line.IsHeader ? HeaderSize : LabelSize;
            cell.Label.fontStyle = line.IsHeader ? FontStyles.Bold : FontStyles.Normal;
            cell.Label.color = line.IsHeader ? Color.white : LabelColor;
            SetWidth(cell.Label.rectTransform, line.IsHeader ? ColumnWidth : LabelWidth);
        }

        if (cell.Value != null)
        {
            cell.Value.text = line.IsHeader ? string.Empty : (line.Value ?? string.Empty);
        }
    }

    /// <summary>
    /// 票面上那行状态说明：只说下一步能干什么，明细交给正文里的「本机处置」那一段。
    ///
    /// 【为什么不在这儿复述结果】这行只有两行高，结论 + 意见 + 签名 + 时间塞进去必然被截断，
    /// 而截断掉的恰恰是意见和签名 —— 那才是回看时要核对的东西。
    /// </summary>
    private string NoteFor(JobTicket ticket)
    {
        if (ticket != null && JobTicketApprovalStore.Has(ticket.sid))
        {
            return "这张票在本机处置过，明细见正文「本机处置」。再点「通过 / 不通过」会覆盖它";
        }

        return "点「通过 / 不通过」填意见和签名，结果只记在本机、不回传门户";
    }

    private void RenderMessage(string message)
    {
        Render(new List<Line>(), message);
    }

    private static Line Head(string title)
    {
        return new Line { Label = title, Value = string.Empty, IsHeader = true };
    }

    private static Line Pair(string label, string value)
    {
        return new Line { Label = label, Value = value ?? string.Empty };
    }

    /// <summary>票面还没拉到时的骨架，用列表行里已有的信息。</summary>
    private static List<Line> FromListRow(JobTicket ticket)
    {
        return new List<Line>
        {
            Head("作业票"),
            Pair("名称", ticket.jobTicket),
            Pair("作业类型", ticket.jobTicketType),
            Pair("申请单位", ticket.applyingUnit),
            Pair("作业单位", ticket.jobTicketUnit),
            Pair("申请时间", ticket.applyTime),
            Pair("当前环节", ticket.jobTicketStatus),
            Pair("作业内容", ticket.jobTicketContent),
        };
    }

    // ------------------------------------------------------------------ 槽位与皮肤

    /// <summary>
    /// 扫出场景里的槽位。层级是 <c>容器 &gt; 行 &gt; 单元格 &gt; [标签, 值]</c>，
    /// 只认「正好有两个 TMP 子节点」的单元格，多一个少一个都跳过 ——
    /// 场景以后加了装饰节点也不至于把标签写到别处去。
    ///
    /// 只收前 <see cref="VisibleRows"/> 行：后面几行是收起来的，写进去没人看得见。
    /// </summary>
    private void EnsureCells()
    {
        if (cellsScanned) return;
        cellsScanned = true;
        cells.Clear();
        rows.Clear();

        Transform root = fieldGridRoot;
        if (root == null)
        {
            Debug.LogError("[票面] fieldGridRoot 没有接线，票面无法显示");
            return;
        }

        for (int r = 0; r < root.childCount; r++)
        {
            Transform row = root.GetChild(r);
            rows.Add(row as RectTransform);
            if (r >= VisibleRows) continue;

            for (int c = 0; c < row.childCount; c++)
            {
                Transform cell = row.GetChild(c);
                TextMeshProUGUI[] texts = cell.GetComponentsInChildren<TextMeshProUGUI>(true);
                if (texts.Length != 2) continue;
                cells.Add(new Cell
                {
                    Root = cell as RectTransform,
                    Label = texts[0],
                    Value = texts[1],
                });
            }
        }

        Debug.Log($"[票面] 扫到 {cells.Count} 个标签/值槽位（{rows.Count} 行里用前 {VisibleRows} 行）");
    }

    /// <summary>
    /// 把场景里那套「宽度随文字走」的排版换成定宽栅格。
    ///
    /// 【为什么必须在运行时改而不是改场景】要改的是 11 行 x 2 格 x 2 个文本 = 44 个节点上的
    /// ContentSizeFitter、宽度、字号、颜色，手写 YAML 补丁得改一百多处，错一处不报错、
    /// 只表现为"某一行没对齐"。而且这些值和这里的常量本来就是一组的，放一起才改得动。
    ///
    /// 【为什么要关掉 ContentSizeFitter】每个单元格和每个 TMP 上都挂着一个 preferred 模式的
    /// Fitter：单元格宽 = 标签宽 + 90 + 值宽，而标签和值又各自按文字量伸缩。于是每行的值
    /// 从不同的 x 起头，第二列被第一列顶得忽左忽右；值那一侧更糟 —— 开了自动换行也不会换，
    /// 因为 Fitter 先把宽度撑到了单行所需，长文本能一路顶出画布。
    /// 关掉之后宽度由这里写死，两列才对得齐，超长文本走省略号。
    /// </summary>
    private void ApplySkin()
    {
        for (int i = 0; i < rows.Count; i++)
        {
            RectTransform row = rows[i];
            if (row == null) continue;

            bool visible = i < VisibleRows;
            if (row.gameObject.activeSelf != visible) row.gameObject.SetActive(visible);
            if (!visible) continue;

            HorizontalLayoutGroup group = row.GetComponent<HorizontalLayoutGroup>();
            if (group != null)
            {
                group.spacing = ColumnGap;
                group.childForceExpandWidth = false;
                group.childForceExpandHeight = false;
            }
            SetSize(row, RowWidth, RowHeight);
        }

        for (int i = 0; i < cells.Count; i++) SkinCell(cells[i]);
    }

    private void SkinCell(Cell cell)
    {
        if (cell.Root == null) return;

        Disable<ContentSizeFitter>(cell.Root);

        HorizontalLayoutGroup group = cell.Root.GetComponent<HorizontalLayoutGroup>();
        if (group != null)
        {
            group.spacing = CellSpacing;
            group.childAlignment = TextAnchor.MiddleLeft;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;
        }
        SetSize(cell.Root, ColumnWidth, RowHeight);

        SkinText(cell.Label, LabelWidth, LabelSize, LabelColor);
        SkinText(cell.Value, ValueWidth, ValueSize, ValueColor);
    }

    private static void SkinText(TextMeshProUGUI text, float width, float size, Color color)
    {
        if (text == null) return;

        Disable<ContentSizeFitter>(text.rectTransform);

        text.fontSize = size;
        text.color = color;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.enableWordWrapping = false;
        // 超长的值宁可截断也不能撑破栅格：真机上一行"作业内容"能有五六十个字
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.fontStyle = FontStyles.Normal;
        text.raycastTarget = false;

        SetSize(text.rectTransform, width, RowHeight);
    }

    private static void SetSize(RectTransform rt, float width, float height)
    {
        if (rt == null) return;
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
    }

    private static void SetWidth(RectTransform rt, float width)
    {
        if (rt == null) return;
        if (Mathf.Abs(rt.rect.width - width) < 0.5f) return;
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
    }

    private static void Disable<T>(Component host) where T : Behaviour
    {
        if (host == null) return;
        T component = host.GetComponent<T>();
        if (component != null) component.enabled = false;
    }

    // ------------------------------------------------------------------ 标题栏 / 底部栏

    /// <summary>
    /// 造标题、状态行、签名印章和返回按钮。
    ///
    /// 【为什么挂在 Report 下而不是票面卡片里】卡片上挂着 VerticalLayoutGroup，
    /// 任何新子节点都会被当成一行排进去（除非再加 LayoutElement 忽略），而且会打乱
    /// <see cref="EnsureCells"/> 按子节点下标认行的规则。两个审批按钮本来就是 Report 的
    /// 直接子节点，跟着它们放，坐标体系也一致。
    ///
    /// 【为什么运行时造而不是摆进场景】场景里那两个审批按钮是纯图片按钮，文字烘在贴图里
    /// （"审批通过" / "不通过"），复制一份得到的是写着"不通过"的返回键。重新做贴图要改美术
    /// 资源，而这里只要一个半透明底 + 一行字，字体从票面单元格上抄，样式自然一致。
    /// </summary>
    private void EnsureChrome()
    {
        if (chromeBuilt) return;

        RectTransform sibling = passButton != null ? passButton.transform as RectTransform : null;
        if (sibling == null)
        {
            Debug.LogError("[票面] passButton 没接线，标题栏和返回按钮无处安放");
            return;
        }

        chromeBuilt = true;
        Transform parent = sibling.parent;
        TMP_FontAsset font = BorrowFont();

        titleText = MakeText(parent, sibling, "DetailTitle", TitleX, TitleY, TitleWidth, 90f,
            TitleSize, Color.white, font, TextAlignmentOptions.MidlineLeft, false);
        titleText.text = "作业票详情";

        // 状态行可能比较长，留两行的高度
        statusText = MakeText(parent, sibling, "DetailStatus", StatusX, StatusY, StatusWidth, 100f,
            StatusSize, StatusColor, font, TextAlignmentOptions.TopLeft, true);

        RectTransform stamp = Frame(parent, sibling, "SignatureStamp",
            StampX, StampY, StampMaxWidth, StampMaxHeight);
        signatureStamp = stamp.gameObject.AddComponent<RawImage>();
        signatureStamp.raycastTarget = false;     // 纯展示，别把射线吃掉
        stamp.gameObject.SetActive(false);

        backButton = MakeButton(parent, sibling, "BackButton", BackButtonX, BackButtonWidth,
            "返回", font, GoBack);
    }

    /// <summary>
    /// 按审批按钮的锚点/旋转/缩放造一个同一坐标系里的框。
    ///
    /// 这套 UI 是摆在 3D 空间里的世界画布，父节点 Report 的 rect 是个零尺寸的点 ——
    /// 抄兄弟节点的 anchor 是唯一稳妥的做法，自己设一套锚点算出来的位置会差一整块面板。
    /// </summary>
    private static RectTransform Frame(Transform parent, RectTransform sibling, string name,
                                       float centerX, float centerY, float width, float height)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = sibling.anchorMin;
        rt.anchorMax = sibling.anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(width, height);
        rt.anchoredPosition = new Vector2(centerX, centerY);
        rt.localRotation = sibling.localRotation;
        rt.localScale = sibling.localScale;
        return rt;
    }

    private static TextMeshProUGUI MakeText(Transform parent, RectTransform sibling, string name,
                                            float centerX, float centerY, float width, float height,
                                            float size, Color color, TMP_FontAsset font,
                                            TextAlignmentOptions align, bool wrap)
    {
        RectTransform rt = Frame(parent, sibling, name, centerX, centerY, width, height);
        TextMeshProUGUI text = rt.gameObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.color = color;
        text.alignment = align;
        text.enableWordWrapping = wrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        if (font != null) text.font = font;
        return text;
    }

    private Button MakeButton(Transform parent, RectTransform sibling, string name,
                              float centerX, float width, string caption,
                              TMP_FontAsset font, UnityEngine.Events.UnityAction onClick)
    {
        RectTransform rt = Frame(parent, sibling, name, centerX, ButtonY, width, FlatButtonHeight);

        Image background = rt.gameObject.AddComponent<Image>();
        background.color = ButtonColor;
        background.raycastTarget = true;

        Button button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(onClick);

        GameObject labelGo = new GameObject("Label", typeof(RectTransform));
        RectTransform labelRt = (RectTransform)labelGo.transform;
        labelRt.SetParent(rt, false);
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;

        TextMeshProUGUI label = labelGo.AddComponent<TextMeshProUGUI>();
        label.text = caption;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.fontSize = ButtonTextSize;
        label.raycastTarget = false;
        if (font != null) label.font = font;

        return button;
    }

    /// <summary>从票面单元格上借字体，免得再引一份资源、也免得字重和票面对不上。</summary>
    private TMP_FontAsset BorrowFont()
    {
        EnsureCells();
        for (int i = 0; i < cells.Count; i++)
        {
            if (cells[i].Label != null && cells[i].Label.font != null) return cells[i].Label.font;
        }
        return null;
    }

    // ------------------------------------------------------------------ 按钮

    private void SetButtonsEnabled(bool enabled)
    {
        if (passButton != null) passButton.interactable = enabled;
        if (rejectButton != null) rejectButton.interactable = enabled;
    }

    /// <summary>
    /// 底部栏的显隐。审批卡片盖上来的时候整条收起 ——
    /// 卡片是模态的，留着半截露在外面的按钮只会让人点错。
    /// </summary>
    private void SetButtonsVisible(bool visible)
    {
        if (passButton != null) passButton.gameObject.SetActive(visible);
        if (rejectButton != null) rejectButton.gameObject.SetActive(visible);
        if (backButton != null) backButton.gameObject.SetActive(visible);
    }

    private void OnPass()
    {
        OpenApproval(true);
    }

    private void OnReject()
    {
        OpenApproval(false);
    }

    /// <summary>
    /// 打开审批卡片。结论由点的是哪个按钮决定，意见和签名在卡片里补。
    /// </summary>
    private void OpenApproval(bool passed)
    {
        // 票面没拉下来也照做：处置是本机的，只要有票 sid 就存得下。
        // 实测相当一部分票服务端会回"流程记录未找到"，那些票同样需要能处置。
        if (currentTicket == null || string.IsNullOrEmpty(currentTicket.sid))
        {
            Debug.LogWarning("[票面] 这张票没有 sid，处置结果无处可存");
            return;
        }

        if (!EnsureApprovalPanel()) return;

        SetButtonsVisible(false);

        JobTicket ticket = currentTicket;
        approvalPanel.Open(currentDetail, ticket.sid, passed, submitted =>
        {
            if (!submitted)
            {
                SetButtonsVisible(true);
                return;
            }

            // 让宿主重新拉一次列表：处置只记在本机，服务端状态不会变，
            // 这张票本来就该还在列表里 —— 重拉是为了让行上的本机处置标记刷出来。
            SetButtonsVisible(true);
            // 重新铺一遍：这一次开头会多出「本机处置」那一段，用户当场就看得见记下了什么
            Render(Compose(ticket, currentDetail),
                (passed ? "已记录「通过」" : "已记录「不通过」") + "，操作完成，3 秒后返回列表");
            OnPassAction?.Invoke(ticket);

            CancelAutoBack();
            autoBack = StartCoroutine(BackAfterDwell());
        });
    }

    /// <summary>
    /// 停留几秒让人看清结果再回列表。
    ///
    /// 【为什么不立刻返回】处置是要留痕的，用户需要一个能看清「到底记下没有」的瞬间；
    /// 立刻切走的话，成功和失败在屏幕上都只是一闪而过。
    /// </summary>
    private IEnumerator BackAfterDwell()
    {
        yield return new WaitForSeconds(DoneDwellSeconds);
        autoBack = null;
        GoBack();
    }

    private void CancelAutoBack()
    {
        if (autoBack == null) return;
        StopCoroutine(autoBack);
        autoBack = null;
    }

    /// <summary>关掉票面、让宿主把列表放回来。返回按钮和自动返回都走这里。</summary>
    private void GoBack()
    {
        Hide();
        OnBackAction?.Invoke();
    }

    private bool EnsureApprovalPanel()
    {
        if (approvalPanel != null) return true;

        if (approvalPrefab == null || approvalParent == null)
        {
            Debug.LogError("[票面] approvalPrefab / approvalParent 没接线，审批卡片打不开");
            return false;
        }

        approvalPanel = Instantiate(approvalPrefab, approvalParent);
        RectTransform card = approvalPanel.transform as RectTransform;
        if (card != null)
        {
            // prefab 里那个位置是票面还挂在右侧副屏时定的。票面搬到正前方之后，
            // 卡片跟着搬：横向对准票面中心、上沿与票面齐平，当成盖在票面上的模态卡片。
            card.localRotation = Quaternion.identity;
            card.localPosition = new Vector3(card.localPosition.x, card.localPosition.y, 0f);
            card.anchoredPosition = new Vector2(ApprovalCardLeft, ApprovalCardTop);
        }
        approvalPanel.transform.localScale = Vector3.one * ApprovalCardScale;
        return true;
    }

    public void Hide()
    {
        if (loading != null)
        {
            StopCoroutine(loading);
            loading = null;
        }
        currentTicket = null;
        currentDetail = null;
        CancelAutoBack();
        if (approvalPanel != null) approvalPanel.Close();

        if (panelRoot != null) panelRoot.SetActive(false);
        else gameObject.SetActive(false);
    }
}
