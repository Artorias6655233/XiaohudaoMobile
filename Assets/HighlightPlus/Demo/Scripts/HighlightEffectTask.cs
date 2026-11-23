using HighlightPlus;
using Unity.VisualScripting;
using UnityEngine;

[ExecuteAlways]
public class HighlightEffectTask : MonoBehaviour
{
    public HighlightEffect highlightEffectTemplate;

    public Transform highlightScope;

    public void Update()
    {
        PerformTask();
    }

    private void PerformTask()
    {
        var childs = highlightScope.GetComponentsInChildren<MeshFilter>();
        for (var i = 0; i < childs.Length; i++) 
        {
            var child = childs[i];
            if (child.GetComponent<HighlightEffect>() == null)
            {
                var component = child.AddComponent<HighlightEffect>();
                child.AddComponent<HighlightEffectLayerDetection>();
                string json = JsonUtility.ToJson(highlightEffectTemplate);
                JsonUtility.FromJsonOverwrite(json, component);
            }
        }
    }
}
