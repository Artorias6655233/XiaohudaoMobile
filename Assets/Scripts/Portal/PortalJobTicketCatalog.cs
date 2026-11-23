using System.Collections.Generic;

/// <summary>
/// 作业票的两张对照表：作业类型 → <c>flowKey</c>，动火作业的审批环节 → <c>vars</c> 键。
///
/// 【为什么是写死的表而不是拉字典】2026-09-22 实测，字典 <c>xhd_tszylx</c> 的取值
/// 和作业票列表**实际返回**的取值有 4 类对不上：
///
///   列表返回        字典给的
///   受限空间    ←→  受限空间作业
///   断路作业    ←→  断路安全作业
///   动土作业    ←→  动土安全作业
///   盲板抽堵    ←→  盲板抽堵作业
///
/// 而打开票用的是列表给的 <c>sid</c> 和 <c>jobTicketType</c>，所以**必须以列表的写法为准**。
/// 拿字典的写法去筛选，「受限空间」实测返回 0 条（实际有 63 条）。
/// 字典在这里既不能当筛选值也不能当映射源，所以不缓存它。
///
/// 两种写法都收进表里：列表哪天改口径，或者换个接口拿到的是字典写法，都还能解析出来。
///
/// 【审批环节那张表只对动火作业有效】其余 7 种作业类型的 <c>vars</c> 键名后端还没给
/// （缺口 A1），拿动火的键名套上去不会报错，只会静默不生效 —— 所以
/// <see cref="TryResolveFireWorkVarsKey"/> 只认动火，别的类型一律返回 false。
/// </summary>
public static class PortalJobTicketCatalog
{
    /// <summary>本期打通的作业类型。</summary>
    public const string FireWorkFlowKey = "fire_work";

    /// <summary>
    /// 作业类型 → flowKey。键同时收列表写法和字典写法。
    /// flowKey 取自接口文档 §3.2 的对照表。
    /// </summary>
    private static readonly Dictionary<string, string> FlowKeys = new Dictionary<string, string>
    {
        // 两边写法一致的 4 类
        { "动火作业", FireWorkFlowKey },
        { "高处作业", "high_place_work" },
        { "吊装安全作业", "lifting_work" },
        { "临时用电安全作业", "temporary_electricity_use" },

        // 两边写法不一致的 4 类：列表写法在前，字典写法在后
        { "受限空间", "limit_space" },
        { "受限空间作业", "limit_space" },
        { "断路作业", "circuit_break" },
        { "断路安全作业", "circuit_break" },
        { "动土作业", "soil_work" },
        { "动土安全作业", "soil_work" },
        { "盲板抽堵", "blind_plate_plugging" },
        { "盲板抽堵作业", "blind_plate_plugging" },
    };

    /// <summary>
    /// 动火作业各审批环节 → <c>vars</c> 键，取自接口文档 §3.3。
    /// 键用的是**作业票列表返回的** <c>jobTicketStatus</c> 写法，六个都在实测数据里出现过。
    ///
    /// 注意这里又是一套词汇：字典 <c>xhd_tszyzt</c> 给的是「副总领导审批」「作业前验收」，
    /// 而列表返回和文档用的是「副总审批」「动火前验票情况」。同样以列表为准。
    /// </summary>
    private static readonly Dictionary<string, string> FireWorkVarsKeys = new Dictionary<string, string>
    {
        { "作业负责人审批", "zyfzr_AuditResult" },
        { "所在部门审批", "bmfzr_AuditResult" },
        { "安全部审批", "aqbmshr_AuditResult" },
        { "副总审批", "fzld_AuditResult" },
        { "企业领导审批", "qyld_AuditResult" },
        { "动火前验票情况", "yp_AuditResult" },
    };

    /// <summary>
    /// 作业类型中文 → flowKey。认不出来返回 false，**不要猜** ——
    /// flowKey 是 URL 的一段，拼错了拿不到票面，而猜出来的值和真值长得一样合理。
    /// </summary>
    public static bool TryResolveFlowKey(string jobTicketType, out string flowKey)
    {
        flowKey = null;
        if (string.IsNullOrWhiteSpace(jobTicketType)) return false;
        return FlowKeys.TryGetValue(jobTicketType.Trim(), out flowKey);
    }

    /// <summary>这张票是不是本期支持审批的类型。</summary>
    public static bool IsFireWork(string jobTicketType)
    {
        return TryResolveFlowKey(jobTicketType, out string flowKey) && flowKey == FireWorkFlowKey;
    }

    /// <summary>
    /// 当前环节 → 提交审批要写的 <c>vars</c> 键。**只认动火作业**，理由见类注释。
    /// </summary>
    public static bool TryResolveFireWorkVarsKey(string jobTicketType, string jobTicketStatus,
        out string varsKey)
    {
        varsKey = null;
        if (!IsFireWork(jobTicketType)) return false;
        if (string.IsNullOrWhiteSpace(jobTicketStatus)) return false;
        return FireWorkVarsKeys.TryGetValue(jobTicketStatus.Trim(), out varsKey);
    }

    /// <summary>供 Editor 校验遍历用。</summary>
    public static IEnumerable<string> KnownJobTicketTypes => FlowKeys.Keys;

    /// <summary>供 Editor 校验遍历用。</summary>
    public static IEnumerable<string> FireWorkApprovalStatuses => FireWorkVarsKeys.Keys;
}
