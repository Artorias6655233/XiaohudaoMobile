using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// 吃真实接口的 P0 数据源，替代演示数据包。三个接口各管一块：
///
///   企业   security/company/list                         51 家，坐标 100% 有效
///   危险源 double-prevention/riskTagRecord/find           492 条，取 tagType=6-1 的 477 条
///   责任人 api/external/companyHazardSourcesResponsiblePerson/list  17 条
///
/// 【罐区和传感器这两块没有来源】GetTankAreas / GetAreaSnapshots 恒返回空集。
/// riskTagRecord 的 tagContent.dataInfo 实测 492 条全是空数组，没有任何阈值或采样值，
/// 别指望从这里拿监测数据——那条链路是「监测预警」tab 的 tank/list + ICC，和这里无关。
///
/// 【危险源怎么挂到企业】riskTagRecord 里没有 companyId / sysOrgCode：deptOrgCode 和
/// topOrgCode 实测 492 条全是监管方那一个 org，relationIds 和 tank.sid / equipCode 零交集。
/// 唯一的线索是 tagRemarks 的「企业简称-罐名」前缀，只能按名字匹配，逻辑在
/// <see cref="P0HazardMapper"/>。这是接口本身的限制；哪天后端给了企业字段，换掉那一个类即可。
/// </summary>
public sealed class NetworkP0DataSource : IP0DataSource
{
    /// <summary>一次拉完。留足余量，不做翻页——这份标注数据是静态的，条数不会暴涨。</summary>
    private const string RiskTagQueryBody = "{\"page\":1,\"pageSize\":2000}";

    private readonly Dictionary<string, P0Company> companies =
        new Dictionary<string, P0Company>(StringComparer.Ordinal);
    private readonly Dictionary<string, List<P0HazardSource>> hazardsByCompany =
        new Dictionary<string, List<P0HazardSource>>(StringComparer.Ordinal);

    /// <summary>危险源 ID -> 所属企业 ID，GetResponsiblePeople 靠它把人挂到危险源上。</summary>
    private readonly Dictionary<string, string> hazardToCompany =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// 企业 ID -> companyName 全称。
    ///
    /// P0Company.name 存的是简称（沙盘标签放不下全称），但 tagRemarks 的前缀有 4 家用的是
    /// 全称里的字样而不是简称——「发展碧辟油品」对应简称「碧辟油品」、「华凯石油」对应
    /// 「华凯燃气」。只按简称匹配会丢掉 93 个点位（其中碧辟一家就 79 个），所以全称也得留着。
    /// </summary>
    private readonly Dictionary<string, string> fullNameById =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>企业 ID -> 责任人。粒度只能到企业，原因见 LoadResponsiblePeople。</summary>
    private readonly Dictionary<string, List<P0ResponsiblePerson>> peopleByCompany =
        new Dictionary<string, List<P0ResponsiblePerson>>(StringComparer.Ordinal);

    private bool loaded;
    private P0AnnotationStore annotationStore;

    public bool IsDemo => false;

    /// <summary>三个接口是否都加载完成。任一步失败都是 false，面板据此走空态。</summary>
    public bool Ready => loaded;

    public void Load()
    {
        throw new InvalidOperationException("网络数据源必须用 LoadAsync");
    }

