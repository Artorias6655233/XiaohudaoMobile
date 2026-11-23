using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// 让世界空间 UI 能被手柄射线正确命中。
///
/// 场景用的是 InputSystemUIInputModule 的 tracked device 通路。按 Unity 官方文档，
/// 世界空间 Canvas 必须挂 TrackedDeviceRaycaster 才会沿手柄射线做命中判定；只有普通
/// GraphicRaycaster 时会退化成"用事件相机的屏幕投影"，命中点和手柄指向对不上。
/// 本工程原先一个 TrackedDeviceRaycaster 都没有。
///
/// 标记物（企业 / 储罐 / 摄像头）是运行时实例化的，所以这里定期补扫而不是只跑一次。
/// </summary>
public class TrackedUiBootstrap : MonoBehaviour
{
    /// <summary>补扫间隔。标记物随子模块切换才创建，需要持续补。</summary>
    private const float RescanInterval = 2f;

    /// <summary>BNG 在 VRUISystem.init() 里 new GameObject("CameraCaster")，只能按名字找。</summary>
    private const string CasterName = "CameraCaster";

    private readonly HashSet<Canvas> processed = new HashSet<Canvas>();

    /// <summary>非根 / Overlay 的 Canvas。它们进不了 <see cref="processed"/>，单独去重只为日志不重复。</summary>
    private readonly HashSet<Canvas> nonRoot = new HashSet<Canvas>();

    /// <summary>
    /// 因"子级没有 Selectable"被跳过的 Canvas。同样进不了 <see cref="processed"/>，
    /// 单独去重只为日志不重复 —— 它们每轮都要重新判一次，见 <see cref="Rescan"/>。
    /// </summary>
    private readonly HashSet<Canvas> noSelectable = new HashSet<Canvas>();
    private float timer;
    private bool trackingOriginAssigned;
    private bool aimRebound;
    private bool aimDriverInstalled;
    private bool dragThresholdApplied;
    private bool casterAttached;
    private int casterAttempts;

    /// <summary>补扫 5 轮（约 10 秒）还没找到 CameraCaster 就报一次错。</summary>
    private const int CasterWarnAfterAttempts = 5;

    /// <summary>被 AimPosePointerDriver 驱动、位姿等于 aim 的指针 Transform。</summary>
    private Transform aimPointer;

