using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Run with Unity -batchmode -executeMethod P0MockDataChecks.Run -quit.
public static class P0MockDataChecks
{
    public static void Run()
    {
        Check(P0DataModeParser.Parse(null) == P0DataMode.Mock,
            "Old configs without p0DataMode must default to mock");
        Check(P0DataModeParser.Parse("mock") == P0DataMode.Mock, "Mock mode must parse");
        Check(P0DataModeParser.Parse("network") == P0DataMode.Network, "Network mode must parse");
        ExpectFailure(() => P0DataModeParser.Parse("mixed"), "Mixed mode must be rejected");

        IP0DataSource source = P0DataSourceFactory.Create(P0DataMode.Mock);
        source.Load();
        Check(source.IsDemo, "Mock source must identify itself as demo data");
        Check(source.GetCompanies().Count == 3, "Mock source must expose three companies");
        Check(source.GetCompanies().Select(item => item.companyId).Distinct().Count() == 3,
            "Company IDs must be stable and unique");
        Check(source.GetTankAreas("company-a").Count == 2, "Company A must have two areas");
        Check(source.GetTankAreas("company-b").All(item => item.companyId == "company-b"),
            "Area filtering must not cross companies");
        Check(source.GetHazards("company-a").Select(item => item.hazardId).Distinct().Count() ==
              source.GetHazards("company-a").Count,
            "Hazard IDs must be unique after load");
        Check(source.GetResponsiblePeople("hazard-a1").Count == 2,
            "Hazard A1 must preserve its one-to-many responsible people relation");
        Check(source.GetResponsiblePeople("hazard-c2").Count == 0,
            "Missing responsible people must remain explicit");
        // network 模式已经接上真实接口（见 NetworkP0DataSource），不再是"未实现"。
        // 这里只保证工厂不会把演示数据包当真实数据发出去——那是最危险的退化：
        // 现场看到的是三家"演示企业 A/B/C"，却以为是生产数据。
        IP0DataSource network = P0DataSourceFactory.Create(P0DataMode.Network);
        Check(!network.IsDemo, "Network mode must not report itself as demo data");
        Check(!(network is MockP0DataSource), "Network mode must not fall back to the demo package");
        ExpectFailure(() => network.Load(),
            "Network source must refuse synchronous Load and force LoadAsync");

        CheckHazardMapping();
        CheckResponsiblePersonRows();
        CheckResponsiblePersonCoverage();
        CheckAlarmRules();
        CheckMonitoringPresentation();
        CheckHazardPresentation(source);
        CheckAnnotationStore();

        Debug.Log("P0MockDataChecks: data source checks passed");
    }