    public IEnumerator LoadAsync()
    {
        companies.Clear();
        fullNameById.Clear();
        hazardsByCompany.Clear();
        hazardToCompany.Clear();
        peopleByCompany.Clear();

        yield return LoadCompanies();
        if (companies.Count == 0)
        {
            // 不抛异常：这是在协程里，抛出去调用方 try/catch 也接不住，
            // 只会变成一条 Unity 报错加一个半初始化的数据源。让 Ready 保持 false，
            // 由面板决定显示什么。
            Debug.LogError("[P0 Network] 企业列表为空，危险源无法关联到企业，本次加载放弃");
            yield break;
        }

        // riskTagRecord 和 ICC 同源，要先有 token
        yield return IccAuth.EnsureToken();
        yield return LoadHazards();
        yield return LoadResponsiblePeople();

        // 批注后端没有接口（见对接文档缺口清单第 2 项），仍然落本机。
        // 键空间用 riskTagRecord 的 sid，和演示页那份 p0_mock_annotations.json 分开存，
        // 免得切换数据模式时两套 ID 串到一起。
        annotationStore = new P0AnnotationStore(
            System.IO.Path.Combine(Application.persistentDataPath, "p0_hazard_annotations.json"),
            "mock-user-self",
            "当前用户");

        loaded = true;
        Debug.Log($"[P0 Network] companies={companies.Count}, " +
                  $"hazards={hazardsByCompany.Values.Sum(items => items.Count)}, " +
                  $"people={peopleByCompany.Values.Sum(items => items.Count)}");
    }

    private IEnumerator LoadCompanies()
    {
        string url = Api.SecurityCompanyList(0, 1000);
        var fetch = new OfflineMock.Result();
        yield return OfflineMock.Fetch(url, fetch);
        if (!fetch.ok)
        {
            Debug.LogError("[P0 Network] 企业列表取数失败：" + fetch.error);
            yield break;
        }

        var response = SandTableJson.Deserialize<ResponseList<P0CompanyRecord>>(
            fetch.json, "security/company/list");

        foreach (P0CompanyRecord company in SandTableJson.Rows(response))
        {
            if (company == null || string.IsNullOrWhiteSpace(company.id)) continue;
            // 坐标解析不出来的企业照样收下：它只是建不出图标，但仍要参与名字匹配，
            // 否则挂在它名下的危险源会跟着一起丢。
            company.TryReadPoint(out double longitude, out double latitude);
            companies[company.id] = new P0Company
            {
                companyId = company.id,
                sysOrgCode = company.sysOrgCode,
                name = company.DisplayName,
                longitude = longitude,
                latitude = latitude,
                // 和企业图标一贯的高度保持一致
                height = 100
            };
            fullNameById[company.id] = (company.companyName ?? string.Empty).Trim();
        }
    }

    private IEnumerator LoadHazards()
    {
        string url = Api.RiskTagRecordFind(IccAuth.Token);
        var fetch = new OfflineMock.Result();
        // 正文不能为空、也不能不分页，两者都会让这个接口"看起来通了其实没数据"，
        // 详见 Api.RiskTagRecordFind 的注释。pageSize 取 2000 是为了一次拉完
        // （实测全量 871 条），这份数据基本不变，拉回来在客户端按企业分组即可。
        yield return OfflineMock.PostJson(url, RiskTagQueryBody, null, fetch);
        if (!fetch.ok)
        {
            Debug.LogError("[P0 Network] 风险标注取数失败：" + fetch.error);
            yield break;
        }

        var response = SandTableJson.Deserialize<RiskTagResponse>(
            fetch.json, "riskTagRecord/find");

        P0HazardMapper.Result mapped = P0HazardMapper.Map(
            companies.Values.ToList(), fullNameById, response?.body?.data);

        foreach (P0HazardSource hazard in mapped.hazards)
        {
            GetOrCreate(hazardsByCompany, hazard.companyId).Add(hazard);
            hazardToCompany[hazard.hazardId] = hazard.companyId;
        }

        if (mapped.unmatchedPrefixes.Count > 0)
        {
            Debug.LogWarning("[P0 Network] 这些标注前缀匹配不到企业，点位已丢弃：" +
                             string.Join("、", mapped.unmatchedPrefixes));
        }
        Debug.Log($"[P0 Network] 风险标注 {response?.body?.data?.Count ?? 0} 条：" +
                  $"非点位 {mapped.skippedNonPoint}，无企业 {mapped.skippedNoCompany}，" +
                  $"无坐标 {mapped.skippedNoCoordinate}，建成危险源 {mapped.hazards.Count}");
    }

