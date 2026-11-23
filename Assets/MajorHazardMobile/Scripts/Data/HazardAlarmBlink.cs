using UnityEngine;
using UnityEngine.UI;

namespace LZW.Module.MajorHazard.Scripts
{
    /// <summary>
    /// 报警 POI 的闪烁效果：标记颜色在报警色与白色之间闪烁，外加一圈向外扩散的波纹。
    /// 挂在三维地图的 POI（Map3DPoi，标记 Image 就在根节点上）上。
    /// 缩放由 Map3DViewer 每帧写根节点，所以波纹用子节点自己的缩放，互不覆盖。
    /// </summary>
    public class HazardAlarmBlink : MonoBehaviour
    {
        public Color color = new Color(1f, 0.15f, 0.1f);

        [Tooltip("闪烁周期（秒）")]
        public float period = 0.8f;

        private Image _marker;
        private RectTransform _ring;
        private Image _ringImage;

        private void Start()
        {
            _marker = GetComponent<Image>();

            var go = new GameObject("AlarmRing", typeof(RectTransform)) { layer = gameObject.layer };
            _ring = (RectTransform)go.transform;
            _ring.SetParent(transform, false);
            _ring.SetSiblingIndex(0);
            _ring.anchorMin = Vector2.zero;
            _ring.anchorMax = Vector2.one;
            _ring.offsetMin = _ring.offsetMax = Vector2.zero;
            _ringImage = go.AddComponent<Image>();
            _ringImage.raycastTarget = false;
            if (_marker != null)
            {
                _ringImage.sprite = _marker.sprite;
                _ringImage.preserveAspect = true;
            }
        }

        private void Update()
        {
            var t = Mathf.Repeat(Time.unscaledTime, period) / period;

            if (_marker != null)
            {
                var flash = Mathf.PingPong(t * 2f, 1f);
                _marker.color = Color.Lerp(color, Color.white, flash * 0.7f);
            }

            _ring.localScale = Vector3.one * (1f + 1.6f * t);
            _ringImage.color = new Color(color.r, color.g, color.b, 0.75f * (1f - t));
        }
    }
}
