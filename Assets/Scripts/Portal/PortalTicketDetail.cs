using System;
using System.Collections.Generic;

/// <summary>
/// 一张作业票拉全之后的只读形态：流程头 + 若干段 + 当前环节能做什么。
///
/// 这是 <see cref="PortalJobTicketDetailService"/> 的产物，UI 只认这个类，
/// 不碰 <c>JObject</c>，也不需要知道那三步调用和双重序列化。
/// </summary>
public sealed class PortalTicketDetail
{
    /// <summary>票面上的一行：左边中文标签，右边值。</summary>
    public sealed class Entry
    {
        public string Label;
        public string Value;

        /// <summary>
        /// 值是图片（签名/图片控件），<see cref="Value"/> 里放的是占位文案而不是 base64。
        /// 要真正显示图片是任务 5 的事，这里先把它和普通文本区分出来。
        /// </summary>
        public bool IsImage;
    }

    /// <summary>一个附件。</summary>
    public sealed class Attachment
    {
        public string FileName;

        /// <summary>下载路径，拼 <c>attach/download?path=</c> 用。</summary>
        public string FilePath;

        public string FileType;

        /// <summary>看起来是图片（按扩展名/类型判断），任务 5 的查看器只收这些。</summary>
        public bool IsImage;
    }

    /// <summary>票面的一段。</summary>
    public sealed class Section
    {
        /// <summary>段标题，如「基础信息」「作业负责人」。</summary>
        public string Title;

        /// <summary>段的机器名，如 <c>dhzy_base</c>。</summary>
        public string Name;

        /// <summary>单条数据的段（<c>dataType=1</c>）：字段按表单顺序排好。</summary>
        public List<Entry> Entries = new List<Entry>();

        /// <summary>多条数据的段（<c>dataType=2</c>，如「安全措施」）：每行一组字段。</summary>
        public List<List<Entry>> Rows = new List<List<Entry>>();

        /// <summary>附件段的文件。</summary>
        public List<Attachment> Attachments = new List<Attachment>();

        public bool IsAttachment;

        /// <summary>这一段有没有东西可显示。</summary>
        public bool HasContent => Entries.Count > 0 || Rows.Count > 0 || Attachments.Count > 0;
    }

    public string Sid;
    public string FlowKey;

    /// <summary>当前环节名，如「作业负责人审批」。</summary>
    public string TaskName;

    /// <summary>提交审批要用的三个 id。</summary>
    public string FormKey;
    public string FormInsId;
    public string FlowInsId;
    public string TaskInsId;

    /// <summary>服务端下发的可用按钮（key → 中文），如 <c>audit</c> → 审核。</summary>
    public Dictionary<string, string> Buttons = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>票面各段，已按表单顺序排好，已剔除下拉数据源和空段。</summary>
    public List<Section> Sections = new List<Section>();

    /// <summary>
    /// 当前环节要写的 <c>vars</c> 键与数据路径（从表单定义里读出来的，不是猜的）。
    /// 为 null 表示这个环节不是审批节点，或者表单没声明 —— 两种情况都不该放开审批按钮。
    /// </summary>
    public PortalFormDefinition.ApprovalVar Approval;

    /// <summary>
    /// 现在能不能审批：服务端给了 <c>audit</c> 按钮，且表单声明了审批变量。
    ///
    /// 两个条件缺一不可。只看按钮会在拿不到 vars 键时提交一份服务端收下但不生效的审批
    /// （<c>result</c> 恒为 true，不会报错）；只看 vars 键会在服务端本来就不让审批时放开按钮。
    /// </summary>
    /// <summary>
    /// 当前环节要提交的流程变量，键取自 <c>configs.expVars</c>，值从票面数据里原样取出。
    ///
    /// 提交时只有审批结果那一条（<see cref="Approval"/> 指向的键）换成用户的选择，
    /// 其余（如 <c>business_sid</c>）必须照原样带回去 —— 服务端对 vars 缺键不报错，
    /// 缺了只会静默不生效，从客户端看不出区别。
    /// </summary>
    public readonly Dictionary<string, string> FlowVars =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public bool CanAudit => Buttons.ContainsKey("audit") && Approval != null
                            && !string.IsNullOrEmpty(ApprovalDataDefId);

    /// <summary>
    /// 不能审批的原因，可审批时为 null。
    ///
    /// 【当前不参与门控】2026-09-23 起处置只写本机，所有票都能操作，这条和
    /// <see cref="CanAudit"/> 一样只是"门户是怎么说的"的记录。哪天把
    /// <c>PortalApprovalSubmitService</c> 接回去，门控要从这里重新长出来。
    ///
    /// 【为什么要有这个】<see cref="CanAudit"/> 是个 bool，界面上的表现只能是「按钮灰着」——
    /// 而 2026-09-23 真机实测，用户打开的每一张票服务端给的都是 <c>[submit/toclose]</c>
    /// 或者空，从来没给过 <c>audit</c>。灰按钮和「这功能坏了」在眼镜里长得一模一样，
    /// 用户连续报了三次「点击不了」。原因必须能上屏。
    /// </summary>
    public string AuditBlockReason
    {
        get
        {
            if (CanAudit) return null;

            if (!Buttons.ContainsKey("audit"))
            {
                string given = Buttons.Count > 0 ? string.Join("/", new List<string>(Buttons.Keys).ToArray()) : "无";
                return "当前账号在「" + (TaskName ?? "本环节") + "」没有审批权（服务端只给了：" + given + "）";
            }

            if (Approval == null) return "表单没声明这一环节的审批变量，提交了也不会生效";

            return "审批段的数据没拿到，提交会被当成新增而不是更新";
        }
    }

    /// <summary>审批段的 <c>dataDefId</c>，提交 <c>saveall</c> 要用。</summary>
    public string ApprovalDataDefId;

    /// <summary>
    /// 审批段拉下来时的原始 JSON。
    ///
    /// 提交走的是**读改写**：把这一份原样改掉审批结果和意见再发回去，而不是只发两个字段。
    /// 段里有 <c>SID</c>、<c>SYS_ORG_CODE</c> 等系统列，只发两个字段服务端会当新增插一行，
    /// 而不是更新当前这条审批记录。
    /// </summary>
    public string ApprovalRawData;

    /// <summary>段总数（含没内容被剔掉的），排查「票面怎么少了一段」时看这个。</summary>
    public int SectionsFetched;

    /// <summary>有多少段因为没有任何内容而没显示。</summary>
    public int SectionsEmpty;
}
