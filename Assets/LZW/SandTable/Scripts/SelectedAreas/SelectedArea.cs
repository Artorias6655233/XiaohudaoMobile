using System;
using UnityEngine;

[DefaultExecutionOrder(100)]
public class SelectedArea : MonoBehaviour
{
    public string targetIntroductionText;
    
    public Transform[] targets;
    
    private MeshRenderer Renderer => GetComponent<MeshRenderer>();
    
    private Collider Collider => GetComponent<Collider>();

    private void Start()
    {
        SetVisible(false);
    }

    private void Update()
    {
        UpdateInteraction();
    }

    private void SetInteractionState(bool state)
    {
        Collider.enabled = state;
    }

    private bool ExistActiveInParent()
    {
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i].gameObject.activeInHierarchy)
                return true;
        }
        return false;
    }

    public void UpdateInteraction()
    {
        SetInteractionState(ExistActiveInParent());
    }

    public void SetVisible(bool visible)
    {
        Renderer.enabled = visible;
    }
}
