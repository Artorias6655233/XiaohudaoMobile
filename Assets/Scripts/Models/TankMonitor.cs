using System.Collections.Generic;

/// <summary>
/// 罐区实时监测（液位 / 储量 / 温度 / 压力）三个接口的模型。
///
/// 三个接口都在 Api.ICC_URL，都要带 Token 头，实测响应形状见各类注释。
/// 刻意不复用 <see cref="Tank"/>：tankDetail 返回的同名字段全是 string，
/// 而 Tank 里 designPressure / tankVol 等是 double，JsonUtility 会直接解析失败。
/// </summary>

// ---------------------------------------------------------------- 接口 1：储罐基础信息

/// <summary>
/// GET jeecg/ext/dataDangerDeviceInfo/tankDetail?deviceCode= 的 result。
/// 数值字段服务端一律给 string（"1.000000" 这种），单位是另外的 *Unit 字段。
/// </summary>
[System.Serializable]
public class TankDetailInfo
{
    public string tankId;
    public string equipCode;
    public string dangesrcCode;
    public string tankName;
    public string tankType;

    public string tankVol;
    public string tankVolUnit;
    public string liquidTop;
    public string liquidTopUnit;

    public string designPressure;
    public string normalPressureTop;
    public string pressureUnit;

    /// <summary>服务端字段名就是全小写的 normaltemlow，不是 normalTemLow。</summary>
    public string normaltemlow;
    public string normalTemTop;
    public string normalTemUnit;

    public string storage;
    public string storageStatus;
    public string runStatus;
}

// JsonUtility 反序列化不了开放泛型，沿用 Models/Response.cs 末尾那套具体子类的做法
[System.Serializable]
public class TankDetailResponse : Response<TankDetailInfo> { }

// ---------------------------------------------------------------- 接口 2：实时/历史数据

/// <summary>
/// POST aiot-admin/api/derviceData/findAll 的一条采样。
///
/// 字段名是固定列名，不随 monitorParam 变：
///   HZD9107 液位 -> liquidlevel，HZD9118 储量 -> reserves，
///   HZD9101 温度 -> temperature，HZD9102 压力 -> stress。
/// 值都是 string，未装该传感器时整列为 "" 或 null。
/// ts 形如 2026-08-26T09:45:32.182+00:00（UTC）。
/// </summary>
[System.Serializable]
public class TankDataPoint
{
    public string ts;
    public string liquidlevel;
    public string reserves;
    public string temperature;
    public string stress;
}

/// <summary>
/// findAll 的信封。body 是裸数组而不是 {total,data}，所以套不上 ExternalResponseList。
///
/// code / page / pageSize / total 成功时返回 null、失败时返回 int，声明成任何一种
/// 都会让 JsonUtility 在另一种情况下出问题；反正用不到，就一个都不声明
/// （JsonUtility 会忽略未声明的键）。
/// </summary>
[System.Serializable]
public class TankDataResponse
{
    public bool success;
    public string message;
    public List<TankDataPoint> body;
}

// ---------------------------------------------------------------- 接口 3：传感器阈值

/// <summary>
/// POST jeecg/ext/dataSensor/detail 的 result。整个 result 可能为 null
/// —— 表示这个罐没装这个参数的传感器，是正常情况，不是错误。
/// </summary>
[System.Serializable]
public class SensorDetail
{
    public string sensorName;
    public string monitorParam;
    public string unit;

    public string rangeLower;
    public string rangeUpper;

    public string alarmUpper;    // 高报
    public string alarm2upper;   // 高高报
    public string alarmLower;    // 低报
    public string alarm2lower;   // 低低报

    public string onlineStatus;
    public string installPosition;
}

[System.Serializable]
public class SensorDetailResponse : Response<SensorDetail> { }

// ---------------------------------------------------------------- 展示层聚合

public enum TankAlarmLevel
{
    Normal,
    High,
    HighHigh,
    Low,
    LowLow,
    NoData,
    NoThreshold
}

/// <summary>卡片里一行的全部展示数据。</summary>
public class TankParamReading
{
    public string paramCode;
    public string displayName;

    /// <summary>已格式化好的展示值（如 "2.28"）；无数据时为 null。</summary>
    public string value;

    /// <summary>解析出来的数值，仅 <see cref="hasValue"/> 为 true 时有意义。</summary>
    public double numeric;

    public string unit;

    /// <summary>该采样自己的 ts；无数据时为 null。</summary>
    public string ts;

    /// <summary>阈值信息，可为 null（没装这个传感器）。</summary>
    public SensorDetail sensor;

    public TankAlarmLevel level = TankAlarmLevel.NoData;
    public bool hasValue;
}

/// <summary>一个 deviceCode 的一次完整取数结果。</summary>
public class TankMonitorSnapshot
{
    public string statusHint;
    public string deviceCode;

    /// <summary>可为 null：基础信息接口失败不该拖垮整张卡片。</summary>
    public TankDetailInfo detail;

    /// <summary>恒为 4 项，顺序：液位 / 储量 / 温度 / 压力。</summary>
    public List<TankParamReading> readings;

    /// <summary>数据来源（"网络" / "离线镜像"），用于卡片底部提示。</summary>
    public string source;
}
