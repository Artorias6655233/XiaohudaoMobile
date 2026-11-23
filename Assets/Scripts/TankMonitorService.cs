using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// 罐区实时监测取数：一个 deviceCode 一次拉齐「基础信息 + 最新采样 + 四个参数的报警阈值」。
///
/// 放在这里而不是塞进 MajorHazard_Management，是因为那边已经 370+ 行，
/// 再加 6 个请求 + 缓存 + 阈值判定会失控。面板只负责「选中变化 -> 起协程 -> 交给卡片」。
///
/// 走 OfflineMock 而不是 DataProvider：这几个接口要 POST、要带 Token 头，DataProvider 两样都不支持。
/// 结果按 deviceCode 缓存在进程内，同一个罐反复点不会重复请求（阈值和基础信息本来就基本不变，
/// 实时值也不做轮询，按需求是「点一次取一次」）。
/// </summary>
public static class TankMonitorService
{
    /// <summary>
    /// 监测参数定义。
    ///
    /// findAll 返回的是固定列名、不按 monitorParam 分列，所以参数码到字段的映射只能写死。
    /// </summary>
    private class ParamDef
    {
        public string code;
        public string displayName;
        public string fallbackUnit;
        public Func<TankDataPoint, string> pick;
    }

    private static readonly ParamDef[] Params =
    {
        new ParamDef { code = "HZD9107", displayName = "液位", fallbackUnit = "m",  pick = p => p.liquidlevel },
        new ParamDef { code = "HZD9118", displayName = "储量", fallbackUnit = "m³", pick = p => p.reserves },
        new ParamDef { code = "HZD9101", displayName = "温度", fallbackUnit = "℃",  pick = p => p.temperature },
        new ParamDef { code = "HZD9102", displayName = "压力", fallbackUnit = "",   pick = p => p.stress },
    };

    private const string ProductId = "1879534415159181312";

    /// <summary>
    /// 只要最新值，理论上 pageSize=1 就够（实测 1/5/20/500 都是按 ts 倒序，body[0] 恒为最新）。
    /// 取 20 条是为了跨过偶发的空采样：某一列在最新那条是空、往前几条却有值时仍能显示出来。
    /// 20 条约 2.6KB，代价可以忽略。
    /// </summary>
    private const int DataPageSize = 20;

    private const string DataQueryBodyFormat =
        "{{\"body\":{{\"productId\":\"{0}\",\"sn\":\"{1}\"}},\"page\":1,\"pageSize\":{2}}}";

    private const string SensorQueryBodyFormat =
        "{{\"monitorParam\":\"{0}\",\"equipCode\":\"{1}\"}}";

    private static readonly Dictionary<string, TankMonitorSnapshot> cache =
        new Dictionary<string, TankMonitorSnapshot>();

    /// <summary>取数结果载体（协程不能有返回值）。</summary>
    public class Result
    {
        public TankMonitorSnapshot snapshot;
        public string error;
    }

    /// <summary>一轮请求的原始产物。401 重试时整轮重来。</summary>
    private class Attempt
    {
        public TankDetailInfo detail;
        public List<TankDataPoint> points;
        public SensorDetail[] sensors = new SensorDetail[Params.Length];
        public bool unauthorized;
        public string source;
    }

    public static void ClearCache()
    {
        cache.Clear();
    }

