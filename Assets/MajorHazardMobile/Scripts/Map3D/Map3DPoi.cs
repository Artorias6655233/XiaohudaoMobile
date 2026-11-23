using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace LZW.Module.Map3DViewer.Scripts
{
    /// <summary>
    /// 三维地图上一个 POI 的 UI：标记按钮 + 名称标签 + 点击弹出的详情框。
    /// 由 <see cref="Map3DViewer"/> 创建并每帧摆放到模型上对应位置的投影处；整体由代码生成，不依赖预制体。
    ///
    /// 标记 Image 挂在 Button 节点上（点击区域 = 标记），标签 / 详情框都关闭 raycastTarget，不挡地图的触摸操作。
    /// </summary>
    public class Map3DPoi : MonoBehaviour
    {
        public Map3DPoiData Data { get; private set; }

        /// <summary>
        /// POI 在舞台（模型实例）中的世界坐标。
        /// </summary>
        public Vector3 WorldPosition { get; set; }

        public Button Button { get; private set; }

        public bool PopupVisible => _popup != null && _popup.gameObject.activeSelf;

        public UnityAction<Map3DPoi> onClick;

        private RectTransform _rect;
        private Image _marker;
        private Text _label;
        private RectTransform _popup;
        private Text _popupText;

        public static Map3DPoi Create(RectTransform parent, Map3DPoiData data, Font font, Sprite markerSprite, float markerSize, int fontSize)
        {
            var go = new GameObject($"POI-{data.id}", typeof(RectTransform)) { layer = parent.gameObject.layer };
            var poi = go.AddComponent<Map3DPoi>();
            poi.Build(parent, font, markerSprite, markerSize, fontSize);
            poi.SetData(data);
            return poi;
        }

        private void Build(RectTransform parent, Font font, Sprite markerSprite, float markerSize, int fontSize)
        {
            _rect = (RectTransform)transform;
            _rect.SetParent(parent, false);
            _rect.anchorMin = _rect.anchorMax = Vector2.zero;
            _rect.pivot = new Vector2(0.5f, 0.5f);
            _rect.sizeDelta = Vector2.one * markerSize;

            // 标记：自身即按钮
            _marker = gameObject.AddComponent<Image>();
            _marker.sprite = markerSprite;
            _marker.preserveAspect = true;
            if (markerSprite == null)
                _rect.localRotation = Quaternion.Euler(0, 0, 45f); // 无图标时用方块转 45° 做菱形

            Button = gameObject.AddComponent<Button>();
            // 全名：LZW.Module.Navigation 命名空间会遮住 UnityEngine.UI.Navigation
            Button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
            Button.onClick.AddListener(() => onClick?.Invoke(this));

            // 标签与详情框放在不随标记旋转的子节点下
            var holder = CreateRect("Holder", _rect);
            holder.localRotation = Quaternion.Inverse(_rect.localRotation);
            holder.sizeDelta = Vector2.zero;

            _label = CreateText("Label", holder, font, fontSize);
            var labelRect = _label.rectTransform;
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.anchoredPosition = new Vector2(0, -markerSize * 0.6f);
            labelRect.sizeDelta = new Vector2(240, fontSize * 1.6f);
            _label.alignment = TextAnchor.UpperCenter;
            var outline = _label.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0, 0, 0, 0.85f);

            _popup = CreateRect("Popup", holder);
            _popup.pivot = new Vector2(0.5f, 0f);
            _popup.anchoredPosition = new Vector2(0, markerSize * 0.7f);
            var bg = _popup.gameObject.AddComponent<Image>();
            bg.color = new Color(0.02f, 0.1f, 0.2f, 0.92f);
            bg.raycastTarget = false;
            var fitter = _popup.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var layout = _popup.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 12, 12);
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            _popupText = CreateText("Text", _popup, font, fontSize);
            _popupText.alignment = TextAnchor.UpperLeft;
            _popupText.horizontalOverflow = HorizontalWrapMode.Overflow;

            _popup.gameObject.SetActive(false);
        }

        public void SetData(Map3DPoiData data)
        {
            Data = data;
            name = $"POI-{data.id}";
            _marker.color = data.color;
            _label.text = data.name;

            var body = string.IsNullOrEmpty(data.detail) ? string.Empty : $"\n{data.detail}";
            _popupText.text = $"<b>{data.name}</b>  <color=#9fd8ff>{data.type}</color>{body}\n" +
                              $"<size={Mathf.RoundToInt(_popupText.fontSize * 0.8f)}><color=#8899aa>{data.longitude:F6}, {data.latitude:F6}</color></size>";
        }

        public void SetPopupVisible(bool visible)
        {
            _popup.gameObject.SetActive(visible);
            if (visible)
                transform.SetAsLastSibling(); // 详情框压在其他 POI 之上
        }

        /// <param name="localPos">在父节点（POI 层）中的位置，原点为左下角</param>
        /// <param name="scale">随视距变化的缩放，近大远小</param>
        public void Place(Vector2 localPos, float scale)
        {
            _rect.anchoredPosition = localPos;
            _rect.localScale = Vector3.one * scale;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = parent.gameObject.layer };
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static Text CreateText(string name, Transform parent, Font font, int fontSize)
        {
            var text = CreateRect(name, parent).gameObject.AddComponent<Text>();
            text.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = Color.white;
            text.supportRichText = true;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }
    }
}
