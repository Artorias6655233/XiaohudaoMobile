using System.Collections;
using System.Collections.Generic;
using LZW.Module.MajorHazard.Mobile.UI;
using LZW.Module.MajorHazard.Scripts;
using UnityEngine;
using UnityEngine.UI;

namespace LZW.Module.MajorHazard.Mobile.Pages
{
    /// <summary>
    /// 4、重大危险源：重大危险源数据表格（数据与三维地图页相同，见 <see cref="HazardSites"/>）。
    /// AR 端是挂手势的分页表格 + 语音查询；移动端手指滑动浏览、按风险等级筛选、输入关键词搜索，点一行弹出详情。
    /// </summary>
    public class MajorHazardTablePage : HazardPage
    {
        private const int MaxDevicesInDetail = 20;

        public override string Title => "重大危险源";
        public override string Summary => "园区重大危险源数据表格，按等级筛选、关键词搜索";
        public override string Glyph => "源";
        public override Color GlyphColor => new Color(0.95f, 0.75f, 0.2f);

        public override string Help => "· 上下滑动浏览重大危险源\n· 点等级标签筛选，在搜索框输入危险源、企业或设备名称查找（可用输入法的语音输入）\n· 点某一行查看详情（企业、风险等级、监测设备）";

        protected override bool SideOnTopInPortrait => true;

        private static readonly MTable.Column[] Columns =
        {
            new MTable.Column("序号", 36, 0, TextAnchor.MiddleCenter),
            new MTable.Column("危险源名称", 0, 2.2f, TextAnchor.MiddleLeft),
            new MTable.Column("所属企业", 0, 2f, TextAnchor.MiddleLeft),
            new MTable.Column("风险等级", 68, 0, TextAnchor.MiddleCenter),
            new MTable.Column("设备", 44, 0, TextAnchor.MiddleCenter),
        };

        private List<HazardSite> _sites = new List<HazardSite>();
        private List<HazardSite> _shown = new List<HazardSite>();
        private string _keyword = "";
        private bool _loading;

        private MTable _table;
        private Text _summary;
        private ChipGroup _filter;

        protected override void BuildContent()
        {
            _table = new MTable(MainArea, Columns);
            MUI.Stretch(_table.Root);

            _summary = MUI.Paragraph(Side, "", MUI.FontSmall, MUI.DimTextColor);
            MUI.Search(Side, "搜索危险源 / 企业 / 设备", text =>
            {
                _keyword = text;
                ApplyFilter();
            });
            _filter = new ChipGroup(Side, HazardSites.LevelFilters, 0, LevelColors());
            _filter.Changed += _ => ApplyFilter();
            _table.ShowMessage("加载中…");
        }

        protected override void OnShown()
        {
            if (_sites.Count == 0 && !_loading)
                StartCoroutine(Load(false));
        }

        protected override void OnHidden()
        {
            _loading = false;
        }

        public override void Reload()
        {
            StartCoroutine(Load(true));
        }

        private IEnumerator Load(bool force)
        {
            if (_loading)
                yield break;
            _loading = true;
            SetStatus("正在获取重大危险源…");
            var ok = false;
            yield return HazardCache.LoadTags(force, r => ok = r);
            _loading = false;
            if (!ok)
            {
                SetStatus("获取重大危险源失败，点右上角「刷新」重试");
                _table.ShowMessage("获取失败");
                yield break;
            }

            _sites = HazardSites.Build(HazardCache.Tags);
            SetStatus("");
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            _shown = HazardSites.Search(HazardSites.Filter(_sites, _filter.Selected), _keyword);
            _summary.text = $"共 {_sites.Count} 处，当前显示 {_shown.Count} 处\n{HazardSites.LevelCounts(_sites)}";

            var selected = _table.Selected;
            _table.Clear();
            if (_shown.Count == 0)
                _table.ShowMessage(_sites.Count == 0 ? (_loading ? "加载中…" : "暂无数据") : "没有符合条件的危险源");
            for (var i = 0; i < _shown.Count; i++)
            {
                var site = _shown[i];
                _table.AddRow(site.id, new MTable.Cell[]
                {
                    new MTable.Cell($"{i + 1}", MUI.DimTextColor),
                    site.name,
                    site.company ?? "—",
                    new MTable.Cell(site.levelName ?? "未评定", site.color),
                    site.devices.Count > 0 ? $"{site.devices.Count}" : "—",
                }, () => ShowDetail(site));
            }
            _table.Select(selected);
            _table.Footer = _shown.Count > 0 ? $"共 {_shown.Count} 条" : "";
            _table.ScrollToTop();
        }

        private void ShowDetail(HazardSite site)
        {
            MOverlay.Instance.ShowDetail(site.name, HazardSites.Detail(site, MaxDevicesInDetail));
        }

        public static Color[] LevelColors()
        {
            return new[] { MUI.ButtonColor, RiskLevel.Major, RiskLevel.Large, RiskLevel.General * 0.8f, RiskLevel.Low };
        }
    }
}
