using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace LZW.Module.MajorHazard.Mobile.UI
{
    /// <summary>
    /// 代码生成 uGUI 的小工具（移动端版的 PipeUI）：配色与 AR 端一致，尺寸按 dp 设计（画布缩放见 <see cref="ResponsiveCanvas"/>），
    /// 可点元素不小于 40dp。圆角用运行时生成的九宫格贴图，不依赖美术资源。
    /// </summary>
    public static class MUI
    {
        public static readonly Color BackgroundColor = new Color(0.02f, 0.06f, 0.11f, 1f);
        public static readonly Color BarColor = new Color(0.03f, 0.12f, 0.22f, 1f);
        public static readonly Color PanelColor = new Color(0.04f, 0.12f, 0.21f, 0.96f);
        public static readonly Color CardColor = new Color(0.06f, 0.17f, 0.29f, 1f);
        public static readonly Color ButtonColor = new Color(0.08f, 0.32f, 0.55f, 1f);
        public static readonly Color ChipColor = new Color(0.08f, 0.2f, 0.33f, 1f);
        public static readonly Color ConfirmColor = new Color(0.1f, 0.5f, 0.25f, 1f);
        public static readonly Color DangerColor = new Color(0.65f, 0.16f, 0.14f, 1f);
        public static readonly Color AccentColor = new Color(0.2f, 0.85f, 1f, 1f);
        public static readonly Color TextColor = new Color(0.92f, 0.96f, 1f, 1f);
        public static readonly Color DimTextColor = new Color(0.65f, 0.75f, 0.85f, 1f);
        public static readonly Color WarnColor = new Color(1f, 0.75f, 0.25f, 1f);
        public static readonly Color FieldColor = new Color(0.02f, 0.08f, 0.15f, 1f);

        public const int FontSmall = 13;
        public const int FontBody = 15;
        public const int FontTitle = 18;
        public const int FontLarge = 22;
        public const float ButtonHeight = 44f;

        private static Font _defaultFont;
        private static Sprite _rounded;

        /// <summary>全局字体（应用启动时设置），为空则用内置字体（Android 上缺字会回退到系统字体）。</summary>
        public static Font Font
        {
            get => _defaultFont != null ? _defaultFont : _defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            set => _defaultFont = value;
        }

        /// <summary>
        /// 白色圆角矩形九宫格（半径 16px）。Image 的 pixelsPerUnitMultiplier 控制实际圆角大小，见 <see cref="Image"/>。
        /// </summary>
        public static Sprite Rounded
        {
            get
            {
                if (_rounded != null)
                    return _rounded;
                const int size = 48;
                const float radius = 16f;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    name = "MUI-Rounded"
                };
                var pixels = new Color32[size * size];
                for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var px = x + 0.5f;
                    var py = y + 0.5f;
                    var cx = Mathf.Clamp(px, radius, size - radius);
                    var cy = Mathf.Clamp(py, radius, size - radius);
                    var d = Vector2.Distance(new Vector2(px, py), new Vector2(cx, cy));
                    var a = Mathf.Clamp01(radius - d + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
                tex.SetPixels32(pixels);
                tex.Apply(false, true);
                _rounded = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                    SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
                return _rounded;
            }
        }

        #region 基础

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = parent.gameObject.layer };
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        public static RectTransform Stretch(RectTransform rect, float left = 0, float right = 0, float top = 0, float bottom = 0)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
            return rect;
        }

        /// <param name="radius">圆角半径（dp），0 = 直角</param>
        public static Image Image(RectTransform rect, Color color, bool raycast = false, float radius = 0f)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            if (radius > 0f)
            {
                image.sprite = Rounded;
                image.type = UnityEngine.UI.Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = 16f / radius;
            }
            return image;
        }

        /// <summary>
        /// 竖排容器（子元素用 LayoutElement 控制高度）。
        /// </summary>
        public static VerticalLayoutGroup Vertical(RectTransform rect, float spacing = 8, int padding = 0)
        {
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return layout;
        }

        public static RectTransform Row(Transform parent, float height = ButtonHeight, float spacing = 8)
        {
            var rect = Rect(parent, "Row");
            var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            Size(rect, -1, height);
            return rect;
        }

        public static LayoutElement Size(Component c, float width, float height, float flexibleWidth = -1)
        {
            if (!c.TryGetComponent<LayoutElement>(out var le))
                le = c.gameObject.AddComponent<LayoutElement>();
            if (width >= 0)
            {
                le.preferredWidth = width;
                le.minWidth = flexibleWidth > 0 ? 0 : width;
            }
            if (height >= 0)
            {
                le.preferredHeight = height;
                le.minHeight = height;
            }
            le.flexibleWidth = flexibleWidth;
            return le;
        }

        /// <summary>占满剩余高度的空白。</summary>
        public static RectTransform Spacer(Transform parent)
        {
            var rect = Rect(parent, "Spacer");
            Size(rect, -1, 0).flexibleHeight = 1;
            return rect;
        }

        public static void Clear(Transform parent)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }
        }

        public static string Ellipsis(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max)
                return s ?? "";
            return s.Substring(0, Math.Max(0, max - 1)) + "…";
        }

        public static string Hex(Color color) => ColorUtility.ToHtmlStringRGB(color);

        #endregion

        #region 文字

        public static Text Text(Transform parent, string content, int size = FontBody, Color? color = null,
            TextAnchor align = TextAnchor.MiddleLeft, float height = -1)
        {
            var rect = Rect(parent, "Text");
            var text = rect.gameObject.AddComponent<Text>();
            text.font = Font;
            text.fontSize = size;
            text.color = color ?? TextColor;
            text.alignment = align;
            text.text = content;
            text.supportRichText = true;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.lineSpacing = 1.1f;
            if (height >= 0)
                Size(text, -1, height);
            return text;
        }

        /// <summary>
        /// 自适应高度的多行文字。
        /// </summary>
        public static Text Paragraph(Transform parent, string content, int size = FontBody, Color? color = null)
        {
            var text = Text(parent, content, size, color, TextAnchor.UpperLeft);
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return text;
        }

        public static Text SectionTitle(Transform parent, string content)
        {
            var text = Text(parent, content, FontBody, AccentColor, TextAnchor.LowerLeft, 26);
            text.fontStyle = FontStyle.Bold;
            return text;
        }

        #endregion

        #region 按钮

        public static Button Button(Transform parent, string label, UnityAction onClick, float width = -1, float height = ButtonHeight,
            Color? color = null, int fontSize = FontBody)
        {
            var rect = Rect(parent, $"Btn-{label}");
            var image = Image(rect, color ?? ButtonColor, true, 8f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(0.7f, 0.85f, 1f, 1f);
            colors.fadeDuration = 0.05f;
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            if (onClick != null)
                button.onClick.AddListener(onClick);

            var text = Text(rect, label, fontSize, TextColor, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform, 6, 6);
            Size(button, width, height, width >= 0 ? 0 : 1);
            return button;
        }

        public static void SetLabel(Button button, string label)
        {
            var text = button.GetComponentInChildren<Text>();
            if (text != null)
                text.text = label;
        }

        public static void SetColor(Button button, Color color)
        {
            if (button.targetGraphic != null)
                button.targetGraphic.color = color;
        }

        /// <summary>
        /// 整块可点击的卡片（列表项）：返回卡片本身，内容由调用方往里加（卡片是竖排容器）。
        /// </summary>
        public static Button Card(Transform parent, UnityAction onClick, Color? color = null, int padding = 12)
        {
            var rect = Rect(parent, "Card");
            var image = Image(rect, color ?? CardColor, true, 10f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(0.75f, 0.9f, 1f, 1f);
            colors.fadeDuration = 0.05f;
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            if (onClick != null)
                button.onClick.AddListener(onClick);
            Vertical(rect, 4, padding);
            return button;
        }

        #endregion

        #region 输入

        /// <summary>
        /// 单行 / 多行输入框。手机上弹出系统输入法（输入法自带的语音输入可代替 AR 端的语音转文字）。
        /// </summary>
        public static InputField Input(Transform parent, string placeholder, UnityAction<string> onEndEdit = null,
            float height = ButtonHeight, bool multiLine = false)
        {
            var rect = Rect(parent, "Input");
            var image = Image(rect, FieldColor, true, 8f);
            var outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.2f, 0.5f, 0.75f, 0.6f);
            outline.effectDistance = new Vector2(1, -1);
            Size(rect, -1, height, 1);

            var text = Text(rect, "", FontBody, TextColor, multiLine ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft);
            Stretch(text.rectTransform, 12, 12, multiLine ? 8 : 0, multiLine ? 8 : 0);
            text.supportRichText = false;
            text.verticalOverflow = multiLine ? VerticalWrapMode.Truncate : VerticalWrapMode.Overflow;

            var hint = Text(rect, placeholder, FontBody, new Color(0.55f, 0.65f, 0.75f, 0.8f),
                multiLine ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft);
            Stretch(hint.rectTransform, 12, 12, multiLine ? 8 : 0, multiLine ? 8 : 0);
            hint.fontStyle = FontStyle.Italic;

            var input = rect.gameObject.AddComponent<InputField>();
            input.targetGraphic = image;
            input.textComponent = text;
            input.placeholder = hint;
            input.lineType = multiLine ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
            input.caretColor = AccentColor;
            input.selectionColor = new Color(0.2f, 0.6f, 1f, 0.4f);
            // 保留输入法上方的输入条：输入框在屏幕下半部分时会被键盘挡住，Unity 不会自动上推页面
            input.shouldHideMobileInput = false;
            input.navigation = new Navigation { mode = Navigation.Mode.None };
            if (onEndEdit != null)
                input.onEndEdit.AddListener(onEndEdit);
            return input;
        }

        /// <summary>
        /// 搜索框：输入时实时回调（输入法确认时也回调一次）。右侧带「清除」按钮。
        /// </summary>
        public static InputField Search(Transform parent, string placeholder, UnityAction<string> onChanged)
        {
            var row = Row(parent, ButtonHeight, 6);
            var input = Input(row, placeholder);
            input.onValueChanged.AddListener(onChanged);
            Button(row, "清除", () => input.text = "", 64, ButtonHeight, ChipColor);
            return input;
        }

        #endregion

        #region 滚动

        /// <summary>
        /// 竖向滚动区域（手指拖动、惯性）。返回内容容器（竖排，高度随内容）。
        /// </summary>
        public static RectTransform Scroll(Transform parent, out ScrollRect scroll, float spacing = 8, int padding = 0)
        {
            var root = Rect(parent, "Scroll");
            var image = Image(root, new Color(0, 0, 0, 0), true); // 空白处也能拖动
            image.raycastTarget = true;
            scroll = root.gameObject.AddComponent<NestedScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.inertia = true;
            scroll.decelerationRate = 0.12f;
            scroll.scrollSensitivity = 30f;

            var viewport = Rect(root, "Viewport");
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            scroll.viewport = viewport;

            var content = Rect(viewport, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = content.offsetMax = Vector2.zero;
            Vertical(content, spacing, padding);
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
            return content;
        }

        /// <summary>
        /// 横向滚动的一行（筛选标签等，内容超出时可左右滑）。返回内容容器（横排）。
        /// </summary>
        public static RectTransform HorizontalScroll(Transform parent, float height, float spacing = 8)
        {
            var root = Rect(parent, "HScroll");
            Image(root, new Color(0, 0, 0, 0), true);
            Size(root, -1, height);
            var scroll = root.gameObject.AddComponent<NestedScrollRect>();
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.decelerationRate = 0.12f;

            var viewport = Rect(root, "Viewport");
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            scroll.viewport = viewport;

            var content = Rect(viewport, "Content");
            content.anchorMin = new Vector2(0f, 0f);
            content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 0.5f);
            content.offsetMin = content.offsetMax = Vector2.zero;
            var layout = content.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            content.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
            return content;
        }

        #endregion
    }
}
