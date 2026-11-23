[System.Serializable]
public class Tank
{
    public long sid;
    public string tankId;
    public string equipCode;
    public string dangesrcCode;
    public string tankName;
    public string tankType;
    public string longitude;
    public string latitude;
    public string pressureType;
    public double? designPressure;
    public double? normalPressureTop;
    public string temType;
    public double? normalTemLow;
    public double? normalTemTop;
    public double tankVol;
    public double liquidTop;
    public string buildDate;
    public string prodDate;
    public string createBy;
    public string createTime;
    public string updateBy;
    public string updateTime;
    public string sysOrgCode;
    public string deptOrgCode;
    public string lastRepairDate;
    public string actived;
    public string runStatus;
    public string provinceDeviceCode;

    /// <summary>
    /// 沙盘里认这个罐的唯一键：Session.tankObjects 的字典键、TankObject.tankID、
    /// Session.tankID 都用它，三个面板必须一致，否则互相看不见对方建的图标。
    ///
    /// 【不能用 tankId】它是企业内序号，不是全局主键。2026-09-21 实测 tank/list 264 条
    /// 只有 247 个不同的 tankId，"1" 就被 5 家企业同时占用（华隆石化、申星化工、广钢气体、
    /// 建滔环保、建滔电子）。拿它当键，后建的覆盖先建的，17 个罐直接没有图标，
    /// 而被覆盖掉的恰好是全库仅有的、有逐罐独立坐标的 5 家企业。
    ///
    /// equipCode 实测 264/264 唯一、无空值，也正是 bestNewData 取数用的那个编码。
    /// 返回 null 表示这条记录没法在沙盘上表示，调用方直接跳过。
    /// </summary>
    public string MarkerKey => string.IsNullOrWhiteSpace(equipCode) ? null : equipCode.Trim();
}
