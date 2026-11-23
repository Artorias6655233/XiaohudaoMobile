using System.Globalization;
using TMPro;
using UnityEngine;

public sealed class P0SensorRow : MonoBehaviour
{
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI valueText;
    public TextMeshProUGUI unitText;
    public TextMeshProUGUI thresholdText;
    public TextMeshProUGUI statusText;

    public void SetData(P0SensorDefinition sensor, P0ParameterState state)
    {
        Set(nameText, sensor?.displayName);
        Set(valueText, state?.value.HasValue == true
            ? state.value.Value.ToString("0.##", CultureInfo.InvariantCulture)
            : "--");
        Set(unitText, state?.value.HasValue == true ? sensor?.unit : string.Empty);
        Set(thresholdText, Thresholds(sensor));
        Set(statusText, state?.reason ?? "无有效数据");

        Color color = ColorFor(state?.level ?? P0AlarmLevel.NoData);
        if (valueText != null) valueText.color = color;
        if (statusText != null) statusText.color = color;
    }

    public void Clear()
    {
        Set(nameText, string.Empty);
        Set(valueText, "--");
        Set(unitText, string.Empty);
        Set(thresholdText, string.Empty);
        Set(statusText, string.Empty);
    }

    private static string Thresholds(P0SensorDefinition sensor)
    {
        if (sensor == null) return string.Empty;
        var parts = new System.Collections.Generic.List<string>();
        if (sensor.lowLowEnabled) parts.Add("低低 " + Format(sensor.lowLow));
        if (sensor.lowEnabled) parts.Add("低 " + Format(sensor.low));
        if (sensor.highEnabled) parts.Add("高 " + Format(sensor.high));
        if (sensor.highHighEnabled) parts.Add("高高 " + Format(sensor.highHigh));
        return parts.Count == 0 ? "未配置" : string.Join(" / ", parts);
    }

    private static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static Color ColorFor(P0AlarmLevel level)
    {
        switch (level)
        {
            case P0AlarmLevel.High:
            case P0AlarmLevel.HighHigh: return Color.red;
            case P0AlarmLevel.Low:
            case P0AlarmLevel.LowLow: return new Color(1f, 0.5f, 0f);
            case P0AlarmLevel.Normal: return Color.green;
            default: return Color.gray;
        }
    }

    private static void Set(TextMeshProUGUI target, string value)
    {
        if (target != null) target.text = value ?? string.Empty;
    }
}
