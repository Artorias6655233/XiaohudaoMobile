using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using DG.Tweening;
using System;
using UnityEngine.EventSystems;

public class UIManager : MonoBehaviour
{

    public static UIManager Ins;
    private void Awake()
    {
        Ins = this;
    }

    [HideInInspector]
    private const float animation_duration = 0.3f;

    public UIObjectManager _UOM;

    private void VariableInit()
    {
        _UOM = GetComponent<UIObjectManager>();
        _SceneTabIndex = -1;
        _ModeIndex = -1;
    }

    private void EventInit()
    {
        //
        OnSettingDisplayEvent();

        // 
        OnSettingSwitchEvent();

        // 
        OnModeSwitchEvent();

        //
        OnSceneTabSwitchEvent();

        //
        OnGuideSwitchEvent();

        //
        OnComponentSwitchEvent();

        // 
        OnMultipleToggleEvent();

        // 
        _UOM._SettingButton.onClick.AddListener(() => {
            onSettingDisplay?.Invoke();
        });

        // _ToggleMultiple
        _UOM._MultipleToggle.onValueChanged.AddListener(value => {
            onMultipleToggle?.Invoke(value);
        });

    }

    public UnityAction onSettingDisplay;

    public void OnSettingDisplayEvent()
    {
        onSettingDisplay += OnSettingDisplayToggle;
    }

    public void OnSettingDisplayToggle()
    {
        bool active = !_UOM._SettingSelectedBox.activeSelf;
        OnSettingDisplay(active);
    }

    public void OnSettingDisplay(bool active)
    {
        _UOM._SettingSelectedBox.SetActive(active);
        _UOM._SettingButton.GetComponent<Image>().DOFade(active ? 0 : 1, 0);
        Debug.LogWarning($"setting display {active}");
    }

    public UnityAction<bool> onMultipleToggle;
    [HideInInspector]
    public bool _isMultiple => _UOM._MultipleToggle.isOn;

    public void OnMultipleToggleEvent()
    {
        onMultipleToggle += OnMultipleToggle;
    }

    public void OnMultipleToggle(bool _checked)
    {
        Debug.LogWarning($"OnMultipleToggle {_checked}");
    }

    public UnityAction<int> onSettingSelect;
    public UnityAction<int> onSettingToggle;
    public UnityAction<int, int> onSettingOptionSelect;
    public UnityAction<int, int, float> onSettingOptionSlide;
    public int settingSelectIndex;
    public float settingOptionSlideValue;
    public void OnSettingSwitchEvent()
    {
        //
        onSettingSelect += OnSettingSelect;
        onSettingToggle += OnSettingToggle;
        onSettingOptionSelect += OnSettingOptionSelect;
        onSettingOptionSlide += OnSettingOptionSlide;
        onSettingSelect?.Invoke(0);
        onSettingOptionSelect?.Invoke(0, 0);
        onSettingOptionSlide?.Invoke(0, 0, 5);

        var boxButtons = _UOM.GetSelectedBoxItems();
        var boxButtonSprite = _UOM._ItemSelectedSprite;
        for (int i = 0; i < boxButtons.Length; i++)
        {
            int _i = i;
            var button = boxButtons[i];
            var option = button.transform.GetChild(1).gameObject;
            var optionBtns = option.GetComponentsInChildren<Button>();
            var sliders = option.GetComponentsInChildren<Slider>();
            for (int k = 0; k < sliders.Length; k++)
            {
                int _k = k;
                var slider = sliders[_k];
                slider.onValueChanged.AddListener(value => {
                    onSettingOptionSlide?.Invoke(_i, _k, value);
                }) ;
            }
            for (int k = 0; k < optionBtns.Length; k++)
            {
                int _k = k;
                var optionBtn = optionBtns[_k];
                optionBtn.onClick.AddListener(() => {
                    // 
                    onSettingOptionSelect?.Invoke(_i, _k);
                });
            }
            button.onClick.AddListener(() => {
                // 
                onSettingSelect?.Invoke(_i);
            });
        }

    }

