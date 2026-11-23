#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace SandTable.Editor
{
    /// <summary>
    /// 走**运行时那条路径**看签名库里有什么：直接调 <see cref="PortalSignatureConfig"/>，
    /// 它内部用的是 <c>Resources.LoadAll</c>（编辑器里没有设备目录，所以只会扫到随包那份）。
    ///
    /// 【与 SpecialJobTicketChecks.CheckSignatures 的分工】那边用 <c>AssetDatabase</c>
    /// 按磁盘路径扫目录，验的是「文件在不在、尺寸是不是 2 的幂」；这边验的是
    /// 「<c>Resources.LoadAll</c> 能不能真把它们取出来」—— 后者才是真机上实际发生的事。
    /// 图放在 Resources 目录外、或者被导成了非 Texture2D 时，前者过后者不过。
    ///
    /// 【空不算失败】签名图由现场后补，可以放进随包目录重新出包，也可以 adb push 到设备的
    /// signatures 目录（那一份优先，且不需要出包）。所以一张都没有只警告，不退非零。
    ///
    ///   Unity.exe -batchmode -nographics -quit -projectPath &lt;repo&gt;/sandtable \
    ///     -executeMethod SandTable.Editor.CliSignatureCheck.Run
    /// </summary>
    internal static class CliSignatureCheck
    {
        public static void Run()
        {
            PortalSignatureConfig.EnsureLoaded();

            int n = PortalSignatureConfig.Options.Count;
            if (n == 0)
            {
                Debug.LogWarning("[签名自检] 签名库是空的。" + PortalSignatureConfig.EmptyHint);
                if (Application.isBatchMode) EditorApplication.Exit(0);
                return;
            }

            foreach (PortalSignatureConfig.Option o in PortalSignatureConfig.Options)
            {
                Debug.Log($"[签名自检] {o.Label}（来源 {o.Source}）：" +
                          $"{o.Texture.width}x{o.Texture.height}");
            }

            Debug.Log("[签名自检] 通过，共 " + n + " 张");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
#endif