    /// <summary>
    /// 拿随包镜像把 riskTagRecord -> 危险源 的映射跑一遍。
    ///
    /// 【为什么值得单独测】这一步靠企业名字匹配（接口没给任何企业外键），挂错企业
    /// 不会抛异常、不会返空，只会让点位悄悄跑到别家企业名下——从日志和界面上都很难
    /// 一眼看出来。数字是按当前这份 871 条镜像实测的，镜像换了要同步更新。
    /// </summary>
    private static void CheckHazardMapping()
    {
        var companies = ReadMirror<ResponseList<P0CompanyRecord>>("security_company_list.json")
            ?.result?.data ?? new List<P0CompanyRecord>();
        Check(companies.Count == 51, "企业镜像应有 51 家，实际 " + companies.Count);

        var p0Companies = companies.Select(item => new P0Company
        {
            companyId = item.id,
            sysOrgCode = item.sysOrgCode,
            name = item.DisplayName
        }).ToList();
        var fullNames = companies.ToDictionary(
            item => item.id, item => (item.companyName ?? string.Empty).Trim());

        var records = ReadMirror<RiskTagResponse>("double-prevention_riskTagRecord_find.json")
            ?.body?.data ?? new List<RiskTagRecord>();
        Check(records.Count == 871, "风险标注镜像应有 871 条，实际 " + records.Count);

        P0HazardMapper.Result mapped = P0HazardMapper.Map(p0Companies, fullNames, records);

        // 515 条 tagType=6-1 点位里只有 1 条 tagRemarks 为空，其余全部应挂到企业上
        Check(mapped.hazards.Count == 514,
            "应建成 514 个危险源，实际 " + mapped.hazards.Count);
        Check(mapped.skippedNonPoint == 356, "非点位应为 356 条，实际 " + mapped.skippedNonPoint);
        Check(mapped.skippedNoCoordinate == 0,
            "点位坐标不应缺失，实际缺 " + mapped.skippedNoCoordinate);
        Check(mapped.unmatchedPrefixes.Count == 0,
            "不应再有匹配不到企业的前缀：" + string.Join("、", mapped.unmatchedPrefixes));

        // 点位最多的一家走的是别名表，它一漏就是 41% 的数据，单独钉住
        P0Company cnpc = p0Companies.First(item => item.name == "中石油南沙油库");
        Check(mapped.hazards.Count(item => item.companyId == cnpc.companyId) == 208,
            "中石油南沙油库应有 208 个点位（别名表命中）");

        // 「碧辟油品」只能靠全称匹配到，简称对不上，一漏就是 79 个
        P0Company bp = p0Companies.First(item => item.name == "碧辟油品");
        Check(mapped.hazards.Count(item => item.companyId == bp.companyId) == 79,
            "碧辟油品应有 79 个点位（全称匹配命中）");

        Check(mapped.hazards.All(item => !string.IsNullOrWhiteSpace(item.level)),
            "每个危险源都应带风险等级");
        Check(mapped.hazards.Select(item => item.hazardId).Distinct().Count() == mapped.hazards.Count,
            "危险源 ID 必须唯一");
    }

    /// <summary>
    /// 责任人这一路单独钉一下：企业列表填的是 result.data，责任人列表只填 result.records，
    /// 只读 data 会静默变成空集（真机上就是这么炸的，面板只显示"责任人资料缺失"）。
    /// </summary>
    private static void CheckResponsiblePersonRows()
    {
        var response = ReadMirror<ResponseList<CompanyHazardSourcesResponsiblePerson>>(
            "api_external_companyHazardSourcesResponsiblePerson_list.json");
        Check(response.result.data == null || response.result.data.Count == 0,
            "这份镜像的 data 本就是空的，若不再是，说明后端改了壳，本检查要重写");

        var rows = SandTableJson.Rows(response);
        Check(rows.Count == 29, "责任人应读出 29 条，实际 " + rows.Count);
        Check(rows.All(item => !string.IsNullOrWhiteSpace(item.sysOrgCode)),
            "责任人必须带 sysOrgCode，否则挂不到企业上");
    }

    /// <summary>
    /// 钉住管理员信息面板的两级匹配覆盖率。
    ///
    /// 精确匹配（scopeCoverage == dangesrcCode）只覆盖 45/264 个罐，是后端数据本身
    /// 没把责任人挂到危险源编码上；企业级兜底（sysOrgCode）能到 245/264。
    /// 这两个数字一旦掉下来，说明后端改了编码口径，面板会大面积变成"暂无登记"。
    /// </summary>
    private static void CheckResponsiblePersonCoverage()
    {
        var people = SandTableJson.Rows(
            ReadMirror<ResponseList<CompanyHazardSourcesResponsiblePerson>>(
                "api_external_companyHazardSourcesResponsiblePerson_list.json"));
        var tanks = SandTableJson.Rows(
            ReadMirror<ResponseList<Tank>>("majorHazard_tank_list.json"));
        Check(tanks.Count == 264, "罐体镜像应有 264 条，实际 " + tanks.Count);

        int exact = tanks.Count(tank => people.Any(
            person => (person.scopeCoverage ?? "").Trim() == (tank.dangesrcCode ?? "").Trim()));
        int withFallback = tanks.Count(tank =>
            people.Any(person =>
                (person.scopeCoverage ?? "").Trim() == (tank.dangesrcCode ?? "").Trim() ||
                (person.sysOrgCode ?? "").Trim() == (tank.sysOrgCode ?? "").Trim()));

        Check(exact == 45, "精确匹配应覆盖 45 个罐，实际 " + exact);
        Check(withFallback == 245, "加企业级兜底后应覆盖 245 个罐，实际 " + withFallback);
        Check(withFallback > exact * 4,
            "企业级兜底必须显著提升覆盖，否则这层兜底就没有意义");
    }

