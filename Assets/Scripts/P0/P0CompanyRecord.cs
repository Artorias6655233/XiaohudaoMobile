using System;

/// <summary>
/// security/company/list 里 P0 用得上的那几个字段。
///
/// 【为什么不直接复用 <see cref="Company"/>】那个模型把 longitude84 / latitude84 声明成
/// float，而接口这两个字段 51 条全是 null，Newtonsoft 反序列化时会直接抛
/// 「Null object cannot be converted to a value type」。运行时那条老路走的是 JsonUtility
/// （吞掉 null），所以一直没暴露；P0 这条路用 Newtonsoft，一上来就会炸。
///
/// 只声明真正要用的字段，也就不会再被那份 50 多个字段的模型将来的改动波及。
/// 经纬度按字符串收：接口返回的就是带引号的字符串。
/// </summary>
[Serializable]
public class P0CompanyRecord
{
    public string id;
    public string sysOrgCode;
    public string companyName;
    public string companyShortName;
    public string longitude;
    public string latitude;

    /// <summary>实际控制人。「重大危险源管控信息」的负责人一栏优先显示它。</summary>
    public string companyControllerName;

    /// <summary>法定代表人，实际控制人缺失时的退路。</summary>
    public string legalRepresentative;

    /// <summary>沙盘标签放不下全称，一律用简称；没有简称才退回全称。</summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(companyShortName) ? companyName : companyShortName;

    public bool TryReadPoint(out double lng, out double lat)
    {
        lat = 0;
        return double.TryParse(longitude, out lng) && double.TryParse(latitude, out lat);
    }
}
