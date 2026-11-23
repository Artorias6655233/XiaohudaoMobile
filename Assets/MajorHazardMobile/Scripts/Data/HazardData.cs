using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LZW.Module.MajorHazard.Scripts
{
    /// <summary>
    /// 双重预防「一张图」上的一个点状标注（double-prevention/riskTagRecord/find，coordinateType=84 即 WGS84）。
    /// tagType：6-1 = 危险源监测设备（tagContent.checkDataList 里是设备），5 = 企业（tagRemarks 为企业名），其他为风险标注。
    /// </summary>
    public class HazardTag
    {
        public const string TypeDevice = "6-1";
        public const string TypeCompany = "5";

        public string sid;
        public string tagType;
        public string riskLevelId;
        public string riskLevelName;
        public string riskColor;
        public string remarks;
        public string[] relationIds = Array.Empty<string>();
        public double longitude;
        public double latitude;
        public readonly List<HazardTagDevice> devices = new List<HazardTagDevice>();

        public static HazardTag Parse(JObject item)
        {
            var tag = new HazardTag
            {
                sid = HazardJson.Str(item, "sid"),
                tagType = HazardJson.Str(item, "tagType"),
                riskLevelId = HazardJson.Str(item, "riskLevelId"),
                riskLevelName = HazardJson.Str(item, "riskLevelName"),
                riskColor = HazardJson.Str(item, "riskColor"),
                remarks = HazardJson.Str(item, "tagRemarks"),
            };

            var relations = HazardJson.Str(item, "relationIds");
            if (relations != null)
                tag.relationIds = relations.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            // tagContent 是 JSON 字符串：{"geometry":[[lon,lat]],"checkDataList":[设备…]}
            if (HazardJson.Parse(HazardJson.Str(item, "tagContent")) is not JObject content)
                return null;
            if (content.SelectToken("geometry[0]") is not JArray point || point.Count < 2)
                return null;
            tag.longitude = point[0].Value<double>();
            tag.latitude = point[1].Value<double>();
            if (!HazardJson.IsValidLonLat(tag.longitude, tag.latitude))
                return null;

            foreach (var device in HazardJson.FindList(content, "checkDataList"))
            {
                tag.devices.Add(new HazardTagDevice
                {
                    deviceId = HazardJson.Str(device, "deviceId"),
                    deviceCode = HazardJson.Str(device, "deviceCode"),
                    deviceName = HazardJson.Str(device, "deviceName"),
                    companyName = HazardJson.Str(device, "companyName"),
                    dangerSourceName = HazardJson.Str(device, "dangerSourceName"),
                    sysOrgCode = HazardJson.Str(device, "sysOrgCode"),
                });
            }
            return tag;
        }

        public string TypeLabel => tagType switch
        {
            TypeDevice => "危险源设备",
            TypeCompany => "企业",
            _ => "风险标注"
        };

        public Color Color => HazardJson.TryColor(riskColor, out var c) ? c : RiskLevel.ColorOf(riskLevelName);
    }

    public class HazardTagDevice
    {
        public string deviceId;
        public string deviceCode;
        public string deviceName;
        public string companyName;
        public string dangerSourceName;
        public string sysOrgCode;
    }

    /// <summary>
    /// 风险等级（double-prevention/riskConfigLevelControlRang/find）。rank 0 = 重大风险 … 3 = 低风险。
    /// </summary>
    public class RiskLevel
    {
        public static readonly Color Major = new Color(0.9f, 0.12f, 0.12f);
        public static readonly Color Large = new Color(1f, 0.55f, 0.1f);
        public static readonly Color General = new Color(1f, 0.88f, 0.15f);
        public static readonly Color Low = new Color(0.2f, 0.6f, 1f);
        public static readonly Color Unknown = new Color(0.6f, 0.65f, 0.7f);

        /// <summary>
        /// 接口取不到时使用的默认等级（sid 与平台一致，抓包所得）。
        /// </summary>
        public static readonly RiskLevel[] Defaults =
        {
            new RiskLevel("1828630367557423105", "重大风险", 0),
            new RiskLevel("1828631324353662977", "较大风险", 1),
            new RiskLevel("1828631440821096449", "一般风险", 2),
            new RiskLevel("1828631592294191106", "低风险", 3),
        };

        public readonly string sid;
        public readonly string name;
        public readonly int rank;

        public RiskLevel(string sid, string name, int rank)
        {
            this.sid = sid;
            this.name = name;
            this.rank = rank;
        }

        public Color Color => ColorOf(name);

        public static Color ColorOf(string levelName)
        {
            if (string.IsNullOrEmpty(levelName))
                return Unknown;
            if (levelName.Contains("重大"))
                return Major;
            if (levelName.Contains("较大"))
                return Large;
            if (levelName.Contains("一般"))
                return General;
            if (levelName.Contains("低"))
                return Low;
            return Unknown;
        }

        /// <summary>
        /// 排序用：数字越小风险越高，未知排最后。
        /// </summary>
        public static int RankOf(string levelName)
        {
            foreach (var level in Defaults)
                if (level.name == levelName)
                    return level.rank;
            return 9;
        }
    }
}
