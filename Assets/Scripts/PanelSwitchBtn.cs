using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PanelSwitchBtn : MonoBehaviour
{
    private static Dictionary<PanelType, PanelSwitchBtn> panelSprites = new Dictionary<PanelType, PanelSwitchBtn>();
    public PanelType panelType;
    private bool isSelected => Session.Instance.panelType == panelType;

    void Start()
    {
        panelSprites[panelType] = this;
        var button = GetComponent<Button>();
        button.onClick.AddListener(OnClick);
    }

    void OnClick()
    {
        // 如果已经是这个面板类型，则关闭；否则打开
        if (Session.Instance.panelType == panelType)
        {
            Session.Instance.panelType = PanelType.None;
        }
        else
        {
            Session.Instance.panelType = panelType;
        }
    }

}
