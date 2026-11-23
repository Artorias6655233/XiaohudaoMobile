using UnityEngine;
using BNG;
using TMPro;
using UnityEngine.UI;

public class AnchorLocationUIManager : MonoBehaviour
{
    public enum AButtonStatus
    {
        None,
        Down,
        Up,
    }

    private InputBridge inputBridge;

    public Transform anchorLocationReferencePoint;

    public Transform anchorLocation;

    private AButtonStatus aButtonStatus;

    private float aButtonDownAccelerateTime;

    private const float aButtonDownMaxTime = 2f;

    public TMP_Text aButtonDownTipsText;

    public TMP_Text aButtonDownPrecentageText;

    public RectTransform aButtonDownPrecentageImage;

    protected (Vector3, Quaternion) restorePose;
    protected Transform restoreParent;
    protected Tumbler tumbler => GetComponent<Tumbler>();

    // Start is called before the first frame update
    protected virtual void Start()
    {
        aButtonStatus = AButtonStatus.Up;
        aButtonDownTipsText.text = $"Tips: Press and hold the A \nbutton for at least {aButtonDownMaxTime} seconds.";
        inputBridge = InputBridge.Instance;
    }

    protected virtual void Update()
    {
        if ((inputBridge && inputBridge.AButtonDown) || Input.GetKeyDown(KeyCode.P))
        {
            SetAButtonStatus(AButtonStatus.Down);
            RecordPose();
        }

        if ((inputBridge && inputBridge.AButtonUp) || Input.GetKeyUp(KeyCode.P))
        {
            SetAButtonStatus(AButtonStatus.Up);
            if (aButtonDownPrecentageText.text.Equals("Place completed!") == false)
                aButtonDownPrecentageText.text = "User canceled.";
            UpdatePlacementProgress(0);
            RestoreLockPose();
        }

        if (aButtonStatus.Equals(AButtonStatus.Down))
        {
            aButtonDownAccelerateTime += Time.deltaTime;
            aButtonDownAccelerateTime = Mathf.Clamp(aButtonDownAccelerateTime, 0, aButtonDownMaxTime);
            aButtonDownPrecentageText.text = aButtonDownAccelerateTime.ToString("F1") + $" / {aButtonDownMaxTime} s";
            UpdatePlacementProgress(aButtonDownAccelerateTime / aButtonDownMaxTime);
            if (aButtonDownAccelerateTime >= aButtonDownMaxTime)
            {
                aButtonDownAccelerateTime = aButtonDownMaxTime;
                SetAButtonStatus(AButtonStatus.None);
                aButtonDownPrecentageText.text = "Place completed!";
                OnAButtonDownEnoughTime();
                RestoreLockPose();
            }
        }
        else
        {
            aButtonDownAccelerateTime = 0;
        }
    }

    private void RecordPose()
    {
        restorePose = (transform.localPosition, transform.localRotation);
        restoreParent = transform.parent;
        transform.SetParent(null);
        if (tumbler) tumbler.enabled = false;
    }

    private void RestoreLockPose()
    {
        transform.SetParent(restoreParent);
        (transform.localPosition, transform.localRotation) = restorePose;
        if (tumbler) tumbler.enabled = true;
    }

    private void SetAButtonStatus(AButtonStatus aButtonStatus)
    {
        this.aButtonStatus = aButtonStatus;
    }

    private void OnAButtonDownEnoughTime()
    {
        Debug.Log($">>> OnAButtonDownEnoughTime");
        anchorLocation.SetPositionAndRotation(anchorLocationReferencePoint.position, anchorLocationReferencePoint.rotation);
    }

    private void UpdatePlacementProgress(float progress)
    {
        float width = aButtonDownPrecentageImage.sizeDelta.x;
        float _widthNormal = Mathf.Clamp(progress, 0, 1);
        RectTransform progressBar = aButtonDownPrecentageImage.GetChild(0).GetComponent<RectTransform>();
        progressBar.sizeDelta = new Vector2(_widthNormal * width, progressBar.sizeDelta.y);
        // Gradient color: white -> green.
        progressBar.GetComponent<Image>().color = new Color(1 - progress, 1, 1 - progress, 1);
    }
}
