using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

/// <summary>
/// 让可见光束在命中点收住，不再穿过面板。
///
/// BNG 的 UIPointer 是按 VRUISystem.EventData.pointerCurrentRaycast 画线的，但本工程里
/// VRUISystem 根本没在跑：EventSystem 每帧只 Process 组件列表中【第一个】
/// ShouldActivateModule 的模块，场景里预置的是 InputSystemUIInputModule，而 VRUISystem
/// 是 VRCanvas / Session 在运行时 AddComponent 上去的，排在它后面。
///
/// 于是 EventData.pointerCurrentRaycast.gameObject 恒为 null，UIPointer 每帧都走
/// HidePointer()；又因为 RightHandPointer 上 HidePointerIfNoObjectsFound = false，
/// 它只会画一条 FixedPointerLength(0.5) * LineDistanceModifier(0.8) = 0.4 米的【定长】
/// 光束 —— 长度跟命中与否无关。左手面板离手柄约 0.26 米，光束自然穿了出去。
///
/// 这里直接向真正在跑的那个模块要命中结果（InputSystemUIInputModule.GetLastRaycastResult），
/// 按实际命中距离画线。命中射线的原点/朝向已由 <see cref="AimPosePointerDriver"/> 对齐到
/// 本 Transform，所以 RaycastResult.distance 可以直接当作本地 z 用。
/// </summary>
[DefaultExecutionOrder(1100)] // 晚于 AimPosePointerDriver(1000)，用它摆好的位姿
public class AimPointerBeam : MonoBehaviour
{
    private BNG.UIPointer pointer;
    private LineRenderer line;
    private GameObject cursor;
    private InputSystemUIInputModule module;
    private InputDevice device;

    /// <summary>
    /// 没有命中任何 UI 时的光束长度（米）。
    ///
    /// 别用 UIPointer 上那个 FixedPointerLength(0.5) * LineDistanceModifier(0.8) = 0.4 米：
    /// 场景里带 TrackedDeviceRaycaster 的 Canvas 常驻只有 6 个（5 大类 / 天气 / 左手面板），
    /// 沙盘地形、建筑、摄像头图标都不可命中。按 0.4 米兜底的话，射线一离开那几块 UI
    /// 就从两米缩成一截 0.4 米的短桩，看起来像"射线断了"。
    ///
    /// 正常 VR 激光的语义是"没命中就一直射出去，命中才收住"，所以兜底要给足。
    /// </summary>
    public float missLength = 5f;

    private void Start()
    {
        pointer = GetComponent<BNG.UIPointer>();
        if (pointer == null)
        {
            Debug.LogError("[AimPointerBeam] 同物体上没有 UIPointer，光束长度修正未生效");
            enabled = false;
            return;
        }

        line = pointer.lineRenderer;
        cursor = pointer.Cursor;

        // UIPointer.Update 会用那份永远为空的 EventData 把光束改回定长，两边会打架
        pointer.enabled = false;
    }

    private void LateUpdate()
    {
        if (line == null)
        {
            return;
        }

        if (module == null)
        {
            module = FindObjectOfType<InputSystemUIInputModule>();
            if (module == null)
            {
                return;
            }
        }

        // 手柄可能中途掉线重连，deviceId 会变，所以每次都校验 added
        if (device == null || !device.added)
        {
            device = XrAimPose.FindRightHandDevice();
        }

        float length = missLength;
        if (device != null)
        {
            RaycastResult hit = module.GetLastRaycastResult(device.deviceId);
            if (hit.isValid && hit.distance > 0f)
            {
                length = hit.distance;
            }
        }

        line.useWorldSpace = false;
        line.SetPosition(0, Vector3.zero);
        line.SetPosition(1, new Vector3(0f, 0f, length));
        line.enabled = true;

        // UIPointer 被禁用后就没人管光标了。它原本也一直是隐藏的（每帧走 HidePointer），
        // 这里保持隐藏，避免它停在手柄原点变成一个可见的点。
        if (cursor != null && cursor.activeSelf)
        {
            cursor.SetActive(false);
        }
    }
}
