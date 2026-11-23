using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MessageBoxAlertController : MonoBehaviour
{
    public static MessageBoxAlertController Ins;
    private void Awake()
    {
        Ins = this;
    }
    public float yOffset = 50;
    public float itemYOffset = 10;
    private GameObject itemPrefab;
    void Start()
    {
        itemPrefab = transform.GetChild(0).gameObject;
        AlertWelcome();
    }
    public void CreateMsgBoxItem(string message, float delay = 3f)
    {
        if (itemPrefab == null)
            return;
        var item = Instantiate(itemPrefab, transform);
        var itemScript = item.GetComponent<MessageBoxAlertItem>();
        itemScript.SetMessage(message, delay);
        item.SetActive(true);
        itemScript.IncAnimationCount();
        
        // inc y
        var itemScripts = transform.GetComponentsInChildren<MessageBoxAlertItem>();
        foreach (var iter in itemScripts)
        {
            if (iter.gameObject.activeSelf && !iter.Equals(itemScript))
                iter.IncAnimationCount();
        }
    }

    public void AlertWelcome()
    {
        CreateMsgBoxItem("欢迎来到数字沙盘系统！", 3);
    }

}
