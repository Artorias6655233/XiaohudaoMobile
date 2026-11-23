using System;
using System.Collections;
using System.Collections.Generic;
using LZW.Module.Nsxhcip;
using Newtonsoft.Json.Linq;

namespace LZW.Module.MajorHazard.Scripts
{
    /// <summary>
    /// 重大危险源安全管理用到的平台接口（docs/TODO_ZDWXY.md，抓包见 docs/tools/zdwxyfxgl.* / fxpg.*）。
    /// - double-prevention/*：门户 token（<see cref="NsxhcipSession"/>），请求体 {"body":{…},"page":1,"pageSize":n}；
    /// - jeecg/ext/*：jeecg token（<see cref="NsxhcipJeecgSession"/>）。
    /// 所有方法失败时以 null 回调（列表接口为 null 而不是空列表，以区分「没有数据」和「请求失败」）。
    /// </summary>
    public static class HazardApi
    {
        public const int Timeout = 20;

        private const string DoublePrevention = NsxhcipSession.Host + "/double-prevention";
        private const string Jeecg = NsxhcipJeecgSession.Host;

        #region 一张图标注（设备 / 企业坐标）

        public static IEnumerator FindTags(Action<List<HazardTag>> done)
        {
            const string body = "{\"body\":{\"drawType\":\"point\"},\"page\":1,\"pageSize\":1000}";
            string text = null;
            yield return NsxhcipSession.PostJson($"{DoublePrevention}/riskTagRecord/find?coordinateType=84", body, Timeout,
                t => text = t, true);

            var root = HazardJson.Parse(text);
            if (!HazardJson.IsSuccess(root))
            {
                done(null);
                yield break;
            }

            var tags = new List<HazardTag>();
            foreach (var item in HazardJson.FindList(root, "body.data", "body"))
            {
                var tag = HazardTag.Parse(item);
                if (tag != null)
                    tags.Add(tag);
            }
            done(tags);
        }

        #endregion

        #region 1、智能监测预警

        /// <summary>
        /// 所有危险源监测设备。
        /// </summary>
        public static IEnumerator ListDangerDevices(Action<List<JObject>> done)
        {
            string text = null;
            yield return NsxhcipJeecgSession.PostJson($"{Jeecg}/ext/dataDangerDeviceInfo/list?pageNo=1&pageSize=999999", "{}", Timeout,
                t => text = t);
            done(JeecgList(text));
        }

        /// <summary>
        /// 传入设备编码，返回其中处于报警状态的设备（及报警信息）。
        /// </summary>
        public static IEnumerator ActiveAlarms(IEnumerable<string> deviceCodes, Action<List<JObject>> done)
        {
            var body = new JObject { ["deviceCodeList"] = new JArray(deviceCodes) };
            string text = null;
            yield return NsxhcipJeecgSession.PostJson($"{Jeecg}/ext/dataDangerDeviceInfo/alarmList/activeSnapshot", body.ToString(), Timeout,
                t => text = t);
            done(JeecgList(text));
        }

        #endregion

        #region 2、智能风险管控

        public static IEnumerator FindRiskLevels(Action<List<RiskLevel>> done)
        {
            string text = null;
            yield return NsxhcipSession.PostJson($"{DoublePrevention}/riskConfigLevelControlRang/find",
                "{\"body\":{},\"page\":1,\"pageSize\":999}", Timeout, t => text = t);

            var root = HazardJson.Parse(text);
            if (!HazardJson.IsSuccess(root))
            {
                done(null);
                yield break;
            }

            var levels = new List<RiskLevel>();
            foreach (var item in HazardJson.FindList(root, "body.data", "body"))
            {
                var sid = HazardJson.Str(item, "sid");
                var name = HazardJson.Str(item, "levelName");
                if (sid == null || name == null)
                    continue;
                int.TryParse(HazardJson.Str(item, "levelControlRank"), out var rank);
                levels.Add(new RiskLevel(sid, name, rank));
            }
            levels.Sort((a, b) => a.rank.CompareTo(b.rank));
            done(levels);
        }

        /// <summary>
        /// 重大危险源企业的风险点（与网页「重大危险源风险管理」一致：findDangerSource = 1）。
        /// </summary>
        public static IEnumerator FindRiskPoints(Action<List<JObject>> done)
        {
            string text = null;
            yield return NsxhcipSession.PostJson($"{DoublePrevention}/riskPoint/find",
                "{\"body\":{\"findDangerSource\":1},\"page\":1,\"pageSize\":5000}", Timeout, t => text = t);

            var root = HazardJson.Parse(text);
            done(HazardJson.IsSuccess(root) ? HazardJson.FindList(root, "body.data", "body") : null);
        }

