using System.Collections;

/// <summary>
/// 企业证件数据的业务边界。页面只认这个接口，不认它背后是演示包还是接口。
///
/// 形状照 <see cref="IP0DataSource"/>：同步 <see cref="Load"/> 给 Editor 校验用，
/// 运行时一律走 <see cref="LoadAsync"/>。
/// </summary>
public interface ISpecialJobCertSource
{
    /// <summary>true 表示演示数据，页面必须常驻角标说明，不能让人当成真实台账。</summary>
    bool IsDemo { get; }

    /// <summary>加载是否成功完成。失败时为 false，页面据此走空态而不是显示半份数据。</summary>
    bool Ready { get; }

    /// <summary>同步加载。只有演示包能这么干，网络实现直接抛。</summary>
    void Load();

    /// <summary>运行时统一加载入口。</summary>
    IEnumerator LoadAsync();

    /// <summary>
    /// 按企业取汇总。
    /// </summary>
    /// <param name="companyCode">
    /// 企业的 <c>sysOrgCode</c>（不是 <c>company.id</c>）。沙盘上选中的企业走
    /// <c>companyObjects[id].companyDetails.sysOrgCode</c> 换到这个值。
    /// </param>
    /// <returns>没有该企业的数据时返回 <see cref="CompanyCertSummary.Empty"/>，不返回 null。</returns>
    CompanyCertSummary GetByCompany(string companyCode);
}
