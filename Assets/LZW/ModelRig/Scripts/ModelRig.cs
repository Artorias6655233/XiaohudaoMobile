using UnityEngine;
using BNG;
using UnityEngine.Events;
using Mirror;

public class ModelRig : MonoBehaviour
{
    public static ModelRig Ins;

    private InputBridge inputBridge;

    public HandleObjectMode handleObjectMode = HandleObjectMode.None;

    public Transform _selectedAnchor;

    public Camera m_Camera;

    public Transform trackingSpace;

    public Transform leftControllerAnchor;

    public Transform rightControllerAnchor;

    private bool doubleGripStatus;

    public UnityAction<bool> onDoubleGrip;

    private bool isOperating;
    public bool IsOperating { get => isOperating; set { isOperating = value; } }

    private float minScale = 0.04f;

    private float maxScale = 4f;

    public bool leftControllerGripActiveOnEditor;

    public bool rightControllerGripActiveOnEditor;

    protected virtual void Awake()
    {
        Ins = this;
    }

    // Start is called before the first frame update
    protected virtual void Start()
    {
        inputBridge = InputBridge.Instance;
        onDoubleGrip += OnDoubleGrip;
    }

    // Update is called once per frame
    protected virtual void Update()
    {
        if (_selectedAnchor == null)
            return;

        OnHandleObjectAction();

        if (Input.GetKeyDown(KeyCode.C)) {
            OnDoubleGrip(false);
        }
    }

    public void SetOperatingState(bool value)
    {
        IsOperating = value;
    }

    public void SetHandleMode(HandleObjectMode handleObjectMode)
    {
        this.handleObjectMode = handleObjectMode;
    }

    public void ClearHandleMode()
    {
        handleObjectMode = HandleObjectMode.None;
    }

    public void OnToggleZoomByHands()
    {
        ToggleZoomByHands(handleObjectMode != HandleObjectMode.ZoomByHands);
    }

    public void ToggleZoomByHands(bool value)
    {
        SetHandleMode(value ? HandleObjectMode.ZoomByHands : HandleObjectMode.None);
    }

    private bool triggerBoth = false;
    private bool triggerBothBegin = false;
    private Quaternion _selectedAnchorBeginRot;
    private Vector3 _selectedAnchorBeginPos;
    private Vector3 _selectedAnchorBeginScale;
    private Vector3 fromDirection;
    private Vector3 centerPos;
    private Vector3 ctrlLeftPos = Vector3.zero;
    private Vector3 ctrlRightPos = Vector3.zero;
    // TODO: record camera position.
    private Vector3 cameraBeginPos;
    // NEW
    private Vector3 centerPosDelta;
    private Vector3 selectedAnchorPosDelta;

    private bool _controlledByHandNew = false;

    public void ZoomByHands()
    {
        (bool triggerLeft, bool triggerRight) = GetControllerGrip();

        triggerBoth = triggerLeft && triggerRight;

        // Called when doubleGripStatus changed.
        if (triggerBoth != doubleGripStatus)
        {
            doubleGripStatus = triggerBoth;
            onDoubleGrip?.Invoke(triggerBoth);
        }

        // TODO: 
        SetOperatingState(triggerBoth);
        if (!triggerBoth)
        {
            triggerBothBegin = false;
            return;
        }

        if (triggerBoth && !triggerBothBegin)
        {
            triggerBothBegin = true;
            _selectedAnchorBeginRot = _selectedAnchor.rotation;
            _selectedAnchorBeginPos = _selectedAnchor.position;
            _selectedAnchorBeginScale = _selectedAnchor.localScale;

            // Record left / right hands begging position.
            ctrlLeftPos = GetControllerLocalPosition(ControllerHand.Left);
            ctrlLeftPos = trackingSpace.TransformPoint(ctrlLeftPos);
            ctrlRightPos = GetControllerLocalPosition(ControllerHand.Right);
            ctrlRightPos = trackingSpace.TransformPoint(ctrlRightPos);
            fromDirection = ctrlRightPos - ctrlLeftPos;
            centerPos = (ctrlRightPos + ctrlLeftPos) / 2;
            // TODO: record camera pos
            cameraBeginPos = m_Camera.transform.position;
            // NEW
            if (_controlledByHandNew)
            {
                centerPosDelta = centerPos - cameraBeginPos;
                selectedAnchorPosDelta = _selectedAnchorBeginPos - cameraBeginPos;
            }
            // TODO: Vibration
            inputBridge.VibrateController(1, 1, 0.25f, ControllerHand.Left);
            inputBridge.VibrateController(1, 1, 0.25f, ControllerHand.Right);
        }

        // NEW
        if (_controlledByHandNew)
        {
            cameraBeginPos = m_Camera.transform.position;
            centerPos = cameraBeginPos + centerPosDelta;
            _selectedAnchorBeginPos = cameraBeginPos + selectedAnchorPosDelta;
        }

        // Left / right controller pos realtime.
        Vector3 ctrlLeftPosRT = GetControllerLocalPosition(ControllerHand.Left);
        ctrlLeftPosRT = trackingSpace.TransformPoint(ctrlLeftPosRT);
        Vector3 ctrlRightPosRT = GetControllerLocalPosition(ControllerHand.Right);
        ctrlRightPosRT = trackingSpace.TransformPoint(ctrlRightPosRT);
        Vector3 toDirection = ctrlRightPosRT - ctrlLeftPosRT;

        // Calculate signed angle use axis-y
        //float signedAngle = Vector3.SignedAngle(fromDirection, toDirection, Vector3.up);
        Quaternion qua = Quaternion.FromToRotation(fromDirection.X0Z(), toDirection.X0Z());
        _selectedAnchor.rotation = _selectedAnchorBeginRot * qua;

        // Move 
        // TODO: Move Y
        var centerPos2 = (ctrlLeftPosRT + ctrlRightPosRT) / 2;
        var deltaPos = centerPos2 - centerPos;
        Vector3 modelTargetPos = _selectedAnchorBeginPos + deltaPos;
        if (_controlledByHandNew)
        {
            // TODO: Calc XZ
            CalcHandleModelTargetPos(cameraBeginPos.XZ(), centerPos.XZ(),
            centerPos2.XZ(), _selectedAnchorBeginPos.XZ(), out Vector2 _selectedAnchorPos);
            modelTargetPos = new Vector3(_selectedAnchorPos.x,
                (_selectedAnchorBeginPos + deltaPos).y, _selectedAnchorPos.y);
        }
        _selectedAnchor.position = modelTargetPos;

        float scale = Vector3.SqrMagnitude(toDirection) - Vector3.SqrMagnitude(fromDirection);
        Vector3 targetValue = _selectedAnchorBeginScale + Vector3.one * scale;
        if (targetValue.x <= minScale)
            targetValue = Vector3.one * minScale;
        if (targetValue.x >= maxScale)
            targetValue = Vector3.one * maxScale;
        _selectedAnchor.localScale = targetValue;
    }

