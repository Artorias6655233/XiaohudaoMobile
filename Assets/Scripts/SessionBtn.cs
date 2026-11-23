using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SessionBtn : MonoBehaviour
{
    private static Dictionary<SessionType, SessionBtn> sessionSprites = new Dictionary<SessionType, SessionBtn>();
    public SessionType sessionType;
    private Sprite normalSprite;
    private Sprite selectedSprite;
    private bool isSelected => Session.Instance.sessionType == sessionType;

    void Start()
    {
        sessionSprites[sessionType] = this;
        var button = GetComponent<UnityEngine.UI.Button>();
        normalSprite = button.image.sprite;
        selectedSprite = button.spriteState.highlightedSprite;
        button.onClick.AddListener(OnClick);
    }

    void OnClick()
    {
        Session.Instance.sessionType = sessionType;
        foreach (var kvp in sessionSprites)
            kvp.Value.UpdateVisuals();
    }

    void UpdateVisuals()
    {
        var button = GetComponent<UnityEngine.UI.Button>();
        if (isSelected) button.image.sprite = selectedSprite;
        else button.image.sprite = normalSprite;
    }
}
