using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

public sealed class MockP0DataSource : IP0DataSource
{
    public const string ResourcePath = "P0Mock/p0_mock_data";

    private readonly Dictionary<string, P0Company> companies =
        new Dictionary<string, P0Company>(StringComparer.Ordinal);
    private readonly Dictionary<string, List<P0TankArea>> areasByCompany =
        new Dictionary<string, List<P0TankArea>>(StringComparer.Ordinal);
    private readonly Dictionary<string, List<P0HazardSource>> hazardsByCompany =
        new Dictionary<string, List<P0HazardSource>>(StringComparer.Ordinal);
    private readonly Dictionary<string, P0ResponsiblePerson> people =
        new Dictionary<string, P0ResponsiblePerson>(StringComparer.Ordinal);

    private bool loaded;
    private P0AnnotationStore annotationStore;

    public bool IsDemo => true;

    public bool Ready => loaded;

    public void Load()
    {
        TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
        if (asset == null) throw new InvalidOperationException("演示数据包不可用");

        P0MockDataSet data;
        try
        {
            data = JsonConvert.DeserializeObject<P0MockDataSet>(asset.text);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("演示数据包解析失败", exception);
        }

        if (data == null) throw new InvalidOperationException("演示数据包为空");
        BuildIndexes(data);
        annotationStore = new P0AnnotationStore(
            System.IO.Path.Combine(Application.persistentDataPath, "p0_mock_annotations.json"),
            "mock-user-self",
            "当前用户",
            LoadBuiltInAnnotations());
        loaded = true;
        Debug.Log($"[P0 Mock] companies={companies.Count}, areas={areasByCompany.Values.Sum(items => items.Count)}, hazards={hazardsByCompany.Values.Sum(items => items.Count)}");
    }

    /// <summary>
    /// 演示数据包本来就是同步读的，这里只是满足统一入口。
    /// 异常在这里就地咽掉转成 Ready=false —— 协程里抛出去调用方接不住。
    /// <see cref="Load"/> 仍然照抛，Editor 的数据包校验靠的就是它。
    /// </summary>
    public IEnumerator LoadAsync()
    {
        try
        {
            Load();
        }
        catch (Exception exception)
        {
            Debug.LogError("[P0] 演示数据包不可用：" + exception.Message);
        }
        yield break;
    }

    public IReadOnlyList<P0Company> GetCompanies()
    {
        EnsureLoaded();
        return ReadOnly(companies.Values.OrderBy(item => item.companyId).Select(Clone));
    }

    public IReadOnlyList<P0TankArea> GetTankAreas(string companyId)
    {
        EnsureLoaded();
        return areasByCompany.TryGetValue(companyId ?? string.Empty, out List<P0TankArea> items)
            ? ReadOnly(items.Select(Clone))
            : ReadOnly(Enumerable.Empty<P0TankArea>());
    }

    public IReadOnlyList<P0AreaSnapshot> GetAreaSnapshots(
        string companyId,
        int frameIndex,
        DateTimeOffset now)
    {
        EnsureLoaded();
        if (!areasByCompany.TryGetValue(companyId ?? string.Empty, out List<P0TankArea> areas))
            return ReadOnly(Enumerable.Empty<P0AreaSnapshot>());

        var snapshots = new List<P0AreaSnapshot>();
        foreach (P0TankArea area in areas)
        {
            var snapshot = new P0AreaSnapshot { areaId = area.areaId, frameIndex = frameIndex };
            foreach (P0SensorDefinition sensor in area.sensors)
            {
                P0SensorFrame frame = SelectFrame(sensor.frames, frameIndex);
                snapshot.readings.Add(new P0SensorReading
                {
                    sensorId = sensor.sensorId,
                    areaId = area.areaId,
                    parameter = sensor.parameter,
                    value = frame?.value,
                    sampledAt = now.AddSeconds(-(frame?.ageSeconds ?? 0))
                });
            }
            snapshots.Add(snapshot);
        }
        return new ReadOnlyCollection<P0AreaSnapshot>(snapshots);
    }

    public IReadOnlyList<P0HazardSource> GetHazards(string companyId)
    {
        EnsureLoaded();
        return hazardsByCompany.TryGetValue(companyId ?? string.Empty, out List<P0HazardSource> items)
            ? ReadOnly(items.Select(Clone))
            : ReadOnly(Enumerable.Empty<P0HazardSource>());
    }

