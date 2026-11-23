using System;
using UnityEngine;
using DG.Tweening;
using TMPro;

public class MessageBoxAlertItem: MonoBehaviour
{
    [HideInInspector] public int animationCount = -1;
    private float animationDur = .3f;
    private float destoryDelay = 2.5f;
    public void IncAnimationCount()
    {
        animationCount++;
        Debug.LogWarning($"animationCount={animationCount}");
        var size = transform.GetComponent<RectTransform>().sizeDelta;
        var yOffset = transform.parent.GetComponent<MessageBoxAlertController>().yOffset;
        var itemYOffset = transform.parent.GetComponent<MessageBoxAlertController>().itemYOffset;
        var rtf = transform.GetComponent<RectTransform>();
        rtf.DOAnchorPosY(-(itemYOffset + size.y) * animationCount - yOffset, 
            animationDur);
    }
    public void SetMessage(string message, float delay=2)
    {
        transform.GetChild(0).GetComponent<TMP_Text>().text = message;;
        destoryDelay = delay;
    }
    private void Start()
    {
        // animationCount = -1;
        // IncAnimationCount();
        Destroy(gameObject, destoryDelay);
    }
}