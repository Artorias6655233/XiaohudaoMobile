using System;
using System.Collections.Generic;

public enum P0DataMode
{
    Mock,
    Network
}

public static class P0DataModeParser
{
    public static P0DataMode Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return P0DataMode.Mock;

        switch (value.Trim().ToLowerInvariant())
        {
            case "mock": return P0DataMode.Mock;
            case "network": return P0DataMode.Network;
            default: throw new ArgumentException("仅支持 mock 或 network", nameof(value));
        }
    }
}

[Serializable]
public sealed class P0MockDataSet
{
    public List<P0Company> companies = new List<P0Company>();
    public List<P0TankArea> tankAreas = new List<P0TankArea>();
    public List<P0HazardSource> hazards = new List<P0HazardSource>();
    public List<P0ResponsiblePerson> responsiblePeople = new List<P0ResponsiblePerson>();
}

[Serializable]
public sealed class P0Company
{
    public string companyId;
    public string sysOrgCode;
    public string name;
    public double longitude;
    public double latitude;
    public double height;
    public List<string> tankAreaIds = new List<string>();
    public List<string> hazardIds = new List<string>();
}

[Serializable]
public sealed class P0TankArea
{
    public string areaId;
    public string companyId;
    public string sysOrgCode;
    public string name;
    public double longitude;
    public double latitude;
    public double height;
    public List<string> tankIds = new List<string>();
    public List<string> hazardIds = new List<string>();
    public List<P0SensorDefinition> sensors = new List<P0SensorDefinition>();
}

[Serializable]
public sealed class P0SensorDefinition
{
    public string sensorId;
    public string areaId;
    public string parameter;
    public string displayName;
    public string unit;
    public bool lowLowEnabled;
    public double lowLow;
    public bool lowEnabled;
    public double low;
    public bool highEnabled;
    public double high;
    public bool highHighEnabled;
    public double highHigh;
    public int validitySeconds;
    public List<P0SensorFrame> frames = new List<P0SensorFrame>();
}

[Serializable]
public sealed class P0SensorFrame
{
    public double? value;
    public int ageSeconds;
}

[Serializable]
public sealed class P0AreaSnapshot
{
    public string areaId;
    public int frameIndex;
    public List<P0SensorReading> readings = new List<P0SensorReading>();
}

[Serializable]
public sealed class P0SensorReading
{
    public string sensorId;
    public string areaId;
    public string parameter;
    public double? value;
    public DateTimeOffset sampledAt;
}

[Serializable]
public sealed class P0HazardSource
{
    public string hazardId;
    public string companyId;
    public string sysOrgCode;
    public string areaId;
    public string name;
    public string level;
    public string location;
    public string materials;
    public double longitude;
    public double latitude;
    public double height;
    public List<string> responsiblePersonIds = new List<string>();
}

[Serializable]
public sealed class P0ResponsiblePerson
{
    public string personId;
    public string name;
    public string duty;
    public string department;
    public string phone;
    public List<string> hazardIds = new List<string>();
}

[Serializable]
public sealed class P0Annotation
{
    public string annotationId;
    public string targetType;
    public string targetId;
    public string authorId;
    public string authorName;
    public string content;
    public DateTimeOffset createdAt;
    public DateTimeOffset updatedAt;
    public int version;
}
