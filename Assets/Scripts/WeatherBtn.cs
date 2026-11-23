using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class WeatherBtn : MonoBehaviour
{
    private static Dictionary<WeatherType, WeatherBtn> weatherSprites = new Dictionary<WeatherType, WeatherBtn>();
    public WeatherType weatherType;
    private Sprite normalSprite;
    private Sprite selectedSprite;
    private bool isSelected => Session.Instance.weatherType == weatherType;

    public virtual void Start()
    {
        weatherSprites[weatherType] = this;
        var button = GetComponent<UnityEngine.UI.Button>();
        normalSprite = button.image.sprite;
        selectedSprite = button.spriteState.highlightedSprite;
        button.onClick.AddListener(OnClick);
        
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
        Session.Instance.weatherType = weatherType;
        foreach (var kvp in weatherSprites)
            kvp.Value.UpdateVisuals();
    }

    void UpdateVisuals()
    {
        var button = GetComponent<UnityEngine.UI.Button>();
        if (isSelected) button.image.sprite = selectedSprite;
        else button.image.sprite = normalSprite;
    }
}