    private IEnumerator LoadResponsiblePeople()
    {
        string url = Api.CompanyHazardSourcesResponsiblePersonList(1, 1000);
        var fetch = new OfflineMock.Result();
        yield return OfflineMock.Fetch(url, fetch);
        if (!fetch.ok)
        {
            Debug.LogError("[P0 Network] 责任人取数失败：" + fetch.error);
            yield break;
        }

        var response = SandTableJson.Deserialize<ResponseList<CompanyHazardSourcesResponsiblePerson>>(
            fetch.json, "companyHazardSourcesResponsiblePerson/list");

        // 按 sysOrgCode 找企业。接口里另有 scopeCoverage（形如 440175096001）看着像危险源编码，
        // 但它和 riskTagRecord 没有任何公共字段，编码体系也未经后端确认，
        // 所以责任人只能挂到企业级：同一家企业的所有危险源共用这批人。
        var byOrgCode = companies.Values
            .Where(item => !string.IsNullOrWhiteSpace(item.sysOrgCode))
            .GroupBy(item => item.sysOrgCode, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().companyId, StringComparer.Ordinal);

        foreach (CompanyHazardSourcesResponsiblePerson person in SandTableJson.Rows(response))
        {
            if (person == null) continue;
            if (!byOrgCode.TryGetValue(person.sysOrgCode ?? string.Empty, out string companyId)) continue;

            GetOrCreate(peopleByCompany, companyId).Add(new P0ResponsiblePerson
            {
                personId = string.IsNullOrWhiteSpace(person.personId) ? person.id : person.personId,
                name = person.name,
                duty = person.position,
                // 接口没有部门字段，用企业名占位，免得详情卡片出现连续两个分隔符
                department = companies.TryGetValue(companyId, out P0Company company)
                    ? company.name
                    : string.Empty,
                phone = person.phone
            });
        }
    }







    public IReadOnlyList<P0Company> GetCompanies()
    {
        EnsureLoaded();
        return ReadOnly(companies.Values.OrderBy(item => item.companyId).Select(Clone));
    }

    /// <summary>接口没有罐区这一层，恒为空。</summary>
    public IReadOnlyList<P0TankArea> GetTankAreas(string companyId)
    {
        EnsureLoaded();
        return ReadOnly(Enumerable.Empty<P0TankArea>());
    }

    /// <summary>没有任何传感器数据源，恒为空。罐区监测走「监测预警」tab 的 ICC 链路。</summary>
    public IReadOnlyList<P0AreaSnapshot> GetAreaSnapshots(
        string companyId,
        int frameIndex,
        DateTimeOffset now)
    {
        EnsureLoaded();
        return ReadOnly(Enumerable.Empty<P0AreaSnapshot>());
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
        if (!hazardToCompany.TryGetValue(hazardId, out string companyId))
            return ReadOnly(Enumerable.Empty<P0ResponsiblePerson>());
        return peopleByCompany.TryGetValue(companyId, out List<P0ResponsiblePerson> items)
            ? ReadOnly(items.Select(Clone))
            : ReadOnly(Enumerable.Empty<P0ResponsiblePerson>());
    }

    public IReadOnlyList<P0Annotation> GetAnnotations(string targetId)
    {
        EnsureLoaded();
        return annotationStore.Load(targetId);
    }

    public P0Annotation AddAnnotation(string targetId, string content)
    {
        EnsureLoaded();
        return annotationStore.Add(targetId, content);
    }

    public P0Annotation UpdateAnnotation(string annotationId, int expectedVersion, string content)
    {
        EnsureLoaded();
        return annotationStore.Update(annotationId, expectedVersion, content);
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

    private static P0HazardSource Clone(P0HazardSource source)
    {
        return JsonConvert.DeserializeObject<P0HazardSource>(JsonConvert.SerializeObject(source));
    }

    private static P0ResponsiblePerson Clone(P0ResponsiblePerson source)
    {
        return JsonConvert.DeserializeObject<P0ResponsiblePerson>(JsonConvert.SerializeObject(source));
    }
}