    public void OnSettingSelect(int index)
    {
        var boxButtons = _UOM.GetSelectedBoxItems();
        var boxButtonSprite = _UOM._ItemSelectedSprite;
        for (int j = 0; j < boxButtons.Length; j++)
        {
            var _button = boxButtons[j];
            var image = _button.GetComponent<Image>();
            var isEqual = j == index;
            image.sprite = isEqual ? boxButtonSprite : null;
            var _option = _button.transform.GetChild(1).gameObject;
            _option.SetActive(isEqual);
        }
        settingSelectIndex = index;
        Debug.Log($"OnSettingSelect: {index}");
    }

    public void OnSettingToggle(int index)
    {
        int toggleIndex = settingSelectIndex == index ? -1 : index;
        OnSettingSelect(toggleIndex);
    }

    public void OnSettingOptionSelect(int settingIndex, int optionIndex)
    {

        var boxButtons = _UOM.GetSelectedBoxItems();
        var boxButtonSprite = _UOM._ItemSelectedSprite;

        var button = boxButtons[settingIndex];
        var option = button.transform.GetChild(1).gameObject;
        var optionBtns = option.GetComponentsInChildren<Button>();
        for (int j = 0; j < optionBtns.Length; j++)
        {
            var _button = optionBtns[j];
            var image = _button.GetComponent<Image>();
            var isEqual = j == optionIndex;
            image.sprite = isEqual ? boxButtonSprite : null;
        }
        Debug.Log($"settingIndex: {settingIndex} optionIndex: {optionIndex}");
    }

    public void OnSettingOptionSlide(int settingIndex, int sliderIndex, float slideValue)
    {
        settingOptionSlideValue = slideValue;
        Debug.Log($"settingIndex: {settingIndex} sliderIndex: {sliderIndex} slideValue: {slideValue}");
    }

    public UnityAction<int> onModeSwitch;
    public UnityAction<bool> onModeActive;
    [HideInInspector] public int _ModeIndex;
    public void OnModeSwitchEvent()
    {
        //
        onModeSwitch += OnModeSwitch;
        onModeActive += OnModeActive;
        //onModeSwitch?.Invoke(0);

        var modeButtons = _UOM._ModeButtons;
        for (int i = 0; i < modeButtons.Length; i++)
        {
            int _i = i;
            var button = modeButtons[_i];
            button.onClick.AddListener(() => {
                onModeSwitch?.Invoke(_i);
            });
        }
    }

    public void OnModeSwitch(int index)
    {
        var modeButtons = _UOM._ModeButtons;
        var modeBtnsSprite = _UOM._ModeBtnsSprite;
        for (int i = 0; i < modeButtons.Length; i++)
        {
            var image = modeButtons[i].GetComponent<Image>();
            image.overrideSprite = i == index ? modeBtnsSprite[i] : null;
        }
        // TODO: switch guide.
        onGuideSwitch?.Invoke(index);
        onGuideTabSwitch?.Invoke(index, 0);
        onModeActive?.Invoke(false);
        // active delay
        StartCoroutine(DelayCall(() => {
            onModeActive?.Invoke(true);
        }, 2));
        _ModeIndex = index;
        Debug.Log($"OnModeSwitch: {index}");
    }

    public void OnModeActive(bool value)
    {
        var modeButtons = _UOM._ModeButtons;
        for (int i = 0; i < modeButtons.Length; i++)
        {
            modeButtons[i].interactable = value;
        }
        Debug.Log($"OnModeActive: {value}");
    }

    //_TabComponent
    public UnityAction<int> onSceneTabSwitch;
    public UnityAction<int> onSceneEnter;
    public UnityAction<int> onSceneExit;
    public UnityAction<Image, int> onSceneTabEnter;
    public UnityAction<Image, int> onSceneTabExit;
    [HideInInspector] public int _SceneTabIndex; 
    public void OnSceneTabSwitchEvent()
    {
        //
        onSceneTabSwitch += OnSceneTabSwitch;
        onSceneEnter += OnSceneEnter;
        onSceneExit += OnSceneExit;
        onSceneTabEnter += OnSceneTabEnter;
        onSceneTabExit += OnSceneTabExit;
        var _buttons = _UOM._TabComponent.GetComponentsInChildren<Button>();
        for (int i = 0; i < _buttons.Length; i++)
        {
            int _i = i;
            var button = _buttons[_i];
            button.onClick.AddListener(() => {
                onSceneTabSwitch?.Invoke(_i);
            });
            // TODO: Register the hover event.
            // 1. Detect event trigger.
            RegisterEventHover(button.GetComponent<Image>(), onSceneTabEnter, onSceneTabExit);
        }
    }

