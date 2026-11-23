#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SandTable.Editor
{
    /// <summary>
    /// 命令行出 Android 包，供 -batchmode -executeMethod 调用：
    ///
    ///   Unity.exe -batchmode -nographics -quit \
    ///     -projectPath &lt;repo&gt;/sandtable \
    ///     -executeMethod SandTable.Editor.CliAndroidBuild.BuildAndroid \
    ///     -logFile &lt;repo&gt;/sandtable/Builds/build-&lt;时间戳&gt;.log
    ///
    /// 刻意【不】改任何 PlayerSettings：脚本后端（IL2CPP）、架构（ARM64）、包名都以
    /// 工程当前设置为准。之前的包是从 Editor 里出的，这里改设置就等于偷偷换了构建口径。
    /// 场景列表同样取 Build Settings 里勾选的那些，不硬编码。
    /// </summary>
    internal static class CliAndroidBuild
    {
        private const string OutputDirectory = "Builds/Android";
        private const string OutputFileNamePrefix = "sandtable";

        /// <summary>记录本次产物路径，方便脚本/人取最新一个包。</summary>
        private const string LatestOutputPathFileName = "sandtable.latest.txt";

        public static void BuildAndroid()
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                throw new BuildFailedException("[CliAndroidBuild] Build Settings 里没有勾选任何场景。");
            }

            string outputDirectory = Path.GetFullPath(OutputDirectory);
            Directory.CreateDirectory(outputDirectory);

            string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string outputPath = Path.Combine(outputDirectory, $"{OutputFileNamePrefix}-{timestamp}.apk");

            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;

            Debug.Log($"[CliAndroidBuild] 包名={PlayerSettings.applicationIdentifier}" +
                $"，后端={PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android)}" +
                $"，架构={PlayerSettings.Android.targetArchitectures}" +
                $"，版本={PlayerSettings.bundleVersion}");

            foreach (string scene in scenes)
            {
                Debug.Log($"[CliAndroidBuild] 场景：{scene}");
            }

            Debug.Log($"[CliAndroidBuild] 开始构建 -> {outputPath}");

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            });

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException(
                    $"[CliAndroidBuild] 构建失败。result={report.summary.result}" +
                    $"，errors={report.summary.totalErrors}，warnings={report.summary.totalWarnings}");
            }

            File.WriteAllText(Path.Combine(outputDirectory, LatestOutputPathFileName), outputPath);

            long sizeMb = new FileInfo(outputPath).Length / (1024 * 1024);
            Debug.Log($"[CliAndroidBuild] 构建成功：{outputPath}（{sizeMb} MB，" +
                $"耗时 {report.summary.totalTime}，warnings={report.summary.totalWarnings}）");
        }
    }
}
#endif
