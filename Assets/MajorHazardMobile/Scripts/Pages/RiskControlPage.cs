using System.Collections;
using System.Collections.Generic;
using System.Linq;
using LZW.Module.MajorHazard.Mobile.UI;
using LZW.Module.MajorHazard.Scripts;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace LZW.Module.MajorHazard.Mobile.Pages
{
    /// <summary>
    /// 2、智能风险管控：按手机定位（GNSS）显示离自己最近的风险点，点等级按钮选择新等级，确认后提交到平台（double-prevention/riskPoint/save）。
    /// AR 端用 VLP 定位、在视野前方呈现，手势 / 语音改等级；移动端用 GPS + 指南针（信息卡上的箭头指向风险点），点按钮改等级。
    ///
    /// 风险点自身的 x84 / y84 大多为空，用所属企业在一张图上的位置代替（<see cref="HazardCache.FindCompany"/>）。
    /// </summary>
    public class RiskControlPage : HazardPage
    {
        [Tooltip("超过该距离（米）的风险点不算「附近」，0 表示不限")]
        [SerializeField] private float maxDistance;

        public override string Title => "智能风险管控";
        public override string Summary => "按手机定位显示最近的风险点，现场修改风险等级";
        public override string Glyph => "控";
        public override Color GlyphColor => new Color(1f, 0.6f, 0.15f);

        public override string Help => "· 根据手机定位自动显示离你最近的风险点，箭头指向它所在的方向（需开启定位权限）\n· 点「上一个 / 下一个」或附近列表手动切换，点「最近」恢复自动跟随\n· 点等级按钮选择新的风险等级，再点「确认修改」提交到平台（会修改平台数据）";

        protected override float PortraitMainShare => 0.42f;

        private class RiskEntry
        {
            public string sid;
            public string name;
            public string levelId;
            public string levelName;
            public string company;
            public string position;
            public string dutyDept;
            public string persons;
            public string locatedBy;
            public bool located;
            public double longitude;
            public double latitude;
            public float distance = float.PositiveInfinity;
            public JObject raw;
        }

        private List<RiskLevel> _levels = new List<RiskLevel>(RiskLevel.Defaults);
        private List<RiskEntry> _entries = new List<RiskEntry>();
        private List<RiskEntry> _sorted = new List<RiskEntry>();
        private RiskEntry _current;
        private RiskLevel _pending;
        private bool _locked; // 用户手动切换过，不再自动跟随最近
        private bool _loading;
        private bool _saving;
        private bool _loaded;
        private DeviceGeoLocation _geo;

        private Text _infoTitle;
        private Text _infoLevel;
        private Text _infoBody;
        private Text _infoDistance;
        private RectTransform _arrow;
        private Text _summary;
        private readonly List<(RiskLevel level, Button button)> _levelButtons = new List<(RiskLevel, Button)>();
        private Button _confirm;
        private RectTransform _nearby;
        private string _nearbyKey;

        protected override void BuildContent()
        {
            _geo = DeviceGeoLocation.Ensure();
            BuildInfoCard();

            _summary = MUI.Paragraph(Side, "", MUI.FontSmall, MUI.DimTextColor);

            var row = MUI.Row(Side);
            MUI.Button(row, "上一个", () => Step(-1), -1, MUI.ButtonHeight, MUI.ChipColor);
            MUI.Button(row, "下一个", () => Step(1), -1, MUI.ButtonHeight, MUI.ChipColor);
            MUI.Button(row, "最近", FollowNearest, -1, MUI.ButtonHeight, MUI.ChipColor);

            MUI.SectionTitle(Side, "修改风险等级");
            for (var i = 0; i < RiskLevel.Defaults.Length; i += 2)
            {
                row = MUI.Row(Side);
                for (var j = i; j < Mathf.Min(i + 2, RiskLevel.Defaults.Length); j++)
                {
                    var level = RiskLevel.Defaults[j];
                    _levelButtons.Add((level, MUI.Button(row, level.name, () => Choose(level.name))));
                }
            }
            _confirm = MUI.Button(Side, "确认修改", Confirm, -1, MUI.ButtonHeight, MUI.ConfirmColor);

            MUI.SectionTitle(Side, "附近风险点");
            _nearby = MUI.Rect(Side, "NearbyList");
            MUI.Vertical(_nearby, 6);
        }

        /// <summary>
        /// 主区域中央的风险信息卡（AR 端是视野正前方的卡片）。
        /// </summary>
        private void BuildInfoCard()
        {
            var scrollContent = MUI.Scroll(MainArea, out var scroll, 0);
            MUI.Stretch((RectTransform)scroll.transform);

            var card = MUI.Rect(scrollContent, "RiskInfo");
            MUI.Image(card, MUI.PanelColor, false, 12f);
            MUI.Vertical(card, 8, 18);

            var top = MUI.Row(card, 56, 12);
            top.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            var arrowBox = MUI.Rect(top, "Arrow");
            MUI.Size(arrowBox, 56, 56, 0);
            MUI.Image(arrowBox, new Color(0.1f, 0.3f, 0.5f, 1f), false, 28f);
            var arrowText = MUI.Text(arrowBox, "▲", 30, MUI.AccentColor, TextAnchor.MiddleCenter);
            MUI.Stretch(arrowText.rectTransform);
            _arrow = arrowText.rectTransform;
            _infoDistance = MUI.Text(top, "", MUI.FontBody, MUI.AccentColor);
            MUI.Size(_infoDistance, 0, -1, 1);

            _infoTitle = MUI.Paragraph(card, "正在获取风险点…", MUI.FontLarge, MUI.TextColor);
            _infoTitle.fontStyle = FontStyle.Bold;
            _infoLevel = MUI.Paragraph(card, "", MUI.FontTitle, MUI.TextColor);
            _infoBody = MUI.Paragraph(card, "", MUI.FontBody, MUI.TextColor);
        }

        protected override void OnShown()
        {
            _geo.Acquire();
            if (!_loaded && !_loading)
                StartCoroutine(Load(false));
        }

        protected override void OnHidden()
        {
            if (_geo != null)
                _geo.Release();
            _loading = false;
            _saving = false;
        }

        public override void Reload()
        {
            StartCoroutine(Load(true));
        }

        #region 数据

        private IEnumerator Load(bool force)
        {
            if (_loading)
                yield break;
            _loading = true;
            SetStatus("正在获取风险点…");

            yield return HazardCache.LoadTags(force);

            List<RiskLevel> levels = null;
            yield return HazardApi.FindRiskLevels(l => levels = l);
            if (levels != null && levels.Count > 0)
                _levels = levels;

            List<JObject> points = null;
            yield return HazardApi.FindRiskPoints(p => points = p);
            _loading = false;

            if (points == null)
            {
                SetStatus("获取风险点失败，点右上角「刷新」重试");
                yield break;
            }

            var previous = _current?.sid;
            _entries = points.Select(CreateEntry).ToList();
            _loaded = true;
            _current = _entries.FirstOrDefault(e => e.sid == previous);
            _locked &= _current != null;
            SetStatus(_entries.Count == 0 ? "平台上没有重大危险源风险点" : "");
            UpdateDistances();
            UpdateView();
        }

        private RiskEntry CreateEntry(JObject item)
        {
            var entry = new RiskEntry
            {
                sid = HazardJson.Str(item, "sid"),
                name = HazardJson.Str(item, "riskPointName") ?? "（未命名风险点）",
                levelId = HazardJson.Str(item, "riskPointLevelId"),
                levelName = HazardJson.Str(item, "riskPointLevelName"),
                company = HazardJson.Str(item, "riskUnitDeptName"),
                position = HazardJson.Str(item, "riskPointPosition"),
                dutyDept = HazardJson.Str(item, "dutyDeptName"),
                persons = JoinNonEmpty("、", HazardJson.Str(item, "principalResponsiblePerson"), HazardJson.Str(item, "controlDutyUserNames")),
                raw = item
            };
            if (entry.levelName == null && entry.levelId != null)
                entry.levelName = _levels.FirstOrDefault(l => l.sid == entry.levelId)?.name;

            if (HazardJson.TryDouble(item, out var lon, "x84") && HazardJson.TryDouble(item, out var lat, "y84") && HazardJson.IsValidLonLat(lon, lat))
            {
                entry.longitude = lon;
                entry.latitude = lat;
                entry.located = true;
                entry.locatedBy = "风险点坐标";
            }
            else
            {
                var company = HazardCache.FindCompany(
                    HazardJson.Str(item, "riskUnitDeptId") ?? HazardJson.Str(item, "topOrgCode"), entry.company);
                if (company != null)
                {
                    entry.longitude = company.longitude;
                    entry.latitude = company.latitude;
                    entry.located = true;
                    entry.locatedBy = "所属企业位置";
                }
            }
            return entry;
        }

        #endregion

        #region 定位与显示

        protected override void Refresh()
        {
            if (!_loaded)
                return;
            UpdateDistances();
            UpdateView();
        }

        private void UpdateDistances()
        {
            if (!_geo.HasFix)
            {
                foreach (var entry in _entries)
                    entry.distance = float.PositiveInfinity;
                _sorted = _entries.OrderBy(e => RiskLevel.RankOf(e.levelName)).ToList();
            }
            else
            {
                foreach (var entry in _entries)
                    entry.distance = entry.located
                        ? DeviceGeoLocation.Distance(_geo.Longitude, _geo.Latitude, entry.longitude, entry.latitude)
                        : float.PositiveInfinity;
                // 同一企业的风险点距离相同，风险高的排前面
                _sorted = _entries
                    .Where(e => maxDistance <= 0 || e.distance <= maxDistance || !e.located)
                    .OrderBy(e => e.distance)
                    .ThenBy(e => RiskLevel.RankOf(e.levelName))
                    .ToList();
            }

            // 未手动切换、也没有待确认的修改时，自动跟随最近的风险点
            if ((!_locked && _pending == null) || _current == null)
                _current = _sorted.FirstOrDefault();
        }

        private void UpdateView()
        {
            var located = _entries.Count(e => e.located);
            _summary.text = $"风险点 {_entries.Count} 个（{located} 个可定位）\n" +
                            (_geo.HasFix ? $"{_geo.Describe()}，按距离排序" : $"{_geo.Describe()}，按风险等级排序") +
                            (_locked ? "\n已手动选择，点「最近」恢复自动跟随" : "");

            if (_current == null)
            {
                _infoTitle.text = _loading ? "正在获取风险点…" : "附近没有风险点";
                _infoLevel.text = _infoBody.text = _infoDistance.text = "";
                _arrow.gameObject.SetActive(false);
            }
            else
            {
                var c = _current;
                var hasDistance = c.located && !float.IsInfinity(c.distance);
                _infoDistance.text = hasDistance
                    ? $"{Direction(c)} {FormatDistance(c.distance)}\n<size={MUI.FontSmall}><color=#{MUI.Hex(MUI.DimTextColor)}>（{c.locatedBy}）</color></size>"
                    : c.located ? "未定位，无法计算距离" : "无坐标";
                UpdateArrow(c, hasDistance);
                _infoTitle.text = c.name;
                var levelColor = MUI.Hex(RiskLevel.ColorOf(c.levelName));
                _infoLevel.text = $"风险等级：<color=#{levelColor}>{c.levelName ?? "未评定"}</color>" +
                                  (_pending != null ? $"  →  <color=#{MUI.Hex(_pending.Color)}>{_pending.name}</color>（待确认）" : "");
                _infoBody.text = JoinNonEmpty("\n",
                    Line("企业", c.company), Line("位置", c.position), Line("责任部门", c.dutyDept), Line("责任人", c.persons)) ?? "";
            }

            foreach (var (level, button) in _levelButtons)
            {
                var active = _pending != null ? _pending.name == level.name : _current?.levelName == level.name;
                MUI.SetColor(button, active ? level.Color * 0.75f + new Color(0, 0, 0, 0.25f) : MUI.ButtonColor);
            }
            MUI.SetLabel(_confirm, _saving ? "提交中…" : _pending != null ? $"确认修改为「{_pending.name}」" : "确认修改");

            // 列表只在内容变化时重建（不含实时距离），避免滑动中被打断
            var nearby = _sorted.Take(30).ToList();
            var key = string.Join("|", nearby.Select(e => $"{e.sid}:{e.levelName}:{e == _current}"));
            if (key == _nearbyKey)
                return;
            _nearbyKey = key;
            MUI.Clear(_nearby);
            if (nearby.Count == 0)
                MUI.Text(_nearby, _loading ? "加载中…" : "暂无风险点", MUI.FontBody, MUI.DimTextColor, TextAnchor.MiddleCenter, 50);
            foreach (var e in nearby)
            {
                var card = MUI.Card(_nearby, () => Select(e), e == _current ? new Color(0.1f, 0.34f, 0.52f, 1f) : (Color?)null, 10);
                MUI.Paragraph(card.transform, e.name, MUI.FontBody, MUI.TextColor);
                var dist = float.IsInfinity(e.distance) ? "" : $"　{FormatDistance(e.distance)}";
                MUI.Paragraph(card.transform, $"<color=#{MUI.Hex(RiskLevel.ColorOf(e.levelName))}>{e.levelName ?? "未评定"}</color>　{e.company}{dist}",
                    MUI.FontSmall, MUI.DimTextColor);
            }
        }

        /// <summary>
        /// 箭头：有指南针时指向风险点相对屏幕上方的方向；没有指南针时指向正北为上的方位。
        /// </summary>
        private void UpdateArrow(RiskEntry entry, bool hasDistance)
        {
            _arrow.gameObject.SetActive(hasDistance);
            if (!hasDistance)
                return;
            var bearing = DeviceGeoLocation.Bearing(_geo.Longitude, _geo.Latitude, entry.longitude, entry.latitude);
            var relative = float.IsNaN(_geo.Heading) ? bearing : bearing - _geo.Heading;
            _arrow.localRotation = Quaternion.Euler(0f, 0f, -relative);
        }

        /// <summary>
        /// 风险点相对手机朝向的方位（正前方 / 左前方 …）；没有指南针时给出东南西北。
        /// </summary>
        private string Direction(RiskEntry entry)
        {
            var bearing = DeviceGeoLocation.Bearing(_geo.Longitude, _geo.Latitude, entry.longitude, entry.latitude);
            if (float.IsNaN(_geo.Heading))
            {
                string[] names = { "北", "东北", "东", "东南", "南", "西南", "西", "西北" };
                return $"{names[Mathf.RoundToInt(bearing / 45f) % 8]}方";
            }
            var angle = Mathf.DeltaAngle(_geo.Heading, bearing);
            var abs = Mathf.Abs(angle);
            var side = angle < 0 ? "左" : "右";
            return abs < 20 ? "正前方" : abs < 70 ? $"{side}前方" : abs < 110 ? $"{side}侧" : abs < 160 ? $"{side}后方" : "正后方";
        }

        #endregion

        #region 交互

        private void Select(RiskEntry entry)
        {
            if (_saving)
                return;
            _current = entry;
            _pending = null;
            _locked = true;
            UpdateView();
        }

        private void Step(int delta)
        {
            if (_sorted.Count == 0)
                return;
            var index = _current == null ? -1 : _sorted.IndexOf(_current);
            Select(_sorted[(index + delta + _sorted.Count) % _sorted.Count]);
        }

        private void FollowNearest()
        {
            if (_saving)
                return;
            _locked = false;
            _pending = null;
            _current = _sorted.FirstOrDefault();
            UpdateView();
        }

        private void Choose(string levelName)
        {
            if (_current == null || _saving)
                return;
            // 按钮用默认等级名，提交用接口返回的等级（sid 以接口为准）
            var level = _levels.FirstOrDefault(l => l.name == levelName) ?? RiskLevel.Defaults.FirstOrDefault(l => l.name == levelName);
            if (level == null)
                return;
            if (level.name == _current.levelName)
            {
                _pending = null;
                Toast($"「{_current.name}」已经是{level.name}");
            }
            else
            {
                _pending = level;
                _locked = true; // 待确认期间不要切走
            }
            UpdateView();
        }

        private void Confirm()
        {
            if (_saving)
                return;
            if (_current == null || _pending == null)
            {
                Toast("请先点等级按钮选择新的风险等级");
                return;
            }
            var entry = _current;
            var level = _pending;
            MOverlay.Instance.Confirm("修改风险等级",
                $"将「{entry.name}」的风险等级\n由 <color=#{MUI.Hex(RiskLevel.ColorOf(entry.levelName))}>{entry.levelName ?? "未评定"}</color> 改为 <color=#{MUI.Hex(level.Color)}>{level.name}</color>？\n\n<color=#{MUI.Hex(MUI.WarnColor)}>提交后会同步修改平台数据。</color>",
                "确认提交", () => StartCoroutine(Save(entry, level)));
        }

        private IEnumerator Save(RiskEntry entry, RiskLevel level)
        {
            _saving = true;
            SetStatus($"正在提交：{entry.name} → {level.name}…");
            UpdateView();

            // 以详情为准整体提交，避免列表数据缺字段把其他内容覆盖为空
            JObject detail = null;
            yield return HazardApi.RiskPointDetail(entry.sid, d => detail = d);
            var body = (JObject)(detail ?? entry.raw).DeepClone();
            body["riskPointLevelId"] = level.sid;
            body["riskPointLevelName"] = level.name;

            var ok = false;
            var message = "";
            yield return HazardApi.SaveRiskPoint(body, (success, msg) =>
            {
                ok = success;
                message = msg;
            });

            _saving = false;
            if (ok)
            {
                entry.levelId = level.sid;
                entry.levelName = level.name;
                entry.raw = body;
                _pending = null;
                SetStatus("");
                Toast($"已同步到平台：「{entry.name}」改为{level.name}");
            }
            else
            {
                SetStatus($"提交失败：{(string.IsNullOrEmpty(message) ? "请稍后重试" : message)}");
            }
            UpdateView();
        }

        #endregion

        private static string Line(string label, string value) => string.IsNullOrEmpty(value) ? null : $"<color=#9fd8ff>{label}：</color>{value}";

        private static string JoinNonEmpty(string separator, params string[] parts)
        {
            var list = parts.Where(p => !string.IsNullOrEmpty(p)).Distinct().ToList();
            return list.Count == 0 ? null : string.Join(separator, list);
        }

        private static string FormatDistance(float meters) => meters >= 1000 ? $"{meters / 1000f:F1} km" : $"{meters:0} m";
    }
}
