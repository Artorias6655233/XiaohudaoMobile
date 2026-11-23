using System;
using LZW.Module.MajorHazard.Mobile.UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace LZW.Module.MajorHazard.Mobile
{
    /// <summary>
    /// 登录页，移植自 AR 工程的 Login-Page.prefab + Tiger.UI.LoginView / LoginViewModel，逻辑相同：
    /// 用户名 / 密码预填（tiger / 123456），「获取验证码」暂未接入第三方接口，点「登录」即登录成功（原工程同样不校验），
    /// 成功后进入重大危险源首页并提示「登录成功」。
    ///
    /// 布局：横屏（平板 / 手机横持）按原预制体 1436×764 的背景图和坐标整体等比缩放显示；
    /// 竖屏手机上原布局会缩得太小，改为上下排列（标题取自背景图的标题条）。
    /// </summary>
    public class LoginScreen : MonoBehaviour
    {
        private static readonly Color PageColor = new Color32(26, 44, 98, 255);
        private static readonly Color BlueColor = new Color32(66, 112, 238, 255);
        private static readonly Color InputTextColor = new Color(0.196f, 0.196f, 0.196f, 1f);

        /// <summary>原预制体的设计尺寸（Login-Panel）。</summary>
        private static readonly Vector2 DesignSize = new Vector2(1436f, 764f);

        /// <summary>背景图里标题条的位置（像素，左下角为原点）：右侧蓝色区域顶部。</summary>
        private static readonly Rect TitleBand = new Rect(411f, 574f, 977f, 156f);

        /// <summary>与 LoginViewModel 相同的数据：输入框改动写入这里，切换横竖屏布局时从这里回填。</summary>
        private class LoginModel
        {
            public string userName;
            public string password;
            public string captcha;
        }

        public event UnityAction LoggedIn;

        private readonly LoginModel _model = new LoginModel();
        private Sprite _background;
        private Sprite _buttonSprite;
        private RectTransform _root;
        private RectTransform _landscape;
        private RectTransform _portrait;
        private bool? _isPortrait;

        public static LoginScreen Create(RectTransform parent, Sprite background, Sprite buttonSprite)
        {
            var rect = MUI.Rect(parent, "Login");
            MUI.Stretch(rect);
            var screen = rect.gameObject.AddComponent<LoginScreen>();
            screen._root = rect;
            screen._background = background;
            screen._buttonSprite = buttonSprite;
            screen.Build();
            return screen;
        }

        private void Build()
        {
            MUI.Image(_root, PageColor, true);
            Pull();
            _landscape = BuildLandscape();
            _portrait = BuildPortrait();
            ApplyLayout();
        }

        private void Update()
        {
            ApplyLayout();
        }

        /// <summary>对应 LoginViewModel.Pull：预填账号。</summary>
        private void Pull()
        {
            _model.userName = "tiger";
            _model.password = "123456";
            _model.captcha = "";
        }

        /// <summary>对应 LoginViewModel.Login：原工程不校验，直接返回成功。</summary>
        private void Login()
        {
            Debug.Log($"[{nameof(LoginScreen)}] 登录：{_model.userName}");
            OnLoginResult("ok");
        }

        private void OnLoginResult(string result)
        {
            if (result != "ok")
                return;
            gameObject.SetActive(false);
            LoggedIn?.Invoke();
            MOverlay.Toast("登录成功");
        }

        /// <summary>原工程待接入第三方接口，这里同样只打日志。</summary>
        private void GetCaptcha()
        {
            Debug.Log($"[{nameof(LoginScreen)}] GetCaptchaButton>>>");
            MOverlay.Toast("验证码服务暂未接入");
        }

        #region 布局

        private void ApplyLayout()
        {
            var size = _root.rect.size;
            if (size.x < 1f || size.y < 1f)
                return;
            var portrait = size.y > size.x;
            if (_isPortrait != portrait)
            {
                _isPortrait = portrait;
                _landscape.gameObject.SetActive(!portrait);
                _portrait.gameObject.SetActive(portrait);
                // 两套布局共用一份数据
                foreach (var input in GetComponentsInChildren<InputField>(true))
                    if (input.name.StartsWith("Input-"))
                        input.SetTextWithoutNotify(Value(input.name.Substring(6)));
            }

            if (!portrait)
            {
                // 保持设计尺寸，整体等比缩放进屏幕
                var scale = Mathf.Min((size.x - 16f) / DesignSize.x, (size.y - 16f) / DesignSize.y);
                _landscape.localScale = new Vector3(scale, scale, 1f);
            }
        }

        /// <summary>
        /// 横屏：原 Login-Page 布局。Login-Panel 1436×764 背景图，Div-Panel 800×450 位于中心偏 (153, -50)，
        /// 竖排 4 行（每行 600×100）：用户名 / 密码 / 验证码 + 获取验证码 / 登录按钮。
        /// </summary>
        private RectTransform BuildLandscape()
        {
            var panel = MUI.Rect(_root, "Login-Panel");
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = DesignSize;
            var bg = panel.gameObject.AddComponent<Image>();
            bg.sprite = _background;
            bg.color = _background != null ? Color.white : BlueColor;
            bg.raycastTarget = false;

            var div = MUI.Rect(panel, "Div-Panel");
            div.anchorMin = div.anchorMax = div.pivot = new Vector2(0.5f, 0.5f);
            div.sizeDelta = new Vector2(800f, 450f);
            div.anchoredPosition = new Vector2(153f, -50f);
            var layout = div.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = true;

            AddDesignItem(div, "用户名", "userName", 420f, false);
            AddDesignItem(div, "密码", "password", 420f, true);
            var captcha = AddDesignItem(div, "验证码", "captcha", 230f, false);
            DesignButton(captcha, "获取验证码", GetCaptcha, new Vector2(160f, 60f), null);

            var submit = DesignRow(div, "Item-Submit");
            DesignButton(submit, "登录", Login, new Vector2(599.4f, 102f), _buttonSprite);
            return panel;
        }

        private RectTransform DesignRow(Transform parent, string name)
        {
            var row = MUI.Rect(parent, name);
            row.sizeDelta = new Vector2(600f, 100f);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 20f;
            layout.childControlWidth = layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            return row;
        }

        private RectTransform AddDesignItem(Transform parent, string label, string key, float inputWidth, bool password)
        {
            var row = DesignRow(parent, $"Item-{key}");
            var title = MUI.Text(row, label, 36, Color.white, TextAnchor.MiddleLeft);
            title.rectTransform.sizeDelta = new Vector2(120f, 80f);
            var input = CreateInput(row, key, password, 30);
            ((RectTransform)input.transform).sizeDelta = new Vector2(inputWidth, 70f);
            return row;
        }

        private Button DesignButton(Transform parent, string label, UnityAction onClick, Vector2 size, Sprite sprite)
        {
            var rect = MUI.Rect(parent, $"Btn-{label}");
            rect.sizeDelta = size;
            var image = sprite != null ? rect.gameObject.AddComponent<Image>() : MUI.Image(rect, Color.white, true, 8f);
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
            }
            image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(onClick);
            var text = MUI.Text(rect, label, 30, InputTextColor, TextAnchor.MiddleCenter);
            MUI.Stretch(text.rectTransform);
            return button;
        }

        /// <summary>
        /// 竖屏：标题条（背景图截取）+ 蓝色表单卡片，尺寸按 dp。
        /// </summary>
        private RectTransform BuildPortrait()
        {
            var root = MUI.Rect(_root, "Login-Portrait");
            MUI.Stretch(root);
            var content = MUI.Scroll(root, out var scroll, 0, 0);
            MUI.Stretch((RectTransform)scroll.transform);
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 48, 24);
            layout.spacing = 0;

            var card = MUI.Rect(content, "Card");
            MUI.Image(card, BlueColor, false, 14f);
            var cardLayout = MUI.Vertical(card, 14, 18);
            cardLayout.padding.bottom = 26;

            var welcome = MUI.Text(card, "WELCOME", MUI.FontLarge, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleCenter, 30);
            welcome.fontStyle = FontStyle.Bold;
            if (_background != null)
            {
                var band = MUI.Rect(card, "Title");
                var image = band.gameObject.AddComponent<Image>();
                image.sprite = Sprite.Create(_background.texture, Scale(TitleBand, _background), new Vector2(0.5f, 0.5f));
                image.preserveAspect = true;
                image.raycastTarget = false;
                MUI.Size(band, -1, 56f);
            }
            else
            {
                var title = MUI.Paragraph(card, "小虎化工区安全风险智能化管控平台", MUI.FontLarge, Color.white);
                title.alignment = TextAnchor.UpperCenter;
                title.fontStyle = FontStyle.Bold;
            }

            AddPortraitItem(card, "用户名", "userName", false);
            AddPortraitItem(card, "密码", "password", true);
            var captcha = AddPortraitItem(card, "验证码", "captcha", false);
            var getCaptcha = MUI.Button(captcha, "获取验证码", GetCaptcha, 104, 46, Color.white, MUI.FontSmall + 1);
            getCaptcha.GetComponentInChildren<Text>().color = InputTextColor;

            var spacer = MUI.Rect(card, "Gap");
            MUI.Size(spacer, -1, 6f);
            var login = MUI.Button(card, "登录", Login, -1, 52, Color.white, MUI.FontTitle);
            login.GetComponentInChildren<Text>().color = InputTextColor;
            if (_buttonSprite != null)
            {
                var image = (Image)login.targetGraphic;
                image.sprite = _buttonSprite;
                image.type = Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = 2f;
            }
            return root;
        }

        private RectTransform AddPortraitItem(Transform parent, string label, string key, bool password)
        {
            var row = MUI.Row(parent, 46, 10);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            var title = MUI.Text(row, label, MUI.FontBody + 1, Color.white);
            MUI.Size(title, 60, -1, 0);
            var input = CreateInput(row, key, password, MUI.FontBody + 1);
            MUI.Size(input, 0, 46, 1);
            return row;
        }

        /// <summary>
        /// 白底输入框（原预制体：UISprite 白底、深灰文字）。输入写入 <see cref="_model"/>（对应 LoginView 的 onValueChanged → ViewModel）。
        /// </summary>
        private InputField CreateInput(Transform parent, string key, bool password, int fontSize)
        {
            var rect = MUI.Rect(parent, $"Input-{key}");
            var image = MUI.Image(rect, Color.white, true, 6f);
            var text = MUI.Text(rect, "", fontSize, InputTextColor);
            MUI.Stretch(text.rectTransform, 10, 10, 6, 7);
            text.supportRichText = false;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            var hint = MUI.Text(rect, "", fontSize, new Color(0.196f, 0.196f, 0.196f, 0.5f));
            MUI.Stretch(hint.rectTransform, 10, 10, 6, 7);

            var input = rect.gameObject.AddComponent<InputField>();
            input.targetGraphic = image;
            input.textComponent = text;
            input.placeholder = hint;
            input.caretColor = InputTextColor;
            input.navigation = new Navigation { mode = Navigation.Mode.None };
            // 原预制体密码框是明文（ContentType Standard），手机上改为掩码
            input.contentType = password ? InputField.ContentType.Password : InputField.ContentType.Standard;
            input.SetTextWithoutNotify(Value(key));
            input.onValueChanged.AddListener(v => SetValue(key, v));
            return input;
        }

        private string Value(string key) => key switch
        {
            "userName" => _model.userName,
            "password" => _model.password,
            "captcha" => _model.captcha,
            _ => ""
        } ?? "";

        private void SetValue(string key, string value)
        {
            switch (key)
            {
                case "userName":
                    _model.userName = value;
                    break;
                case "password":
                    _model.password = value;
                    break;
                case "captcha":
                    _model.captcha = value;
                    break;
            }
        }

        /// <summary>设计图像素坐标 → 贴图像素坐标（导入时贴图可能被缩小）。</summary>
        private static Rect Scale(Rect designRect, Sprite sprite)
        {
            var tex = sprite.texture;
            var r = sprite.textureRect;
            var sx = r.width / DesignSize.x;
            var sy = r.height / DesignSize.y;
            var rect = new Rect(r.x + designRect.x * sx, r.y + designRect.y * sy, designRect.width * sx, designRect.height * sy);
            rect.xMax = Math.Min(rect.xMax, tex.width);
            rect.yMax = Math.Min(rect.yMax, tex.height);
            return rect;
        }

        #endregion
    }
}
