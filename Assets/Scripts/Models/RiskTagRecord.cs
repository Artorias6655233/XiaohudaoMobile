using System;
using System.Collections.Generic;

/// <summary>
/// double-prevention/riskTagRecord/find 的一条标注记录。
///
/// 实测 492 条里 <see cref="tagType"/> 的取值：6-1（477，罐体/监测点）、5（12，企业）、
/// 2/3/4 各 1 条（园区级范围标注，relationIds 一串几十个）。只有 6-1 是逐个点位，
/// 危险源图标用的就是它。
/// </summary>
[Serializable]
public class RiskTagRecord
{
    /// <summary>标注主键，直接当危险源 ID 用。</summary>
    public string sid;

    /// <summary>"6-1" 罐体/监测点、"5" 企业、"2"/"3"/"4" 园区级范围。</summary>
    public string tagType;

    public string riskLevelId;

    /// <summary>重大风险 / 较大风险 / 一般风险 / 低风险。</summary>
    public string riskLevelName;

    /// <summary>
    /// 6-1 的格式是「企业简称-罐名」，例如「福达石化-汽油储罐V402」。
    /// 注意实测有记录尾部带换行，取用前必须 Trim。
    /// </summary>
    public string tagRemarks;

    /// <summary>
    /// 指向别的表的主键（可能多个，逗号分隔）。实测与 tank.sid、tank.equipCode
    /// 【零交集】，指向什么表未确认，所以这里只存不用，别拿它去关联罐体。
    /// </summary>
    public string relationIds;

    /// <summary>形如 {"geometry":[[lng,lat]],"dataInfo":[]}，见 <see cref="RiskTagContent"/>。</summary>
    public string tagContent;

    /// <summary>后端按风险等级给好的颜色，含 8 位 alpha，如 "#a3001480"。</summary>
    public string riskColor;

    public string drawType;
    public string created;
    public string deptOrgCode;

    /// <summary>实测 492 条全是监管方那一个 org，无法用来定位企业。</summary>
    public string topOrgCode;
}

/// <summary>
/// <see cref="RiskTagRecord.tagContent"/> 反序列化后的结构——它是「JSON 里嵌 JSON 字符串」，
/// 要 parse 两次。
/// </summary>
[Serializable]
public class RiskTagContent
{
    /// <summary>drawType=point 时恒为 1 个点，元素是 [经度, 纬度]。</summary>
    public List<List<double>> geometry;

    /// <summary>实测 492 条全是空数组，没有任何传感器/阈值数据。</summary>
    public List<object> dataInfo;
}

/// <summary>
/// riskTagRecord/find 的响应壳。
///
/// 没有复用 <see cref="ExternalResponseList{T}"/>：那个把 body.total 声明成 string，
/// 而这个接口的 total 是数字（实测 492）。为一个字段赌反序列化器的宽容度不值得。
/// </summary>
[Serializable]
public class RiskTagResponse
{
    public string status;
    public string message;
    public RiskTagBody body;
}

[Serializable]
public class RiskTagBody
{
    public List<RiskTagRecord> data;
    public int total;
}
