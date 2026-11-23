using System;
using System.Collections.Generic;

/// <summary>
/// 企业证件管理的业务模型。
///
/// 【为什么另起一套而不是复用 <see cref="Certificate"/>】
/// 跳板后端的 <c>specialJob/certificate/table/list</c> 确实有这张表，但它当不了这个功能的数据源：
///   1. 全量只有 5 条，51 家企业里只覆盖 5 家，每家 1 本证；
///   2. <c>personName</c> 里存的是人员 ID（形如 1888883793875902465），不是姓名，
///      <c>companyInfo</c> / <c>thirdParty</c> 同理，直接渲染出来是一串数字；
///   3. 没有「持证人数」口径——按证件条数算人数会把一人多证的人重复计数。
/// 门户后端（www.nsxhcip.cn）那边整个功能域也没有支撑，见接口文档第八节缺口清单 C1–C4。
/// 所以这一版用演示数据跑通口径和交互，接口到位后换 <see cref="ISpecialJobCertSource"/> 的实现即可。
/// </summary>
public static class CertStatus
{
    public const string Valid = "有效";
    public const string Expiring = "临期";
    public const string Expired = "已过期";

    /// <summary>距到期多少天内算「临期」。演示口径，正式接口以服务端返回的状态为准。</summary>
    public const int ExpiringWithinDays = 30;

    public static bool IsKnown(string value) =>
        value == Valid || value == Expiring || value == Expired;
}

/// <summary>
/// 列表里的一行：一个人的一本证。
///
/// 人和证是一对多，但列表按「证」平铺，所以 <see cref="personId"/> 会重复——
/// 统计人数必须按它去重，不能拿行数当人数。
/// </summary>
[Serializable]
public sealed class CertRow
{
    public string certId;
    public string personId;
    public string personName;
    public string phone;

    /// <summary>从业年限。</summary>
    public int workYears;

    /// <summary>证件类型中文名，如「特种作业操作证（焊接与热切割作业）」。</summary>
    public string certTypeName;

    /// <summary>准操项目，如「熔化焊接与热切割」。详情卡上的「证件名称」那一栏。</summary>
    public string certName;

    public string certNumber;

    /// <summary>发证日期，yyyy-MM-dd。</summary>
    public string issueDate;

    /// <summary>有效期至，yyyy-MM-dd。</summary>
    public string expireDate;

    /// <summary>有效 / 临期 / 已过期。演示数据由本地按日期算，正式接口取服务端返回值。</summary>
    public string statusText;

    /// <summary>第三方（承包商）单位名称，自有员工为空。</summary>
    public string thirdParty;

    /// <summary>管理单位，通常是发证或备案的主体。</summary>
    public string managementUnit;
}

/// <summary>一家企业的证件汇总。人数和证件数分开给，不互相替代。</summary>
public sealed class CompanyCertSummary
{
    public static readonly CompanyCertSummary Empty =
        new CompanyCertSummary(0, 0, new List<CertRow>());

    public CompanyCertSummary(int holderCount, int certCount, IReadOnlyList<CertRow> rows)
    {
        this.holderCount = holderCount;
        this.certCount = certCount;
        this.rows = rows ?? new List<CertRow>();
    }

    /// <summary>持证人数，按 <see cref="CertRow.personId"/> 去重。</summary>
    public int holderCount { get; }

    /// <summary>证件本数，等于行数。</summary>
    public int certCount { get; }

    public IReadOnlyList<CertRow> rows { get; }

    public bool IsEmpty => certCount == 0;
}
