using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class TimeBtn : MonoBehaviour
{
    private static Dictionary<TimeType, TimeBtn> timeSprites = new Dictionary<TimeType, TimeBtn>();
    public TimeType timeType;
    private bool isSelected => Session.Instance.timeType == timeType;

    public virtual void Start()
    {
        timeSprites[timeType] = this;
        var button = GetComponent<Button>();
        
        button.onClick.AddListener(OnClick);
        
        // 配置按钮的色彩状态
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = Color.white;
        colors.pressedColor = Color.white;
        colors.selectedColor = Color.white;
        colors.disabledColor = new Color(1, 1, 1, 0.2f);
        button.colors = colors;
        
        // 监听 PanelTypeChanged 事件
        Session.Instance.OnPanelTypeChanged += UpdateVisuals;
    }
    
    void OnDestroy()
    {
        if (Session.Instance != null)
        {
            Session.Instance.OnPanelTypeChanged -= UpdateVisuals;
        }
    }

    void OnClick()
    {
        Session.Instance.timeType = timeType;
        foreach (var kvp in timeSprites)
            kvp.Value.UpdateVisuals();
    }

    void UpdateVisuals()
    {
        var button = GetComponent<Button>();
        if (button == null || button.image == null)
        {
            return;
        }
        
        var image = button.image;
        
        if (isSelected)
        {
            // 选中状态：alpha 为 1
            Color color = image.color;
            color.a = 1f;
            image.color = color;
        }
        else
        {
            // 未选中状态：alpha 为 0.5
            Color color = image.color;
            color.a = 0.2f;
            image.color = color;
        }
    }
}