    public IReadOnlyList<P0ResponsiblePerson> GetResponsiblePeople(string hazardId)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(hazardId)) return ReadOnly(Enumerable.Empty<P0ResponsiblePerson>());
        return ReadOnly(people.Values
            .Where(item => item.hazardIds != null && item.hazardIds.Contains(hazardId))
            .OrderBy(item => item.personId)
            .Select(Clone));
    }

    public IReadOnlyList<P0Annotation> GetAnnotations(string hazardId)
    {
        EnsureLoaded();
        return annotationStore.Load(hazardId);
    }

    public P0Annotation AddAnnotation(string hazardId, string content)
    {
        EnsureLoaded();
        return annotationStore.Add(hazardId, content);
    }

    public P0Annotation UpdateAnnotation(string annotationId, int expectedVersion, string content)
    {
        EnsureLoaded();
        return annotationStore.Update(annotationId, expectedVersion, content);
    }

    private static IEnumerable<P0Annotation> LoadBuiltInAnnotations()
    {
        TextAsset asset = Resources.Load<TextAsset>("P0Mock/p0_builtin_annotations");
        if (asset == null) return Enumerable.Empty<P0Annotation>();
        P0AnnotationDocument document = JsonConvert.DeserializeObject<P0AnnotationDocument>(asset.text);
        return document?.items ?? Enumerable.Empty<P0Annotation>();
    }

    private void BuildIndexes(P0MockDataSet data)
    {
        companies.Clear();
        areasByCompany.Clear();
        hazardsByCompany.Clear();
        people.Clear();

        foreach (P0Company company in data.companies ?? new List<P0Company>())
        {
            RequireId(company?.companyId, "企业");
            if (!companies.TryAdd(company.companyId, company))
                throw new InvalidOperationException("企业 ID 重复：" + company.companyId);
        }

        foreach (P0TankArea area in data.tankAreas ?? new List<P0TankArea>())
        {
            RequireCompany(area?.companyId, area?.sysOrgCode, "罐区 " + area?.areaId);
            if (string.IsNullOrWhiteSpace(area.areaId)) throw new InvalidOperationException("罐区 ID 为空");
            GetOrCreate(areasByCompany, area.companyId).Add(area);
        }

        var hazardIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (P0HazardSource hazard in data.hazards ?? new List<P0HazardSource>())
        {
            RequireCompany(hazard?.companyId, hazard?.sysOrgCode, "危险源 " + hazard?.hazardId);
            RequireId(hazard.hazardId, "危险源");
            if (!hazardIds.Add(hazard.hazardId))
                throw new InvalidOperationException("危险源 ID 重复：" + hazard.hazardId);
            GetOrCreate(hazardsByCompany, hazard.companyId).Add(hazard);
        }

        foreach (P0ResponsiblePerson person in data.responsiblePeople ?? new List<P0ResponsiblePerson>())
        {
            RequireId(person?.personId, "责任人");
            if (!people.TryAdd(person.personId, person))
                throw new InvalidOperationException("责任人 ID 重复：" + person.personId);
        }
    }

    private void RequireCompany(string companyId, string sysOrgCode, string owner)
    {
        if (!companies.TryGetValue(companyId ?? string.Empty, out P0Company company) ||
            !string.Equals(company.sysOrgCode, sysOrgCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(owner + " 无法关联到唯一企业");
        }
    }

    private static void RequireId(string id, string type)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException(type + " ID 为空");
    }

    private static List<T> GetOrCreate<T>(Dictionary<string, List<T>> index, string key)
    {
        if (!index.TryGetValue(key, out List<T> items))
        {
            items = new List<T>();
            index.Add(key, items);
        }
        return items;
    }

    private static P0SensorFrame SelectFrame(List<P0SensorFrame> frames, int frameIndex)
    {
        if (frames == null || frames.Count == 0) return null;
        int normalized = ((frameIndex % frames.Count) + frames.Count) % frames.Count;
        return frames[normalized];
    }

    private void EnsureLoaded()
    {
        if (!loaded) throw new InvalidOperationException("P0 数据源尚未加载");
    }

    private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> items)
    {
        return new ReadOnlyCollection<T>(items.ToList());
    }

    private static P0Company Clone(P0Company source)
    {
        return new P0Company
        {
            companyId = source.companyId,
            sysOrgCode = source.sysOrgCode,
            name = source.name,
            longitude = source.longitude,
            latitude = source.latitude,
            height = source.height,
            tankAreaIds = new List<string>(source.tankAreaIds ?? new List<string>()),
            hazardIds = new List<string>(source.hazardIds ?? new List<string>())
        };
    }

    private static P0TankArea Clone(P0TankArea source)
    {
        return JsonConvert.DeserializeObject<P0TankArea>(JsonConvert.SerializeObject(source));
    }

    private static P0HazardSource Clone(P0HazardSource source)
    {
        return JsonConvert.DeserializeObject<P0HazardSource>(JsonConvert.SerializeObject(source));
    }

    private static P0ResponsiblePerson Clone(P0ResponsiblePerson source)
    {
        return JsonConvert.DeserializeObject<P0ResponsiblePerson>(JsonConvert.SerializeObject(source));
    }
}
