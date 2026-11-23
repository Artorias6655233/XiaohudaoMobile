using UnityEngine;
using UnityEngine.UI;

public class UIObjectManager : MonoBehaviour
{
    public static UIObjectManager Ins;

    private void Awake()
    {
        Ins = this;
    }

    public Sprite[] sprites;

    public RectTransform[] rectTransforms;

    public Sprite[] _ModeBtnsSprite => new Sprite[2] { sprites[0], sprites[1] };

    public Sprite _ItemSelectedSprite => sprites[2];

    public RectTransform _ModeComponent => rectTransforms[0];

    public Button[] _ModeButtons => _ModeComponent.GetComponentsInChildren<Button>();

    public RectTransform _SettingComponent => rectTransforms[1];

    public GameObject _SettingSelectedBox => _SettingComponent.GetChild(0).gameObject;

    public Button _SettingButton => _SettingComponent.GetChild(1).GetComponent<Button>();

    public Button[] GetSelectedBoxItems() { 
        var box = _SettingSelectedBox.transform.GetChild(0);
        int childCount = box.childCount;
        Button[] buttons = new Button[childCount];
        for (int i = 0; i < childCount; i++)
        {
            var child = box.GetChild(i);
            buttons[i] = child.GetComponent<Button>();
        }
        return buttons;
    }

    public RectTransform _SceneSelectComponent => rectTransforms[2];
    public Transform _TabComponent => _SceneSelectComponent.GetChild(1);
    public Toggle _MultipleToggle => _SceneSelectComponent.GetChild(0).GetChild(1).GetComponent<Toggle>();

    public RectTransform _GuideComponent => rectTransforms[3];
}
