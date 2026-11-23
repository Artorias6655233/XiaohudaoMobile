using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// 右手 aim 姿态（OpenXR 的 /input/aim/pose）读取。
///
/// grip（devicePosition / deviceRotation）是握把姿态，aim（pointerPosition /
/// pointerRotation）才是"手柄指向哪里"。实测两者在本机 Oculus Touch OpenXR 布局下
/// 相差 42.34 度俯仰。
/// </summary>
public static class XrAimPose
{
    public static InputDevice FindRightHandDevice()
    {
        var devices = InputSystem.devices;
        for (int i = 0; i < devices.Count; i++)
        {
            InputDevice d = devices[i];
            if (!d.added)
            {
                continue;
            }

            for (int u = 0; u < d.usages.Count; u++)
            {
                if (d.usages[u] == CommonUsages.RightHand)
                {
                    return d;
                }
            }
        }

        return null;
    }

    /// <summary>读取追踪空间下的 aim 位姿。设备或控件缺失时返回 false。</summary>
    public static bool TryRead(out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        InputDevice device = FindRightHandDevice();
        if (device == null)
        {
            return false;
        }

        var posControl = device.TryGetChildControl<Vector3Control>("pointerPosition");
        var rotControl = device.TryGetChildControl<QuaternionControl>("pointerRotation");
        if (posControl == null || rotControl == null)
        {
            return false;
        }

        position = posControl.ReadValue();
        rotation = rotControl.ReadValue();
        return true;
    }
}

/// <summary>
/// 把指针 Transform 直接按 aim 姿态驱动。
///
/// 原本 RightHandPointer 挂在 BNG 的手柄 anchor 下，实测其朝向 = grip + 60 度，
/// 与命中射线不重合。这里直接用 aim 覆盖世界位姿，使"看到的光束"与"判定用的射线"
/// 都等于 aim，偏差归零。
///
/// 执行顺序放到 Update 末尾：晚于 BNG 更新 anchor，早于所有 LateUpdate
/// （SelectedLaserPointer 在 LateUpdate 里按本 Transform 画线）。
/// </summary>
[DefaultExecutionOrder(1000)]
public class AimPosePointerDriver : MonoBehaviour
{
    /// <summary>追踪空间原点。为空则把 aim 位姿当世界坐标用。</summary>
    public Transform trackingOrigin;

    private void Update()
    {
        if (!XrAimPose.TryRead(out Vector3 pos, out Quaternion rot))
        {
            return;
        }

        if (trackingOrigin != null)
        {
            transform.SetPositionAndRotation(trackingOrigin.TransformPoint(pos), trackingOrigin.rotation * rot);
        }
        else
        {
            transform.SetPositionAndRotation(pos, rot);
        }
    }
}
