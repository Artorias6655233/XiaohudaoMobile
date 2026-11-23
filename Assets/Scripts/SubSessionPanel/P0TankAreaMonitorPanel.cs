using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;

public sealed class P0TankAreaMonitorPanel : MonoBehaviour
{
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI companyText;
    public TextMeshProUGUI areaText;
    public TextMeshProUGUI sourceBadgeText;
    public TextMeshProUGUI footerText;
    public P0SensorRow liquidLevelRow;
    public P0SensorRow temperatureRow;
    public P0SensorRow concentrationRow;

    public void ShowHint(string companyName, string message)
    {
        gameObject.SetActive(true);
        Set(companyText, companyName);
        Set(areaText, "请选择罐区");
        Set(sourceBadgeText, "演示数据");
        Set(footerText, message);
        liquidLevelRow?.Clear();
        temperatureRow?.Clear();
        concentrationRow?.Clear();
    }

    public void Show(
        string companyName,
        P0TankArea area,
        P0AreaSnapshot snapshot,
        IReadOnlyList<P0ParameterState> states)
    {
        gameObject.SetActive(true);
        Set(titleText, "罐区监测预警");
        Set(companyText, companyName);
        Set(areaText, area?.name);
        Set(sourceBadgeText, "演示数据");

        Bind("liquidLevel", liquidLevelRow, area, states);
        Bind("temperature", temperatureRow, area, states);
        Bind("concentration", concentrationRow, area, states);

        DateTimeOffset sampledAt = snapshot?.readings != null && snapshot.readings.Count > 0
            ? snapshot.readings.Max(item => item.sampledAt)
            : default;
        string reasons = states == null
            ? string.Empty
            : string.Join("；", states.Where(item => item.level == P0AlarmLevel.NoData ||
                    item.level == P0AlarmLevel.Stale || item.level == P0AlarmLevel.Unknown)
                .Select(item => item.reason).Distinct());
        string time = sampledAt == default
            ? "无采样时间"
            : sampledAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        Set(footerText, $"Mock 帧 {snapshot?.frameIndex ?? 0} · {time}" +
                        (string.IsNullOrEmpty(reasons) ? string.Empty : " · " + reasons));
    }

    public void Hide() => gameObject.SetActive(false);

    private static void Bind(
        string parameter,
        P0SensorRow row,
        P0TankArea area,
        IReadOnlyList<P0ParameterState> states)
    {
        if (row == null) return;
        P0SensorDefinition sensor = area?.sensors?.FirstOrDefault(item => item.parameter == parameter);
        P0ParameterState state = states?.FirstOrDefault(item => item.parameter == parameter);
        row.SetData(sensor, state);
    }

    private static void Set(TextMeshProUGUI target, string value)
    {
        if (target != null) target.text = value ?? string.Empty;
    }
}