    public void OnSceneTabSwitch(int index)
    {
        Debug.Log($"OnSceneTabSwitch: {index}");
        _SceneTabIndex = index;
        onSceneEnter?.Invoke(_SceneTabIndex);
    }

    public void OnSceneEnter(int index)
    {
        Debug.Log($"OnSceneEnter: {index}");
    }

    public void OnSceneExit(int index)
    {
        Debug.Log($"OnSceneExit: {index}");
    }

    public void ExitScene()
    {
        _UOM._SceneSelectComponent.gameObject.SetActive(true);
        _UOM._SettingComponent.gameObject.SetActive(false);
        _UOM._GuideComponent.gameObject.SetActive(false);
        _UOM._ModeComponent.gameObject.SetActive(false);
        onSceneExit?.Invoke(_SceneTabIndex);
    }

    public void OnSceneTabEnter(Image image, int index)
    {
        Debug.Log($"OnSceneTabEnter {image.name} {index}");
    }

    public void OnSceneTabExit(Image image, int index)
    {
        Debug.Log($"OnSceneTabExit {image.name} {index}");
    }

    //_GuideComponent index-> MR:0, AR:1 
    public UnityAction<int> onGuideSwitch;
    public UnityAction<int, int> onGuideTabSwitch;
    public UnityAction<int> onGuideTabNext;
    private int guideTabIndex;
    public void OnGuideSwitchEvent()
    {
        //
        onGuideSwitch += OnGuideSwitch;
        onGuideTabSwitch += OnGuideTabSwitch;
        onGuideTabNext += OnGuideTabNext;
        onGuideSwitch?.Invoke(0);
        onGuideTabSwitch?.Invoke(0, 0);

        var _childCount = _UOM._GuideComponent.childCount;
        for (int i = 0; i < _childCount; i++)
        {
            int _i = i;
            var child = _UOM._GuideComponent.GetChild(_i);
            // tab/card
            var buttons = child.GetChild(0).GetComponentsInChildren<Button>();
            for (int j = 0; j < buttons.Length; j++)
            {
                int _j = j;
                var button = buttons[_j];
                button.onClick.AddListener(() => {
                    onGuideTabNext?.Invoke(_i);
                });
            }
        }
    }

    public void OnGuideSwitch(int index)
    {
        var _childCount = _UOM._GuideComponent.childCount;
        for (int i = 0; i < _childCount; i++)
        {
            var child = _UOM._GuideComponent.GetChild(i);
            child.gameObject.SetActive(index == i);
        }
        Debug.Log($"OnGuideSwitch: {index}");
    }
    
    public void OnGuideTabSwitch(int index, int tabIndex)
    {
        var child = _UOM._GuideComponent.GetChild(index);
        // pivot
        var pivot = child.GetChild(0).GetComponent<RectTransform>();
        // tab/card
        var cardCount = pivot.childCount;

        // TODO: Hide if tabIndex >= cardCount;
        bool hide = false;
        if (tabIndex >= cardCount)
        {
            tabIndex = 0;
            hide = true;
        }
        else
        {
            hide = false;
        }

        if (cardCount == 0)
            return;
        if (tabIndex > cardCount - 1)
            tabIndex = 0;
        if (tabIndex < 0)
            tabIndex = cardCount - 1;
        guideTabIndex = tabIndex;
        float tabWidth = pivot.sizeDelta.x;
        pivot.DOAnchorPosX(-tabIndex * tabWidth, hide ? 0 : animation_duration);
        // TODO: Hide
        if (hide)
            onGuideSwitch?.Invoke(-1);
        Debug.Log($"OnGuideTabSwitch: {index} tabIndex: {tabIndex}");
    }

