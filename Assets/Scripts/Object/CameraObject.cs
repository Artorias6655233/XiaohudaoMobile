using UnityEngine;
using UnityEngine.UI;
using TMPro;

public enum CameraState
{
    Normal,
    Alarm
}

[System.Serializable]
public class CameraInfo
{
    public string id;
    public string name;
    public string location;
    public string ip;
    public string serialNumber;
    public double latitude;
    public double longitude;
    public string type;
}

public class CameraObject : MonoBehaviour
{
    public string cameraID;
    public CameraInfo cameraDetails;

    public Image normalStateImage;
    public Image alarmStateImage;

    /// <summary>选中时图标相对原始大小放大的倍数。只作用在图标子物体上，不动根节点。</summary>
    private const float SelectedScaleMultiplier = 1.4f;

    /// <summary>选中时的图标颜色。</summary>
    private static readonly Color SelectedColor = new Color(1f, 0.85f, 0.2f, 1f);

    private CameraState currentState = CameraState.Normal;
    private bool subscribed;

    /// <summary>EnableClick 是否已经跑过，用于幂等（两个图标各有一个 Button，没法用单个引用判）。</summary>
    private bool clickEnabled;

    /// <summary>当前选中态，与 <see cref="selectionApplied"/> 一起用于跳过无变化的重绘。</summary>
    private bool selectedNow;
    private bool selectionApplied;

    /// <summary>图标在场景里原本的颜色 / 缩放，取消选中时还原用。见 CaptureBaseVisuals。</summary>
    private Color normalBaseColor = Color.white;
    private Color alarmBaseColor = Color.white;
    private Vector3 normalBaseScale = Vector3.one;
    private Vector3 alarmBaseScale = Vector3.one;
    private bool baseVisualsCaptured;

    void Start()
    {
        // 摄像头模板上本来就没有 Button，所以这里通常拿到 null；真正让标记可点是
        // EnableClick() 干的（只对有告警的摄像头调用）。这一步保留是为了兼容
        // "以后有人在模板里补了 Button" 的情况。
        BindClick(GetComponentInChildren<Button>());

        Subscribe();
        UpdateStateImages();
        UpdateSelectionAppearance();
    }

    void OnDestroy()
    {
        if (subscribed && Session.Instance != null)
        {
            Session.Instance.OnCameraIDChanged -= UpdateSelectionAppearance;
            subscribed = false;
        }
    }

    /// <summary>
    /// 让本标记能被手柄射线点中。幂等，只对"有告警"的摄像头调用。
    ///
    /// 为什么不直接在模板里加 Button：TrackedUiBootstrap 只给"子级有 Selectable"的
    /// Canvas 挂 TrackedDeviceRaycaster，而 InputSystemUIInputModule.PerformRaycast 对
    /// tracked 指针是遍历 s_Instances、取【第一个产生命中的】就返回，跨实例之间不比较
    /// 距离。摄像头有 499 个独立 Canvas，全挂上去会显著抬高"点近处面板却命中远处图标"
    /// 的概率。只给命中告警的那几十个加 Button，raycaster 数量就压回可接受范围。
    /// </summary>
    public void EnableClick()
    {
        if (clickEnabled)
        {
            return;
        }

        // 两个图标都要挂：UpdateStateImages 只会激活当前状态那一个，
        // 只挂 normal 的话，一旦切成 Alarm（也就是高亮那一刻）Button 就随物体一起失活，
        // 结果"高亮了但点不动"。
        bool any = BindClickTo(normalStateImage) | BindClickTo(alarmStateImage);

        if (!any)
        {
            Debug.LogWarning($"[CameraObject] {cameraID} 没有可用的图标 Image，无法开启点击");
            return;
        }

        clickEnabled = true;
    }

    private bool BindClickTo(Image icon)
    {
        if (icon == null)
        {
            return false;
        }

        // 图形必须参与射线命中，否则 TrackedDeviceRaycaster 的 graphic.Raycast 过不了
        icon.raycastTarget = true;

        Button target = icon.gameObject.GetComponent<Button>();
        if (target == null)
        {
            target = icon.gameObject.AddComponent<Button>();
        }

        target.targetGraphic = icon;
        // 图标的选中表现由 SetSelected 统一管，别让 Button 自己的 tint 再插一脚
        target.transition = Selectable.Transition.None;

        BindClick(target);
        return true;
    }

