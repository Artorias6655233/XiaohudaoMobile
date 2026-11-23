/// <summary>
/// 按配置产出证件数据源。照 <see cref="P0DataSourceFactory"/> 的写法。
///
/// 复用 <see cref="P0DataMode"/> 而不是另起一个枚举：它本身就只是 Mock/Network 两个值，
/// 连同 <c>P0DataModeParser</c> 一起用，免得再抄一份一模一样的解析和默认值规则。
/// </summary>
public static class SpecialJobCertSourceFactory
{
    public static ISpecialJobCertSource Create(P0DataMode mode)
    {
        if (mode == P0DataMode.Mock) return new MockSpecialJobCertSource();
        return new NetworkSpecialJobCertSource();
    }
}
