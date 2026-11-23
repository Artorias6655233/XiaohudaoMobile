#if UNITY_ANDROID
using Qualcomm.Snapdragon.Spaces;
#endif
using UnityEngine;
using UnityEngine.XR.OpenXR;

public class SxrDebug : MonoBehaviour
{
    // Start is called before the first frame update
    private void Start()
    {
        SetPassthroughState(true);
    }

    private void SetPassthroughState(bool value)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        BaseRuntimeFeature baseRuntimeFeature = OpenXRSettings.Instance.GetFeature<BaseRuntimeFeature>();
        if (baseRuntimeFeature != null)
        {
            baseRuntimeFeature.SetPassthroughEnabled(value);
        }
        else
        {
            Debug.Log($"SetPassthroughState (null): {baseRuntimeFeature}");
        }
#endif
    }
}
