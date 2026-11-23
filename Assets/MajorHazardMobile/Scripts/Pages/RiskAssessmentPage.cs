using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LZW.Module.MajorHazard.Mobile.UI;
using LZW.Module.MajorHazard.Scripts;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace LZW.Module.MajorHazard.Mobile.Pages
{
    /// <summary>
    /// 3、智能风险评估：新增或调取重大危险源风险评估报告，修改后提交到平台。
    /// 接口为 jeecg/ext/majorHazardRiskAssessmentReport（list / add / edit），与 AR 端 RiskAssessmentPanel 相同。
    /// AR 端靠语音调取、语音逐项填写；移动端是搜索框 + 表单（输入法自带语音输入，仍可说话填写），危险源从可搜索的列表里选。
    ///
    /// 平台保存时校验「危险源与报告属于同一企业」，提交平台时按所选危险源带上其 sysOrgCode（未经平台验证）。
    /// 演示模式（默认开，PlayerPrefs「MajorHazard.ReportDemo」）下报告只存本机（<see cref="RiskReportDemoStore"/>），
    /// 列表 = 平台报告 + 演示报告，首次使用时为前几个危险源生成示例报告。
    /// </summary>
    public class RiskAssessmentPage : HazardPage
    {
        /// <summary>资质证书分类（字典 xhd_zjzsfl）。</summary>
        private static readonly (string value, string text)[] Classifications =
        {
            ("H3603", "安全评价报告"), ("H3605", "SIL 等级评估报告"), ("3", "其他")
        };

        /// <summary>报告状态（字典 xhd_aqfxpgbgzt）。</summary>
        private static readonly (string value, string text)[] Statuses = { ("1", "正常"), ("2", "停用") };

        private const string DemoPref = "MajorHazard.ReportDemo";

        [Tooltip("默认演示模式：保存只写本机，不提交平台（开关切换后记在 PlayerPrefs）")]
        [SerializeField] private bool demoModeByDefault = true;

        public override string Title => "智能风险评估";
        public override string Summary => "风险评估报告的调取、新增与修改";
        public override string Glyph => "评";
        public override Color GlyphColor => new Color(0.6f, 0.45f, 0.95f);

        public override string Help => "· 在搜索框输入报告、危险源或企业名称调取报告，点列表中的报告打开\n· 点「新增报告」新建一份\n· 在表单中修改各项（可用输入法的语音输入），改过的项以黄色标出，点「保存报告」提交\n· 演示模式下只保存在本机，关闭后会提交到平台（写生产数据）";

        protected override float SideWidth => 340f;
        protected override float PortraitMainShare => 0.56f;

        private class HazardOption
        {
            public string id;
            public string name;
            public string company;
            public string org;
            public string Display => string.IsNullOrEmpty(company) ? name : $"{company} - {name}";
        }

        private List<JObject> _reports = new List<JObject>();
        private readonly List<HazardOption> _hazards = new List<HazardOption>();
        private JObject _selected;
        private JObject _draft;
        private bool _loading;
        private bool _saving;
        private bool _loaded;
        private bool _demo;
        private string _keyword = "";
        private string _listKey;

        // 表单
        private RectTransform _form;
        private Text _formTitle;
        private Text _formHint;
        private readonly Dictionary<string, Text> _labels = new Dictionary<string, Text>();
        private readonly Dictionary<string, InputField> _inputs = new Dictionary<string, InputField>();
        private Button _hazardButton;
        private ChipGroup _classification;
        private ChipGroup _status;
        private Text _readonlyInfo;
        private Button _saveButton;

        // 列表
        private Text _summary;
        private Button _demoButton;
        private RectTransform _list;

        protected override void BuildContent()
        {
            BuildForm();

            _summary = MUI.Paragraph(Side, "", MUI.FontSmall, MUI.DimTextColor);
            MUI.Search(Side, "搜索报告 / 危险源 / 企业", text =>
            {
                _keyword = text;
                UpdateList();
            });
            var row = MUI.Row(Side);
            MUI.Button(row, "新增报告", NewReport, -1, MUI.ButtonHeight, MUI.ConfirmColor);
            _demo = PlayerPrefs.GetInt(DemoPref, demoModeByDefault ? 1 : 0) == 1;
            _demoButton = MUI.Button(row, "", ToggleDemo, -1, MUI.ButtonHeight, MUI.ChipColor, MUI.FontSmall);
            UpdateDemoLabel();
            MUI.SectionTitle(Side, "评估报告");
            _list = MUI.Rect(Side, "ReportList");
            MUI.Vertical(_list, 6);
        }

        /// <summary>
        /// 主区域：报告表单（可滑动）。
        /// </summary>
        private void BuildForm()
        {
            var content = MUI.Scroll(MainArea, out var scroll, 0);
            MUI.Stretch((RectTransform)scroll.transform);

            _form = MUI.Rect(content, "ReportForm");
            MUI.Image(_form, MUI.PanelColor, false, 12f);
            MUI.Vertical(_form, 8, 16);

            _formTitle = MUI.Paragraph(_form, "", MUI.FontLarge, MUI.TextColor);
            _formTitle.fontStyle = FontStyle.Bold;
            _formHint = MUI.Paragraph(_form, "", MUI.FontBody, MUI.DimTextColor);

            AddInput("reportName", "报告名称", "请输入报告名称");
            _labels["companyMajorHazardId"] = MUI.Text(_form, "危险源", MUI.FontSmall + 1, MUI.DimTextColor, TextAnchor.LowerLeft, 22);
            _hazardButton = MUI.Button(_form, "点击选择危险源", PickHazard, -1, MUI.ButtonHeight, MUI.FieldColor);
            _hazardButton.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
            AddInput("assessmentTime", "评估日期", "yyyy-MM-dd", () => SetField("assessmentTime", DateTime.Today.ToString("yyyy-MM-dd")));
            AddInput("assessor", "评估人", "请输入评估人");
            AddInput("assessorPhone", "联系电话", "请输入联系电话").keyboardType = TouchScreenKeyboardType.PhonePad;
            AddInput("nameOfEvaluationAgency", "评估机构", "请输入评估机构名称");

            _labels["classificationOfQualificationCertificate"] = MUI.Text(_form, "证书分类", MUI.FontSmall + 1, MUI.DimTextColor, TextAnchor.LowerLeft, 22);
            _classification = new ChipGroup(_form, Classifications.Select(c => c.text).ToArray());
            _classification.Changed += i => SetField("classificationOfQualificationCertificate", Classifications[i].value);
            _labels["status"] = MUI.Text(_form, "状态", MUI.FontSmall + 1, MUI.DimTextColor, TextAnchor.LowerLeft, 22);
            _status = new ChipGroup(_form, Statuses.Select(s => s.text).ToArray());
            _status.Changed += i => SetField("status", Statuses[i].value);

            _readonlyInfo = MUI.Paragraph(_form, "", MUI.FontSmall, MUI.DimTextColor);

            var row = MUI.Row(_form, 48);
            MUI.Button(row, "撤销修改", Revert, -1, 48, MUI.ChipColor);
            _saveButton = MUI.Button(row, "保存报告", Save, -1, 48, MUI.ConfirmColor);
        }

        private InputField AddInput(string key, string label, string placeholder, Action onToday = null)
        {
            _labels[key] = MUI.Text(_form, label, MUI.FontSmall + 1, MUI.DimTextColor, TextAnchor.LowerLeft, 22);
            InputField input;
            if (onToday != null)
            {
                var row = MUI.Row(_form, MUI.ButtonHeight, 6);
                input = MUI.Input(row, placeholder);
                MUI.Button(row, "今天", () => onToday(), 64, MUI.ButtonHeight, MUI.ChipColor);
            }
            else
            {
                input = MUI.Input(_form, placeholder);
            }
            input.onEndEdit.AddListener(text => OnInput(key, text));
            _inputs[key] = input;
            return input;
        }

        protected override void OnShown()
        {
            if (!_loaded && !_loading)
                StartCoroutine(Load());
            UpdateForm();
        }

        protected override void OnHidden()
        {
            _loading = false;
            _saving = false;
        }

        public override void Reload()
        {
            StartCoroutine(Load());
        }

        #region 数据

        private IEnumerator Load(Func<JObject, bool> select = null)
        {
            if (_loading)
                yield break;
            _loading = true;
            SetStatus("正在获取评估报告…");
            UpdateList();

            if (_hazards.Count == 0)
                yield return LoadHazards();

            if (_demo)
                RiskReportDemoStore.SeedIfEmpty(_hazards.Select(h => (h.id, h.name, h.company)).ToList());

            List<JObject> reports = null;
            yield return HazardApi.ListReports(r => reports = r);
            _loading = false;
            if (reports == null && !_demo)
            {
                SetStatus("获取评估报告失败，点右上角「刷新」重试");
                UpdateList();
                yield break;
            }

            _reports = _demo ? RiskReportDemoStore.Merge(reports) : reports;
            _loaded = true;
            SetStatus(reports == null ? "获取平台报告失败，只显示演示报告" : "");

            var selectedId = Id(_selected);
            var keep = select != null
                ? _reports.FirstOrDefault(select)
                : selectedId == null ? null : _reports.FirstOrDefault(r => Id(r) == selectedId);
            if (keep != null)
                Select(keep);
            else if (_draft != null && !IsNew)
                _draft = _selected = null;
            _listKey = null;
            UpdateList();
            UpdateForm();
        }

        private IEnumerator LoadHazards()
        {
            List<JObject> hazards = null;
            List<JObject> companies = null;
            yield return HazardApi.ListMajorHazards(h => hazards = h);
            yield return HazardApi.ListCompanies(c => companies = c);
            if (hazards == null)
                yield break;

            // 危险源只有 sysOrgCode，企业名从企业列表按组织编码对上
            var companyByOrg = new Dictionary<string, string>();
            foreach (var company in companies ?? new List<JObject>())
            {
                var org = HazardJson.Str(company, "sysOrgCode", "deptOrgCode");
                var name = HazardJson.Str(company, "companyShortName", "companyName");
                if (org != null && name != null)
                    companyByOrg.TryAdd(org, name);
            }

            _hazards.Clear();
            foreach (var hazard in hazards)
            {
                var id = HazardJson.Str(hazard, "id");
                var name = HazardJson.Str(hazard, "hazardName");
                if (id == null || name == null)
                    continue;
                var org = HazardJson.Str(hazard, "sysOrgCode");
                _hazards.Add(new HazardOption
                {
                    id = id,
                    name = name,
                    org = org,
                    company = HazardJson.Str(hazard, "companyName") ?? (org != null && companyByOrg.TryGetValue(org, out var c) ? c : null)
                });
            }
        }

        #endregion

        #region 显示

        private bool IsNew => _draft != null && string.IsNullOrEmpty(Id(_draft));

        private bool Dirty => _draft != null && (_selected == null || !JToken.DeepEquals(_draft, _selected));

        private HazardOption HazardOf(JObject report) => _hazards.FirstOrDefault(h => h.id == HazardJson.Str(report, "companyMajorHazardId"));

        private void UpdateList()
        {
            var demoCount = _reports.Count(RiskReportDemoStore.IsDemo);
            _summary.text = $"评估报告 {_reports.Count} 份{(demoCount > 0 ? $"（演示 {demoCount} 份）" : "")}，重大危险源 {_hazards.Count} 个";

            var keyword = (_keyword ?? "").Trim();
            var shown = keyword.Length == 0
                ? _reports
                : _reports.Where(r => Contains(HazardJson.Str(r, "reportName"), keyword)
                                      || Contains(HazardOf(r)?.name, keyword) || Contains(HazardOf(r)?.company, keyword)).ToList();

            var key = $"{_loading}|{Id(_selected)}|{string.Join("|", shown.Select(r => $"{Id(r)}:{HazardJson.Str(r, "reportName")}:{HazardJson.Str(r, "assessmentTime")}"))}";
            if (key == _listKey)
                return;
            _listKey = key;

            MUI.Clear(_list);
            if (shown.Count == 0)
                MUI.Text(_list, _loading ? "加载中…" : keyword.Length > 0 ? $"没有找到「{keyword}」相关的报告" : "暂无评估报告",
                    MUI.FontBody, MUI.DimTextColor, TextAnchor.MiddleCenter, 50);
            foreach (var report in shown)
            {
                var r = report;
                var isSelected = _selected != null && Id(r) == Id(_selected);
                var card = MUI.Card(_list, () => TrySelect(r), isSelected ? new Color(0.1f, 0.34f, 0.52f, 1f) : (Color?)null, 10);
                var demo = RiskReportDemoStore.IsDemo(r) ? "<color=#7fd7a0>[演示]</color> " : "";
                MUI.Paragraph(card.transform, $"{demo}{HazardJson.Str(r, "reportName") ?? "（未命名）"}", MUI.FontBody, MUI.TextColor);
                var hazard = HazardOf(r);
                MUI.Paragraph(card.transform, $"{Short(HazardJson.Str(r, "assessmentTime"))}　{hazard?.Display ?? ""}", MUI.FontSmall, MUI.DimTextColor);
            }
        }

        private void UpdateForm()
        {
            var hasDraft = _draft != null;
            foreach (var input in _inputs.Values)
                input.interactable = hasDraft && !_saving;
            _hazardButton.interactable = hasDraft && !_saving;
            MUI.SetLabel(_saveButton, _saving ? "提交中…" : _demo ? "保存报告（本机）" : "保存报告");

            if (!hasDraft)
            {
                _formTitle.text = _loading ? "正在获取评估报告…" : "请选择或新增一份评估报告";
                _formHint.text = "在右侧（竖屏为下方）列表中点一份报告打开，或点「新增报告」新建。";
                _formHint.gameObject.SetActive(true);
                foreach (var pair in _inputs)
                    pair.Value.SetTextWithoutNotify("");
                MUI.SetLabel(_hazardButton, "点击选择危险源");
                _readonlyInfo.text = "";
                foreach (var label in _labels.Values)
                    label.color = MUI.DimTextColor;
                return;
            }

            var state = IsNew ? "<color=#ffd04a>（新增，未保存）</color>" : Dirty ? "<color=#ffd04a>（已修改，未保存）</color>" : "";
            var demoTag = RiskReportDemoStore.IsDemo(_draft) ? "<color=#7fd7a0>【演示】</color>" : "";
            _formTitle.text = $"{demoTag}{HazardJson.Str(_draft, "reportName") ?? "（未填写报告名称）"} {state}";
            _formHint.gameObject.SetActive(false);

            foreach (var pair in _inputs)
                if (!pair.Value.isFocused)
                    pair.Value.SetTextWithoutNotify(HazardJson.Str(_draft, pair.Key) ?? "");
            var hazard = HazardOf(_draft);
            var hazardId = HazardJson.Str(_draft, "companyMajorHazardId");
            MUI.SetLabel(_hazardButton, hazard?.Display ?? hazardId ?? "点击选择危险源 ▸");
            _classification.Select(Math.Max(0, Array.FindIndex(Classifications, c => c.value == HazardJson.Str(_draft, "classificationOfQualificationCertificate"))), false);
            _status.Select(Math.Max(0, Array.FindIndex(Statuses, s => s.value == HazardJson.Str(_draft, "status"))), false);

            var info = new List<string>();
            var attachment = AttachmentName(HazardJson.Str(_draft, "attachment"));
            if (attachment != null)
                info.Add($"附件：{attachment}");
            var created = HazardJson.Str(_draft, "createTime");
            if (created != null)
                info.Add($"创建时间：{created}{(HazardJson.Str(_draft, "createBy") is { } by ? $"（{by}）" : "")}");
            _readonlyInfo.text = string.Join("\n", info);

            // 与已保存版本不同的字段高亮
            foreach (var pair in _labels)
            {
                var changed = _selected != null ? !JToken.DeepEquals(_draft[pair.Key], _selected[pair.Key]) : HazardJson.Str(_draft, pair.Key) != null;
                pair.Value.color = changed ? new Color(1f, 0.82f, 0.3f) : MUI.DimTextColor;
            }
        }

        #endregion

        #region 交互

        /// <summary>
        /// 切换报告前，当前草稿有未保存的修改时先确认。
        /// </summary>
        private void TrySelect(JObject report)
        {
            if (_saving)
                return;
            if (Dirty && report != _selected)
            {
                MOverlay.Instance.Confirm("放弃修改？", "当前报告有未保存的修改，切换后修改会丢失。", "放弃并切换", () => Select(report), MUI.DangerColor);
                return;
            }
            Select(report);
        }

        private void Select(JObject report)
        {
            _selected = report;
            _draft = (JObject)report.DeepClone();
            UpdateList();
            UpdateForm();
        }

        private void NewReport()
        {
            if (_saving)
                return;
            if (Dirty)
            {
                MOverlay.Instance.Confirm("放弃修改？", "当前报告有未保存的修改，新建后修改会丢失。", "放弃并新建", CreateDraft, MUI.DangerColor);
                return;
            }
            CreateDraft();
        }

        private void CreateDraft()
        {
            _selected = null;
            _draft = new JObject
            {
                ["assessmentTime"] = DateTime.Today.ToString("yyyy-MM-dd"),
                ["classificationOfQualificationCertificate"] = Classifications[0].value,
                ["status"] = Statuses[0].value
            };
            UpdateList();
            UpdateForm();
            Toast("已新建报告草稿，请填写报告名称并选择危险源");
        }

        private void Revert()
        {
            if (_saving)
                return;
            if (_selected != null)
                Select(_selected);
            else
            {
                _draft = null;
                UpdateForm();
            }
        }

        private void OnInput(string key, string text)
        {
            if (_draft == null || _saving)
                return;
            text = (text ?? "").Trim();
            if (key == "assessmentTime" && text.Length > 0)
            {
                var date = ParseDate(text);
                if (date == null)
                {
                    Toast($"日期格式不正确：{text}（例：{DateTime.Today:yyyy-MM-dd}）");
                    UpdateForm();
                    return;
                }
                text = date;
            }
            if (key == "assessorPhone" && text.Length > 0 && text.Count(char.IsDigit) < 7)
            {
                Toast("联系电话至少 7 位数字");
                UpdateForm();
                return;
            }
            SetField(key, text);
        }

        private void SetField(string key, string value)
        {
            if (_draft == null || _saving)
                return;
            if (string.IsNullOrEmpty(value))
            {
                // 原来就没有的字段不要写成空串（否则会被当成修改）
                if (_selected == null || _selected[key] == null)
                    _draft.Remove(key);
                else
                    _draft[key] = "";
            }
            else
            {
                _draft[key] = value;
            }
            UpdateForm();
        }

        private void PickHazard()
        {
            if (_draft == null || _saving)
                return;
            if (_hazards.Count == 0)
            {
                Toast("重大危险源列表没有加载，点右上角「刷新」重试");
                return;
            }
            MOverlay.Instance.Pick("选择危险源", _hazards, h => h.name, h => SetField("companyMajorHazardId", h.id), h => h.company);
        }

        private void Save()
        {
            if (_draft == null || _saving)
            {
                Toast(_saving ? "正在提交，请稍候" : "请先选择或新增一份报告");
                return;
            }
            // 正在编辑的输入框先提交
            foreach (var pair in _inputs)
                if (pair.Value.isFocused)
                    OnInput(pair.Key, pair.Value.text);
            if (!Dirty)
            {
                Toast("没有需要保存的修改");
                return;
            }
            if (HazardJson.Str(_draft, "reportName") == null || HazardJson.Str(_draft, "companyMajorHazardId") == null)
            {
                Toast("报告名称和危险源为必填项");
                return;
            }
            if (_demo)
            {
                StartCoroutine(SaveRoutine());
                return;
            }
            MOverlay.Instance.Confirm(IsNew ? "新增评估报告" : "保存评估报告",
                $"将「{HazardJson.Str(_draft, "reportName")}」{(IsNew ? "新增" : "保存")}到平台？\n\n<color=#{MUI.Hex(MUI.WarnColor)}>当前为平台模式，会写入平台生产数据。</color>",
                "提交到平台", () => StartCoroutine(SaveRoutine()));
        }

        private IEnumerator SaveRoutine()
        {
            _saving = true;
            UpdateForm();
            var isNew = IsNew;
            var body = (JObject)_draft.DeepClone();
            // 去掉 jeecg 字典翻译字段（xxx_dictText），只提交原始值
            foreach (var property in body.Properties().Where(p => p.Name.EndsWith("_dictText")).ToList())
                property.Remove();
            var name = HazardJson.Str(body, "reportName");
            var id = Id(body);

            if (_demo)
            {
                // 演示：只存本机，不提交平台
                var saved = RiskReportDemoStore.Save(body);
                _saving = false;
                _selected = null;
                Toast($"已保存（演示数据，未提交平台）：{name}");
                var savedId = Id(saved);
                yield return Load(r => Id(r) == savedId);
                yield break;
            }

            body.Remove(RiskReportDemoStore.DemoKey);
            if (id != null && id.StartsWith("demo-"))
            {
                _saving = false;
                UpdateForm();
                MOverlay.Instance.ShowDetail("无法提交", "这是演示报告，平台上没有。请打开演示模式保存，或新增一份再提交。");
                yield break;
            }
            // 平台校验「危险源与报告属于同一企业」：报告归属取所选危险源的企业（不传时取登录账号的组织，必然对不上）
            var hazardOrg = _hazards.FirstOrDefault(h => h.id == HazardJson.Str(body, "companyMajorHazardId"))?.org;
            var hazardChanged = _selected == null || HazardJson.Str(_selected, "companyMajorHazardId") != HazardJson.Str(body, "companyMajorHazardId");
            if (hazardOrg != null && (isNew || hazardChanged || HazardJson.Str(body, "sysOrgCode") == null))
                body["sysOrgCode"] = hazardOrg;
            SetStatus($"正在{(isNew ? "新增" : "保存")}：{name}…");

            var ok = false;
            var message = "";
            yield return HazardApi.SaveReport(body, (success, msg) =>
            {
                ok = success;
                message = msg;
            });
            _saving = false;

            if (!ok)
            {
                SetStatus($"提交失败：{(string.IsNullOrEmpty(message) ? "请稍后重试" : message)}");
                UpdateForm();
                yield break;
            }

            SetStatus("");
            Toast($"已同步到平台：{name}");
            // 新增的报告没有返回 id，按名称找回（列表按创建时间倒序，取第一条）
            yield return Load(r => isNew ? HazardJson.Str(r, "reportName") == name : Id(r) == id);
        }

        private void ToggleDemo()
        {
            if (_saving || _loading)
                return;
            if (_demo)
            {
                MOverlay.Instance.Confirm("关闭演示模式", "关闭后，保存的报告会提交到平台（写生产数据）。确定关闭？", "关闭演示模式", () => SetDemo(false), MUI.DangerColor);
                return;
            }
            SetDemo(true);
        }

        private void SetDemo(bool demo)
        {
            _demo = demo;
            PlayerPrefs.SetInt(DemoPref, _demo ? 1 : 0);
            UpdateDemoLabel();
            // 草稿可能是另一种模式下的记录，清掉重新加载
            _draft = _selected = null;
            Toast(_demo ? "演示模式：保存只写本机，不提交平台" : "平台模式：保存会提交到平台");
            StartCoroutine(Load());
        }

        private void UpdateDemoLabel()
        {
            MUI.SetLabel(_demoButton, _demo ? "演示模式：开" : "演示模式：关");
            MUI.SetColor(_demoButton, _demo ? new Color(0.12f, 0.4f, 0.3f, 1f) : MUI.ChipColor);
        }

        #endregion

        private static string Id(JObject report) => HazardJson.Str(report, "id");

        private static bool Contains(string text, string keyword) => !string.IsNullOrEmpty(text) && text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>「2026-10-28」「2026/10/28」「2026年10月28日」「20261028」→ 2026-10-28；识别不了返回 null。</summary>
        private static string ParseDate(string text)
        {
            text = text.Replace("年", "-").Replace("月", "-").Replace("日", "").Replace("号", "").Replace("/", "-").Replace(".", "-").Trim();
            string[] formats = { "yyyy-M-d", "yyyyMMdd", "yyyy-M-d H:m:s" };
            return DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date.ToString("yyyy-MM-dd")
                : null;
        }

        private static string AttachmentName(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            var name = path.Substring(path.LastIndexOf('/') + 1);
            // 进场安全培训记录_1757492641103_1792477975561.pdf → 进场安全培训记录.pdf
            var ext = System.IO.Path.GetExtension(name);
            var cut = name.IndexOf('_');
            return cut > 0 ? name.Substring(0, cut) + ext : name;
        }

        private static string Short(string date) => string.IsNullOrEmpty(date) ? "" : date.Length >= 10 ? date.Substring(0, 10) : date;
    }
}
