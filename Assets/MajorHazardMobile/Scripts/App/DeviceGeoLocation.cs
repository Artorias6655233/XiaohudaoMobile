using System.Collections;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace LZW.Module.MajorHazard.Mobile
{
    /// <summary>
    /// 手机定位（GNSS，WGS84）+ 指南针朝向，代替 AR 端的 VLP 视觉定位：智能风险管控按与手机的距离找「离我最近」的风险点，
    /// 按朝向给出「正前方 / 左前方 …」。第一次使用时申请定位权限。
    /// 编辑器里没有定位，用模拟位置（小虎岛 AnchorPoint_1）。
    /// </summary>
    public class DeviceGeoLocation : MonoBehaviour
    {
        public static DeviceGeoLocation Instance { get; private set; }

        /// <summary>编辑器 / 没有定位时的模拟位置。</summary>
        public static readonly Vector2 SimulatedLonLat = new Vector2(113.544888f, 22.838851f);

        public enum State
        {
            Stopped,
            NoPermission,
            Disabled,
            Starting,
            Running,
            Failed,
            Simulated
        }

        public State CurrentState { get; private set; } = State.Stopped;

        public bool HasFix => CurrentState == State.Running || CurrentState == State.Simulated;

        public double Longitude { get; private set; }
        public double Latitude { get; private set; }
        public float Accuracy { get; private set; }

        /// <summary>屏幕上方所指的方向（正北为 0，顺时针，度），没有指南针时为 NaN。</summary>
        public float Heading { get; private set; } = float.NaN;

        private int _users;
        private Coroutine _routine;

        public static DeviceGeoLocation Ensure()
        {
            if (Instance != null)
                return Instance;
            var go = new GameObject(nameof(DeviceGeoLocation));
            DontDestroyOnLoad(go);
            return Instance = go.AddComponent<DeviceGeoLocation>();
        }

        /// <summary>开始定位（引用计数，页面显示时调用，隐藏时 <see cref="Release"/>）。</summary>
        public void Acquire()
        {
            _users++;
            if (_routine == null && CurrentState != State.Running && CurrentState != State.Simulated)
                _routine = StartCoroutine(StartRoutine());
        }

        public void Release()
        {
            _users = Mathf.Max(0, _users - 1);
            if (_users > 0)
                return;
            if (_routine != null)
                StopCoroutine(_routine);
            _routine = null;
            if (Input.location.status != LocationServiceStatus.Stopped)
                Input.location.Stop();
            Input.compass.enabled = false;
            CurrentState = State.Stopped;
        }

        public string Describe()
        {
            return CurrentState switch
            {
                State.Running => $"已定位（精度约 {Accuracy:0} m）",
                State.Simulated => "编辑器：使用模拟位置",
                State.Starting => "正在定位…",
                State.NoPermission => "未授予定位权限，无法按距离排序",
                State.Disabled => "手机定位服务未开启",
                State.Failed => "定位失败，请到室外开阔处重试",
                _ => "未定位"
            };
        }

        private IEnumerator StartRoutine()
        {
            if (Application.isEditor)
            {
                Longitude = SimulatedLonLat.x;
                Latitude = SimulatedLonLat.y;
                Accuracy = 0;
                CurrentState = State.Simulated;
                _routine = null;
                yield break;
            }

            CurrentState = State.Starting;
#if UNITY_ANDROID
            if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
            {
                Permission.RequestUserPermission(Permission.FineLocation);
                // 等用户在系统弹窗里选择（弹窗期间应用失去焦点）
                var waited = 0f;
                while (!Permission.HasUserAuthorizedPermission(Permission.FineLocation) && waited < 30f)
                {
                    yield return new WaitForSecondsRealtime(0.5f);
                    waited += 0.5f;
                    if (Application.isFocused && waited > 2f && !Permission.HasUserAuthorizedPermission(Permission.FineLocation))
                        break;
                }
                if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
                {
                    CurrentState = State.NoPermission;
                    _routine = null;
                    yield break;
                }
            }
#endif
            if (!Input.location.isEnabledByUser)
            {
                CurrentState = State.Disabled;
                _routine = null;
                yield break;
            }

            Input.compass.enabled = true;
            Input.location.Start(5f, 2f);
            var timeout = 30f;
            while (Input.location.status == LocationServiceStatus.Initializing && timeout > 0f)
            {
                yield return new WaitForSecondsRealtime(0.5f);
                timeout -= 0.5f;
            }

            CurrentState = Input.location.status == LocationServiceStatus.Running ? State.Running : State.Failed;
            _routine = null;
        }

        private void Update()
        {
            if (CurrentState != State.Running)
                return;
            if (Input.location.status != LocationServiceStatus.Running)
            {
                CurrentState = State.Failed;
                return;
            }
            var data = Input.location.lastData;
            Longitude = data.longitude;
            Latitude = data.latitude;
            Accuracy = data.horizontalAccuracy;
            // 部分设备 trueHeading 一直为 0（需要磁偏角数据），退回磁北
            var heading = Input.compass.trueHeading != 0f ? Input.compass.trueHeading : Input.compass.magneticHeading;
            // 指南针以手机竖直方向的顶边为准，横屏时「屏幕上方」相差 90°
            heading += Screen.orientation switch
            {
                ScreenOrientation.LandscapeLeft => 90f,
                ScreenOrientation.LandscapeRight => 270f,
                ScreenOrientation.PortraitUpsideDown => 180f,
                _ => 0f
            };
            Heading = Input.compass.enabled ? heading % 360f : float.NaN;
        }

        /// <summary>两点水平距离（米，haversine）。</summary>
        public static float Distance(double lon1, double lat1, double lon2, double lat2)
        {
            const double r = 6371000.0;
            var dLat = (lat2 - lat1) * Mathf.Deg2Rad;
            var dLon = (lon2 - lon1) * Mathf.Deg2Rad;
            var a = System.Math.Sin(dLat / 2) * System.Math.Sin(dLat / 2) +
                    System.Math.Cos(lat1 * Mathf.Deg2Rad) * System.Math.Cos(lat2 * Mathf.Deg2Rad) *
                    System.Math.Sin(dLon / 2) * System.Math.Sin(dLon / 2);
            return (float)(r * 2 * System.Math.Atan2(System.Math.Sqrt(a), System.Math.Sqrt(1 - a)));
        }

        /// <summary>从点 1 看点 2 的方位角（正北为 0，顺时针，度）。</summary>
        public static float Bearing(double lon1, double lat1, double lon2, double lat2)
        {
            var phi1 = lat1 * Mathf.Deg2Rad;
            var phi2 = lat2 * Mathf.Deg2Rad;
            var dLon = (lon2 - lon1) * Mathf.Deg2Rad;
            var y = System.Math.Sin(dLon) * System.Math.Cos(phi2);
            var x = System.Math.Cos(phi1) * System.Math.Sin(phi2) - System.Math.Sin(phi1) * System.Math.Cos(phi2) * System.Math.Cos(dLon);
            return (float)((System.Math.Atan2(y, x) * Mathf.Rad2Deg + 360.0) % 360.0);
        }
    }
}
