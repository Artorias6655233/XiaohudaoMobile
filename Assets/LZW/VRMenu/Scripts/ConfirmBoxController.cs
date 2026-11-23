using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class ConfirmBoxController : MonoBehaviour
{
    public static ConfirmBoxController Ins;

    private void Awake()
    {
        Ins = this;
    }

    public UnityAction onBoxOpen;
    public UnityAction onBoxClose;
    public UnityAction onClickYes;
    public UnityAction onClickNo;

    public GameObject box;
    public TMP_Text messageText;
    public Button yesBtn;
    public Button noBtn;

    public bool boxStatus;

    public KeyCode keyToggleBox = KeyCode.Alpha1;
    public KeyCode keyClickYes = KeyCode.Alpha2;
    public KeyCode keyClickNo = KeyCode.Alpha3;

    void Start()
    {
        SetBoxStatus(boxStatus);
        EventInit();
    }

    private void EventInit()
    {
        onBoxOpen += OnBoxOpen;
        onBoxClose += OnBoxClose;
        onClickYes += OnClickYes;
        onClickNo += OnClickNo;
        yesBtn.onClick.AddListener(onClickYes);
        noBtn.onClick.AddListener(onClickNo);
    }

    private void OnBoxOpen()
    {
        Debug.Log($"OnBoxOpen");
    }

    private void OnBoxClose()
    {
        Debug.Log($"OnBoxClose");
    }

    private void OnClickYes()
    {
        AlertTips($"点击了确认");
        CloseBox();
        Debug.Log($"OnClickYes");
    }

    private void OnClickNo()
    {
        AlertTips($"点击了取消");
        CloseBox();
        Debug.Log($"OnClickNo");
    }

    public void OpenBox(string message =  "")
    {
        if (!string.IsNullOrEmpty(message))
            SetMessage(message);
        SetBoxStatus(true);
        onBoxOpen?.Invoke();
    }

    public void CloseBox()
    {
        SetBoxStatus(false);
        onBoxClose?.Invoke();
    }

    public void SetBoxStatus(bool status)
    {
        boxStatus = status;
        box.gameObject.SetActive(boxStatus);
    }

    public void ToggleBox()
    {
        box.gameObject.SetActive(!box.gameObject.activeSelf);
        boxStatus = box.gameObject.activeSelf;
    }

    public void SetMessage(string message)
    {
        messageText.text = message;
    }

    private void Update()
    {
#if UNITY_EDITOR
        if (Input.GetKeyDown(keyToggleBox)) 
        {
            ToggleBox();
        }
        if (Input.GetKeyDown(keyClickYes))
        {
            yesBtn.onClick?.Invoke();
        }
        if (Input.GetKeyDown(keyClickNo))
        {
            noBtn.onClick?.Invoke();
        }
#endif
    }

    private void AlertTips(string message)
    {
        var mbc = MessageBoxAlertController.Ins;
        if (mbc == null)
            return;
        mbc.CreateMsgBoxItem(message);
    }
}
