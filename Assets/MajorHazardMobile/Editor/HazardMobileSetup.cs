using LZW.Module.MajorHazard.Mobile;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace LZW.Module.MajorHazard.Mobile.Editor
{
    /// <summary>
    /// 一键配置：URP 渲染管线、生成场景（一个挂 <see cref="HazardMobileApp"/> 的物体 + 相机 + 平行光）、构建场景列表、安卓发布设置。
    /// 可重复执行（会覆盖场景文件）。
    /// </summary>
    public static class HazardMobileSetup
    {
        private const string ScenePath = "Assets/MajorHazardMobile/MajorHazardMobile.unity";
        private const string PipelinePath = "Assets/Settings/New Universal Render Pipeline Asset.asset";
        private const string ModelPath = "Assets/ArtWorks/XHD.prefab";
        private const string FontPath = "Assets/MajorHazardMobile/Fonts/SourceHanSansCN-Regular.otf";
        private const string LoginBackgroundPath = "Assets/MajorHazardMobile/Textures/Login_bg.png";
        private const string LoginButtonPath = "Assets/MajorHazardMobile/Textures/Common_btn_A_02.png";

        [MenuItem("Tools/MajorHazard/Setup Mobile Scene")]
        public static void Setup()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            SetupRenderPipeline();
            SetupPlayer();
            CreateScene();

            EditorUtility.DisplayDialog("重大危险源 · 移动端",
                $"已完成：\n· URP 渲染管线\n· 场景 {ScenePath}（已设为唯一构建场景）\n· 安卓发布设置（包名 {PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android)}，自动旋转，IL2CPP ARM64）\n\n" +
                (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android ? "" : "当前平台不是 Android，出包前请在 File/Build Settings 切换到 Android。"),
                "好");
        }

        private static void SetupRenderPipeline()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                Debug.LogError($"[{nameof(HazardMobileSetup)}] 找不到 {PipelinePath}，三维模型的材质（URP Lit）会显示为紫色");
                return;
            }
            GraphicsSettings.defaultRenderPipeline = pipeline;
            // 各画质等级不单独指定，统一用默认管线
            var current = QualitySettings.GetQualityLevel();
            for (var i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = null;
            }
            QualitySettings.SetQualityLevel(current, false);
        }

        private static void SetupPlayer()
        {
            PlayerSettings.productName = "重大危险源安全管理";
            PlayerSettings.companyName = "DeepMirror";
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.dm.xiaohudao.majorhazard");

            // 手机竖屏、平板横屏都支持，界面按方向自适应
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.Android.startInFullscreen = false;
            PlayerSettings.Android.renderOutsideSafeArea = true;
        }

        private static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = cameraGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.06f, 0.11f, 1f);
            cam.cullingMask = 0; // 界面是 Overlay 画布，三维地图有自己的相机

            var lightGo = new GameObject("Directional Light");
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.6f, 0.68f);

            var appGo = new GameObject("HazardMobileApp");
            var app = appGo.AddComponent<HazardMobileApp>();
            var so = new SerializedObject(app);
            so.FindProperty("font").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            so.FindProperty("mapModel").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            so.FindProperty("loginBackground").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(LoginBackgroundPath);
            so.FindProperty("loginButton").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(LoginButtonPath);
            so.ApplyModifiedPropertiesWithoutUndo();
            if (so.FindProperty("loginBackground").objectReferenceValue == null)
                Debug.LogWarning($"[{nameof(HazardMobileSetup)}] 找不到登录背景 {LoginBackgroundPath}（需为 Sprite 类型）");
            if (so.FindProperty("font").objectReferenceValue == null)
                Debug.LogWarning($"[{nameof(HazardMobileSetup)}] 找不到字体 {FontPath}");
            if (so.FindProperty("mapModel").objectReferenceValue == null)
                Debug.LogWarning($"[{nameof(HazardMobileSetup)}] 找不到模型 {ModelPath}");

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
        }
    }
}