    private static T ReadMirror<T>(string fileName)
    {
        string path = Path.Combine(Application.streamingAssetsPath, OfflineMock.Folder, fileName);
        Check(File.Exists(path), "缺少离线镜像 " + fileName);
        return SandTableJson.Deserialize<T>(File.ReadAllText(path), fileName);
    }

    private static void CheckAnnotationStore()
    {
        string tempDirectory = Path.Combine(
            Path.GetTempPath(), "xiaohudao-p0-annotations-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        string tempPath = Path.Combine(tempDirectory, "p0_mock_annotations.json");
        try
        {
            File.WriteAllText(tempPath,
                "{\"items\":[{\"annotationId\":\"other-author-id\",\"targetType\":\"hazardSource\",\"targetId\":\"hazard-a1\",\"authorId\":\"other-user\",\"authorName\":\"历史用户\",\"content\":\"历史批注\",\"createdAt\":\"2026-09-20T08:00:00+08:00\",\"updatedAt\":\"2026-09-20T08:00:00+08:00\",\"version\":1}]}");
            var store = new P0AnnotationStore(tempPath, "mock-user-self", "当前用户");
            P0Annotation added = store.Add("hazard-a1", "  液位异常，建议复核。  ");
            Check(added.content == "液位异常，建议复核。", "Annotation content must be trimmed");
            Check(new P0AnnotationStore(tempPath, "mock-user-self", "当前用户")
                    .Load("hazard-a1").Count == 2,
                "Saved annotations must survive store recreation");

            P0Annotation edited = store.Update(added.annotationId, 1, "已安排人员复核。");
            Check(edited.version == 2, "Annotation edit must increment version");
            ExpectFailure(() => store.Update(added.annotationId, 1, "旧版本覆盖"),
                "Stale annotation versions must be rejected");
            ExpectFailure(() => store.Add("hazard-a1", "   "),
                "Blank annotations must be rejected");
            ExpectFailure(() => store.Add("hazard-a1", new string('字', 501)),
                "Annotations over 500 characters must be rejected");
            ExpectFailure(() => store.Update("other-author-id", 1, "越权修改"),
                "Annotations from another author must not be editable");
            Check(!File.Exists(tempPath + ".tmp"), "Atomic save must not leave a temp file");

            File.WriteAllText(tempPath, "broken-json");
            new P0AnnotationStore(tempPath, "mock-user-self", "当前用户")
                .Load("hazard-a1");
            Check(Directory.GetFiles(tempDirectory, "*.broken-*").Length == 1,
                "Broken annotation file must be quarantined");

            EditorSceneManager.OpenScene("Assets/LZW/SandTable/SandTable.unity", OpenSceneMode.Single);
            MajorHazard_CompanyManagement host =
                UnityEngine.Object.FindObjectOfType<MajorHazard_CompanyManagement>(true);
            // 场景没接这个 prefab 的话，StartMockMode 会在 p0Source.Load() 之前就 return，
            // 整个危险源管控页（企业图标、危险源图标、统计卡）全都不显示，不只是批注挂掉
            Check(host.p0AnnotationPrefab != null, "Annotation panel must be wired");
            P0AnnotationPanel panel = host.p0AnnotationPrefab.GetComponent<P0AnnotationPanel>();
            Check(panel != null, "The wired prefab must actually carry a P0AnnotationPanel");
            Check(panel.contentInput != null && panel.rows != null && panel.rows.Count > 0,
                "Annotation panel must expose the history rows and the input field");
            Check(panel.rows.TrueForAll(row => row != null && row.bodyText != null),
                "Every history row must be wired, a null row would throw while paging");
            Check(host.GetComponent<P0AnnotationShortcut>() != null,
                "Hazard management host must expose the X-button shortcut");
        }
        finally
        {
            if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, true);
        }
    }

