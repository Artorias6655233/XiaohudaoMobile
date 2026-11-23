using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace LZW.Module.Map3DViewer.Scripts
{
    /// <summary>
    /// 在一个 UI 窗口里显示可旋转 / 缩放的三维地图（小虎岛 XHD 模型），并在模型上叠加 POI。
    ///
    /// 实现：
    /// - 运行时把模型实例化到远离主场景的「舞台」上（主相机远裁剪面之外，互不可见），由一台专用相机渲染到 RenderTexture，
    ///   本节点下的 RawImage 显示它。三维内容因此严格限制在窗口矩形内，不会遮挡右侧按钮 / 标题，并随页面一起平移。
    /// - 相机绕模型包围盒中心（加平移偏移）环绕，视角来自 <see cref="Map3DTouchControl"/>（触屏 / 鼠标）。
    /// - POI 是 UI 按钮，每帧把其世界坐标投影到窗口内，旋转缩放时与模型保持相对位置；RectMask2D 裁掉窗口外的部分。
    /// - 经纬度由模型中的 AnchorPoint_N 节点拟合（<see cref="GeoMapper"/>）。
    ///
    /// 用法：在未激活的物体上 AddComponent，设置 <see cref="Model"/> 后激活，调用 <see cref="SetPois"/> 填数据，监听 <see cref="onPoiClick"/>。
    /// （移植自 AR 端 LZW/Module/Map3DViewer：去掉了语音口令和眼镜焦点，手势换成触屏。）
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(Map3DTouchControl))]
    public class Map3DViewer : MonoBehaviour
    {
        private static int _stageCount;

        [Header("模型")]
        [Tooltip("模型预制体（如 ArtWorks/XHD.prefab），也可以拖场景中的物体（会被移到舞台上）")]
        [SerializeField] private GameObject model;

        [Tooltip("名称以这些前缀开头的节点只用于定位，运行时隐藏")]
        [SerializeField] private string[] hiddenNodePrefixes = { "AnchorPoint" };

        [Tooltip("舞台位置，需在主相机远裁剪面之外。多个 Viewer 依次沿 X 错开 stageSpacing")]
        [SerializeField] private Vector3 stageOrigin = new Vector3(0, -10000, 0);

        [SerializeField] private float stageSpacing = 5000f;

        [Header("地理坐标映射（锚点名需与模型中的节点名一致）")]
        [SerializeField] private GeoAnchor[] anchors =
        {
            new GeoAnchor("AnchorPoint_1", 113.544888, 22.838851),
            new GeoAnchor("AnchorPoint_2", 113.563123, 22.836761),
            new GeoAnchor("AnchorPoint_3", 113.525848, 22.839671),
            new GeoAnchor("AnchorPoint_4", 113.538538, 22.851293),
        };

        [Header("渲染")]
        [SerializeField] private Color backgroundColor = new Color(0.03f, 0.07f, 0.12f, 1f);

        [SerializeField, Range(10f, 90f)] private float fieldOfView = 35f;

        [Tooltip("默认视距下模型占窗口的比例，越小模型越大")]
        [SerializeField] private float fitPadding = 0.85f;

        [Tooltip("RenderTexture 分辨率相对窗口实际像素的倍率")]
        [SerializeField, Range(0.25f, 2f)] private float renderScale = 1f;

        [SerializeField] private int antiAliasing = 4;

        [SerializeField] private LayerMask cullingMask = ~0;

        [Header("POI")]
        [Tooltip("为空则用内置字体")]
        [SerializeField] private Font poiFont;

        [Tooltip("为空则用菱形色块")]
        [SerializeField] private Sprite poiSprite;

        [SerializeField] private float poiSize = 40f;
        [SerializeField] private int poiFontSize = 24;

        [Tooltip("POI 离地高度（模型本地单位），叠加在每个 POI 自身的 height 上")]
        [SerializeField] private float poiHeightOffset = 20f;

        [Tooltip("POI 随缩放变大变小的范围")]
        [SerializeField] private Vector2 poiScaleRange = new Vector2(0.8f, 1.4f);

        [Tooltip("投影超出窗口这么多像素后隐藏")]
        [SerializeField] private float poiViewMargin = 10f;

        [Header("交互")]
        [Tooltip("每次显示时恢复默认视角并关闭详情框")]
        [SerializeField] private bool resetOnEnable = true;

        /// <summary>
        /// 点击 POI 时触发（默认行为是切换该 POI 的详情框）。
        /// </summary>
        public UnityAction<Map3DPoiData> onPoiClick;

        public GeoMapper Mapper { get; private set; }

        public IReadOnlyList<Map3DPoi> Pois => _pois;

        public Map3DTouchControl Orbit => _orbit;

        /// <summary>模型预制体，需在物体激活（建立舞台）之前设置。</summary>
        public GameObject Model
        {
            get => model;
            set => model = value;
        }

        public Font PoiFont
        {
            get => poiFont;
            set => poiFont = value;
        }

        private readonly List<Map3DPoi> _pois = new List<Map3DPoi>();

        private RectTransform _rect;
        private RawImage _image;
        private RectTransform _poiLayer;
        private Map3DTouchControl _orbit;
        private Canvas _canvas;

        private GameObject _stage;
        private Transform _modelRoot;
        private Camera _camera;
        private RenderTexture _rt;
        private Vector3 _center;
        private float _horizontalRadius = 1f;
        private float _boundsRadius = 1f;

        private Rect _screenRect;
        private bool _built;

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _orbit = GetComponent<Map3DTouchControl>();
            _orbit.containsScreenPoint = p => RectTransformUtility.RectangleContainsScreenPoint(_rect, p, CanvasCamera);
            _orbit.screenSize = () => _screenRect.size;
            BuildUI();
        }

        private void OnEnable()
        {
            if (model == null)
                Debug.LogWarning($"[{nameof(Map3DViewer)}] {name} 未设置 model", this);

            EnsureStage();
            if (_stage != null)
                _stage.SetActive(true);

            if (!resetOnEnable)
                return;

            _orbit.ResetView(true);
            foreach (var poi in _pois)
                poi.SetPopupVisible(false);
        }

        private void OnDisable()
        {
            if (_stage != null)
                _stage.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_stage != null)
                Destroy(_stage);
            ReleaseRenderTexture();
        }

        private void LateUpdate()
        {
            if (_camera == null)
                return;

            var visible = UpdateScreenRect();
            _camera.enabled = visible;
            _orbit.Interactable = visible;
            if (!visible)
                return;

            EnsureRenderTexture();
            ApplyCamera();
            LayoutPois();
        }

        #region Public API

        /// <summary>
        /// 用新数据替换所有 POI。
        /// </summary>
        public void SetPois(IEnumerable<Map3DPoiData> data)
        {
            ClearPois();
            foreach (var d in data)
                CreatePoi(d);
        }

        public Map3DPoi AddPoi(Map3DPoiData data)
        {
            var poi = CreatePoi(data);
            return poi;
        }

        public bool RemovePoi(string id)
        {
            var index = _pois.FindIndex(p => p.Data.id == id);
            if (index < 0)
                return false;

            Destroy(_pois[index].gameObject);
            _pois.RemoveAt(index);
            return true;
        }

        public void ClearPois()
        {
            foreach (var poi in _pois)
                if (poi != null)
                    Destroy(poi.gameObject);
            _pois.Clear();
        }

        /// <summary>
        /// 打开指定 POI 的详情框（同时关闭其他的），id 为空则全部关闭。
        /// </summary>
        public void ShowPopup(string id)
        {
            foreach (var poi in _pois)
                poi.SetPopupVisible(poi.Data.id == id && !string.IsNullOrEmpty(id));
        }

        public void ResetView()
        {
            _orbit.ResetView();
        }

        /// <summary>
        /// 经纬度 → 舞台上的世界坐标。模型或锚点缺失时返回模型中心。
        /// </summary>
        public Vector3 GeoToWorld(double longitude, double latitude, float height = 0f)
        {
            EnsureStage();
            if (Mapper == null || _modelRoot == null)
                return _center;
            return _modelRoot.TransformPoint(Mapper.ToLocal(longitude, latitude, height));
        }

        #endregion

        #region Build

        private void BuildUI()
        {
            if (_built)
                return;
            _built = true;

            if (!TryGetComponent<RectMask2D>(out _))
                gameObject.AddComponent<RectMask2D>();

            // 放在最底层，预制体里原有的子节点（边框、标题等）仍显示在地图之上
            _image = CreateStretched("Map3D-Image").gameObject.AddComponent<RawImage>();
            _image.raycastTarget = false;
            _image.color = Color.white;
            _image.transform.SetSiblingIndex(0);

            _poiLayer = CreateStretched("Map3D-Pois");
            _poiLayer.SetSiblingIndex(1);
        }

        private RectTransform CreateStretched(string objectName)
        {
            var go = new GameObject(objectName, typeof(RectTransform)) { layer = gameObject.layer };
            var rect = (RectTransform)go.transform;
            rect.SetParent(transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        private void EnsureStage()
        {
            if (_stage != null || model == null)
                return;

            _stage = new GameObject($"Map3D-Stage ({name})");
            _stage.transform.position = stageOrigin + Vector3.right * (stageSpacing * _stageCount++);

            // 场景中的物体直接搬过来，预制体则实例化
            _modelRoot = model.scene.IsValid() ? model.transform : Instantiate(model).transform;
            _modelRoot.gameObject.SetActive(true);
            _modelRoot.SetParent(_stage.transform, false);
            _modelRoot.localPosition = Vector3.zero;
            _modelRoot.localRotation = Quaternion.identity;

            var anchorTransforms = HideHelperNodes();
            FitMapper(anchorTransforms);
            ComputeBounds();
            CreateCamera();

            // 已有的 POI（在舞台建立前添加的）重新定位
            foreach (var poi in _pois)
                poi.WorldPosition = GeoToWorld(poi.Data.longitude, poi.Data.latitude, poi.Data.height + poiHeightOffset);
        }

        private Dictionary<string, Transform> HideHelperNodes()
        {
            var found = new Dictionary<string, Transform>();
            foreach (var t in _modelRoot.GetComponentsInChildren<Transform>(true))
            {
                if (!IsHelperNode(t.name))
                    continue;

                found[t.name] = t;
                foreach (var r in t.GetComponents<Renderer>())
                    r.enabled = false;
                foreach (var c in t.GetComponents<Collider>())
                    c.enabled = false;
            }
            return found;
        }

        private bool IsHelperNode(string nodeName)
        {
            foreach (var prefix in hiddenNodePrefixes)
                if (!string.IsNullOrEmpty(prefix) && nodeName.StartsWith(prefix))
                    return true;
            return false;
        }

        private void FitMapper(Dictionary<string, Transform> nodes)
        {
            var geo = new List<Vector2d>();
            var local = new List<Vector3>();
            foreach (var anchor in anchors)
            {
                if (!nodes.TryGetValue(anchor.name, out var node))
                {
                    Debug.LogWarning($"[{nameof(Map3DViewer)}] 模型中找不到锚点 {anchor.name}", this);
                    continue;
                }

                geo.Add(new Vector2d(anchor.longitude, anchor.latitude));
                local.Add(_modelRoot.InverseTransformPoint(node.position));
            }

            if (geo.Count < 3)
            {
                Debug.LogError($"[{nameof(Map3DViewer)}] 有效锚点不足 3 个，POI 无法按经纬度定位", this);
                return;
            }

            Mapper = GeoMapper.Fit(geo, local);
            Debug.Log($"[{nameof(Map3DViewer)}] 经纬度映射拟合完成，{geo.Count} 个锚点，最大残差 {Mapper.MaxResidual:F2}（模型本地单位）");
        }

        private void ComputeBounds()
        {
            var hasBounds = false;
            var bounds = new Bounds(_modelRoot.position, Vector3.zero);
            foreach (var r in _modelRoot.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r is ParticleSystemRenderer)
                    continue;

                if (hasBounds)
                {
                    bounds.Encapsulate(r.bounds);
                }
                else
                {
                    bounds = r.bounds;
                    hasBounds = true;
                }
            }

            _center = bounds.center;
            _horizontalRadius = Mathf.Max(0.01f, new Vector2(bounds.extents.x, bounds.extents.z).magnitude);
            _boundsRadius = Mathf.Max(0.01f, bounds.extents.magnitude);
        }

        private void CreateCamera()
        {
            var go = new GameObject("Map3D-Camera");
            go.transform.SetParent(_stage.transform, false);
            _camera = go.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = backgroundColor;
            _camera.fieldOfView = fieldOfView;
            _camera.cullingMask = cullingMask;
            _camera.stereoTargetEye = StereoTargetEyeMask.None;
            _camera.allowHDR = false;
            _camera.enabled = false;
        }

        #endregion

        #region Render

        private Camera CanvasCamera
        {
            get
            {
                if (_canvas == null)
                {
                    var canvas = GetComponentInParent<Canvas>();
                    _canvas = canvas != null ? canvas.rootCanvas : null;
                }
                return _canvas == null || _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            }
        }

        /// <summary>
        /// 计算窗口在屏幕上的像素矩形，返回是否可见（页面切走、标签页隐藏时不渲染也不响应手势）。
        /// </summary>
        private bool UpdateScreenRect()
        {
            var corners = new Vector3[4];
            _rect.GetWorldCorners(corners);
            var cam = CanvasCamera;
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var corner in corners)
            {
                var p = RectTransformUtility.WorldToScreenPoint(cam, corner);
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }

            _screenRect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            return _screenRect.width >= 2f && _screenRect.height >= 2f
                   && _screenRect.Overlaps(new Rect(0, 0, Screen.width, Screen.height));
        }

        private void EnsureRenderTexture()
        {
            var width = Mathf.Clamp(Mathf.RoundToInt(_screenRect.width * renderScale), 64, 2048);
            var height = Mathf.Clamp(Mathf.RoundToInt(_screenRect.height * renderScale), 64, 2048);
            if (_rt != null && Mathf.Abs(_rt.width - width) <= 2 && Mathf.Abs(_rt.height - height) <= 2)
                return;

            ReleaseRenderTexture();
            _rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = $"Map3D-RT ({name})",
                antiAliasing = Mathf.Max(1, antiAliasing)
            };
            _rt.Create();
            _camera.targetTexture = _rt;
            _camera.aspect = (float)width / height;
            _image.texture = _rt;
        }

        private void ReleaseRenderTexture()
        {
            if (_rt == null)
                return;

            if (_camera != null)
                _camera.targetTexture = null;
            if (_image != null)
                _image.texture = null;
            _rt.Release();
            Destroy(_rt);
            _rt = null;
        }

        private void ApplyCamera()
        {
            // 竖直 / 水平视场取较小者，保证模型在两个方向都放得下
            var halfV = fieldOfView * 0.5f * Mathf.Deg2Rad;
            var halfH = Mathf.Atan(Mathf.Tan(halfV) * _camera.aspect);
            var half = Mathf.Min(halfV, halfH);
            var baseDistance = _horizontalRadius * fitPadding / Mathf.Sin(half);
            var distance = baseDistance / Mathf.Max(0.01f, _orbit.Zoom);

            var rotation = Quaternion.Euler(_orbit.Pitch, _orbit.Yaw, 0f);
            var pan = _orbit.Pan * _horizontalRadius;
            var center = _center + new Vector3(pan.x, 0f, pan.y);
            _camera.transform.SetPositionAndRotation(center - rotation * Vector3.forward * distance, rotation);
            _camera.nearClipPlane = Mathf.Max(distance * 0.01f, distance - _boundsRadius * 1.5f);
            _camera.farClipPlane = distance + _boundsRadius * 1.5f;
        }

        #endregion

        #region POI

        private Map3DPoi CreatePoi(Map3DPoiData data)
        {
            var poi = Map3DPoi.Create(_poiLayer, data, poiFont, poiSprite, poiSize, poiFontSize);
            poi.WorldPosition = GeoToWorld(data.longitude, data.latitude, data.height + poiHeightOffset);
            poi.onClick += OnPoiClicked;
            _pois.Add(poi);
            return poi;
        }

        private void OnPoiClicked(Map3DPoi poi)
        {
            ShowPopup(poi.PopupVisible ? null : poi.Data.id);
            onPoiClick?.Invoke(poi.Data);
        }

        private void LayoutPois()
        {
            var size = _poiLayer.rect.size;
            var scale = Mathf.Clamp(Mathf.Sqrt(_orbit.Zoom), poiScaleRange.x, poiScaleRange.y);
            // 像素边距换算为 UI 单位
            var margin = poiViewMargin * (size.x / Mathf.Max(1f, _screenRect.width));

            foreach (var poi in _pois)
            {
                var vp = _camera.WorldToViewportPoint(poi.WorldPosition);
                var pos = new Vector2(vp.x * size.x, vp.y * size.y);
                var inView = vp.z > 0f
                             && pos.x >= -margin && pos.x <= size.x + margin
                             && pos.y >= -margin && pos.y <= size.y + margin;

                if (poi.gameObject.activeSelf != inView)
                    poi.gameObject.SetActive(inView);

                if (inView)
                    poi.Place(pos, scale);
            }
        }

        #endregion
    }
}
