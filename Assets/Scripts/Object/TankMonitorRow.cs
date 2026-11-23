using System.Globalization;
using TMPro;
using UnityEngine;

/// <summary>
/// 罐区监测卡片里的一行（液位 / 储量 / 温度 / 压力各一个实例）。
///
/// 按仓库里行组件的一贯写法：纯 public 字段拖引用 + 一个 SetData，
/// 字段全部允许为空，场景漏接一个不该让整张卡片炸掉。
/// </summary>
public class TankMonitorRow : MonoBehaviour
{
    public TextMeshProUGUI nameText;       // 液位
    public TextMeshProUGUI valueText;      // 2.28   ← 超限时变色
    public TextMeshProUGUI unitText;       // m
    public TextMeshProUGUI thresholdText;  // 低 2.2 / 高 10.5
    public TextMeshProUGUI statusText;     // 正常 / 高报 / 高高报 / 低报 / 低低报 / 无数据

    /// <summary>没数据时数值位显示什么。</summary>
    private const string NoValuePlaceholder = "--";

    // 与 TankObject.UpdateHitpointColor 保持同一套口径，不另起色板
    private static readonly Color HighColor = Color.red;
    private static readonly Color LowColor = new Color(1f, 0.5f, 0f);   // 橙，RGB 255,128,0
    private static readonly Color NormalColor = Color.green;
    private static readonly Color NoDataColor = new Color(0.6f, 0.6f, 0.6f);

    public void SetData(TankParamReading reading)
    {
        if (reading == null)
        {
            Clear();
            return;
        }

        SetText(nameText, reading.displayName);
        SetText(valueText, reading.hasValue ? reading.value : NoValuePlaceholder);
        SetText(unitText, reading.hasValue ? reading.unit : string.Empty);
        SetText(thresholdText, BuildThresholdText(reading.sensor));
        SetText(statusText, DescribeLevel(reading.level));

        Color color = ColorFor(reading.level);
        if (valueText != null) valueText.color = color;
        if (statusText != null) statusText.color = color;
    }

    public void Clear()
    {
        SetText(nameText, string.Empty);
        SetText(valueText, NoValuePlaceholder);
        SetText(unitText, string.Empty);
        SetText(thresholdText, string.Empty);
        SetText(statusText, string.Empty);

        if (valueText != null) valueText.color = NoDataColor;
        if (statusText != null) statusText.color = NoDataColor;
    }

    /// <summary>
    /// 拼阈值说明。传感器缺失（该罐没装这个参数）或阈值全部未设置时返回空串，
    /// 而不是拼一串"高 - / 低 -"的噪音。
    /// </summary>
    private static string BuildThresholdText(SensorDetail sensor)
    {
        if (sensor == null) return string.Empty;

        string high = FormatThreshold(sensor.alarmUpper);
        string highHigh = FormatThreshold(sensor.alarm2upper);
        string low = FormatThreshold(sensor.alarmLower);
        string lowLow = FormatThreshold(sensor.alarm2lower);

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        if (lowLow != null) Append(sb, $"低低 {lowLow}");
        if (low != null) Append(sb, $"低 {low}");
        if (high != null) Append(sb, $"高 {high}");
        if (highHigh != null) Append(sb, $"高高 {highHigh}");

        return sb.ToString();
    }

    private static void Append(System.Text.StringBuilder sb, string part)
    {
        if (sb.Length > 0) sb.Append(" / ");
        sb.Append(part);
    }

    /// <summary>未设置（含服务端用 0 表示未设置的情况）时返回 null。</summary>
    private static string FormatThreshold(string raw)
    {
        double value;
        if (!TankMonitorService.TryThreshold(raw, out value)) return null;

        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string DescribeLevel(TankAlarmLevel level)
    {
        switch (level)
        {
            case TankAlarmLevel.HighHigh: return "高高报";
            case TankAlarmLevel.High: return "高报";
            case TankAlarmLevel.LowLow: return "低低报";
            case TankAlarmLevel.Low: return "低报";
            case TankAlarmLevel.Normal: return "正常";
            case TankAlarmLevel.NoThreshold: return "未提供阈值";
            default: return "无数据";
        }
    }

    private static Color ColorFor(TankAlarmLevel level)
    {
        switch (level)
        {
            case TankAlarmLevel.HighHigh:
            case TankAlarmLevel.High:
                return HighColor;
            case TankAlarmLevel.LowLow:
            case TankAlarmLevel.Low:
                return LowColor;
            case TankAlarmLevel.Normal:
                return NormalColor;
            default:
                return NoDataColor;
        }
    }

    private static void SetText(TextMeshProUGUI textComp, string value)
    {
        if (textComp != null)
        {
            textComp.text = value ?? string.Empty;
        }
    }
}