    /// <summary>
    /// tracked 指针的拖拽阈值倍率。
    ///
    /// 注意这个倍率在源码里是乘在【平方】上的：
    ///   (pressPosition - position).sqrMagnitude >= pixelDragThreshold² * multiplier
    /// 所以距离容差 = pixelDragThreshold * sqrt(multiplier)。
    /// 场景里 EventSystem.pixelDragThreshold = 10，默认倍率 2 只有 10*sqrt(2) ≈ 14.1 像素。
    /// 取 20 得到 10*sqrt(20) ≈ 44.7 像素。
    /// </summary>
    private const float DragThresholdMultiplier = 20f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        GameObject go = new GameObject("~TrackedUiBootstrap");
        DontDestroyOnLoad(go);
        go.AddComponent<TrackedUiBootstrap>();
    }

    private void Start()
    {
        Debug.Log("[TrackedUI] bootstrap 已启用：为世界空间 Canvas 补 TrackedDeviceRaycaster");
        AssignTrackingOrigin();
        ApplyDragThreshold();
        SwitchToAimPose();
        AttachEventCameraToAimPointer();
        Rescan();
    }

    private void Update()
    {
        timer += Time.deltaTime;
        if (timer < RescanInterval)
        {
            return;
        }

        timer = 0f;

        if (!trackingOriginAssigned)
        {
            AssignTrackingOrigin();
        }

        if (!dragThresholdApplied)
        {
            ApplyDragThreshold();
        }

        if (!aimRebound || !aimDriverInstalled)
        {
            SwitchToAimPose();
        }

        // VRUISystem 是 VRCanvas 第一次 Start 时才 AddComponent 出来的，
        // 早于它跑就找不到 CameraCaster，所以要重试。
        if (!casterAttached)
        {
            AttachEventCameraToAimPointer();
        }

        Rescan();
    }

    /// <summary>
    /// 把命中射线和可见光束都切到 aim 姿态。
    ///
    /// 模块默认绑的是 &lt;XRController&gt;/devicePosition + /deviceRotation（grip 握把姿态），
    /// 而手柄"指向"对应的是 aim。实测 grip→aim 差 42.34 度、grip→可见光束差 60 度，
    /// 所以照准光束点击时射线偏 60 度，什么都命中不了。两边统一到 aim 后偏差归零。
    /// </summary>
    private void SwitchToAimPose()
    {
        if (!aimRebound)
        {
            InputSystemUIInputModule module = FindObjectOfType<InputSystemUIInputModule>();
            if (module == null)
            {
                return;
            }

            // 覆盖现有 action 的绑定路径，而不是新建 action。
            // InputActionReference.Create 要求 action 隶属于 InputActionAsset，
            // 裸 InputActionMap 会抛 InvalidOperationException。
            bool posOk = OverrideBindingPath(module.trackedDevicePosition,
                "devicePosition", "<XRController>{RightHand}/pointerPosition");
            bool rotOk = OverrideBindingPath(module.trackedDeviceOrientation,
                "deviceRotation", "<XRController>{RightHand}/pointerRotation");

            // 无论成败都置位，避免每帧重试刷屏
            aimRebound = true;

            if (posOk && rotOk)
            {
                Debug.Log("[TrackedUI] 命中射线已从 grip 切到 aim（pointerPosition / pointerRotation）");
            }
            else
            {
                Debug.LogError($"[TrackedUI] 切换 aim 绑定失败 posOk={posOk} rotOk={rotOk}，" +
                    "为保持光束与射线一致，不再安装 AimPosePointerDriver");
                return;
            }
        }

        if (!aimDriverInstalled)
        {
            Transform origin = FindTrackingSpace();
            BNG.UIPointer[] pointers = FindObjectsOfType<BNG.UIPointer>();

            for (int i = 0; i < pointers.Length; i++)
            {
                if (pointers[i].ControllerSide != BNG.ControllerHand.Right)
                {
                    continue;
                }

                // 记下来给 AttachEventCameraToAimPointer 用：这个 Transform 的世界位姿
                // 就是命中射线本身
                aimPointer = pointers[i].transform;

                // 光束长度改由实际命中结果驱动，否则它是一条与命中无关的定长线，会穿过面板
                if (pointers[i].GetComponent<AimPointerBeam>() == null)
                {
                    pointers[i].gameObject.AddComponent<AimPointerBeam>();
                    Debug.Log($"[TrackedUI] 光束 '{pointers[i].name}' 已改为按实际命中距离收线");
                }

                if (pointers[i].GetComponent<AimPosePointerDriver>() != null)
                {
                    continue;
                }

                AimPosePointerDriver driver = pointers[i].gameObject.AddComponent<AimPosePointerDriver>();
                driver.trackingOrigin = origin;
                aimDriverInstalled = true;
                Debug.Log($"[TrackedUI] 可见光束 '{pointers[i].name}' 已改为按 aim 姿态驱动" +
                    $"（trackingOrigin={(origin != null ? origin.name : "<世界空间>")}）");
            }
        }
    }

    /// <summary>
    /// 把 BNG 的 CameraCaster 挂到 aim 指针上，修正 Mask 的命中过滤。
    ///
    /// TrackedDeviceRaycaster 求出命中点后还会做一次投影往返（TrackedDeviceRaycaster.cs:188）：
    ///     Vector2 screenPos = eventCamera.WorldToScreenPoint(worldPos);
    ///     if (graphic.Raycast(screenPos, eventCamera)) ...
    /// 其中 eventCamera 就是 canvas.worldCamera。左手面板的 tab 全在 ScrollRect 的
    /// Viewport 下，而 Viewport 上挂着 Mask —— Mask 实现了 ICanvasRaycastFilter，
    /// 会把 screenPos 再投回世界判断是否落在 Viewport 矩形内（Mask.cs:141
    /// RectTransformUtility.RectangleContainsScreenPoint）。这一来一回，只有当
    /// worldPos 位于该相机【前方】时才自洽。
    ///
    /// 而 canvas.worldCamera 是 BNG 设的：VRCanvas.Start 调 VRUISystem.AddCanvas，
    /// 把 Canvas 指向 VRUISystem 自建的 "CameraCaster"（FOV 仅 5 度）。本工程场景里
    /// 没有预置 VRUISystem，它是 VRCanvas 第一次 Start 时才被 AddComponent 出来的，
    /// 那一刻 RightPointerTransform 为 null，UpdateControllerHand 的判空保护直接跳过
    /// 挂接，相机于是停在世界原点朝 +Z 一动不动。结果左手面板上只有恰好落在这台相机
    /// 前方的那一片能通过 Mask 过滤，其余整片点不中，且边界随人走动而漂移。
    ///
    /// 沙盘那 5 个大类按钮的父链上没有任何 Mask（已核对场景层级），不走这道过滤，
    /// 所以不受影响 —— 这正是"沙盘能全命中、左手面板 tab 只有中下部能命中"的原因。
    ///
    /// 修法是补上 BNG 原本就要接、却没接上的那个引用，并且用 aim 姿态：命中点因此
    /// 恒定落在相机光轴上，投影往返自洽。
    ///
    /// 注意 VRUISystem.AddCanvasToCamera 外面套着 #if !UNITY_EDITOR，
    /// 所以这个问题只在打包后出现，Editor 里复现不了。
    /// </summary>
    private void AttachEventCameraToAimPointer()
    {
        if (aimPointer == null)
        {
            return;
        }

        // 相机组件是 enabled = false 的，必须带 includeInactive 才找得到
        Camera[] cameras = FindObjectsOfType<Camera>(true);
        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i].name != CasterName)
            {
                continue;
            }

            Transform caster = cameras[i].transform;
            Transform before = caster.parent;

            caster.SetParent(aimPointer, false);
            caster.localPosition = Vector3.zero;
            caster.localRotation = Quaternion.identity;
            casterAttached = true;

            Debug.Log($"[TrackedUI] BNG {CasterName} 已挂到 aim 指针 '{aimPointer.name}'" +
                $"（原父级={(before != null ? before.name : "<无，停在世界原点>")}）：" +
                "Viewport 的 Mask 要靠 worldCamera 做屏幕点回投，相机不跟手时面板会整片点不中");
            return;
        }

        // 找不到就会每 2 秒空转一次，至少要让日志里看得见
        casterAttempts++;
        if (casterAttempts == CasterWarnAfterAttempts)
        {
            Debug.LogError($"[TrackedUI] 找不到名为 '{CasterName}' 的相机，Mask 命中过滤未修正。" +
                "若 BNG 改了该对象名，需同步改 CasterName");
        }
    }

    /// <summary>
    /// 放宽 tracked 指针的拖拽阈值，避免手抖把点击判成拖拽。
    ///
    /// InputSystemUIInputModule.ProcessPointerButtonDrag 一旦判定进入拖拽，且
    /// pointerPress != pointerDrag，就会执行：
    ///     eventData.eligibleForClick = false;
    ///     eventData.pointerPress = null;
    /// 点击被彻底作废。左手面板的列表项都在 ScrollRect 内，pointerDrag 会向上找到
    /// 那个 ScrollRect，必然不等于按钮本身，所以只要抖动越过阈值，点击就没了、
    /// 列表还会跟着滚。默认容差仅 14.1 像素，0.26 米处的小控件上太容易越界。
    /// </summary>
    private void ApplyDragThreshold()
    {
        InputSystemUIInputModule module = FindObjectOfType<InputSystemUIInputModule>();
        if (module == null)
        {
            return;
        }

        float before = module.trackedDeviceDragThresholdMultiplier;
        module.trackedDeviceDragThresholdMultiplier = DragThresholdMultiplier;
        dragThresholdApplied = true;

        EventSystem es = EventSystem.current;
        float px = es != null ? es.pixelDragThreshold * Mathf.Sqrt(DragThresholdMultiplier) : -1f;
        Debug.Log($"[TrackedUI] 拖拽阈值倍率 {before:F1} -> {DragThresholdMultiplier:F1}" +
            $"（容差约 {px:F1} 像素，原约 {(es != null ? es.pixelDragThreshold * Mathf.Sqrt(before) : -1f):F1} 像素）");
    }

    /// <summary>
    /// tracked device 的位姿是追踪空间坐标。xrTrackingOrigin 留空时模块会把它当世界坐标用，
    /// 一旦玩家被移动（传送 / locomotion）射线就会整体偏掉。
    /// </summary>
    private void AssignTrackingOrigin()
    {
        InputSystemUIInputModule module = FindObjectOfType<InputSystemUIInputModule>();
        if (module == null)
        {
            return;
        }

        if (module.xrTrackingOrigin != null)
        {
            trackingOriginAssigned = true;
            return;
        }

        Transform origin = FindTrackingSpace();
        if (origin == null)
        {
            return;
        }

        module.xrTrackingOrigin = origin;
        trackingOriginAssigned = true;
        Debug.Log($"[TrackedUI] xrTrackingOrigin 已设为 '{origin.name}'");
    }

    /// <summary>
    /// 把 action 里匹配 <paramref name="gripFragment"/> 的绑定覆盖成 <paramref name="aimPath"/>。
    /// 覆盖前先 Disable，之后恢复原启用状态。
    /// </summary>
    private static bool OverrideBindingPath(InputActionReference reference, string gripFragment, string aimPath)
    {
        InputAction action = reference != null ? reference.action : null;
        if (action == null)
        {
            return false;
        }

        bool wasEnabled = action.enabled;
        if (wasEnabled)
        {
            action.Disable();
        }

        int changed = 0;
        for (int i = 0; i < action.bindings.Count; i++)
        {
            string path = action.bindings[i].path;
            if (!string.IsNullOrEmpty(path) && path.Contains(gripFragment))
            {
                action.ApplyBindingOverride(i, aimPath);
                changed++;
            }
        }

        if (wasEnabled)
        {
            action.Enable();
        }

        return changed > 0;
    }

    private static Transform FindTrackingSpace()
    {
        // 带 includeInactive：AimPointerBeam 接管光束后会把 UIPointer 组件禁用，
        // 而 FindObjectsOfType 默认把"被禁用的组件"也当 inactive 排除掉
        BNG.UIPointer[] pointers = FindObjectsOfType<BNG.UIPointer>(true);
        for (int i = 0; i < pointers.Length; i++)
        {
            Transform t = pointers[i].transform;
            while (t != null)
            {
                if (t.name == "TrackingSpace")
                {
                    return t;
                }

                t = t.parent;
            }
        }

        return null;
    }

    private void Rescan()
    {
        Canvas[] canvases = FindObjectsOfType<Canvas>();
        int added = 0;
        int skipped = 0;
        int notRoot = 0;
        int preexisting = 0;

        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];

            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay || canvas.rootCanvas != canvas)
            {
                // 这条分支原来是静默的，但它一次能吞掉上百个 Canvas（嵌套 Canvas 只有
                // 根 Canvas 能挂 raycaster）。不记数的话，"沙盘上的标记点不中"会查不出来。
                // 这类 Canvas 进不了 processed，所以单独记一份，否则每轮都会重复计数刷屏
                if (nonRoot.Add(canvas))
                {
                    notRoot++;
                }

                continue;
            }

            if (processed.Contains(canvas))
            {
                continue;
            }

            // 预制体里自带 raycaster 的（沙盘标记那批）走这条。它们用的是 Unity 默认参数
            // （maxDistance 1000 米），和这里统一收紧过的不一样，数量大时值得知道
            if (canvas.GetComponent<TrackedDeviceRaycaster>() != null)
            {
                processed.Add(canvas);
                preexisting++;
                continue;
            }

            // 只给"有可交互控件"的 Canvas 挂 raycaster。
            //
            // InputSystemUIInputModule.PerformRaycast 对 tracked 指针是遍历
            // TrackedDeviceRaycaster.s_Instances、取【第一个产生命中的】直接返回，
            // 跨实例之间不比较距离。实例越多，射线穿过两个 Canvas 时选错的概率越大
            // （表现为点到旁边/上面的元素）。摄像头图标有 499 个独立 Canvas，默认
            // 一个 Selectable 都没有，全挂上去只会放大这个问题。
            //
            // 注意这里【不能】把 canvas 记进 processed：CameraObject.EnableClick() 会在
            // 运行时给"有告警"的那几十个摄像头补 Button，补完必须还能被本轮补扫捞到。
            // 原先 processed.Add 在这个判断之前，Canvas 一旦被跳过就永久退休，
            // 后加的 Button 永远拿不到 raycaster。
            if (canvas.GetComponentInChildren<Selectable>(true) == null)
            {
                if (noSelectable.Add(canvas))
                {
                    skipped++;
                }

                continue;
            }

            processed.Add(canvas);
            noSelectable.Remove(canvas);

            TrackedDeviceRaycaster raycaster = canvas.gameObject.AddComponent<TrackedDeviceRaycaster>();

            // 不做物理遮挡剔除：摄像头图标那类标记本身没有 Collider，
            // 而园区地形 / 建筑的 Collider 会把它们整片挡掉。
            raycaster.checkFor3DOcclusion = false;
            raycaster.checkFor2DOcclusion = false;
            raycaster.ignoreReversedGraphics = true;

            // 默认 1000 米。收紧上限，避免远处沙盘标记先于近处面板命中。
            raycaster.maxDistance = 30f;

            added++;
        }

        if (added > 0 || skipped > 0 || notRoot > 0 || preexisting > 0)
        {
            Debug.Log($"[TrackedUI] 本轮新增 raycaster {added} 个，跳过（无 Selectable）{skipped} 个" +
                $"，跳过（非根 Canvas / Overlay）{notRoot} 个，跳过（已自带 raycaster）{preexisting} 个" +
                $"，累计扫描 {processed.Count} 个 Canvas");
        }
    }
}