    private static void CheckHazardPresentation(IP0DataSource source)
    {
        var allHazards = source.GetCompanies()
            .SelectMany(company => source.GetHazards(company.companyId))
            .ToList();
        allHazards.Add(source.GetHazards("company-a")[0]);

        P0HazardProjectionResult result = P0HazardProjection.ForCompany(
            allHazards, "company-a", "mock-org-a");
        Check(result.total == 3, "Hazard count must use unique hazard IDs");
        Check(result.byLevel["一级"] == 1, "Hazard level count must be accurate");
        Check(result.items.All(item => item.companyId == "company-a"),
            "Hazard projection must not cross companies");
        Check(source.GetResponsiblePeople("hazard-c2").Count == 0,
            "Missing responsible people must remain explicit");

        EditorSceneManager.OpenScene("Assets/LZW/SandTable/SandTable.unity", OpenSceneMode.Single);
        MajorHazard_CompanyManagement host =
            UnityEngine.Object.FindObjectOfType<MajorHazard_CompanyManagement>(true);
        Check(host != null, "Hazard management host must exist in the main scene");
        Check(host.p0HazardDetailPrefab != null, "Hazard detail panel must be wired");
        P0HazardDetailPanel panel = host.p0HazardDetailPrefab.GetComponent<P0HazardDetailPanel>();
        Check(panel != null && panel.responsiblePeopleText != null && panel.summaryText != null,
            "Hazard detail panel must expose summary and responsible people fields");
    }

    private static void CheckMonitoringPresentation()
    {
        var root = new GameObject("tank-area-marker-test");
        try
        {
            TankAreaObject marker = root.AddComponent<TankAreaObject>();
            marker.alarmImage = new GameObject("alarm", typeof(RectTransform), typeof(Image))
                .GetComponent<Image>();
            marker.alarmImage.transform.SetParent(root.transform, false);
            marker.selectionImage = new GameObject("selection", typeof(RectTransform), typeof(Image))
                .GetComponent<Image>();
            marker.selectionImage.transform.SetParent(root.transform, false);

            marker.Apply(P0AlarmLevel.High, false);
            Check(marker.AlarmColor == Color.red && !marker.SelectionVisible,
                "High alarm must remain red when unselected");
            marker.Apply(P0AlarmLevel.High, true);
            Check(marker.AlarmColor == Color.red && marker.SelectionVisible,
                "Selection must not overwrite the alarm color");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }

        var guard = new P0ActivationGuard();
        int companyAGeneration = guard.Activate("company-a");
        Check(guard.IsCurrent(companyAGeneration, "company-a"),
            "Current company generation must be accepted");
        guard.Activate("company-b");
        Check(!guard.IsCurrent(companyAGeneration, "company-a"),
            "Old company generation must be rejected");
        guard.Deactivate();
        Check(!guard.IsCurrent(guard.Generation, "company-b"),
            "Inactive page must reject updates");

        EditorSceneManager.OpenScene("Assets/LZW/SandTable/SandTable.unity", OpenSceneMode.Single);
        MajorHazard_TankList host = UnityEngine.Object.FindObjectOfType<MajorHazard_TankList>(true);
        Check(host != null, "Monitoring host must exist in the main scene");
        Check(host.p0MonitoringPanelPrefab != null, "P0 monitoring panel must be wired");
        Check(host.GetComponentInParent<Canvas>()?.renderMode == RenderMode.WorldSpace,
            "P0 monitoring UI must use a world-space canvas");

        P0TankAreaMonitorPanel panel = host.p0MonitoringPanelPrefab
            .GetComponent<P0TankAreaMonitorPanel>();
        Check(panel != null && panel.liquidLevelRow != null &&
              panel.temperatureRow != null && panel.concentrationRow != null,
            "P0 monitoring panel must expose all three sensor rows");
    }