    public void SetControlledByHandNew(bool value)
    {
        _controlledByHandNew = value;
    }

    public void ToggleControlledByHandNew()
    {
        SetControlledByHandNew(!_controlledByHandNew);
    }

    public void CalcHandleModelTargetPos(Vector2 cameraPos, Vector2 centerPos1, Vector2 centerPos2, Vector2 modelPos1, out Vector2 modelPos2)
    {
        // Calc directions
        Vector2 direction1 = centerPos1 - cameraPos;
        Vector2 direction2 = centerPos2 - cameraPos;
        Vector2 modelDir = modelPos1 - cameraPos;

        // Calc the modulus value of vec1(centerPos1->cameraPos) and vec2(centerPos2->cameraPos)
        float modValue1 = Vector2.Distance(centerPos1, cameraPos);
        float modValue2 = Vector2.Distance(centerPos2, cameraPos);

        // Calc the modulus value of vec(modelPos1->cameraPos)
        float modelModValue = Vector2.Distance(modelPos1, cameraPos);

        // Calc the delta distance from modValue1 to modValue2
        float modDelta = modValue2 - modValue1;
        // Calc offset
        Vector2 deltaOffset = direction2 * (modDelta / modValue2);

        // Calc singed angle from direction1 to direction2
        float signedAngle = Vector2.SignedAngle(direction1, direction2);

        // Calc target the model want to rotate
        float modelTargetRotation = (Vector2.SignedAngle(new Vector2(1, 0), modelDir.normalized) + signedAngle) * Mathf.Deg2Rad;

        // Calc target the model want to move
        var modelTargetPos = cameraPos + new Vector2(Mathf.Cos(modelTargetRotation), Mathf.Sin(modelTargetRotation)) * modelModValue + deltaOffset;

        modelPos2 = modelTargetPos;
    }

    public void OnHandleObjectAction()
    {
        switch (handleObjectMode)
        {
            case HandleObjectMode.ZoomByHands:
                ZoomByHands();
                break;
        }
    }

    private Vector3 GetControllerLocalPosition(ControllerHand controllerHand)
    {
        Vector3 localPos = inputBridge.GetControllerLocalPosition(controllerHand);

        if (Application.isEditor && !controllerHand.Equals(ControllerHand.None))
        {
            localPos = controllerHand == ControllerHand.Left ? leftControllerAnchor.localPosition : rightControllerAnchor.localPosition;
        }

        return localPos;
    }

    private (bool, bool) GetControllerGrip()
    {
        float threshold = .7f;
        if (Application.isEditor)
        {
            return (leftControllerGripActiveOnEditor || inputBridge.LeftGrip > threshold, 
                rightControllerGripActiveOnEditor || inputBridge.RightGrip > threshold);
        }
        return (inputBridge.LeftGrip > threshold, inputBridge.RightGrip > threshold);
    }

    private void OnDoubleGrip(bool status)
    {
        MirrorSyncManager mirrorSyncManager = (NetworkManager.singleton as MirrorSyncManager);
        if (mirrorSyncManager == null)
        {
            Debug.LogWarning($"Pleayse check network manager firstly.");
            return;
        }

        if (status)
        {
            mirrorSyncManager.RequestAuthorityForce();
            return;
        }

        // Delay removing the authority.
        Invoke(nameof(RemoveAuthority), 0.1f);
    }

    private void RemoveAuthority() => (NetworkManager.singleton as MirrorSyncManager).RemoveAuthority();

    public enum HandleObjectMode
    {
        None,
        ZoomByHands,
    }
}