        public static IEnumerator RiskPointDetail(string sid, Action<JObject> done)
        {
            string text = null;
            yield return NsxhcipSession.Get($"{DoublePrevention}/riskPoint/detail/{sid}", Timeout, t => text = t);

            var root = HazardJson.Parse(text);
            done(HazardJson.IsSuccess(root) ? root.SelectToken("body") as JObject : null);
        }

        /// <summary>
        /// 保存风险点（带 sid 即修改）。网页端同样是把详情表单整体提交。
        /// </summary>
        public static IEnumerator SaveRiskPoint(JObject point, Action<bool, string> done)
        {
            string text = null;
            yield return NsxhcipSession.PostJson($"{DoublePrevention}/riskPoint/save", point.ToString(), Timeout, t => text = t);

            var root = HazardJson.Parse(text);
            done(HazardJson.IsSuccess(root), root == null ? "请求失败" : HazardJson.Message(root));
        }

        #endregion

        #region 3、智能风险评估

        public static IEnumerator ListReports(Action<List<JObject>> done)
        {
            string text = null;
            yield return NsxhcipJeecgSession.Get(
                $"{Jeecg}/ext/majorHazardRiskAssessmentReport/list?column=createTime&order=desc&pageNo=1&pageSize=200", Timeout,
                t => text = t);
            done(JeecgList(text));
        }

        /// <summary>
        /// 新增或修改评估报告（网页端：有 id 走 edit，否则 add，都是 POST JSON）。
        /// </summary>
        public static IEnumerator SaveReport(JObject report, Action<bool, string> done)
        {
            var isNew = string.IsNullOrEmpty(HazardJson.Str(report, "id"));
            var url = $"{Jeecg}/ext/majorHazardRiskAssessmentReport/{(isNew ? "add" : "edit")}";
            string text = null;
            yield return NsxhcipJeecgSession.PostJson(url, report.ToString(), Timeout, t => text = t);

            var root = HazardJson.Parse(text);
            done(HazardJson.IsSuccess(root), root == null ? "请求失败" : HazardJson.Message(root));
        }

        /// <summary>
        /// 园区所有重大危险源（hazardName / sysOrgCode / 经纬度…）。
        /// </summary>
        public static IEnumerator ListMajorHazards(Action<List<JObject>> done)
        {
            string text = null;
            yield return NsxhcipJeecgSession.Get($"{Jeecg}/ext/companyMajorHazard/lists", Timeout, t => text = t);
            done(JeecgList(text));
        }

        public static IEnumerator ListCompanies(Action<List<JObject>> done)
        {
            string text = null;
            yield return NsxhcipJeecgSession.PostJson($"{Jeecg}/ext/companyInfo/lists", "{}", Timeout, t => text = t);
            done(JeecgList(text));
        }

        #endregion

        #region 0、安全监督自适应查看

        /// <summary>
        /// 事件管理（icc）的报警事件，按报警时间倒序分页（抓包 docs/tools/aqjdzsy.*）。
        /// deviceType：1 公共区域视频分析 / 2 周界区域 / 3 一键报警 / 4 企业区域视频，null = 全部；isUrge：0 未督办 / 1 已督办。
        /// 回调 (本页数据, 总条数)，失败时数据为 null。
        /// </summary>
        public static IEnumerator FindAlarmEvents(string deviceType, string isUrge, int page, int pageSize, Action<List<JObject>, int> done)
        {
            var filter = new JObject { ["isUrge"] = isUrge };
            if (!string.IsNullOrEmpty(deviceType))
                filter["deviceType"] = deviceType;
            var body = new JObject { ["body"] = filter, ["page"] = page, ["pageSize"] = pageSize };

            string text = null;
            yield return NsxhcipSession.PostJson($"{NsxhcipSession.Host}/icc/dataEventDetail/find", body.ToString(), Timeout,
                t => text = t);

            var root = HazardJson.Parse(text);
            if (!HazardJson.IsSuccess(root))
            {
                done(null, 0);
                yield break;
            }
            int.TryParse(HazardJson.Str(root, "body.total"), out var total);
            done(HazardJson.FindList(root, "body.data"), total);
        }

        #endregion

        /// <summary>
        /// jeecg 的列表：result 为数组，或分页对象 result.records。
        /// </summary>
        private static List<JObject> JeecgList(string text)
        {
            var root = HazardJson.Parse(text);
            if (!HazardJson.IsSuccess(root))
                return null;
            return HazardJson.FindList(root, "result.records", "result", "result.list", "result.data", "data");
        }
    }
}
