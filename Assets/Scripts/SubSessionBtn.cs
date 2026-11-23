using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SubSessionBtn : MonoBehaviour
{
    private static Dictionary<SubSessionType, SubSessionBtn> sessionSprites = new Dictionary<SubSessionType, SubSessionBtn>();
    public SubSessionType subSessionType;
    private Sprite normalSprite;
    private Sprite selectedSprite;
    private bool isSelected => Session.Instance.subSessionType == subSessionType;

    void Start()
    {
        sessionSprites[subSessionType] = this;
        var button = GetComponent<UnityEngine.UI.Button>();
        normalSprite = button.image.sprite;
        selectedSprite = button.spriteState.highlightedSprite;

        // 左手面板的 tab 用的是 SpriteSwap 过渡，而美术把 selectedSprite 配成了【普通图】。
        // Selectable 在 Selected 状态下会 image.overrideSprite = selectedSprite，
        // overrideSprite 的优先级高于 image.sprite，于是 UpdateVisuals 刚设上的选中图
        // 又被盖回普通图 —— 点完按钮它一直是 EventSystem 的 currentSelectedGameObject，
        // 表现就是"点了没反应"。
        //
        // 沙盘那 5 个大类按钮的 selectedSprite 配的就是选中图，所以它们看得见效果。
        // 这里把配置对齐过去，不改美术资源。
        var spriteState = button.spriteState;
        spriteState.selectedSprite = selectedSprite;
        button.spriteState = spriteState;

        button.onClick.AddListener(OnClick);
    }

    void OnClick()
    {
        Session.Instance.subSessionType = subSessionType;
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
