using System.Collections;
using UnityEngine;

public class TimePanel : Panel
{
    public virtual IEnumerator Start()
    {
        yield return new WaitUntil(() => Session.Instance != null);
        
        // 注册到 Session
        Session.Instance.timePanel = gameObject;
        
        // 初始隐藏
        gameObject.SetActive(false);
        
        // 订阅事件
        Session.Instance.OnPanelTypeChanged += OnPanelTypeChanged;
    }
    
    private void OnPanelTypeChanged()
    {
        // 根据面板类型显示/隐藏
        gameObject.SetActive(Session.Instance.panelType == PanelType.Time);
    }
}