    /// <summary>
    /// 拉一个储罐的监测数据。命中缓存时同步返回。
    /// </summary>
    public static IEnumerator Load(string deviceCode, Result outcome)
    {
        if (string.IsNullOrEmpty(deviceCode))
        {
            outcome.error = "该储罐未关联监测设备";
            yield break;
        }

        TankMonitorSnapshot cached;
        if (cache.TryGetValue(deviceCode, out cached))
        {
            outcome.snapshot = cached;
            yield break;
        }

        yield return IccAuth.EnsureToken(false);
        if (!IccAuth.HasToken)
        {
            outcome.error = "监测平台登录失败";
            yield break;
        }

        // 第一轮撞上 401 就整轮放弃：后面几个请求也必然 401，白等好几次超时。
        Attempt attempt = new Attempt();
        yield return FetchAll(deviceCode, attempt, true);

        if (attempt.unauthorized)
        {
            // token 过期时 OfflineMock 会静默回退随包镜像，光看 fetch.ok 分辨不出来，
            // 只能靠 httpCode。这套判定与 ClosedPark_CameraMonitor 里那段同源。
            Debug.LogWarning("[罐区监测] 接口返回 401，重新登录后重试");
            yield return IccAuth.EnsureToken(true);

            if (!IccAuth.HasToken)
            {
                outcome.error = "监测平台登录失败";
                yield break;
            }

            attempt = new Attempt();
            yield return FetchAll(deviceCode, attempt, false);

            if (attempt.unauthorized)
            {
                Debug.LogWarning("[罐区监测] 重登后仍是 401，下面用的是随包镜像数据，不是实时值");
            }
        }

        TankMonitorSnapshot snapshot = Build(deviceCode, attempt);

        // 六个请求一个都没成的话不要写缓存 —— 否则一次网络抖动会让这个罐
        // 在整个进程生命周期里都显示"无数据"，再点也不会重试。
        if (snapshot.source != null)
        {
            cache[deviceCode] = snapshot;
        }

        outcome.snapshot = snapshot;
    }

    /// <param name="stopOnUnauthorized">
    /// true 表示一撞 401 立刻收工（第一轮）；false 表示撞了也把剩下的请求发完，
    /// 好歹让回退到的镜像数据能填满卡片（重登后的第二轮）。
    /// </param>
    private static IEnumerator FetchAll(string deviceCode, Attempt attempt, bool stopOnUnauthorized)
    {
        Dictionary<string, string> headers = new Dictionary<string, string> { { "Token", IccAuth.Token } };

        // --- 1. 储罐基础信息（GET） ---
        OfflineMock.Result fetch = new OfflineMock.Result();
        yield return OfflineMock.Fetch(Api.TankDetail(deviceCode), fetch, headers);

        if (fetch.httpCode == 401)
        {
            attempt.unauthorized = true;
            if (stopOnUnauthorized) yield break;
        }

        if (fetch.ok)
        {
            attempt.source = fetch.source;
            BackgroundJson.Result parsed = new BackgroundJson.Result();
            yield return BackgroundJson.Parse(typeof(TankDetailResponse), fetch.json, parsed);
            TankDetailResponse response = parsed.value as TankDetailResponse;
            attempt.detail = response != null ? response.result : null;
        }

        // --- 2. 实时/历史采样（POST） ---
        fetch = new OfflineMock.Result();
        string body = string.Format(DataQueryBodyFormat, ProductId, deviceCode, DataPageSize);
        yield return OfflineMock.PostJson(Api.DeviceDataFindAll(), body, headers, fetch);

        if (fetch.httpCode == 401)
        {
            attempt.unauthorized = true;
            if (stopOnUnauthorized) yield break;
        }

        if (fetch.ok)
        {
            if (attempt.source == null) attempt.source = fetch.source;

            BackgroundJson.Result parsed = new BackgroundJson.Result();
            yield return BackgroundJson.Parse(typeof(TankDataResponse), fetch.json, parsed);
            TankDataResponse response = parsed.value as TankDataResponse;

            // sn 不存在时服务端返回 {"success":false,"code":500,...,"body":null}，
            // 不是异常，卡片显示"无数据"即可，只留一行日志便于排查。
            if (response != null && response.success)
            {
                attempt.points = response.body;
            }
            else if (response != null && !string.IsNullOrEmpty(response.message))
            {
                Debug.LogWarning($"[罐区监测] {deviceCode} 实时数据接口返回失败：{response.message}");
            }
        }

        // --- 3. 四个参数的报警阈值（POST，每个参数单独查一次） ---
        for (int i = 0; i < Params.Length; i++)
        {
            fetch = new OfflineMock.Result();
            string sensorBody = string.Format(SensorQueryBodyFormat, Params[i].code, deviceCode);
            yield return OfflineMock.PostJson(Api.DataSensorDetail(), sensorBody, headers, fetch);

            if (fetch.httpCode == 401)
            {
                attempt.unauthorized = true;
                if (stopOnUnauthorized) yield break;
            }

            if (!fetch.ok) continue;

            BackgroundJson.Result parsed = new BackgroundJson.Result();
            yield return BackgroundJson.Parse(typeof(SensorDetailResponse), fetch.json, parsed);
            SensorDetailResponse response = parsed.value as SensorDetailResponse;

            // result 为 null 是正常情况：这个罐没装这个参数的传感器。
            attempt.sensors[i] = response != null ? response.result : null;
        }
    }

