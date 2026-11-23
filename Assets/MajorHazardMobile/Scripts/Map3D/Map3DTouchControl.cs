using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace LZW.Module.Map3DViewer.Scripts
{
    /// <summary>
    /// 手机 / 平板触屏操作三维地图，输出 <see cref="Yaw"/> / <see cref="Pitch"/> / <see cref="Zoom"/> / <see cref="Pan"/>，
    /// 由 <see cref="Map3DViewer"/> 换算成相机位姿。替代 AR 端的 Map3DOrbitControl（Rokid 手势）：
    /// - 单指拖动：水平 → 绕竖直轴旋转，竖直 → 俯仰
    /// - 双指捏合：缩放；双指同向拖动：平移地图；双指扭转：旋转
    /// - 双击：恢复默认视角
    /// 编辑器 / 鼠标：左键拖动旋转，右键拖动平移，滚轮缩放，中键复位。
    /// 只响应从地图上开始的触摸（地图之上的按钮、面板不会误触发）。
    /// </summary>
    public class Map3DTouchControl : MonoBehaviour
    {
        public enum Mode
        {
            None,
            Rotate,
            Zoom
        }

        [Header("默认视角")]
        [Tooltip("绕竖直轴的角度。XHD 模型 +X 朝西、+Z 朝南，180 时画面上方为北")]
        [SerializeField] private float defaultYaw = 180f;

        [SerializeField] private float defaultPitch = 50f;

        [Header("旋转")]
        [Tooltip("手指横移地图整幅宽度时旋转的角度")]
        [SerializeField] private float yawSensitivity = 270f;

        [Tooltip("手指竖移地图整幅高度时俯仰的角度")]
        [SerializeField] private float pitchSensitivity = 120f;

        [SerializeField, Range(0f, 89f)] private float minPitch = 15f;
        [SerializeField, Range(1f, 90f)] private float maxPitch = 89f;

        [Header("缩放 / 平移")]
        [Tooltip("相对默认视距的倍率范围，>1 为放大")]
        [SerializeField] private float minZoom = 0.6f;
        [SerializeField] private float maxZoom = 6f;

        [Tooltip("平移范围（以模型水平半径为单位）")]
        [SerializeField] private float maxPan = 1f;

        [SerializeField] private float wheelZoomStep = 0.15f;

        [Header("手感")]
        [Tooltip("平滑系数，越大越跟手、越小越稳")]
        [SerializeField] private float smoothing = 14f;

        [Tooltip("移动超过该像素才算拖动（否则视为点击，交给 POI 按钮）")]
        [SerializeField] private float dragThreshold = 12f;

        [SerializeField] private float doubleTapTime = 0.3f;

        public UnityAction<Mode> onModeChange;

        public Mode CurrentMode => _mode;

        public float Yaw { get; private set; }
        public float Pitch { get; private set; }
        public float Zoom { get; private set; } = 1f;

        /// <summary>
        /// 观察中心相对模型中心的水平偏移（世界坐标 x / z，单位为模型水平半径）。
        /// </summary>
        public Vector2 Pan { get; private set; }

        /// <summary>由 Viewer 设置：地图不在屏幕上时不响应触摸。</summary>
        public bool Interactable { get; set; } = true;

        /// <summary>由 Viewer 设置：判断屏幕点是否在地图上，以及地图的屏幕像素尺寸。</summary>
        public Func<Vector2, bool> containsScreenPoint;
        public Func<Vector2> screenSize;

        /// <summary>视角是否仍在变化（操作中或平滑过渡中）。</summary>
        public bool IsMoving => _mode != Mode.None || _settling;

        private Mode _mode = Mode.None;

        private float _targetYaw;
        private float _targetPitch;
        private float _targetZoom = 1f;
        private Vector2 _targetPan;
        private bool _settling;

        // 触摸状态
        private int _primaryId = -1;
        private Vector2 _primaryStart;
        private bool _dragging;
        private Vector2 _lastSingle;
        private float _lastPinchDistance;
        private float _lastPinchAngle;
        private Vector2 _lastPinchCenter;
        private bool _pinching;
        private float _lastTapTime = -1f;
        private Vector2 _lastTapPos;

        // 鼠标状态
        private int _mouseButton = -1;
        private Vector2 _mouseLast;

        private static readonly List<RaycastResult> RaycastResults = new List<RaycastResult>();

        private void Awake()
        {
            ResetView(true);
        }

        private void OnDisable()
        {
            _primaryId = -1;
            _pinching = false;
            _mouseButton = -1;
            SetMode(Mode.None);
        }

        private void Update()
        {
            if (!Interactable)
            {
                _primaryId = -1;
                _pinching = false;
                _mouseButton = -1;
                SetMode(Mode.None);
            }
            else if (Input.touchSupported && Input.touchCount > 0 || _primaryId >= 0)
            {
                UpdateTouch();
            }
            else if (Input.mousePresent)
            {
                UpdateMouse();
            }

            ApplySmoothed();
        }

        /// <summary>
        /// 恢复默认视角。immediate 为 true 时直接跳到默认视角，不做平滑过渡。
        /// </summary>
        public void ResetView(bool immediate = false)
        {
            SetMode(Mode.None);
            _targetYaw = defaultYaw;
            _targetPitch = Mathf.Clamp(defaultPitch, minPitch, maxPitch);
            _targetZoom = 1f;
            _targetPan = Vector2.zero;

            if (!immediate)
            {
                _settling = true;
                return;
            }

            Yaw = _targetYaw;
            Pitch = _targetPitch;
            Zoom = _targetZoom;
            Pan = _targetPan;
            _settling = false;
        }

        /// <summary>
        /// 外部（如按钮）调整视角，参数为相对当前目标值的增量 / 倍率。
        /// </summary>
        public void Nudge(float deltaYaw, float deltaPitch, float zoomFactor = 1f)
        {
            _targetYaw += deltaYaw;
            _targetPitch = Mathf.Clamp(_targetPitch + deltaPitch, minPitch, maxPitch);
            _targetZoom = Mathf.Clamp(_targetZoom * zoomFactor, minZoom, maxZoom);
            _settling = true;
        }

        #region Touch

        private void UpdateTouch()
        {
            var count = Input.touchCount;

            // 第一根手指按下：只有落在地图上（且最上层的 UI 属于地图）才接管
            if (_primaryId < 0)
            {
                for (var i = 0; i < count; i++)
                {
                    var t = Input.GetTouch(i);
                    if (t.phase != TouchPhase.Began || !StartsOnMap(t.position))
                        continue;
                    _primaryId = t.fingerId;
                    _primaryStart = _lastSingle = t.position;
                    _dragging = false;
                    _pinching = false;
                    break;
                }
                if (_primaryId < 0)
                    return;
            }

            if (!TryGetTouch(_primaryId, out var primary) || primary.phase == TouchPhase.Ended || primary.phase == TouchPhase.Canceled)
            {
                if (!_dragging && !_pinching && TryGetTouch(_primaryId, out var ended))
                    HandleTap(ended.position);
                _primaryId = -1;
                _pinching = false;
                SetMode(Mode.None);
                return;
            }

            // 第二根手指：任意位置都算（第一根已经在地图上）
            if (count >= 2 && TryGetSecond(out var second))
            {
                var center = (primary.position + second.position) / 2f;
                var distance = Vector2.Distance(primary.position, second.position);
                var angle = Mathf.Atan2(second.position.y - primary.position.y, second.position.x - primary.position.x) * Mathf.Rad2Deg;
                if (!_pinching)
                {
                    _pinching = true;
                    _dragging = true;
                    SetMode(Mode.Zoom);
                }
                else
                {
                    if (_lastPinchDistance > 1f && distance > 1f)
                        _targetZoom = Mathf.Clamp(_targetZoom * distance / _lastPinchDistance, minZoom, maxZoom);
                    _targetYaw += Mathf.DeltaAngle(_lastPinchAngle, angle);
                    PanBy(center - _lastPinchCenter);
                    _settling = true;
                }
                _lastPinchDistance = distance;
                _lastPinchAngle = angle;
                _lastPinchCenter = center;
                return;
            }

            if (_pinching)
            {
                // 从双指回到单指：以当前位置为新起点，避免跳动
                _pinching = false;
                _lastSingle = primary.position;
                SetMode(Mode.Rotate);
                return;
            }

            if (!_dragging && Vector2.Distance(primary.position, _primaryStart) > dragThreshold)
            {
                _dragging = true;
                _lastSingle = primary.position;
                SetMode(Mode.Rotate);
            }

            if (_dragging)
            {
                RotateBy(primary.position - _lastSingle);
                _lastSingle = primary.position;
            }
        }

        private void HandleTap(Vector2 position)
        {
            if (Time.unscaledTime - _lastTapTime < doubleTapTime && Vector2.Distance(position, _lastTapPos) < dragThreshold * 4f)
            {
                _lastTapTime = -1f;
                ResetView();
                return;
            }
            _lastTapTime = Time.unscaledTime;
            _lastTapPos = position;
        }

        private static bool TryGetTouch(int fingerId, out Touch touch)
        {
            for (var i = 0; i < Input.touchCount; i++)
            {
                touch = Input.GetTouch(i);
                if (touch.fingerId == fingerId)
                    return true;
            }
            touch = default;
            return false;
        }

        private bool TryGetSecond(out Touch touch)
        {
            for (var i = 0; i < Input.touchCount; i++)
            {
                touch = Input.GetTouch(i);
                if (touch.fingerId != _primaryId && touch.phase != TouchPhase.Ended && touch.phase != TouchPhase.Canceled)
                    return true;
            }
            touch = default;
            return false;
        }

        #endregion

        #region Mouse

        private void UpdateMouse()
        {
            var mousePos = (Vector2)Input.mousePosition;

            if (_mouseButton < 0)
            {
                for (var b = 0; b < 3; b++)
                {
                    if (!Input.GetMouseButtonDown(b) || !StartsOnMap(mousePos))
                        continue;
                    if (b == 2)
                    {
                        ResetView();
                        break;
                    }
                    _mouseButton = b;
                    _mouseLast = mousePos;
                    _primaryStart = mousePos;
                    _dragging = false;
                    break;
                }
            }
            else if (!Input.GetMouseButton(_mouseButton))
            {
                if (!_dragging && _mouseButton == 0)
                    HandleTap(mousePos);
                _mouseButton = -1;
                SetMode(Mode.None);
            }
            else
            {
                if (!_dragging && Vector2.Distance(mousePos, _primaryStart) > dragThreshold)
                {
                    _dragging = true;
                    _mouseLast = mousePos;
                    SetMode(_mouseButton == 0 ? Mode.Rotate : Mode.Zoom);
                }
                if (_dragging)
                {
                    if (_mouseButton == 0)
                        RotateBy(mousePos - _mouseLast);
                    else
                        PanBy(mousePos - _mouseLast);
                    _mouseLast = mousePos;
                }
            }

            var wheel = Input.mouseScrollDelta.y;
            if (!Mathf.Approximately(wheel, 0f) && (containsScreenPoint == null || containsScreenPoint(mousePos)))
                Nudge(0f, 0f, 1f + wheel * wheelZoomStep);
        }

        #endregion

        /// <summary>
        /// 触摸点在地图矩形内，且该处最上层的 UI 是地图本身或地图里的元素（POI）。
        /// </summary>
        private bool StartsOnMap(Vector2 screenPoint)
        {
            if (containsScreenPoint != null && !containsScreenPoint(screenPoint))
                return false;
            var es = EventSystem.current;
            if (es == null)
                return true;
            RaycastResults.Clear();
            es.RaycastAll(new PointerEventData(es) { position = screenPoint }, RaycastResults);
            return RaycastResults.Count == 0 || RaycastResults[0].gameObject.transform.IsChildOf(transform);
        }

        private Vector2 MapSize()
        {
            var size = screenSize?.Invoke() ?? new Vector2(Screen.width, Screen.height);
            return new Vector2(Mathf.Max(1f, size.x), Mathf.Max(1f, size.y));
        }

        /// <param name="pixelDelta">屏幕像素位移</param>
        private void RotateBy(Vector2 pixelDelta)
        {
            var size = MapSize();
            // 手指向右拖 → 场景跟手向右转（相机反向绕行）；向上拖 → 场景向后仰（相机降低）
            _targetYaw -= pixelDelta.x / size.x * yawSensitivity;
            _targetPitch = Mathf.Clamp(_targetPitch - pixelDelta.y / size.y * pitchSensitivity, minPitch, maxPitch);
            _settling = true;
        }

        /// <param name="pixelDelta">屏幕像素位移；地图跟着手指走，放大后同样的手指位移对应更小的地面距离</param>
        private void PanBy(Vector2 pixelDelta)
        {
            var size = MapSize();
            var delta = new Vector2(pixelDelta.x / size.x, pixelDelta.y / size.y) * (2f / Mathf.Max(0.01f, _targetZoom));
            // 屏幕方向 → 地面方向（随当前朝向），平移量存成世界坐标，之后再旋转时观察点不跟着漂移
            var rotation = Quaternion.Euler(0f, _targetYaw, 0f);
            var ground = rotation * Vector3.right * delta.x + rotation * Vector3.forward * delta.y;
            _targetPan = Vector2.ClampMagnitude(_targetPan - new Vector2(ground.x, ground.z), maxPan);
            _settling = true;
        }

        private void SetMode(Mode mode)
        {
            if (_mode == mode) return;

            _mode = mode;
            if (mode != Mode.None)
                _settling = true;
            onModeChange?.Invoke(mode);
        }

        private void ApplySmoothed()
        {
            if (!_settling) return;

            var t = 1f - Mathf.Exp(-smoothing * Time.unscaledDeltaTime);
            Yaw = Mathf.Lerp(Yaw, _targetYaw, t);
            Pitch = Mathf.Lerp(Pitch, _targetPitch, t);
            Zoom = Mathf.Lerp(Zoom, _targetZoom, t);
            Pan = Vector2.Lerp(Pan, _targetPan, t);

            if (_mode == Mode.None
                && Mathf.Abs(Yaw - _targetYaw) < 0.01f
                && Mathf.Abs(Pitch - _targetPitch) < 0.01f
                && Mathf.Abs(Zoom - _targetZoom) < 0.0005f
                && (Pan - _targetPan).sqrMagnitude < 1e-8f)
            {
                Yaw = _targetYaw;
                Pitch = _targetPitch;
                Zoom = _targetZoom;
                Pan = _targetPan;
                _settling = false;
            }
        }
    }
}
