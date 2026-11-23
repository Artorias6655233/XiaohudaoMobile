#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEngine;

public class BuilderController : MonoBehaviour
{
    [HideInInspector]
    public BuildTarget buildTarget = BuildTarget.Android;

    [HideInInspector]
    public BuildOptions buildOptions = BuildOptions.None;

    //public UnityEngine.SceneManagement.Scene[] scenes;
    [ReadOnly]
    public string companyName = "deepmirror";

    public string buildPath;

    [ReadOnly]
    public string buildName;
    
    [Header("Android only")]
    public bool useCustomKeystore;

    private bool useCustomKeystoreLast;

    public string keystorePath => Path.Combine(Application.dataPath, "../", "user.keystore");

    [Header("Please enter manually if using keystore")]
    [ReadOnly]
    public string keyaliasName = "123456789";
    [ReadOnly]
    public string keyaliasPass = "123456789";

    public enum ProjectName
    {
        沙盘演示,
        武汉东湖管廊,
    }

    public ProjectName projectName = ProjectName.沙盘演示;
    
    private ProjectName projectNameLast;

    public enum ProjectPackageName 
    {
        com_deepmirror_sandtable,
        com_deepmirror_whdhgl,
    }

    [HideInInspector]
    public ProjectPackageName projectPackageName = ProjectPackageName.com_deepmirror_sandtable;

    private void OnValidate()
    {
        if  (projectNameLast != projectName || useCustomKeystoreLast != useCustomKeystore)
        {
            projectNameLast = projectName;
            useCustomKeystoreLast = useCustomKeystore;
            OnValidateUpdate();
        }
    }

    private void OnValidateUpdate()
    {
        int projectIndex = (int)projectName;
        buildName = projectName.ToString();
        if (Enum.IsDefined(typeof(ProjectPackageName), projectIndex))
        {
            BuildTargetGroup buildTargetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget);
            projectPackageName = (ProjectPackageName)projectIndex;
            SetSceneActive(projectIndex);
            string projectPackageName_ = projectPackageName.ToString().Replace('_', '.');
            PlayerSettings.SetApplicationIdentifier(buildTargetGroup, projectPackageName_);
            PlayerSettings.productName= buildName;
            PlayerSettings.companyName = companyName;
        }
        SetAndroidKeyStore();
        //EditorUtility.SetDirty(this);
    }

    public void BuildGame()
    {
        if (string.IsNullOrEmpty(buildPath)) 
        {
            EditorUtility.DisplayDialog("Tips", "build path is empty", "Confirm", "Cancel");
            return;
        }
        if (string.IsNullOrEmpty(buildName))
        {
            EditorUtility.DisplayDialog("Tips", "build name is empty", "Confirm", "Cancel");
            return;
        }

        bool confirmBuild = EditorUtility.DisplayDialog("Tips", "It will builds the app, sure?", "Confirm", "Cancel");

        if (confirmBuild == false) {
            return;
        }

        PrefabLightmapData.ClearLightmaps();
        PrefabLightmapData[] prefabLightmapDatas = FindObjectsOfType<PrefabLightmapData>(true);
        foreach (var plmd in prefabLightmapDatas)
        {
            plmd.BindLightmapInBuild();
        }
        
        // Obtain levels in Build Settings.
        string[] scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();

        if (Directory.Exists(buildPath) == false) 
        {
            Directory.CreateDirectory(buildPath);
        }

        // Execute
        BuildPipeline.BuildPlayer(scenes, Path.Combine(buildPath, buildName + ".apk"), buildTarget, buildOptions);

        Process.Start("explorer.exe", buildPath);
    }

    public string[] ConvertToPaths(UnityEngine.SceneManagement.Scene[] scenes)
    {
        string[] paths = new string[scenes.Length];
        for (int i = 0; i < scenes.Length; i++)
        {
            paths[i] = scenes[i].path;
        }
        return paths;
    }

    public void SetSceneActive(int index)
    {
        EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
        for (int i = 0; i < scenes.Length && i < Enum.GetValues(typeof(ProjectName)).Length; i++)
        {
            scenes[i].enabled = index == i;
        }
        EditorBuildSettings.scenes = scenes;
    }

    /// <summary>
    /// Due to some platforms or computers, machine must be check on the keystore to build.
    /// </summary>
    public void SetAndroidKeyStore()
    {
#if UNITY_ANDROID
        PlayerSettings.Android.useCustomKeystore = useCustomKeystore;
        if (PlayerSettings.Android.useCustomKeystore)
        {
            PlayerSettings.Android.keystoreName = keystorePath;
            PlayerSettings.Android.keyaliasName = keyaliasName;
            PlayerSettings.Android.keyaliasPass = keyaliasPass;
            return;
        }
        // Clear
        PlayerSettings.Android.keystoreName = "";
        PlayerSettings.Android.keyaliasName = "";
        PlayerSettings.Android.keyaliasPass = "";
#endif
    }
}

[CustomEditor(typeof(BuilderController))]
public class BuilderControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        if (GUILayout.Button("Build"))
        {
            ((BuilderController)target).BuildGame();
        }
    }
}
#endif