    private static TankMonitorSnapshot Build(string deviceCode, Attempt attempt)
    {
        TankMonitorSnapshot snapshot = new TankMonitorSnapshot
        {
            deviceCode = deviceCode,
            detail = attempt.detail,
            source = attempt.source,
            readings = new List<TankParamReading>(Params.Length),
        };

        for (int i = 0; i < Params.Length; i++)
        {
            ParamDef def = Params[i];
            SensorDetail sensor = attempt.sensors[i];

            TankParamReading reading = new TankParamReading
            {
                paramCode = def.code,
                displayName = def.displayName,
                sensor = sensor,
                unit = sensor != null && !string.IsNullOrEmpty(sensor.unit)
                    ? sensor.unit.Trim()
                    : def.fallbackUnit,
                level = TankAlarmLevel.NoData,
            };

            // 最新那条该列可能是空，往前找第一条有值的
            if (attempt.points != null)
            {
                foreach (TankDataPoint point in attempt.points)
                {
                    if (point == null) continue;

                    string raw = def.pick(point);
                    if (string.IsNullOrEmpty(raw) || raw.Trim().Length == 0) continue;

                    double numeric;
                    if (!double.TryParse(raw.Trim(), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out numeric))
                    {
                        continue;
                    }

                    reading.hasValue = true;
                    reading.numeric = numeric;
                    reading.value = numeric.ToString("0.##", CultureInfo.InvariantCulture);
                    reading.ts = point.ts;
                    reading.level = EvaluateLevel(numeric, sensor);
                    break;
                }
            }

            snapshot.readings.Add(reading);
        }

        return snapshot;
    }

    private static TankAlarmLevel EvaluateLevel(double value, SensorDetail sensor)
    {
        if (sensor == null) return TankAlarmLevel.Normal;

        double threshold;

        if (TryThreshold(sensor.alarm2upper, out threshold) && value >= threshold)
            return TankAlarmLevel.HighHigh;

        if (TryThreshold(sensor.alarmUpper, out threshold) && value >= threshold)
            return TankAlarmLevel.High;

        if (TryThreshold(sensor.alarm2lower, out threshold) && value <= threshold)
            return TankAlarmLevel.LowLow;

        if (TryThreshold(sensor.alarmLower, out threshold) && value <= threshold)
            return TankAlarmLevel.Low;

        return TankAlarmLevel.Normal;
    }

    /// <summary>
    /// 解析一个阈值。空、解析不了、或者【等于 0】都当作"未设置"。
    ///
    /// 0 也要跳过是实测出来的：储量(HZD9118)那组会返回
    /// alarm2upper=0.0000 / alarmLower=0.0000 / alarm2lower=0.0000，而量程下限本来也是 0。
    /// 照单全收的话，储量读数为 0 的罐会被判成"低报"，属于假报警。
    /// 温度那种 alarm2lower=-10 的负值阈值不受影响，照常生效。
    /// </summary>
    public static bool TryThreshold(string raw, out double value)
    {
        value = 0;

        if (string.IsNullOrEmpty(raw)) return false;

        if (!double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return false;

        return value != 0;
    }
}
