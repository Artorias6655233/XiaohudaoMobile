using System;
using UnityEngine;
using UnityEngine.UI;

namespace LZW.Module.MajorHazard.Mobile.UI
{
    /// <summary>
    /// 画布缩放：UI 按 dp 设计（1 单位 ≈ 1dp = 1/160 英寸），手机、平板上的文字和按钮物理大小一致；
    /// 屏幕短边换算后限制在 [minShortSide, maxShortSide] dp 之间（编辑器 / 没有 dpi 信息的设备也能有合理的大小）。
    /// 同时把 <see cref="SafeArea"/> 应用到内容根节点（刘海 / 挖孔屏），屏幕旋转时触发 <see cref="Changed"/>。
    /// </summary>
    [RequireComponent(typeof(CanvasScaler))]
    public class ResponsiveCanvas : MonoBehaviour
    {
        [SerializeField] private float minShortSide = 360f;
        [SerializeField] private float maxShortSide = 820f;

        /// <summary>应用安全区域的节点。</summary>
        public RectTransform SafeArea { get; set; }

        /// <summary>屏幕尺寸 / 方向变化（布局需要重排时）。</summary>
        public event Action Changed;

        public bool Portrait => Screen.height > Screen.width;

        private CanvasScaler _scaler;
        private Vector2Int _size;
        private Rect _safe;

        private void Awake()
        {
            _scaler = GetComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            Refresh();
        }

        private void Update()
        {
            if (_size.x != Screen.width || _size.y != Screen.height || _safe != Screen.safeArea)
                Refresh();
        }

        public void Refresh()
        {
            _size = new Vector2Int(Screen.width, Screen.height);
            _safe = Screen.safeArea;

            var shortSide = Mathf.Max(1, Mathf.Min(Screen.width, Screen.height));
            var dpi = Screen.dpi > 1f ? Screen.dpi : 160f;
            var shortSideDp = Mathf.Clamp(shortSide / (dpi / 160f), minShortSide, maxShortSide);
            _scaler.scaleFactor = shortSide / shortSideDp;

            if (SafeArea != null && Screen.width > 0 && Screen.height > 0)
            {
                SafeArea.anchorMin = new Vector2(_safe.xMin / Screen.width, _safe.yMin / Screen.height);
                SafeArea.anchorMax = new Vector2(_safe.xMax / Screen.width, _safe.yMax / Screen.height);
                SafeArea.offsetMin = SafeArea.offsetMax = Vector2.zero;
            }

            Changed?.Invoke();
        }
    }
}
