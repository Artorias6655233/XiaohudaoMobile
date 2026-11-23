using System.Collections;
using System.Collections.Generic;
using System.Linq;
using LZW.Module.MajorHazard.Mobile.UI;
using LZW.Module.MajorHazard.Scripts;
using LZW.Module.Map3DViewer.Scripts;
using UnityEngine;
using UnityEngine.UI;

namespace LZW.Module.MajorHazard.Mobile.Pages
{
    /// <summary>
    /// 5、重大危险源三维地图查看：园区三维地图（<see cref="Map3DViewer"/>）上标出各重大危险源（<see cref="HazardSites"/>），
    /// 触屏旋转 / 缩放 / 平移，点标记或列表查看危险源信息，可按等级筛选、关键词搜索（代替 AR 端的语音查询）。
    /// </summary>
    public class MajorHazardMapPage : HazardPage
    {
        [Tooltip("只显示该经纬度范围内的标注（三维模型只覆盖小虎岛），x = 最小值，y = 最大值；为 0 不限")]
        [SerializeField] private Vector2 longitudeRange = new Vector2(113.50f, 113.59f);

        [SerializeField] private Vector2 latitudeRange = new Vector2(22.81f, 22.88f);

        private const int MaxDevicesInDetail = 5;

        public override string Title => "重大危险源三维地图查看";
        public override string Summary => "小虎岛三维地图，查看各重大危险源位置与信息";
        public override string Glyph => "图";
        public override Color GlyphColor => new Color(0.3f, 0.8f, 0.55f);

        public override string Help => "· 地图：单指拖动旋转，双指捏合缩放，双指拖动平移，双击或点「复位」恢复默认视角\n· 点地图上的标记显示危险源信息，再点一次关闭\n· 在搜索框输入名称或点下方列表，地图上定位到该危险源";

        protected override float PortraitMainShare => 0.55f;

        private List<HazardSite> _sites = new List<HazardSite>();
        private List<HazardSite> _shown = new List<HazardSite>();
        private string _keyword = "";
        private bool _loading;
        private string _selectedId;

        private Map3DViewer.Scripts.Map3DViewer _map;
        private Text _summary;
        private ChipGroup _filter;
        private RectTransform _list;

        protected override void BuildContent()
        {
            _map = HazardMap.Create(MainArea, App.MapModel, "单指旋转 · 双指缩放/平移 · 双击复位");
            if (_map != null)
                _map.onPoiClick += data => _selectedId = data?.id; // 详情框由 Map3DViewer 自己切换

            _summary = MUI.Paragraph(Side, "", MUI.FontSmall, MUI.DimTextColor);
            MUI.Search(Side, "搜索危险源 / 企业 / 设备", text =>
            {
                _keyword = text;
                ApplyFilter();
                if (_shown.Count == 1)
                    ShowDetail(_shown[0].id); // 搜到唯一一个直接定位
            });
            _filter = new ChipGroup(Side, HazardSites.LevelFilters, 0, MajorHazardTablePage.LevelColors());
            _filter.Changed += _ => ApplyFilter();
            _list = MUI.Rect(Side, "SiteList");
            MUI.Vertical(_list, 6);
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
                yield break;
            }

            _sites = HazardSites.Build(HazardCache.Tags, InRange);
            SetStatus("");
            ApplyFilter();
        }

        private bool InRange(HazardTag tag)
        {
            var lonOk = longitudeRange == Vector2.zero || tag.longitude >= longitudeRange.x && tag.longitude <= longitudeRange.y;
            var latOk = latitudeRange == Vector2.zero || tag.latitude >= latitudeRange.x && tag.latitude <= latitudeRange.y;
            return lonOk && latOk;
        }

        private void ApplyFilter()
        {
            _shown = HazardSites.Search(HazardSites.Filter(_sites, _filter.Selected), _keyword);

            if (_map != null)
                _map.SetPois(_shown.Select(s => new Map3DPoiData
                {
                    id = s.id,
                    name = s.name,
                    type = s.levelName ?? "",
                    longitude = s.longitude,
                    latitude = s.latitude,
                    color = s.color,
                    detail = HazardSites.Detail(s, MaxDevicesInDetail),
                    userData = s
                }));

            MUI.Clear(_list);
            if (_shown.Count == 0)
                MUI.Text(_list, _sites.Count == 0 ? "暂无危险源" : "没有符合条件的危险源", MUI.FontBody, MUI.DimTextColor, TextAnchor.MiddleCenter, 50);
            foreach (var s in _shown)
            {
                var card = MUI.Card(_list, () => ShowDetail(s.id), null, 10);
                MUI.Paragraph(card.transform, $"<color=#{MUI.Hex(s.color)}>◆</color> {s.name}", MUI.FontBody, MUI.TextColor);
                var sub = string.Join("　", new[] { s.levelName ?? "未评定", s.company != s.name ? s.company : null, s.devices.Count > 0 ? $"设备 {s.devices.Count} 台" : null }
                    .Where(t => !string.IsNullOrEmpty(t)));
                MUI.Paragraph(card.transform, sub, MUI.FontSmall, MUI.DimTextColor);
            }

            _summary.text = $"地图上 {_shown.Count} 处（共 {_sites.Count} 处）\n{HazardSites.LevelCounts(_sites)}";
            if (_selectedId != null && _shown.All(s => s.id != _selectedId))
                _selectedId = null;
        }

        private void ShowDetail(string id)
        {
            _selectedId = id;
            if (_map != null)
                _map.ShowPopup(id);
            else
            {
                var site = _shown.FirstOrDefault(s => s.id == id);
                if (site != null)
                    MOverlay.Instance.ShowDetail(site.name, HazardSites.Detail(site, 20));
            }
        }
    }
}
