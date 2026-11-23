using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

/// <summary>
/// 把 riskTagRecord 的标注记录映射成危险源，并挂到企业上。
///
/// 【为什么单独拆一个类】riskTagRecord 里没有任何企业外键：deptOrgCode / topOrgCode
/// 实测 492 条全是监管方那一个 org，relationIds 和 tank.sid / equipCode 零交集。
/// 唯一线索是 tagRemarks 的「企业简称-罐名」前缀，只能按名字匹配——这是整条链路上
/// 最容易出错、也最容易悄悄错的一步（挂错企业不会报任何错，只是沙盘上点位跑到别家去）。
/// 抽成不依赖协程和网络的纯函数，Editor 才能拿随包镜像把它跑一遍，见 P0MockDataChecks。
/// </summary>
public static class P0HazardMapper
{
    /// <summary>逐个点位的标注类型。5 是企业、2/3/4 是园区级范围标注，都不是危险源。</summary>
    public const string PointTagType = "6-1";

    /// <summary>
    /// 简称和全称都对不上的企业别名：tagRemarks 前缀 -> companyShortName。
    ///
    /// 实测 12 个前缀里只有这一个四级匹配全落空，而它偏偏是点位最多的一家
    /// （196 个，占 41%），漏掉等于沙盘上少一大片。
    /// </summary>
    private static readonly Dictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "中石油油库", "中石油南沙油库" },
        };

    public sealed class Result
    {
        public readonly List<P0HazardSource> hazards = new List<P0HazardSource>();

        /// <summary>匹配不到企业的前缀，用于日志。空前缀不计入。</summary>
        public readonly HashSet<string> unmatchedPrefixes = new HashSet<string>(StringComparer.Ordinal);

        public int skippedNonPoint;
        public int skippedNoCompany;
        public int skippedNoCoordinate;
    }

    /// <param name="companies">已加载的企业，name 存的是简称。</param>
    /// <param name="fullNameById">企业 ID -> companyName 全称，匹配时简称全称都要看。</param>
    public static Result Map(
        IReadOnlyCollection<P0Company> companies,
        IReadOnlyDictionary<string, string> fullNameById,
        IEnumerable<RiskTagRecord> records)
    {
        var result = new Result();
        var index = BuildNameIndex(companies, fullNameById);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (RiskTagRecord record in records ?? Enumerable.Empty<RiskTagRecord>())
        {
            if (record == null || !string.Equals(record.tagType, PointTagType, StringComparison.Ordinal))
            {
                result.skippedNonPoint++;
                continue;
            }

            // 实测有记录的 tagRemarks 尾部带换行，不 Trim 会连企业前缀都切错
            string remarks = (record.tagRemarks ?? string.Empty).Trim();
            P0Company company = Resolve(PrefixOf(remarks), companies, fullNameById, index);
            if (company == null)
            {
                result.skippedNoCompany++;
                string prefix = PrefixOf(remarks);
                if (!string.IsNullOrEmpty(prefix)) result.unmatchedPrefixes.Add(prefix);
                continue;
            }

            if (!TryReadPoint(record.tagContent, out double longitude, out double latitude))
            {
                result.skippedNoCoordinate++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(record.sid) || !seen.Add(record.sid)) continue;

            result.hazards.Add(new P0HazardSource
            {
                hazardId = record.sid,
                companyId = company.companyId,
                sysOrgCode = company.sysOrgCode,
                // 罐区这一层接口给不出来，详情卡片上这一行会直接不显示
                areaId = string.Empty,
                name = NameOf(remarks),
                level = record.riskLevelName,
                location = string.Empty,
                materials = string.Empty,
                longitude = longitude,
                latitude = latitude,
                height = 0
            });
        }

        return result;
    }

    /// <summary>
    /// 简称和全称合到一个索引。简称先进且不被全称覆盖：51 家简称无重复，
    /// 而全称之间互为子串的情况不少。
    /// </summary>
    private static Dictionary<string, P0Company> BuildNameIndex(
        IReadOnlyCollection<P0Company> companies,
        IReadOnlyDictionary<string, string> fullNameById)
    {
        var index = new Dictionary<string, P0Company>(StringComparer.Ordinal);
        foreach (P0Company company in companies)
        {
            if (!string.IsNullOrWhiteSpace(company?.name)) index[company.name.Trim()] = company;
        }
        foreach (P0Company company in companies)
        {
            string full = FullNameOf(company, fullNameById);
            if (!string.IsNullOrWhiteSpace(full) && !index.ContainsKey(full)) index[full] = company;
        }
        return index;
    }

    /// <summary>
    /// 四级降级：精确简称/全称 -> 别名表 -> 简称或全称里唯一子串命中 -> 放弃。
    ///
    /// 第三级刻意要求「唯一」：前缀「小虎建滔」和「南沙建滔」在全称里都含「建滔」，
    /// 命中多家时宁可丢点位，也不能把 A 企业的罐挂到 B 企业头上——挂错不报错，
    /// 只会让现场看到一堆位置离谱的图标。
    /// </summary>
    private static P0Company Resolve(
        string prefix,
        IReadOnlyCollection<P0Company> companies,
        IReadOnlyDictionary<string, string> fullNameById,
        Dictionary<string, P0Company> index)
    {
        if (string.IsNullOrEmpty(prefix)) return null;
        if (index.TryGetValue(prefix, out P0Company hit)) return hit;

        if (Aliases.TryGetValue(prefix, out string alias) &&
            index.TryGetValue(alias, out P0Company aliased))
        {
            return aliased;
        }

        List<P0Company> partial = companies
            .Where(item =>
                (!string.IsNullOrWhiteSpace(item?.name) && item.name.Contains(prefix)) ||
                FullNameOf(item, fullNameById).Contains(prefix))
            .Take(2)
            .ToList();
        return partial.Count == 1 ? partial[0] : null;
    }

    private static string FullNameOf(
        P0Company company, IReadOnlyDictionary<string, string> fullNameById)
    {
        return company != null && fullNameById != null &&
               fullNameById.TryGetValue(company.companyId ?? string.Empty, out string full)
            ? full
            : string.Empty;
    }

    /// <summary>取「企业简称-罐名」里的企业简称。</summary>
    public static string PrefixOf(string remarks)
    {
        if (string.IsNullOrWhiteSpace(remarks)) return null;
        int dash = remarks.IndexOf('-');
        return dash > 0 ? remarks.Substring(0, dash).Trim() : remarks.Trim();
    }

    /// <summary>取「企业简称-罐名」里的罐名；没有分隔符时整串当名字。</summary>
    public static string NameOf(string remarks)
    {
        if (string.IsNullOrWhiteSpace(remarks)) return string.Empty;
        int dash = remarks.IndexOf('-');
        return dash >= 0 && dash + 1 < remarks.Length
            ? remarks.Substring(dash + 1).Trim()
            : remarks.Trim();
    }

    /// <summary>
    /// 从 tagContent 里抠出经纬度。这个字段是「JSON 里嵌 JSON 字符串」，要 parse 两次。
    /// drawType 实测 492 条全是 point、geometry 恒为 1 个点，但这里不假设，越界就当无坐标。
    /// </summary>
    public static bool TryReadPoint(string tagContent, out double longitude, out double latitude)
    {
        longitude = 0;
        latitude = 0;
        if (string.IsNullOrWhiteSpace(tagContent)) return false;

        RiskTagContent content;
        try
        {
            content = JsonConvert.DeserializeObject<RiskTagContent>(tagContent);
        }
        catch (Exception)
        {
            return false;
        }

        List<double> point = content?.geometry?.FirstOrDefault();
        if (point == null || point.Count < 2) return false;

        longitude = point[0];
        latitude = point[1];
        return P0HazardProjection.ValidCoordinates(longitude, latitude);
    }
}
