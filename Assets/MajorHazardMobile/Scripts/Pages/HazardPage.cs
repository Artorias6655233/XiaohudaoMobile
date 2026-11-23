using LZW.Module.MajorHazard.Mobile.UI;
using UnityEngine;
using UnityEngine.UI;

namespace LZW.Module.MajorHazard.Mobile.Pages
{
    /// <summary>
    /// 重大危险源移动端功能页基类（对应 AR 端 HazardPanelBase）：主区域（地图 / 表格 / 信息卡）+ 操作面板（<see cref="Side"/>）。
    /// - 横屏 / 平板：主区域在左，操作面板在右（与 AR 端布局一致）；
    /// - 竖屏手机：上下排列，<see cref="SideOnTopInPortrait"/> 决定操作面板在上（表格页：筛选在上）还是在下（地图页：列表在下）。
    /// 操作面板整体可上下滑动（手机横屏高度只有三百多 dp），面板里的列表直接用卡片排列，不再套一层滚动区域。
    /// 标题、刷新、帮助在应用顶栏（<see cref="HazardMobileApp"/>）。页面隐藏时 Unity 会中止协程。
    /// </summary>
    public abstract class HazardPage : MonoBehaviour
    {
        public abstract string Title { get; }

        /// <summary>首页卡片上的一句话说明。</summary>
        public abstract string Summary { get; }

        /// <summary>首页卡片图标上的字。</summary>
        public abstract string Glyph { get; }

        public abstract Color GlyphColor { get; }

        /// <summary>顶栏「?」弹出的操作说明。</summary>
        public virtual string Help => null;

        protected HazardMobileApp App { get; private set; }

        protected RectTransform MainArea { get; private set; }

        /// <summary>操作面板的内容容器（竖排，可滑动）。</summary>
        protected RectTransform Side { get; private set; }

        private RectTransform _sidePanel;

        protected virtual float SideWidth => 360f;

        protected virtual bool SideOnTopInPortrait => false;

        /// <summary>竖屏且操作面板在下时，主区域占的高度比例。</summary>
        protected virtual float PortraitMainShare => 0.5f;

        /// <summary>周期刷新间隔（秒）。</summary>
        protected virtual float RefreshInterval => 0.5f;

        protected bool Portrait { get; private set; }

        private Text _status;
        private bool _built;
        private float _nextRefresh;
        private float _nextLayout;
        private bool _hasLayout;

        public void Init(HazardMobileApp app)
        {
            App = app;
        }

        protected virtual void OnEnable()
        {
            if (!_built)
                Build();
            _nextRefresh = 0f;
            _hasLayout = false;
            OnShown();
        }

        protected virtual void OnDisable()
        {
            OnHidden();
        }

        protected virtual void Update()
        {
            if (!_hasLayout || Time.unscaledTime >= _nextLayout)
                ApplyLayout();

            if (Time.unscaledTime < _nextRefresh)
                return;
            _nextRefresh = Time.unscaledTime + RefreshInterval;
            Refresh();
        }

        private void Build()
        {
            _built = true;
            var root = (RectTransform)transform;
            MUI.Stretch(root);

            MainArea = MUI.Rect(root, "Main");
            _sidePanel = MUI.Rect(root, "Side");
            MUI.Image(_sidePanel, MUI.PanelColor, true, 12f);
            Side = MUI.Scroll(_sidePanel, out var scroll, 8, 12);
            MUI.Stretch((RectTransform)scroll.transform);

            BuildContent();

            _status = MUI.Paragraph(Side, "", MUI.FontSmall, MUI.WarnColor);
            _status.gameObject.SetActive(false);
            ApplyLayout();
        }

        /// <summary>生成主区域和操作面板的内容。</summary>
        protected abstract void BuildContent();

        /// <summary>页面显示时调用（首次显示时内容已生成）。</summary>
        protected virtual void OnShown()
        {
        }

        /// <summary>页面隐藏时调用。进行中的协程此时已被 Unity 中止。</summary>
        protected virtual void OnHidden()
        {
        }

        /// <summary>周期刷新（<see cref="RefreshInterval"/>）。</summary>
        protected virtual void Refresh()
        {
        }

        /// <summary>顶栏「刷新」按钮。</summary>
        public virtual void Reload()
        {
        }

        /// <summary>横竖屏切换后（布局已更新）。</summary>
        protected virtual void OnLayoutChanged()
        {
        }

        protected void SetStatus(string message)
        {
            if (_status == null)
                return;
            _status.text = message ?? "";
            _status.gameObject.SetActive(!string.IsNullOrEmpty(message));
            _status.transform.SetAsLastSibling();
        }

        protected static void Toast(string message) => MOverlay.Toast(message);

        /// <summary>
        /// 按当前屏幕方向摆放主区域和操作面板。竖屏且面板在上时，面板高度跟随内容。
        /// </summary>
        private void ApplyLayout()
        {
            _nextLayout = Time.unscaledTime + 0.25f;
            var root = (RectTransform)transform;
            var size = root.rect.size;
            if (size.x < 1f || size.y < 1f)
                return;

            const float gap = 8f;
            var portrait = size.y > size.x * 1.05f;
            var changed = portrait != Portrait || !_hasLayout;
            Portrait = portrait;
            _hasLayout = true;

            if (!portrait)
            {
                var width = Mathf.Clamp(SideWidth, 280f, size.x * 0.48f);
                MUI.Stretch(MainArea, gap, width + gap * 2, gap, gap);
                _sidePanel.anchorMin = new Vector2(1f, 0f);
                _sidePanel.anchorMax = new Vector2(1f, 1f);
                _sidePanel.pivot = new Vector2(1f, 0.5f);
                _sidePanel.sizeDelta = new Vector2(width, -gap * 2);
                _sidePanel.anchoredPosition = new Vector2(-gap, 0f);
            }
            else if (SideOnTopInPortrait)
            {
                var height = Mathf.Min(LayoutUtility.GetPreferredHeight(Side), size.y * 0.6f);
                _sidePanel.anchorMin = new Vector2(0f, 1f);
                _sidePanel.anchorMax = new Vector2(1f, 1f);
                _sidePanel.pivot = new Vector2(0.5f, 1f);
                _sidePanel.sizeDelta = new Vector2(-gap * 2, height);
                _sidePanel.anchoredPosition = new Vector2(0f, -gap);
                MUI.Stretch(MainArea, gap, gap, height + gap * 2, gap);
            }
            else
            {
                var mainHeight = Mathf.Round(size.y * PortraitMainShare);
                MainArea.anchorMin = new Vector2(0f, 1f);
                MainArea.anchorMax = new Vector2(1f, 1f);
                MainArea.pivot = new Vector2(0.5f, 1f);
                MainArea.sizeDelta = new Vector2(-gap * 2, mainHeight - gap);
                MainArea.anchoredPosition = new Vector2(0f, -gap);
                MUI.Stretch(_sidePanel, gap, gap, mainHeight + gap, gap);
            }

            if (changed)
                OnLayoutChanged();
        }
    }
}