    public void OnGuideTabNext(int index)
    {
        OnGuideTabSwitch(index, guideTabIndex + 1);
    }

    // 
    public enum UIComponent { 
        Mode,
        Setting,
        Scene,
        Guide,
    }
    public UnityAction<UIComponent[]> onComponentSwitch;

    public void OnComponentSwitchEvent() {
        onComponentSwitch += OnComponentSwitch;
    }


    public void OnComponentSwitch(UIComponent[] uIComponents)
    {
        List<GameObject> goShow = new List<GameObject>();
        List<GameObject> goHide= new List<GameObject>();
        //Enum.IsDefined(typeof(UIComponent), uIComponents);
        foreach (var iter in Enum.GetValues(typeof(UIComponent)))
        {
            
        }
        string uc = "";
        foreach (var uIComponent in uIComponents)
        {
            int ucIndex = uc.GetHashCode();
            uc += uIComponent.ToString() + $": {ucIndex}";
            
        }
        Debug.LogWarning($"ui component {uc}");
    }

    public GameObject GetUICompoentByEnum(UIComponent uIComponent)
    {
        switch (uIComponent)
        {
            case UIComponent.Mode:
                return _UOM._ModeComponent.gameObject;
            case UIComponent.Setting:
                return _UOM._SettingComponent.gameObject;
            case UIComponent.Scene:
                return _UOM._SceneSelectComponent.gameObject;
            case UIComponent.Guide:
                return _UOM._GuideComponent.gameObject;
            default:
                return null;
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        VariableInit();
        EventInit();
    }

    #region public functions

    public void LockPosAndViewToCamera(Camera camera, float distance)
    {
        if (transform.parent != null)
        {
            Debug.LogWarning("please detach from parent first.");
            return;
        }

        // calc the angle between camera and menu on plane xz in the world axis
        transform.LookAt(new Vector3(camera.transform.position.x, transform.position.y, camera.transform.position.z));
        transform.Rotate(Vector3.up * 180, Space.World);

        Vector2 vec1 = new Vector2(camera.transform.forward.x, camera.transform.forward.z);
        Vector2 vec2 = new Vector2(transform.position.x - camera.transform.position.x, 
            transform.position.z - camera.transform.position.z);

        // coord menu pos
        Vector2 coordPos = distance * vec2.normalized;
        float offsetY = 0;
        //float rotateSpeed = 1;
        transform.position = camera.transform.position + new Vector3(coordPos.x, offsetY, coordPos.y);

        float angle = Vector2.SignedAngle(vec1, vec2);

        transform.RotateAround(camera.transform.position, Vector3.up, angle);
    }

    public void AttachToCamera(Camera camera, float distance)
    {
        transform.SetParent(camera.transform);
        transform.SetLocalPositionAndRotation(new Vector3(0, 0, distance), Quaternion.identity);
    }

    public void DetachFromCamera()
    {
        if (transform.parent == null)
            return;
        transform.SetParent(null);
        transform.rotation = Quaternion.identity;
    }

    public void RegisterEventHover(Image image, UnityAction<Image, int> onEnter, UnityAction<Image, int> onExit) {
        EventTrigger eventTrigger = image.GetComponent<EventTrigger>();
        if (image.GetComponent<EventTrigger>() == null) {
            eventTrigger = image.gameObject.AddComponent<EventTrigger>();
        }
        EventTrigger.Entry entryOnEnter= new EventTrigger.Entry();
        entryOnEnter.eventID = EventTriggerType.PointerEnter; 
        entryOnEnter.callback.AddListener(baseEventData => { onEnter?.Invoke(image, -1);  });
        eventTrigger.triggers.Add(entryOnEnter);

        EventTrigger.Entry entryOnExit = new EventTrigger.Entry();
        entryOnExit.eventID = EventTriggerType.PointerExit;
        entryOnExit.callback.AddListener(baseEventData => { onExit?.Invoke(image, -1); });
        eventTrigger.triggers.Add(entryOnExit);
    }

    private System.Collections.IEnumerator DelayCall(UnityAction unityAction, float delay = 2)
    {
        yield return new WaitForSeconds(delay);
        unityAction.Invoke();
    }

    #endregion

}