    private static void CheckAlarmRules()
    {
        DateTimeOffset now = new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.FromHours(8));
        var cases = new[]
        {
            new AlarmCase("high-high boundary", 90, Sensor(highHigh: 90), P0AlarmLevel.HighHigh),
            new AlarmCase("high boundary", 80, Sensor(high: 80), P0AlarmLevel.High),
            new AlarmCase("zero low threshold", 0, Sensor(low: 0), P0AlarmLevel.Low),
            new AlarmCase("low-low boundary", -10, Sensor(lowLow: -10), P0AlarmLevel.LowLow),
            new AlarmCase("inside range", 50, Sensor(low: 10, high: 80), P0AlarmLevel.Normal),
            new AlarmCase("no enabled threshold", 50, Sensor(), P0AlarmLevel.Unknown)
        };

        foreach (AlarmCase item in cases)
        {
            P0ParameterState state = P0AlarmEvaluator.Evaluate(
                item.sensor,
                Reading(item.value, now),
                now);
            Check(state.level == item.expected, item.name);
        }

        P0SensorDefinition expiring = Sensor(low: 10, high: 80);
        expiring.validitySeconds = 60;
        Check(P0AlarmEvaluator.Evaluate(expiring, null, now).level == P0AlarmLevel.NoData,
            "Missing sample must not become zero");
        Check(P0AlarmEvaluator.Evaluate(expiring, Reading(null, now), now).level == P0AlarmLevel.NoData,
            "Null value must remain no data");
        Check(P0AlarmEvaluator.Evaluate(expiring, Reading(double.NaN, now), now).level == P0AlarmLevel.NoData,
            "NaN must remain no data");
        Check(P0AlarmEvaluator.Evaluate(expiring, Reading(double.PositiveInfinity, now), now).level == P0AlarmLevel.NoData,
            "Infinity must remain no data");
        Check(P0AlarmEvaluator.Evaluate(expiring, Reading(20, now.AddSeconds(-61)), now).level == P0AlarmLevel.Stale,
            "Expired sample must be stale");

        Check(P0AlarmEvaluator.Aggregate(new[]
        {
            State(P0AlarmLevel.Normal),
            State(P0AlarmLevel.High),
            State(P0AlarmLevel.Low)
        }) == P0AlarmLevel.High, "Area must use highest severity");
    }

    private static P0SensorDefinition Sensor(
        double? lowLow = null,
        double? low = null,
        double? high = null,
        double? highHigh = null)
    {
        return new P0SensorDefinition
        {
            sensorId = "test-sensor",
            areaId = "test-area",
            parameter = "test",
            validitySeconds = 60,
            lowLowEnabled = lowLow.HasValue,
            lowLow = lowLow.GetValueOrDefault(),
            lowEnabled = low.HasValue,
            low = low.GetValueOrDefault(),
            highEnabled = high.HasValue,
            high = high.GetValueOrDefault(),
            highHighEnabled = highHigh.HasValue,
            highHigh = highHigh.GetValueOrDefault()
        };
    }

    private static P0SensorReading Reading(double? value, DateTimeOffset sampledAt)
    {
        return new P0SensorReading
        {
            sensorId = "test-sensor",
            areaId = "test-area",
            parameter = "test",
            value = value,
            sampledAt = sampledAt
        };
    }

    private static P0ParameterState State(P0AlarmLevel level)
    {
        return new P0ParameterState { level = level };
    }

    private sealed class AlarmCase
    {
        public readonly string name;
        public readonly double value;
        public readonly P0SensorDefinition sensor;
        public readonly P0AlarmLevel expected;

        public AlarmCase(string name, double value, P0SensorDefinition sensor, P0AlarmLevel expected)
        {
            this.name = name;
            this.value = value;
            this.sensor = sensor;
            this.expected = expected;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void ExpectFailure(Action action, string message)
    {
        try
        {
            action();
        }
        catch (Exception)
        {
            return;
        }

        throw new Exception(message);
    }
}
