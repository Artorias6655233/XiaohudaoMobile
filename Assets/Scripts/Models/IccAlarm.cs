using System.Collections.Generic;

/// <summary>
/// ICC 告警明细（icc/dataEventDetail/find 的一条记录）。
///
/// 刻意只声明用得到的字段：样本响应里 alarmPictureSize / lastUpdated 等字段是 null，
/// 而 JsonUtility 把 null 灌进 int 字段会出问题（tools/mock-api/server.py 专门做了个
/// --null-safe 模式就是为了排这类异常）。用不到的数值字段一概不声明，
/// JsonUtility 会忽略响应里多出来的键。
/// </summary>
[System.Serializable]
public class IccAlarm
{
    public string sid;
    public string deviceCode;

    /// <summary>形如 "小虎南四路28_紧急求助终端_20240428"。</summary>
    public string deviceName;

    public string alarmType;
    public string alarmGrade;
    public string alarmDate;
    public string alarmTime;

    /// <summary>已经是完整可读的中文句子，详情面板以它为主展示字段。</summary>
    public string alarmContent;

    public string alarmDuration;
    public string isUrge;
    public string disposeContent;
    public string disposeWay;

    /// <summary>样本里恒有值，可以安全声明成 int。</summary>
    public int alarmStatus;

    /// <summary>
    /// deviceName 的第一段，即地点名，用于和 cameras_4.csv 的 name 匹配。
    ///
    /// 告警设备是 deviceType=3（紧急求助终端），CSV 里的是 type=1 的摄像头，
    /// 两者不是同一台设备，只能按同一杆上的地点名对应。已核对：CSV 的 499 个 name
    /// 全部唯一且没有一个含 '_'，所以取第一段做键不会撞。
    /// </summary>
    public string LocationName
    {
        get
        {
            if (string.IsNullOrEmpty(deviceName))
            {
                return string.Empty;
            }

            int cut = deviceName.IndexOf('_');
            return (cut < 0 ? deviceName : deviceName.Substring(0, cut)).Trim();
        }
    }
}

/// <summary>
/// icc/dataEventDetail/find 的响应外壳。
///
/// 实测真实响应是 {status, message, body:{data:[...], total:"9"}}，和工程里既有的
/// external 壳（<see cref="ExternalResponseList{T}"/>）完全一致，所以直接复用，
/// 不再自己定义。total 在那边就是 string，也和实际返回对得上。
///
/// 必须建这个具体子类：BackgroundJson 在响应小于 256KB 时走的是 JsonUtility，
/// 而 JsonUtility 反序列化不了开放泛型 —— 同 DoubleDangerExternalResponse 的理由。
/// </summary>
[System.Serializable]
public class IccAlarmExternalResponse : ExternalResponseList<IccAlarm> { }
