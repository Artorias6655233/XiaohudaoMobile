using System.Collections;
using System.Collections.Generic;
using System.Linq;
using LZW.Module.MajorHazard.Mobile.UI;
using LZW.Module.MajorHazard.Scripts;
using LZW.Module.Map3DViewer.Scripts;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace LZW.Module.MajorHazard.Mobile.Pages
{
    /// <summary>
    /// 1、智能监测预警：危险源传感器超阈值报警后实时接收，并在三维地图上以闪烁标记显示报警设备的位置。
    /// AR 端是在现实画面中投放闪烁 POI（VLP），手机没有视觉定位，改为标在园区三维地图上（同一套设备坐标）。
    ///
    /// 数据分三步（与 AR 端 HazardAlarmPanel 相同）：
    /// ① 一张图标注 → 设备坐标（<see cref="HazardCache"/>）；② 危险源监测设备列表 → 所有 deviceCode；
    /// ③ 报警快照（传入全部 deviceCode）→ 当前处于报警状态的设备。①② 缓存，③ 定时轮询；新出现的报警 Toast + 振动提示。
    /// </summary>
    public class HazardAlarmPage : HazardPage
    {
        private const float PollInterval = 15f;
        private const float DeviceLifetime = 600f;

        private static readonly Color AlarmColor = new Color(1f, 0.15f, 0.1f);

        /// <summary>
        /// 报警 / 设备字段 → 详情标签。报警快照没有抓到样例，按常见字段名尽量匹配，同一标签只取第一个。
        /// </summary>
        private static readonly (string key, string label)[] FieldLabels =
        {
            ("companyName", "企业"), ("dangerSourceName", "危险源"), ("deviceName", "设备"), ("deviceCode", "设备编码"),
            ("alarmTypeName", "报警类型"), ("alarmType_dictText", "报警类型"), ("alarmType", "报警类型"),
            ("alarmLevelName", "报警等级"), ("alarmLevel", "报警等级"), ("alarmGrade", "报警等级"),
            ("monitorValue", "监测值"), ("alarmValue", "监测值"), ("realValue", "监测值"), ("currentValue", "监测值"), ("value", "监测值"),
            ("unit", "单位"),
            ("thresholdValue", "阈值"), ("threshold", "阈值"), ("upperLimit", "上限"), ("lowerLimit", "下限"),
            ("alarmContent", "报警内容"), ("alarmDesc", "报警内容"),
            ("alarmTime", "报警时间"), ("alarmDate", "报警时间"), ("startTime", "报警时间"), ("createTime", "报警时间"),
        };

        private static readonly string[] LongitudeKeys = { "longitude84", "longitude", "lng", "lon" };
        private static readonly string[] LatitudeKeys = { "latitude84", "latitude", "lat" };

        public override string Title => "智能监测预警";
        public override string Summary => "危险源设备超阈值报警实时接收，三维地图闪烁标注";
        public override string Glyph => "警";
        public override Color GlyphColor => new Color(0.95f, 0.3f, 0.25f);

        public override string Help => "· 每 15 秒自动获取危险源监测设备的报警，新报警会弹出提示并振动\n· 报警设备在三维地图上以红色闪烁标记显示\n· 点列表或地图上的标记查看报警详情\n· 地图：单指拖动旋转，双指捏合缩放，双指拖动平移，双击复位";

        protected override float PortraitMainShare => 0.52f;

        private class AlarmEntry
        {
            public string code;
            public string name;
            public string company;
            public string time;
            public bool located;
            public double longitude;
            public double latitude;
            public readonly List<(string label, string value)> fields = new List<(string, string)>();
        }

        private readonly Dictionary<string, JObject> _devices = new Dictionary<string, JObject>();
        private readonly HashSet<string> _known = new HashSet<string>();
        private List<AlarmEntry> _alarms = new List<AlarmEntry>();
        private float _devicesLoadedAt = float.NegativeInfinity;
        private float _nextPoll;
        private bool _polling;
        private bool _firstResult = true;
        private string _updatedAt;
        private string _listKey;

        private Map3DViewer.Scripts.Map3DViewer _map;
        private Text _summary;
        private RectTransform _list;

        protected override void BuildContent()
        {
            _map = HazardMap.Create(MainArea, App.MapModel, "单指旋转 · 双指缩放/平移 · 双击复位");
            if (_map != null)
                _map.onPoiClick += data => ShowDetail(data?.id, false); // 详情框由 Map3DViewer 自己切换

            _summary = MUI.Paragraph(Side, "", MUI.FontBody, MUI.TextColor);
            MUI.SectionTitle(Side, "报警设备");
            _list = MUI.Rect(Side, "AlarmList");
            MUI.Vertical(_list, 6);
            MUI.Text(_list, "正在获取报警…", MUI.FontBody, MUI.DimTextColor, TextAnchor.MiddleCenter, 60);
        }

        protected override void OnShown()
        {
            _nextPoll = 0f;
        }

        protected override void OnHidden()
        {
            _polling = false;
        }

        public override void Reload()
        {
            _nextPoll = 0f;
        }

        protected override void Refresh()
        {
            if (!_polling && Time.unscaledTime >= _nextPoll)
                StartCoroutine(Poll());

            var located = _alarms.Count(a => a.located);
            _summary.text = $"监测设备 {_devices.Count} 台，报警 <color=#ff5a4a><b>{_alarms.Count}</b></color> 条（地图上 {located} 条）" +
                            (_updatedAt == null ? "" : $"\n<size={MUI.FontSmall}><color=#{MUI.Hex(MUI.DimTextColor)}>更新于 {_updatedAt}，每 {PollInterval:0} 秒刷新</color></size>");
        }

        private IEnumerator Poll()
        {
            _polling = true;
            _nextPoll = Time.unscaledTime + PollInterval;
            if (_updatedAt == null)
                SetStatus("正在获取报警…");

            yield return HazardCache.LoadTags(false);

            if (_devices.Count == 0 || Time.realtimeSinceStartup - _devicesLoadedAt > DeviceLifetime)
            {
                List<JObject> devices = null;
                yield return HazardApi.ListDangerDevices(d => devices = d);
                if (devices != null)
                {
                    _devices.Clear();
                    foreach (var device in devices)
                    {
                        var code = HazardJson.Str(device, "deviceCode");
                        if (code != null)
                            _devices[code] = device;
                    }
                    _devicesLoadedAt = Time.realtimeSinceStartup;
                }
            }

            if (_devices.Count == 0)
            {
                SetStatus("获取危险源监测设备失败，稍后重试");
                _polling = false;
                yield break;
            }

            List<JObject> alarms = null;
            yield return HazardApi.ActiveAlarms(_devices.Keys, a => alarms = a);
            _polling = false;
            if (alarms == null)
            {
                SetStatus("获取报警失败，稍后重试");
                yield break;
            }

            ApplyAlarms(alarms);
            _updatedAt = System.DateTime.Now.ToString("HH:mm:ss");
            SetStatus(HazardCache.HasTags ? "" : "设备坐标（一张图标注）获取失败，报警无法在地图上定位");
        }

        private void ApplyAlarms(List<JObject> alarms)
        {
            var entries = new List<AlarmEntry>();
            var seen = new HashSet<string>();
            foreach (var alarm in alarms)
            {
                var code = HazardJson.Str(alarm, "deviceCode", "code");
                if (code == null || !seen.Add(code))
                    continue;
                _devices.TryGetValue(code, out var device);
                entries.Add(CreateEntry(code, alarm, device));
            }

            // 报警优先显示有坐标的、再按时间倒序
            entries.Sort((a, b) => a.located != b.located ? (a.located ? -1 : 1) : string.CompareOrdinal(b.time, a.time));

            var fresh = entries.Where(e => !_known.Contains(e.code)).ToList();
            if (_firstResult)
            {
                if (entries.Count > 0)
                    Toast($"当前有 {entries.Count} 个危险源设备处于报警状态");
            }
            else if (fresh.Count > 0)
            {
                Toast(fresh.Count == 1 ? $"危险源报警：{fresh[0].name}" : $"新增 {fresh.Count} 条危险源报警：{fresh[0].name} 等");
#if UNITY_ANDROID || UNITY_IOS
                Handheld.Vibrate();
#endif
            }
            _firstResult = false;
            _known.Clear();
            foreach (var entry in entries)
                _known.Add(entry.code);

            _alarms = entries;

            // 地图和列表重建会关掉打开的详情框，报警没变化时不重建
            var key = string.Join("|", entries.Select(e => $"{e.code}:{e.time}:{e.located}"));
            if (key == _listKey)
                return;
            _listKey = key;

            if (_map != null)
            {
                _map.SetPois(entries.Where(e => e.located).Select(e => new Map3DPoiData
                {
                    id = e.code,
                    name = e.name,
                    type = "报警",
                    longitude = e.longitude,
                    latitude = e.latitude,
                    color = AlarmColor,
                    detail = string.Join("\n", e.fields.Take(6).Select(f => $"{f.label}：{f.value}"))
                }));
                foreach (var poi in _map.Pois)
                    poi.gameObject.AddComponent<HazardAlarmBlink>().color = AlarmColor;
            }

            MUI.Clear(_list);
            if (entries.Count == 0)
                MUI.Text(_list, "当前没有报警", MUI.FontBody, MUI.DimTextColor, TextAnchor.MiddleCenter, 60);
            foreach (var e in entries)
            {
                var card = MUI.Card(_list, () => ShowDetail(e.code, true), null, 10);
                var title = MUI.Paragraph(card.transform, $"{(e.located ? "<color=#ff6a5a>◆</color>" : "<color=#8899aa>◇</color>")} {e.name}", MUI.FontBody, MUI.TextColor);
                title.fontStyle = FontStyle.Bold;
                var sub = string.Join("　", new[] { e.company, string.IsNullOrEmpty(e.time) ? null : ShortTime(e.time), e.located ? null : "无坐标" }
                    .Where(s => !string.IsNullOrEmpty(s)));
                if (sub.Length > 0)
                    MUI.Paragraph(card.transform, sub, MUI.FontSmall, MUI.DimTextColor);
            }
        }

        private AlarmEntry CreateEntry(string code, JObject alarm, JObject device)
        {
            var name = HazardJson.Str(alarm, "deviceName") ?? HazardJson.Str(device, "deviceName") ?? code;
            var entry = new AlarmEntry
            {
                code = code,
                name = name,
                company = HazardJson.Str(alarm, "companyName") ?? HazardJson.Str(device, "companyName"),
                time = HazardJson.Str(alarm, "alarmTime", "alarmDate", "startTime", "createTime"),
            };

            // 定位：一张图上的设备标注 → 设备自身的经纬度
            var tag = HazardCache.FindByDevice(code) ?? HazardCache.FindByDevice(HazardJson.Str(device, "id", "deviceId"));
            if (tag != null)
            {
                entry.longitude = tag.longitude;
                entry.latitude = tag.latitude;
                entry.located = true;
                entry.company ??= tag.devices.FirstOrDefault(d => d.deviceCode == code)?.companyName;
            }
            else if (TryLonLat(alarm, out var lon, out var lat) || TryLonLat(device, out lon, out lat))
            {
                entry.longitude = lon;
                entry.latitude = lat;
                entry.located = true;
            }

            var labels = new HashSet<string>();
            if (!string.IsNullOrEmpty(entry.company) && labels.Add("企业"))
                entry.fields.Add(("企业", entry.company));
            foreach (var (key, label) in FieldLabels)
            {
                if (labels.Contains(label))
                    continue;
                var value = HazardJson.Str(alarm, key) ?? HazardJson.Str(device, key);
                if (value == null || value.StartsWith("{") || value.StartsWith("["))
                    continue;
                labels.Add(label);
                entry.fields.Add((label, value));
            }
            if (!entry.located)
                entry.fields.Add(("位置", "无坐标，无法在地图上标注"));
            return entry;
        }

        private static bool TryLonLat(JToken item, out double lon, out double lat)
        {
            lat = 0;
            return HazardJson.TryDouble(item, out lon, LongitudeKeys)
                   && HazardJson.TryDouble(item, out lat, LatitudeKeys)
                   && HazardJson.IsValidLonLat(lon, lat);
        }

        /// <summary>
        /// 有坐标的报警在地图上打开详情框（点地图标记时 Map3DViewer 已自己切换），没有坐标的弹出详情面板。
        /// </summary>
        /// <param name="fromList">从列表点的</param>
        private void ShowDetail(string code, bool fromList)
        {
            var entry = _alarms.FirstOrDefault(e => e.code == code);
            if (entry == null)
                return;

            if (_map != null && entry.located)
            {
                if (fromList)
                    _map.ShowPopup(code);
                return;
            }

            var body = string.Join("\n", entry.fields.Select(f => $"<color=#9fd8ff>{f.label}：</color>{f.value}"));
            MOverlay.Instance.ShowDetail(entry.name, body);
        }

        private static string ShortTime(string time)
        {
            // 2026-10-28 05:55:46 → 10-28 05:55
            return time.Length >= 16 && time[4] == '-' ? time.Substring(5, 11) : time;
        }
    }
}