    public void SetState(CameraState state)
    {
        currentState = state;
        UpdateStateImages();
        UpdateSelectionAppearance();
    }

    public CameraState GetState()
    {
        return currentState;
    }

    private void BindClick(Button target)
    {
        if (target == null)
        {
            return;
        }

        // 先移再加：EnableClick 幂等之外，Start() 也可能已经绑过同一个 Button
        target.onClick.RemoveListener(OnClicked);
        target.onClick.AddListener(OnClicked);
    }

    private void OnClicked()
    {
        Debug.Log("Camera clicked: " + cameraID);

        // 点已选中的就取消选择，和企业 / 储罐标记的行为保持一致
        if (Session.Instance.cameraID == cameraID)
        {
            Session.Instance.cameraID = string.Empty;
        }
        else
        {
            Session.Instance.cameraID = cameraID;
        }
    }

    private void Subscribe()
    {
        if (subscribed || Session.Instance == null)
        {
            return;
        }

        Session.Instance.OnCameraIDChanged += UpdateSelectionAppearance;
        subscribed = true;
    }

    /// <summary>
    /// 选中态表现：只改图标的颜色与缩放。
    ///
    /// 不能缩放根节点 —— 根节点上挂着 Canvas / Billboard / CesiumGlobeAnchor，
    /// 缩放它等于缩放整个世界空间 Canvas。也不能挪用 CameraState：Alarm 是"有告警"，
    /// 和"被选中"是两件事，两者可以同时成立。
    /// </summary>
    private void UpdateSelectionAppearance()
    {
        bool selected = Session.Instance != null
            && !string.IsNullOrEmpty(cameraID)
            && Session.Instance.cameraID == cameraID;

        SetSelected(selected);
    }

    public void SetSelected(bool selected)
    {
        // 必须在任何一次落笔之前抓一次基准值：CreateCameraInstance 里的
        // SetState(Normal) 比 Start() 先跑，所以不能只在 Start 里抓。
        CaptureBaseVisuals();

        // 499 个摄像头全都订阅了 OnCameraIDChanged，所以每点一次都会走一遍这里。
        // 写 Image.color / localScale 会把所在 Canvas 标脏 —— 无脑写一遍就等于
        // 一次点击触发 499 次 Canvas 重建。值没变就不落笔，只有真正换选中的
        // 那两个（旧的、新的）会脏。
        if (selectionApplied && selectedNow == selected)
        {
            return;
        }

        selectedNow = selected;
        selectionApplied = true;

        // 两个图标都刷：UpdateStateImages 之后被激活的那个也得是对的颜色/大小
        ApplySelection(normalStateImage, normalBaseColor, normalBaseScale, selected);
        ApplySelection(alarmStateImage, alarmBaseColor, alarmBaseScale, selected);
    }

    /// <summary>
    /// 记下图标在场景里原本的颜色与缩放，取消选中时要还原成它们。
    ///
    /// 不能写死成白色 / Vector3.one —— 美术给图标上的底色和尺寸就会被抹掉。
    /// </summary>
    private void CaptureBaseVisuals()
    {
        if (baseVisualsCaptured)
        {
            return;
        }

        if (normalStateImage != null)
        {
            normalBaseColor = normalStateImage.color;
            normalBaseScale = normalStateImage.rectTransform.localScale;
        }

        if (alarmStateImage != null)
        {
            alarmBaseColor = alarmStateImage.color;
            alarmBaseScale = alarmStateImage.rectTransform.localScale;
        }

        baseVisualsCaptured = true;
    }

    private static void ApplySelection(Image icon, Color baseColor, Vector3 baseScale, bool selected)
    {
        if (icon == null)
        {
            return;
        }

        icon.color = selected ? SelectedColor : baseColor;
        icon.rectTransform.localScale = selected
            ? baseScale * SelectedScaleMultiplier
            : baseScale;
    }

    private void UpdateStateImages()
    {
        if (normalStateImage != null) normalStateImage.gameObject.SetActive(false);
        if (alarmStateImage != null) alarmStateImage.gameObject.SetActive(false);

        switch (currentState)
        {
            case CameraState.Normal:
                if (normalStateImage != null) normalStateImage.gameObject.SetActive(true);
                break;
            case CameraState.Alarm:
                if (alarmStateImage != null) alarmStateImage.gameObject.SetActive(true);
                break;
        }
    }
}
