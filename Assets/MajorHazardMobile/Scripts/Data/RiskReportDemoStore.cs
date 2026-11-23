using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LZW.Module.MajorHazard.Scripts
{
    /// <summary>
    /// 智能风险评估的演示数据：报告只保存在本机（persistentDataPath/risk_reports_demo.json），不提交平台。
    /// 平台的 add / edit 会校验「危险源与报告属于同一企业」（报告的 sysOrgCode 取登录账号所属组织，园区管理账号与任何企业都对不上），
    /// 演示时用这里保存。记录带 <see cref="DemoKey"/> 标记；改平台上的报告时以同一 id 存一份本地副本，列表里覆盖平台版本。
    /// </summary>
    public static class RiskReportDemoStore
    {
        /// <summary>演示记录的标记字段（提交平台前要去掉）。</summary>
        public const string DemoKey = "_demo";

        private static string FilePath => Path.Combine(Application.persistentDataPath, "risk_reports_demo.json");

        private static List<JObject> _reports;

        public static IReadOnlyList<JObject> Reports
        {
            get
            {
                EnsureLoaded();
                return _reports;
            }
        }

        public static bool IsDemo(JObject report) => report?[DemoKey]?.Type == JTokenType.Boolean && report[DemoKey].Value<bool>();

        /// <summary>
        /// 平台报告 + 本地演示报告（同 id 的本地副本覆盖平台版本），按创建时间倒序。
        /// </summary>
        public static List<JObject> Merge(IEnumerable<JObject> platform)
        {
            EnsureLoaded();
            var local = _reports.ToDictionary(r => HazardJson.Str(r, "id") ?? "", r => r);
            var merged = new List<JObject>();
            foreach (var report in platform ?? Enumerable.Empty<JObject>())
            {
                var id = HazardJson.Str(report, "id");
                merged.Add(id != null && local.TryGetValue(id, out var copy) ? copy : report);
            }
            var platformIds = new HashSet<string>(merged.Select(r => HazardJson.Str(r, "id") ?? ""));
            merged.AddRange(_reports.Where(r => !platformIds.Contains(HazardJson.Str(r, "id") ?? "")));
            return merged.OrderByDescending(r => HazardJson.Str(r, "createTime") ?? "", StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// 保存（新增或覆盖同 id 的记录），返回保存后的记录。
        /// </summary>
        public static JObject Save(JObject report)
        {
            EnsureLoaded();
            var saved = (JObject)report.DeepClone();
            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var id = HazardJson.Str(saved, "id");
            if (id == null)
            {
                id = $"demo-{DateTime.Now.Ticks}";
                saved["id"] = id;
                saved["createTime"] = now;
                saved["createBy"] = "移动端演示";
            }
            saved["updateTime"] = now;
            saved[DemoKey] = true;

            _reports.RemoveAll(r => HazardJson.Str(r, "id") == id);
            _reports.Add(saved);
            Write();
            return saved;
        }

        /// <summary>
        /// 本地还没有演示报告时，为前几个危险源生成几份（危险源 id / 名称来自平台，其余字段是虚构的演示内容）。
        /// </summary>
        public static void SeedIfEmpty(IReadOnlyList<(string id, string name, string company)> hazards)
        {
            EnsureLoaded();
            if (_reports.Count > 0 || hazards == null || hazards.Count == 0)
                return;

            var samples = new[]
            {
                ("安全评价报告", "H3603", "张伟", "13800000001", "华南安全评价有限公司（演示）", -12),
                ("SIL 等级评估报告", "H3605", "李娜", "13800000002", "广州安全技术咨询有限公司（演示）", -45),
                ("安全现状评价报告", "H3603", "王强", "13800000003", "南沙安全工程技术有限公司（演示）", -90),
                ("重大危险源评估报告", "3", "刘洋", "13800000004", "华南安全评价有限公司（演示）", -150),
            };
            for (var i = 0; i < samples.Length && i < hazards.Count; i++)
            {
                var (title, classification, assessor, phone, agency, days) = samples[i];
                var hazard = hazards[i];
                var date = DateTime.Today.AddDays(days);
                _reports.Add(new JObject
                {
                    ["id"] = $"demo-seed-{i + 1}",
                    ["reportName"] = $"{hazard.company ?? ShortName(hazard.name)}{title}",
                    ["companyMajorHazardId"] = hazard.id,
                    ["assessmentTime"] = date.ToString("yyyy-MM-dd"),
                    ["assessor"] = assessor,
                    ["assessorPhone"] = phone,
                    ["nameOfEvaluationAgency"] = agency,
                    ["classificationOfQualificationCertificate"] = classification,
                    ["status"] = "1",
                    ["createBy"] = "移动端演示",
                    ["createTime"] = date.AddDays(1).ToString("yyyy-MM-dd 09:30:00"),
                    [DemoKey] = true,
                });
            }
            Write();
        }

        /// <summary>清空本地演示报告（下次加载时重新生成示例）。</summary>
        public static void Clear()
        {
            _reports = new List<JObject>();
            Write();
        }

        private static string ShortName(string name) => name != null && name.Length > 12 ? name.Substring(0, 12) : name;

        private static void EnsureLoaded()
        {
            if (_reports != null)
                return;
            _reports = new List<JObject>();
            try
            {
                if (File.Exists(FilePath))
                    _reports = JArray.Parse(File.ReadAllText(FilePath)).OfType<JObject>().ToList();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[{nameof(RiskReportDemoStore)}] 读取 {FilePath} 失败：{e.Message}");
            }
        }

        private static void Write()
        {
            try
            {
                File.WriteAllText(FilePath, new JArray(_reports).ToString());
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[{nameof(RiskReportDemoStore)}] 写入 {FilePath} 失败：{e.Message}");
            }
        }
    }
}
