using System.Collections;
using System.Collections.Generic;
using LZW.Module.MajorHazard.Mobile.UI;
using LZW.Module.MajorHazard.Scripts;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace LZW.Module.MajorHazard.Mobile.Pages
{
    /// <summary>
    /// 0、安全监督自适应查看：事件管理（icc）的报警事件表格，数据 = icc/dataEventDetail/find（与 AR 端 SafetySupervisionPanel 相同）。
    /// 移动端：手指滑动浏览，滑到底自动加载下一页；类别 / 督办用标签筛选；点一行弹出报警详情。停在顶部时定时刷新，新报警自动出现。
    /// </summary>
    public class SafetySupervisionPage : HazardPage
    {
        private const int PageSize = 20;
        private const float AutoRefreshSeconds = 30f;

        public override string Title => "安全监督自适应查看";
        public override string Summary => "园区报警事件实时查看，按类别、督办状态筛选";
        public override string Glyph => "督";
        public override Color GlyphColor => new Color(0.25f, 0.55f, 0.95f);

        public override string Help => "· 上下滑动浏览报警事件，滑到底部自动加载更多\n· 点上方标签按设备类别、督办状态筛选\n· 点某一行查看报警详情\n· 停在列表顶部时每 30 秒自动刷新";

        protected override bool SideOnTopInPortrait => true;

        private static readonly MTable.Column[] Columns =
        {
            new MTable.Column("序号", 36, 0, TextAnchor.MiddleCenter),
            new MTable.Column("报警类型", 0, 1.1f, TextAnchor.MiddleLeft),
            new MTable.Column("报警位置", 0, 2f, TextAnchor.MiddleLeft),
            new MTable.Column("等级", 40, 0, TextAnchor.MiddleCenter),
            new MTable.Column("报警时间", 84, 0, TextAnchor.MiddleCenter),
            new MTable.Column("状态", 52, 0, TextAnchor.MiddleCenter),
        };

        /// <summary>设备类别（字典 xhd_alarm_device_type），null = 全部。</summary>
        private static readonly (string key, string label)[] DeviceTypes =
        {
            (null, "全部"),
            ("1", "公共区域视频分析"),
            ("2", "周界区域"),
            ("3", "一键报警"),
            ("4", "企业区域视频"),
        };

        private static readonly (string key, string label)[] UrgeStates =
        {
            ("0", "未督办"),
            ("1", "已督办"),
        };

        /// <summary>字典 icc_event_alarm_type（抓包时的内容，接口新增的类型显示原始编码）。</summary>
        private static readonly Dictionary<string, string> AlarmTypes = new Dictionary<string, string>
        {
            ["0001"] = "人员超限", ["0003"] = "人员离岗", ["0004"] = "违规停车", ["0005"] = "未戴安全帽",
            ["1"] = "移动监测", ["3"] = "移动侦测", ["6"] = "温度", ["7"] = "液位",
            ["312"] = "火灾识别", ["313"] = "烟雾识别", ["881"] = "火情事件", ["962"] = "人穿越警戒线",
            ["963"] = "机动车穿越警戒线", ["972"] = "无人作业", ["1430"] = "非法闯入", ["1907"] = "对讲事件报警",
            ["15602"] = "打电话检测报警", ["15603"] = "吸烟检测报警", ["15622"] = "员工着装事件", ["15670"] = "人员睡岗",
            ["17020"] = "未穿戴工作服", ["park_violation"] = "车辆违停",
        };

        /// <summary>字典 icc_event_alarm_grade。</summary>
        private static readonly Dictionary<string, (string label, Color color)> Grades = new Dictionary<string, (string, Color)>
        {
            ["1"] = ("严重", new Color(1f, 0.35f, 0.3f)),
            ["2"] = ("一般", new Color(1f, 0.75f, 0.3f)),
            ["3"] = ("轻微", new Color(0.55f, 0.85f, 1f)),
        };

        /// <summary>字典 icc_event_alarm_source。</summary>
        private static readonly Dictionary<string, string> Sources = new Dictionary<string, string>
        {
            ["1"] = "ICC平台", ["2"] = "AI预警", ["3"] = "危险源预警",
        };

        private readonly List<JObject> _events = new List<JObject>();
        private int _total;
        private int _loadedPages;
        private bool _loading;
        private int _requestId;
        private float _nextAutoRefresh;

        private MTable _table;
        private Text _summary;
        private ChipGroup _deviceType;
        private ChipGroup _urge;

        protected override void BuildContent()
        {
            _table = new MTable(MainArea, Columns);
            MUI.Stretch(_table.Root);
            _table.onReachEnd = LoadMore;

            _summary = MUI.Paragraph(Side, "", MUI.FontSmall, MUI.DimTextColor);
            MUI.SectionTitle(Side, "设备类别");
            _deviceType = new ChipGroup(Side, System.Array.ConvertAll(DeviceTypes, d => d.label));
            _deviceType.Changed += _ => OnFilterChanged();
            MUI.SectionTitle(Side, "督办状态");
            _urge = new ChipGroup(Side, System.Array.ConvertAll(UrgeStates, u => u.label));
            _urge.Changed += _ => OnFilterChanged();
            UpdateSummary();
        }

        protected override void OnShown()
        {
            Load(true);
        }

        protected override void OnHidden()
        {
            _loading = false;
        }

        public override void Reload()
        {
            Load(true);
        }

        protected override void Refresh()
        {
            // 只在停在顶部时定时刷新（新报警会把条目往下挤，正在看的行会跳走）
            if (_loading || Time.unscaledTime < _nextAutoRefresh || !_table.AtTop)
                return;
            Load(true);
        }

        #region 数据

        private bool HasMore => _events.Count < _total;

        private void OnFilterChanged()
        {
            _events.Clear();
            _total = 0;
            _loadedPages = 0;
            _table.ShowMessage("加载中…");
            Load(true);
        }

        private void LoadMore()
        {
            if (!_loading && HasMore)
                Load(false);
        }

        /// <param name="reset">从第 1 页重新加载（刷新），否则加载下一页</param>
        private void Load(bool reset)
        {
            if (!isActiveAndEnabled)
                return;
            StartCoroutine(LoadRoutine(reset));
        }

        private IEnumerator LoadRoutine(bool reset)
        {
            // 筛选 / 刷新连续触发时只采用最后一次请求的结果
            var id = ++_requestId;
            _loading = true;
            _nextAutoRefresh = Time.unscaledTime + AutoRefreshSeconds;
            if (_events.Count == 0)
                _table.ShowMessage("加载中…");
            else if (!reset)
                _table.Footer = "正在加载更多…";

            var page = reset ? 1 : _loadedPages + 1;
            List<JObject> data = null;
            var total = 0;
            yield return HazardApi.FindAlarmEvents(DeviceTypes[_deviceType.Selected].key, UrgeStates[_urge.Selected].key, page, PageSize,
                (d, t) =>
                {
                    data = d;
                    total = t;
                });
            if (id != _requestId)
                yield break;
            _loading = false;
            _nextAutoRefresh = Time.unscaledTime + AutoRefreshSeconds;

            if (data == null)
            {
                SetStatus("获取报警事件失败，点右上角「刷新」重试");
                if (_events.Count == 0)
                    _table.ShowMessage("获取失败");
                UpdateFooter();
                yield break;
            }

            SetStatus("");
            if (reset)
                _events.Clear();
            var start = _events.Count;
            _events.AddRange(data);
            _loadedPages = page;
            _total = Mathf.Max(total, _events.Count);
            Render(start);
        }

        #endregion

        #region 表格

        /// <param name="start">从这一条开始补行（0 = 整表重建）</param>
        private void Render(int start)
        {
            var selected = _table.Selected as string;
            if (start == 0)
                _table.Clear();
            if (_events.Count == 0)
                _table.ShowMessage("暂无数据");

            for (var i = start; i < _events.Count; i++)
            {
                var item = _events[i];
                var sid = HazardJson.Str(item, "sid");
                var grade = Grade(item);
                var ended = Ended(item);
                _table.AddRow(sid, new MTable.Cell[]
                {
                    new MTable.Cell($"{i + 1}", MUI.DimTextColor),
                    AlarmType(item),
                    Location(item),
                    new MTable.Cell(grade.label, grade.color),
                    ShortTime(HazardJson.Str(item, "alarmDate")),
                    new MTable.Cell(ended ? "已结束" : "报警中", ended ? MUI.DimTextColor : MUI.WarnColor),
                }, () => ShowDetail(item));
            }
            _table.Select(selected);
            UpdateSummary();
            UpdateFooter();
        }

        private void UpdateFooter()
        {
            _table.Footer = _events.Count == 0 ? "" : HasMore ? $"已加载 {_events.Count}/{_total} 条，上滑加载更多" : $"共 {_total} 条";
        }

        private void UpdateSummary()
        {
            _summary.text = $"报警事件 <color=#{MUI.Hex(MUI.WarnColor)}>{_total}</color> 条　" +
                            $"{DeviceTypes[_deviceType.Selected].label} · {UrgeStates[_urge.Selected].label}";
        }

        private void ShowDetail(JObject item)
        {
            MOverlay.Instance.ShowDetail(AlarmType(item), Detail(item));
        }

        #endregion

        #region 字段

        private static string AlarmType(JObject item)
        {
            var key = HazardJson.Str(item, "alarmType");
            if (key == null)
                return "—";
            return AlarmTypes.TryGetValue(key, out var label) ? label : key;
        }

        /// <summary>报警位置：通道名（如「奇宁化工-罐区」），没有则用设备名。</summary>
        private static string Location(JObject item)
        {
            return HazardJson.Str(item, "channelName", "deviceName", "location") ?? "—";
        }

        private static (string label, Color color) Grade(JObject item)
        {
            var key = HazardJson.Str(item, "alarmGrade");
            if (key != null && Grades.TryGetValue(key, out var grade))
                return grade;
            return (key ?? "—", MUI.TextColor);
        }

        /// <summary>有结束时间（alarmTime，伴随 alarmStatus=1 和持续时长）即报警已结束。</summary>
        private static bool Ended(JObject item)
        {
            return HazardJson.Str(item, "alarmTime") != null;
        }

        /// <summary>「2026-11-18 18:40:47」→「11-18 18:40」。</summary>
        private static string ShortTime(string time)
        {
            if (string.IsNullOrEmpty(time))
                return "—";
            return time.Length >= 16 ? time.Substring(5, 11) : time;
        }

        private static string Detail(JObject item)
        {
            var grade = Grade(item);
            var lines = new List<string>
            {
                $"<color=#9fd8ff>等级：</color><color=#{MUI.Hex(grade.color)}>{grade.label}</color>",
                $"<color=#9fd8ff>设备：</color>{HazardJson.Str(item, "deviceName") ?? "—"}",
            };
            var channel = HazardJson.Str(item, "channelName");
            if (channel != null)
                lines.Add($"<color=#9fd8ff>通道：</color>{channel}");
            var deviceType = HazardJson.Str(item, "deviceType");
            var category = deviceType == null ? null : System.Array.Find(DeviceTypes, d => d.key == deviceType).label;
            if (category != null)
                lines.Add($"<color=#9fd8ff>类别：</color>{category}");
            var source = HazardJson.Str(item, "alarmSource");
            if (source != null)
                lines.Add($"<color=#9fd8ff>来源：</color>{(Sources.TryGetValue(source, out var s) ? s : source)}");
            lines.Add($"<color=#9fd8ff>报警时间：</color>{HazardJson.Str(item, "alarmDate") ?? "—"}");
            if (Ended(item))
            {
                var duration = HazardJson.Str(item, "alarmDuration");
                lines.Add($"<color=#9fd8ff>结束时间：</color>{HazardJson.Str(item, "alarmTime")}{(duration != null ? $"（持续 {duration}）" : "")}");
            }
            else
            {
                lines.Add($"<color=#9fd8ff>状态：</color><color=#{MUI.Hex(MUI.WarnColor)}>报警中</color>");
            }
            var dispose = HazardJson.Str(item, "disposeContent", "disposeWay");
            if (dispose != null)
                lines.Add($"<color=#9fd8ff>处置：</color>{dispose}");
            var content = HazardJson.Str(item, "alarmContent");
            if (content != null)
                lines.Add(content);
            return string.Join("\n", lines);
        }

        #endregion
    }
}
