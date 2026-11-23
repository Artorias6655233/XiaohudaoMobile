#if UNITY_EDITOR
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public class CustomBuildProcessor : IPreprocessBuildWithReport
{
    public int callbackOrder { get { return 0; } }

    public void OnPreprocessBuild(BuildReport report)
    {
        Debug.Log($"CustomBuildProcessor Start");
        PrefabLightmapData[] datas = GameObject.FindObjectsOfType<PrefabLightmapData>();
        Debug.Log($"PrefabLightmapData count = {datas.Length}");
        for (int i = 0; i < datas.Length; i++)
        {
            datas[i].BindLightmap();
        }
        Debug.Log($"CustomBuildProcessor End");
    }
}
#endif