using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LZW.Module.MajorHazard.Scripts
{
    /// <summary>
    /// 地图 / 表格上的一个重大危险源点位。
    /// </summary>
    public class HazardSite
    {
        public string id;
        public string name;
        public string company;
        public string typeLabel;
        public string levelName;
        public Color color;
        public double longitude;
        public double latitude;
        public List<string> devices = new List<string>();
    }

    /// <summary>
    /// 一张图标注（riskTagRecord）→ 重大危险源点位，三维地图页和数据表格页共用。
    /// 设备标注按「企业 + 危险源」合并为一个点（同一储罐区的多台设备坐标几乎重合），企业 / 其他标注各自一个点。
    /// </summary>
    public static class HazardSites
    {
        public static readonly string[] LevelFilters = { "全部", "重大风险", "较大风险", "一般风险", "低风险" };

        /// <param name="inRange">只保留该范围内的标注，为 null 不限</param>
        public static List<HazardSite> Build(IReadOnlyList<HazardTag> tags, System.Func<HazardTag, bool> inRange = null)
        {
            var sites = new List<HazardSite>();
            var groups = new Dictionary<string, List<(HazardTag tag, HazardTagDevice device)>>();

            foreach (var tag in tags)
            {
                if (inRange != null && !inRange(tag))
                    continue;

                if (tag.tagType == HazardTag.TypeDevice && tag.devices.Count > 0)
                {
                    foreach (var device in tag.devices)
                    {
                        var key = $"{device.companyName}|{device.dangerSourceName}";
                        if (!groups.TryGetValue(key, out var list))
                            groups[key] = list = new List<(HazardTag, HazardTagDevice)>();
                        list.Add((tag, device));
                    }
                    continue;
                }

                sites.Add(new HazardSite
                {
                    id = tag.sid,
                    name = tag.remarks ?? tag.TypeLabel,
                    company = tag.tagType == HazardTag.TypeCompany ? tag.remarks : null,
                    typeLabel = tag.TypeLabel,
                    levelName = tag.riskLevelName,
                    color = tag.Color,
                    longitude = tag.longitude,
                    latitude = tag.latitude
                });
            }

            foreach (var pair in groups)
            {
                var group = pair.Value;
                var first = group[0];
                // 一组里风险最高的等级代表这个危险源
                var top = group.OrderBy(g => RiskLevel.RankOf(g.tag.riskLevelName)).First().tag;
                sites.Add(new HazardSite
                {
                    id = pair.Key, // 一条标注里可能有多个危险源的设备，不能用标注 sid
                    name = first.device.dangerSourceName ?? first.device.companyName ?? "危险源",
                    company = first.device.companyName,
                    typeLabel = "危险源",
                    levelName = top.riskLevelName,
                    color = top.Color,
                    longitude = group.Average(g => g.tag.longitude),
                    latitude = group.Average(g => g.tag.latitude),
                    devices = group.Select(g => TrimCompany(g.device.deviceName, first.device.companyName))
                        .Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList()
                });
            }

            return sites.OrderBy(s => RiskLevel.RankOf(s.levelName)).ThenBy(s => s.company).ThenBy(s => s.name).ToList();
        }

        public static List<HazardSite> Filter(List<HazardSite> sites, int levelFilter)
        {
            return levelFilter <= 0 ? sites : sites.Where(s => s.levelName == LevelFilters[levelFilter]).ToList();
        }

        /// <summary>
        /// 各等级数量，如「重大 3  较大 5  一般 2  低 1」。
        /// </summary>
        public static string LevelCounts(List<HazardSite> sites)
        {
            return string.Join("  ", LevelFilters.Skip(1).Select(f => $"{f.Replace("风险", "")} {sites.Count(s => s.levelName == f)}"));
        }

        /// <summary>
        /// 详情文字：企业、类型、风险等级、设备（最多 maxDevices 台）。
        /// </summary>
        public static string Detail(HazardSite site, int maxDevices)
        {
            var lines = new List<string>();
            if (!string.IsNullOrEmpty(site.company) && site.company != site.name)
                lines.Add($"企业：{site.company}");
            lines.Add($"类型：{site.typeLabel}");
            lines.Add($"风险等级：{site.levelName ?? "未评定"}");
            if (site.devices.Count > 0)
            {
                lines.Add($"监测设备：{site.devices.Count} 台");
                lines.AddRange(site.devices.Take(maxDevices).Select(n => "· " + n));
                if (site.devices.Count > maxDevices)
                    lines.Add($"…共 {site.devices.Count} 台");
            }
            return string.Join("\n", lines);
        }

        /// <summary>
        /// 按关键词筛选（危险源名 / 企业名 / 设备名包含关键词，忽略大小写和空格），关键词为空返回全部。
        /// 移动端用输入框搜索代替 AR 端的语音查询（输入法自带的语音输入同样可用）。
        /// </summary>
        public static List<HazardSite> Search(List<HazardSite> sites, string keyword)
        {
            keyword = (keyword ?? "").Replace(" ", "").Trim();
            if (keyword.Length == 0)
                return sites;
            bool Has(string s) => !string.IsNullOrEmpty(s) && s.Replace(" ", "").IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0;
            return sites.Where(s => Has(s.name) || Has(s.company) || s.devices.Any(Has)).ToList();
        }

        /// <summary>
        /// 设备名一般是「企业-介质，储罐」，去掉重复的企业前缀。
        /// </summary>
        private static string TrimCompany(string deviceName, string company)
        {
            if (string.IsNullOrEmpty(deviceName))
                return deviceName;
            if (!string.IsNullOrEmpty(company) && deviceName.StartsWith(company))
                deviceName = deviceName.Substring(company.Length).TrimStart('-', '－', ' ');
            return deviceName;
        }
    }
}
