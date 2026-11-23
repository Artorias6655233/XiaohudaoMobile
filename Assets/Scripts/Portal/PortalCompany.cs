using System.Collections.Generic;

/// <summary>
/// 把沙盘当前选中的企业换成门户接口要的 <c>applyingUnitId</c>。
///
/// 【为什么不用建映射表】2026-09-22 实测：门户组织机构树里企业节点的 <c>sid</c>，
/// 和跳板后端 <c>security/company/list</c> 的 <c>sysOrgCode</c> 是**同一套编码**——
/// 51 家企业全部对得上，没有一家对不上。所以不需要按名称建映射、不需要落配置，
/// 直接把 <c>sysOrgCode</c> 当 <c>applyingUnitId</c> 用即可。
/// 这条会不会哪天变，由 <c>PortalSmokeChecks</c> 的「企业编码同源」那一步盯着。
///
/// 【那还要这个类干什么】因为沙盘里传来传去的是 <c>Session.companyID</c>，
/// 那是企业的 <c>id</c>，**不是** <c>sysOrgCode</c>，两者 51 条没有一条相同。
/// 拿 <c>companyID</c> 去查作业票，结果是 0 条且不报错 —— 和「这家企业确实没有作业票」
/// 长得一模一样。所以这一步换算必须集中在一处，并且失败时要说得出原因。
///
/// 现有代码里这段换算已经散了两处写法（<c>Double_ReportList.ResolveSysOrgCode</c> 靠
/// 遍历 <c>sysOrgCodeToID</c> 反查、<c>SpecialJob_CertificateList</c> 走
/// <c>companyObjects</c>），本类统一成后者：<c>companyObjects</c> 是建企业图标时直接写入的，
/// 而 <c>sysOrgCodeToID</c> 要等某个特定面板跑过才有内容，先进哪个页面会影响结果。
/// </summary>
public static class PortalCompany
{
    /// <summary>换算失败的原因。每一种都要能让使用方说出一句人话，而不是显示空列表。</summary>
    public enum Failure
    {
        None,

        /// <summary>沙盘上还没选企业。</summary>
        NoSelection,

        /// <summary>企业目录还没加载（图标还没建出来）。</summary>
        DirectoryNotReady,

        /// <summary>选中的企业不在目录里。理论上不该发生，发生了就是有别的地方在改 companyID。</summary>
        NotInDirectory,

        /// <summary>目录里有这家企业，但它没有 sysOrgCode，换不出编码。</summary>
        NoOrgCode,
    }

    /// <summary>
    /// 取当前选中企业的 <c>applyingUnitId</c>。
    /// </summary>
    /// <param name="unitId">换算结果；失败时为 null。</param>
    /// <param name="failure">失败原因，用 <see cref="Describe"/> 转成提示文案。</param>
    /// <returns>换算成功返回 true。</returns>
    public static bool TryResolveCurrent(out string unitId, out Failure failure)
    {
        unitId = null;

        Session session = Session.Instance;
        if (session == null || string.IsNullOrEmpty(session.companyID))
        {
            failure = Failure.NoSelection;
            return false;
        }

        Dictionary<string, CompanyObject> directory = session.companyObjects;
        if (directory == null || directory.Count == 0)
        {
            failure = Failure.DirectoryNotReady;
            return false;
        }

        if (!directory.TryGetValue(session.companyID, out CompanyObject company) ||
            company == null || company.companyDetails == null)
        {
            failure = Failure.NotInDirectory;
            return false;
        }

        string code = company.companyDetails.sysOrgCode;
        if (string.IsNullOrWhiteSpace(code))
        {
            failure = Failure.NoOrgCode;
            return false;
        }

        unitId = code.Trim();
        failure = Failure.None;
        return true;
    }

    /// <summary>
    /// 失败原因转提示文案。
    ///
    /// 刻意区分这几句：用户看到「请在沙盘上选择企业」会去点企业，看到「企业目录还没加载完」
    /// 会等一下，看到「未关联门户组织」才知道要找人。全都显示成空列表就只能靠猜。
    /// </summary>
    public static string Describe(Failure failure)
    {
        switch (failure)
        {
            case Failure.NoSelection: return "请在沙盘上选择企业";
            case Failure.DirectoryNotReady: return "企业目录还没加载完，请稍候";
            case Failure.NotInDirectory: return "选中的企业不在企业目录里";
            case Failure.NoOrgCode: return "该企业未关联门户组织编码";
            default: return string.Empty;
        }
    }
}
