using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;

/// <summary>Only projects fields verified on the jump-server monitoring API.</summary>
public static class TankWarningData
{
    private sealed class LatestResponse
    {
        public bool success;
        public Dictionary<string, TankDataPoint> body;
    }

    public static bool SameCompany(string left, string right)
    {
        return !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right)
            && string.Equals(left.Trim(), right.Trim(), StringComparison.Ordinal);
    }

    public static TankMonitorSnapshot ParseLatest(string json, Tank tank)
    {
        var response = JsonConvert.DeserializeObject<LatestResponse>(json,
            new JsonSerializerSettings { DateParseHandling = DateParseHandling.None });
        if (response == null || !response.success || response.body == null
            || string.IsNullOrWhiteSpace(tank.equipCode)
            || !response.body.TryGetValue(tank.equipCode, out var point) || point == null)
            throw new InvalidOperationException("未返回所选储罐的监测数据");

        return new TankMonitorSnapshot
        {
            deviceCode = tank.equipCode,
            detail = Describe(tank),
            source = "网络",
            statusHint = "阈值未接入，暂不判警；浓度待补探头关联。采样时间不代表设备在线。",
            readings = new List<TankParamReading>
            {
                Reading("液位", point.liquidlevel, point.ts),
                Reading("储量", point.reserves, point.ts),
                Reading("温度", point.temperature, point.ts),
                Reading("压力", point.stress, point.ts)
            }
        };
    }

    /// <summary>
    /// 卡片头部信息全部取自 tank/list，本接口不返回罐名罐型。
    ///
    /// tankVol / liquidTop 在 <see cref="Tank"/> 里是非空 double，而接口没配置时给的是 null
    /// （随包镜像 264 条里 tankVol 有 190 条为 null），反序列化后一律落成 0，
    /// 分不出"真的是 0"和"没填"。所以 0 一律当未提供留空，不把一个假的 0 写上屏。
    /// </summary>
    private static TankDetailInfo Describe(Tank tank)
    {
        return new TankDetailInfo
        {
            tankId = tank.tankId,
            equipCode = tank.equipCode,
            tankName = tank.tankName,
            tankType = tank.tankType,
            tankVol = Amount(tank.tankVol),
            liquidTop = Amount(tank.liquidTop),
        };
    }

    /// <summary>接口没给这两项的单位，*Unit 一律留空，宁可不显示也不臆造。</summary>
    private static string Amount(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0) return null;
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static TankParamReading Reading(string name, string raw, string timestamp)
    {
        bool valid = double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && !double.IsNaN(number) && !double.IsInfinity(number);
        return new TankParamReading
        {
            displayName = name,
            // 接口给的是 "1.000000" 这种定长小数，原样上屏会把 MR 卡片的行撑开；
            // 格式与 TankMonitorRow 里阈值的写法保持一致。
            value = valid ? number.ToString("0.##", CultureInfo.InvariantCulture) : null,
            numeric = valid ? number : 0,
            hasValue = valid, ts = timestamp, unit = "未提供",
            level = valid ? TankAlarmLevel.NoThreshold : TankAlarmLevel.NoData
        };
    }
}
