using HighlightPlus;
using UnityEngine;

[ExecuteAlways]
public class HighlightEffectLayerDetection : MonoBehaviour
{
    //public LayerMask layerMask = LayerMask.NameToLayer("SelectObj");

    public void Update()
    {
        PerformTask();
    }

    private void PerformTask()
    {
        var hl = gameObject.GetComponent<HighlightEffect>();
        if (hl == null)
            return;

        LayerMask layerMask = LayerMask.NameToLayer("SelectObj");
        hl.highlighted = gameObject.layer.Equals(layerMask);
    }
}
