using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 数字签名库：一个文件夹，里面放什么图就有什么可选项。
///
/// 【为什么没有清单文件】上一版要维护一份 <c>approval_signatures.json</c>，加一张签名得同时
/// 改 json 和放图，两处对不上就表现成「少了一个选项」。现在改成**扫文件夹**：
/// 把图放进去就有，拿走就没有，不需要改任何配置或代码。
///
/// 【看两个地方，设备上那份优先】
///   1. <c>persistentDataPath/signatures/</c> —— adb push 进去就生效，不用重新出包；
///   2. 随包的 <c>Assets/Resources/SignatureLibrary/</c>。
/// 设备目录只要**存在且有图**就完全接管，不和随包的合并 —— 两边混着出来的列表，
/// 现场没人说得清哪张是哪来的。
///
/// 【文件名就是签名人】选择界面显示的是文件名（去掉扩展名），草稿里记的也是它。
/// 所以文件名要写签名人姓名，比如 <c>张三.png</c>；别写 <c>sign1.png</c>。
///
/// 【图片要求】透明背景 PNG，尺寸用 2 的幂（建议 512x256）。非 2 的幂会被 Unity 自动缩放到
/// 最近的 POT，签名会被拉变形 —— 这条只对随包那份成立，设备目录那份是运行时
/// <c>LoadImage</c> 进来的，不受影响。
/// </summary>
public static class PortalSignatureConfig
{
    /// <summary>随包签名库在 Resources 下的目录名。</summary>
    public const string ResourceFolder = "SignatureLibrary";

    /// <summary>设备上的签名库目录名，位于 persistentDataPath 下。</summary>
    public const string DeviceFolder = "signatures";

    private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg" };

    /// <summary>一张可选签名。</summary>
    public sealed class Option
    {
        /// <summary>稳定标识 = 文件名（不含扩展名）。本地存档里记的就是它。</summary>
        public string Id;

        /// <summary>界面上显示的名字，同样是文件名 —— 所以文件名要写签名人姓名。</summary>
        public string Label;

        /// <summary>图从哪来的，排查用：<c>设备</c> 或 <c>随包</c>。</summary>
        public string Source;

        public Texture2D Texture;
    }

    public static IReadOnlyList<Option> Options => options;
    private static readonly List<Option> options = new List<Option>();

    public static bool Loaded { get; private set; }

    /// <summary>签名库是空的。不是错误 —— 图还没放进去而已，但界面要说清楚。</summary>
    public static bool IsEmpty => options.Count == 0;

    public static string DevicePath => Path.Combine(Application.persistentDataPath, DeviceFolder);

    /// <summary>签名库为空时显示给用户的那句话，含具体路径，免得让人猜图该放哪。</summary>
    public static string EmptyHint =>
        "签名库是空的：把签名 PNG 放进 " + DevicePath +
        "（adb push 即可，文件名写签名人姓名），或放进工程的 Assets/Resources/" +
        ResourceFolder + "/ 后重新出包";

    public static void EnsureLoaded()
    {
        if (Loaded) return;
        Loaded = true;
        options.Clear();

        LoadFromDevice();
        if (options.Count > 0)
        {
            Report("设备");
            return;
        }

        LoadFromResources();
        Report("随包");
    }

    /// <summary>现场往设备目录里加了图之后，不重启应用也能刷出来。</summary>
    public static void Reload()
    {
        Loaded = false;
        EnsureLoaded();
    }

    public static Option Find(string id)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(id)) return null;
        return options.Find(o => string.Equals(o.Id, id, StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ 加载

    private static void LoadFromDevice()
    {
        string dir = DevicePath;

        try
        {
            if (!Directory.Exists(dir))
            {
                // 顺手建出来，现场就能直接 adb push 进去，不用先手动 mkdir
                Directory.CreateDirectory(dir);
                return;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[签名] 设备签名库目录不可用，改用随包的：" + e.Message);
            return;
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(dir);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[签名] 设备签名库读取失败，改用随包的：" + e.Message);
            return;
        }

        Array.Sort(files, StringComparer.Ordinal);   // 顺序要稳定，否则每次启动选项会换位置

        foreach (string file in files)
        {
            if (!HasImageExtension(file)) continue;

            Texture2D tex = ReadTexture(file);
            if (tex == null) continue;

            string name = Path.GetFileNameWithoutExtension(file);
            options.Add(new Option { Id = name, Label = name, Source = "设备", Texture = tex });
        }
    }

    private static void LoadFromResources()
    {
        Texture2D[] textures = Resources.LoadAll<Texture2D>(ResourceFolder);
        if (textures == null) return;

        Array.Sort(textures, (a, b) => StringComparer.Ordinal.Compare(a.name, b.name));

        foreach (Texture2D tex in textures)
        {
            if (tex == null) continue;
            options.Add(new Option { Id = tex.name, Label = tex.name, Source = "随包", Texture = tex });
        }
    }

    private static Texture2D ReadTexture(string file)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(file);
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (tex.LoadImage(bytes)) return tex;

            // 不静默跳过：图坏了要当场看得见，否则表现成「选项少了一张」，现场很难查
            Debug.LogError("[签名] 解不开这张图，已跳过：" + file);
            UnityEngine.Object.Destroy(tex);
        }
        catch (Exception e)
        {
            Debug.LogError("[签名] 读图失败，已跳过 " + file + "：" + e.Message);
        }
        return null;
    }

    private static bool HasImageExtension(string file)
    {
        string ext = Path.GetExtension(file);
        if (string.IsNullOrEmpty(ext)) return false;

        foreach (string allowed in Extensions)
        {
            if (string.Equals(ext, allowed, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static void Report(string source)
    {
        if (options.Count == 0)
        {
            Debug.LogWarning("[签名] " + EmptyHint);
            return;
        }

        Debug.Log("[签名] 签名库（" + source + "）" + options.Count + " 张：" +
                  string.Join("、", options.ConvertAll(o => o.Label)));
    }
}
