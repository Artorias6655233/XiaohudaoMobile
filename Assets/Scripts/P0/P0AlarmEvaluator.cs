using System;
using System.Collections.Generic;

public enum P0AlarmLevel
{
    Unknown,
    NoData,
    Stale,
    Normal,
    LowLow,
    Low,
    High,
    HighHigh
}

[Serializable]
public sealed class P0ParameterState
{
    public string sensorId;
    public string parameter;
    public double? value;
    public DateTimeOffset sampledAt;
    public P0AlarmLevel level;
    public string reason;
}

public static class P0AlarmEvaluator
{
    public static P0ParameterState Evaluate(
        P0SensorDefinition sensor,
        P0SensorReading reading,
        DateTimeOffset now)
    {
        if (sensor == null) throw new ArgumentNullException(nameof(sensor));

        var state = new P0ParameterState
        {
            sensorId = sensor.sensorId,
            parameter = sensor.parameter,
            value = reading?.value,
            sampledAt = reading?.sampledAt ?? default
        };

        if (reading?.value == null || double.IsNaN(reading.value.Value) ||
            double.IsInfinity(reading.value.Value))
        {
            state.level = P0AlarmLevel.NoData;
            state.reason = "无有效数据";
            return state;
        }

        if (sensor.validitySeconds > 0 &&
            now - reading.sampledAt > TimeSpan.FromSeconds(sensor.validitySeconds))
        {
            state.level = P0AlarmLevel.Stale;
            state.reason = "数据已过期";
            return state;
        }

        double value = reading.value.Value;
        if (sensor.highHighEnabled && value >= sensor.highHigh)
            return Alarm(state, P0AlarmLevel.HighHigh, "高高报");
        if (sensor.highEnabled && value >= sensor.high)
            return Alarm(state, P0AlarmLevel.High, "高报");
        if (sensor.lowLowEnabled && value <= sensor.lowLow)
            return Alarm(state, P0AlarmLevel.LowLow, "低低报");
        if (sensor.lowEnabled && value <= sensor.low)
            return Alarm(state, P0AlarmLevel.Low, "低报");

        if (sensor.highHighEnabled || sensor.highEnabled ||
            sensor.lowLowEnabled || sensor.lowEnabled)
        {
            state.level = P0AlarmLevel.Normal;
            state.reason = "正常";
        }
        else
        {
            state.level = P0AlarmLevel.Unknown;
            state.reason = "未配置阈值";
        }

        return state;
    }

    public static P0AlarmLevel Aggregate(IEnumerable<P0ParameterState> states)
    {
        if (states == null) return P0AlarmLevel.Unknown;

        P0AlarmLevel result = P0AlarmLevel.Unknown;
        int resultSeverity = Severity(result);
        foreach (P0ParameterState state in states)
        {
            P0AlarmLevel candidate = state?.level ?? P0AlarmLevel.NoData;
            int candidateSeverity = Severity(candidate);
            if (candidateSeverity > resultSeverity)
            {
                result = candidate;
                resultSeverity = candidateSeverity;
            }
        }
        return result;
    }

    private static P0ParameterState Alarm(
        P0ParameterState state,
        P0AlarmLevel level,
        string reason)
    {
        state.level = level;
        state.reason = reason;
        return state;
    }

    private static int Severity(P0AlarmLevel level)
    {
        switch (level)
        {
            case P0AlarmLevel.HighHigh: return 8;
            case P0AlarmLevel.High: return 7;
            case P0AlarmLevel.LowLow: return 6;
            case P0AlarmLevel.Low: return 5;
            case P0AlarmLevel.NoData: return 4;
            case P0AlarmLevel.Stale: return 3;
            case P0AlarmLevel.Unknown: return 2;
            case P0AlarmLevel.Normal: return 1;
            default: return 0;
        }
    }
}
