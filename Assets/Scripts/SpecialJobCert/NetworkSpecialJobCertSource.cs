using System;
using System.Collections;

/// <summary>
/// 正式接口的数据源。**接口还不存在，所以这里一律抛。**
///
/// 缺的是这四件事（接口文档第八节缺口清单 C1–C4，已转给后端）：
///   C1 按企业查持证人员列表（入参企业编码 + 分页；返回人员、证件类型、编号、发证日期、有效期至、状态、附件）
///   C2 持证人数统计（按人员 ID 去重，人数和证件数分开给）
///   C3 证件状态由服务端判定，不要客户端算日期
///   C4 证件附件读取
///
/// 现有的两个人员接口都顶不上：<c>companyEmployeeInfo/lists</c> 和 <c>getReportPerson</c>
/// 都不接企业参数、不返回证件字段、无分页，返回的是「当前登录账号所属企业」的人员。
///
/// 【为什么不悄悄退回 Mock】退回去的话，接口上线当天没人会发现页面还在演示数据上，
/// 现场就会拿演示数据当台账用。宁可当场报错。
/// </summary>
public sealed class NetworkSpecialJobCertSource : ISpecialJobCertSource
{
    private const string NotImplemented =
        "企业证件管理的正式接口尚未提供（缺口 C1–C4），当前只能用 certDataMode=mock";

    public bool IsDemo => false;

    public bool Ready => false;

    public void Load() => throw new NotSupportedException(NotImplemented);

    public IEnumerator LoadAsync() => throw new NotSupportedException(NotImplemented);

    public CompanyCertSummary GetByCompany(string companyCode) =>
        throw new NotSupportedException(NotImplemented);
}
