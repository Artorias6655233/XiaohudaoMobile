using System.Collections.Generic;
using LZW.Module.MajorHazard.Mobile.Pages;
using LZW.Module.MajorHazard.Mobile.UI;
using LZW.Module.MajorHazard.Scripts;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LZW.Module.MajorHazard.Mobile
{
    /// <summary>
    /// 重大危险源安全管理 · 安卓手机 / 平板版的入口。场景里只需要一个挂本组件的物体（菜单 Tools/MajorHazard/Setup Mobile Scene），
    /// 运行时代码生成全部界面：登录页（<see cref="LoginScreen"/>，登录后才显示下面的内容）+ 顶栏（返回 / 标题 / 帮助 / 刷新）
    /// + 首页功能宫格 + 6 个功能页 + 浮层（对话框、Toast）。
    ///
    /// 功能页与 AR 端 2.AR-Page 的 6 个页签一一对应（逻辑和接口相同，交互改为触屏）：
    /// 安全监督自适应查看 / 智能监测预警 / 智能风险管控 / 智能风险评估 / 重大危险源（表格）/ 重大危险源三维地图查看。
    /// 安卓返回键：关闭对话框 → 回到首页 → 确认退出。
    /// </summary>
    public class HazardMobileApp : MonoBehaviour
    {
        [Tooltip("界面字体（需含中文）")]
        [SerializeField] private Font font;

        [Tooltip("三维地图模型（ArtWorks/XHD.prefab）")]
        [SerializeField] private GameObject mapModel;

        [Tooltip("登录页背景（原工程 UI_Resource/Login/Login_bg.png）")]
        [SerializeField] private Sprite loginBackground;

        [Tooltip("登录按钮底图（原工程 UI_Resource/Common/Common_btn_A_02.png）")]
        [SerializeField] private Sprite loginButton;

        [SerializeField] private string appTitle = "重大危险源安全管理";

        public GameObject MapModel => mapModel;

        private readonly List<HazardPage> _pages = new List<HazardPage>();
        private HazardPage _current;

        private ResponsiveCanvas _responsive;
        private RectTransform _safe;
        private RectTransform _main;
        private LoginScreen _login;
        private RectTransform _home;
        private GridLayoutGroup _grid;
        private RectTransform _content;
        private MOverlay _overlay;

        private Button _back;
        private Text _title;
        private Button _help;
        private Button _refresh;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Input.multiTouchEnabled = true;
            if (font != null)
                MUI.Font = font;

            EnsureEventSystem();
            EnsureCamera();
            EnsureLight();
            BuildCanvas();
            BuildPages();
            GoHome();
        }

        private void OnLoggedIn()
        {
            _main.gameObject.SetActive(true);
            GoHome();
            OnScreenChanged();
            // 提前登录平台并拉取一张图标注，进功能页时更快
            StartCoroutine(HazardCache.LoadTags(false));
        }

        private void Start()
        {
            // Awake 时画布还没有布局，宫格列数在这里再算一次
            OnScreenChanged();
        }

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.Escape))
                return;
            if (_overlay.CloseTop())
                return;
            if (_current != null)
            {
                GoHome();
                return;
            }
            _overlay.Confirm("退出", "确定退出应用？", "退出", Application.Quit, MUI.DangerColor);
        }

        #region 场景基础

        private static void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null)
                return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            var es = go.GetComponent<EventSystem>();
            es.pixelDragThreshold = Mathf.Max(10, Mathf.RoundToInt(Screen.dpi > 0 ? Screen.dpi / 160f * 8f : 10f));
        }

        private static void EnsureCamera()
        {
            if (Camera.main != null)
                return;
            // 界面是 Overlay 画布，主相机只负责清屏（三维地图有自己的相机渲染到贴图）
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = MUI.BackgroundColor;
            cam.cullingMask = 0;
        }

        private static void EnsureLight()
        {
            foreach (var light in FindObjectsOfType<Light>())
                if (light.type == LightType.Directional)
                    return;
            var go = new GameObject("Directional Light");
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = 1.1f;
            l.shadows = LightShadows.None;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.6f, 0.68f);
        }

        #endregion

        #region 界面

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = false;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();
            _responsive = canvasGo.AddComponent<ResponsiveCanvas>();

            var root = (RectTransform)canvasGo.transform;
            var background = MUI.Rect(root, "Background");
            MUI.Stretch(background);
            MUI.Image(background, MUI.BackgroundColor);

            _safe = MUI.Rect(root, "SafeArea");
            MUI.Stretch(_safe);
            _responsive.SafeArea = _safe;

            // 登录后才显示的主界面：顶栏 + 内容
            _main = MUI.Rect(_safe, "Main");
            MUI.Stretch(_main);

            // 顶栏
            const float barHeight = 52f;
            var bar = MUI.Rect(_main, "TopBar");
            bar.anchorMin = new Vector2(0f, 1f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.sizeDelta = new Vector2(0f, barHeight);
            MUI.Image(bar, MUI.BarColor);
            var barLayout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            barLayout.padding = new RectOffset(6, 8, 4, 4);
            barLayout.spacing = 6;
            barLayout.childAlignment = TextAnchor.MiddleLeft;
            barLayout.childControlWidth = barLayout.childControlHeight = true;
            barLayout.childForceExpandWidth = false;
            barLayout.childForceExpandHeight = true;

            var clear = new Color(0, 0, 0, 0);
            _back = MUI.Button(bar, "←", GoHome, 48, 44, clear, MUI.FontLarge + 2);
            _title = MUI.Text(bar, appTitle, MUI.FontTitle, MUI.TextColor);
            _title.fontStyle = FontStyle.Bold;
            _title.horizontalOverflow = HorizontalWrapMode.Wrap;
            MUI.Size(_title, 0, -1, 1);
            _help = MUI.Button(bar, "说明", ShowHelp, 56, 36, MUI.ChipColor, MUI.FontSmall + 1);
            _refresh = MUI.Button(bar, "刷新", () => _current?.Reload(), 56, 36, MUI.ButtonColor, MUI.FontSmall + 1);

            var line = MUI.Rect(bar, "Line");
            line.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            line.anchorMin = new Vector2(0f, 0f);
            line.anchorMax = new Vector2(1f, 0f);
            line.pivot = new Vector2(0.5f, 1f);
            line.sizeDelta = new Vector2(0f, 2f);
            MUI.Image(line, new Color(0.2f, 0.85f, 1f, 0.35f));

            _content = MUI.Rect(_main, "Content");
            MUI.Stretch(_content, 0, 0, barHeight, 0);

            BuildHome();

            // 登录页盖在主界面之上、浮层之下（登录页上也要能弹 Toast / 退出确认）
            _login = LoginScreen.Create(_safe, loginBackground, loginButton);
            _login.LoggedIn += OnLoggedIn;
            _main.gameObject.SetActive(false);

            _overlay = MOverlay.Create(_safe);
            _responsive.Changed += OnScreenChanged;
            _responsive.Refresh();
        }

        private void BuildHome()
        {
            _home = MUI.Rect(_content, "Home");
            MUI.Stretch(_home);
            var content = MUI.Scroll(_home, out var scroll, 12, 16);
            MUI.Stretch((RectTransform)scroll.transform);

            var header = MUI.Paragraph(content, appTitle, MUI.FontLarge + 2, MUI.TextColor);
            header.fontStyle = FontStyle.Bold;
            MUI.Paragraph(content, "小虎岛化工园区 · 重大危险源监测预警、风险管控与评估", MUI.FontSmall + 1, MUI.DimTextColor);

            var gridRect = MUI.Rect(content, "Grid");
            _grid = gridRect.gameObject.AddComponent<GridLayoutGroup>();
            _grid.spacing = new Vector2(12f, 12f);
            _grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _grid.constraintCount = 2;
            _grid.cellSize = new Vector2(300f, 112f);
            gridRect.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private void BuildPages()
        {
            AddPage<SafetySupervisionPage>();
            AddPage<HazardAlarmPage>();
            AddPage<RiskControlPage>();
            AddPage<RiskAssessmentPage>();
            AddPage<MajorHazardTablePage>();
            AddPage<MajorHazardMapPage>();
        }

        private void AddPage<T>() where T : HazardPage
        {
            var rect = MUI.Rect(_content, typeof(T).Name);
            MUI.Stretch(rect);
            rect.gameObject.SetActive(false);
            var page = rect.gameObject.AddComponent<T>();
            page.Init(this);
            _pages.Add(page);
            AddHomeCard(page);
        }

        private void AddHomeCard(HazardPage page)
        {
            var card = MUI.Rect(_grid.transform, $"Card-{page.Title}");
            var image = MUI.Image(card, MUI.CardColor, true, 14f);
            var button = card.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = button.colors;
            colors.pressedColor = new Color(0.75f, 0.9f, 1f, 1f);
            colors.highlightedColor = colors.selectedColor = Color.white;
            button.colors = colors;
            button.onClick.AddListener(() => Open(page));

            var layout = card.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 12, 12);
            layout.spacing = 14;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var icon = MUI.Rect(card, "Icon");
            MUI.Size(icon, 56, 56, 0);
            MUI.Image(icon, page.GlyphColor * 0.85f + new Color(0, 0, 0, 0.15f), false, 14f);
            var glyph = MUI.Text(icon, page.Glyph, 26, Color.white, TextAnchor.MiddleCenter);
            glyph.fontStyle = FontStyle.Bold;
            MUI.Stretch(glyph.rectTransform);

            var texts = MUI.Rect(card, "Texts");
            MUI.Vertical(texts, 4);
            MUI.Size(texts, 0, -1, 1);
            var title = MUI.Paragraph(texts, page.Title, MUI.FontTitle - 1, MUI.TextColor);
            title.fontStyle = FontStyle.Bold;
            MUI.Paragraph(texts, page.Summary, MUI.FontSmall, MUI.DimTextColor);
        }

        private void OnScreenChanged()
        {
            if (_grid == null)
                return;
            // 首页宫格：按可用宽度决定列数（竖屏手机 1~2 列，横屏 / 平板 2~3 列）
            Canvas.ForceUpdateCanvases();
            var width = _content.rect.width - 32f;
            if (width < 10f)
                return;
            var columns = Mathf.Clamp(Mathf.FloorToInt((width + 12f) / (260f + 12f)), 1, 3);
            _grid.constraintCount = columns;
            _grid.cellSize = new Vector2((width - 12f * (columns - 1)) / columns, 112f);
        }

        #endregion

        #region 导航

        public void Open(HazardPage page)
        {
            _overlay.Close();
            if (_current != null)
                _current.gameObject.SetActive(false);
            _home.gameObject.SetActive(false);
            _current = page;
            page.gameObject.SetActive(true);
            UpdateBar();
        }

        public void GoHome()
        {
            _overlay.Close();
            if (_current != null)
                _current.gameObject.SetActive(false);
            _current = null;
            _home.gameObject.SetActive(true);
            UpdateBar();
        }

        private void UpdateBar()
        {
            var onPage = _current != null;
            _back.gameObject.SetActive(onPage);
            _help.gameObject.SetActive(onPage && !string.IsNullOrEmpty(_current.Help));
            _refresh.gameObject.SetActive(onPage);
            _title.text = onPage ? _current.Title : $"  {appTitle}";
        }

        private void ShowHelp()
        {
            if (_current != null)
                _overlay.ShowDetail($"{_current.Title} · 操作说明", _current.Help);
        }

        #endregion
    }
}
