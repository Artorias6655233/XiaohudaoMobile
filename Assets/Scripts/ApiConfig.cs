using System;
using System.IO;
using UnityEngine;

/// <summary>
/// 后端服务地址配置。
///
/// 地址不写死在代码里，读三处，优先级从高到低：
///   1. <c>Application.persistentDataPath/api_config.json</c>
///      —— 现场 adb push 就能改，改完重启应用生效，【不用重新出包】；
///   2. 随包的 <c>Assets/Resources/Config/api_config.json</c>
///      —— 仓库里那份默认值，首次运行会自动落地一份到 (1)，方便现场直接改；
///   3. <see cref="FallbackBaseUrl"/> —— 代码里的最后兜底。
///
/// 为什么这里留了代码兜底，而 <see cref="EntryExitPhraseConfig"/> 刻意不留：
/// 短语读不到只是卡片上少几个按钮，问题当场可见且不致命；BaseUrl 读不到则是整个应用
/// 一条数据都拿不到，连"配置坏了"这个结论都要靠猜。所以宁可多一处真相，也要保证
/// 应用起得来。兜底生效时会打 Error，不会静默。
///
/// 默认值放 Resources 而不是 StreamingAssets 的原因同 <see cref="EntryExitPhraseConfig"/>：
/// <c>sandtable/.gitignore</c> 忽略了整个 StreamingAssets 目录。
/// </summary>
public static class ApiConfig
{
    public const string FileName = "api_config.json";

    /// <summary>Resources 下的相对路径（不带扩展名，Resources.Load 的规矩）。</summary>
    public const string ResourcePath = "Config/api_config";

    /// <summary>三层都失效时的最后兜底。现场内网后端。</summary>
    public const string FallbackBaseUrl = "http://192.168.3.20:8082/";

    private static string baseUrl = FallbackBaseUrl;
    private static P0DataMode p0Mode = P0DataMode.Mock;
    private static P0DataMode certMode = P0DataMode.Mock;
    private static string debugDefaultCompanyId = string.Empty;
    private static string portalUsername = string.Empty;
    private static string portalPwd = string.Empty;

    /// <summary>
    /// 后端根地址，保证以 '/' 结尾，可直接与相对路径拼接。
    /// </summary>
    public static string BaseUrl
    {
        get
        {
            Ensure();
            return baseUrl;
        }
    }

    /// <summary>P0 页面采用的数据源。旧配置缺少字段时固定进入 Mock。</summary>
    public static P0DataMode P0Mode
    {
        get
        {
            Ensure();
            return p0Mode;
        }
    }

    /// <summary>
    /// 企业证件管理采用的数据源。旧配置缺少字段时固定进入 Mock。
    ///
    /// 正式接口（缺口 C1–C4）还没有，配成 network 会在进页面时直接报错而不是悄悄退回演示数据。
    /// </summary>
    public static P0DataMode CertMode
    {
        get
        {
            Ensure();
            return certMode;
        }
    }

    /// <summary>
    /// 联调用：进入监测预警后直接替用户选中的企业 id，留空（默认）表示不自动选。
    ///
    /// 沙盘里 51 个企业图标挨得很近，要找某一家得在头显里转半天，联调时纯属浪费时间。
    /// 配的是 <c>security/company/list</c> 里的 <c>id</c>，不是 <c>sysOrgCode</c>。
    /// 这是调试开关，随包配置里必须留空，只在本机 / 设备的覆盖文件里填。
    /// </summary>
    public static string DebugDefaultCompanyId
    {
        get
        {
            Ensure();
            return debugDefaultCompanyId;
        }
    }

    /// <summary>
    /// 门户账号（<c>www.nsxhcip.cn</c>）。留空表示用 <see cref="IccAuth"/> 里的内置账号。
    ///
    /// 【为什么要可配】待办是按账号的岗位分派的，换一个账号看到的待办就完全不同
    /// （实测 dmtest2 有 0 条、xhdAdmin 有 2 条）。联调期间换账号的频率远高于出包的频率，
    /// 所以这两项必须能在设备上改完重启就生效。
    ///
    /// 【为什么随包那份必须留空】管理员账号的凭据不进仓库。只在设备本机的
    /// <see cref="OverridePath"/> 里填，和 <see cref="DebugDefaultCompanyId"/> 同理。
    /// </summary>
    public static string PortalUsername
    {
        get
        {
            Ensure();
            return portalUsername;
        }
    }

    /// <summary>
    /// 门户账号的 <c>pwd</c>，存的是**已经算好的** <c>Base64(MD5hex(明文))</c>，
    /// 和 <see cref="IccAuth"/> 内置那串同一种形态。明文不进配置也不进仓库。
    /// 与 <see cref="PortalUsername"/> 必须成对出现，只填一个视为没配。
    /// </summary>
    public static string PortalPwd
    {
        get
        {
            Ensure();
            return portalPwd;
        }
    }

    /// <summary>门户账号是否被配置覆盖了。日志里要能看出当前用的是哪一路，否则排查待办为空时全是猜。</summary>
    public static bool HasPortalAccountOverride =>
        !string.IsNullOrEmpty(PortalUsername) && !string.IsNullOrEmpty(PortalPwd);

    /// <summary>是否已经尝试过加载（不代表成功）。</summary>
    public static bool Loaded { get; private set; }

    /// <summary>本次实际采用的来源，排查"改了 json 却没生效"时看这个。</summary>
    public static string Source { get; private set; } = "未加载";

    public static string OverridePath => Path.Combine(Application.persistentDataPath, FileName);

    [Serializable]
    private class Payload
    {
        public string baseUrl;
        public string p0DataMode;
        public string certDataMode;
        public string debugDefaultCompanyId;
        public string portalUsername;
        public string portalPwd;
    }

    /// <summary>
    /// 启动时就在主线程把配置读好。
    ///
    /// 必须这么做而不是纯惰性加载：<c>Application.persistentDataPath</c> 只能在主线程访问，
    /// 而列表面板的大响应是丢到后台线程解析的，万一哪天有人在后台线程拼 URL，
    /// 惰性加载就会在那儿抛异常。启动时加载完，后面各处读到的都是已缓存的字符串。
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Ensure()
    {
        if (Loaded) return;
        Reload();
    }

    public static void Reload()
    {
        Loaded = true;

        // --- 1. 设备本机的覆盖文件 ---
        string overridePath = OverridePath;
        if (File.Exists(overridePath))
        {
            try
            {
                if (Apply(File.ReadAllText(overridePath), "本机配置 " + overridePath))
                {
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[接口地址] 本机配置读取失败，改用随包默认：{e.Message}");
            }
        }

        // --- 2. 随包的默认配置 ---
        TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
        if (asset == null)
        {
            baseUrl = FallbackBaseUrl;
            Source = "代码兜底";
            Debug.LogError($"[接口地址] Resources 里找不到 {ResourcePath}.json，退到代码兜底 {baseUrl}");
            return;
        }

        if (!Apply(asset.text, "随包配置 " + ResourcePath))
        {
            baseUrl = FallbackBaseUrl;
            Source = "代码兜底";
            Debug.LogError($"[接口地址] 随包配置不可用，退到代码兜底 {baseUrl}");
            return;
        }

        // 落地一份，现场就能直接改这份而不用重新出包
        TryWriteOverride(asset.text);
    }

    private static bool Apply(string json, string source)
    {
        if (string.IsNullOrEmpty(json)) return false;

        Payload payload;
        try
        {
            payload = JsonUtility.FromJson<Payload>(json);
        }
        catch (Exception e)
        {
            Debug.LogError($"[接口地址] {source} 解析失败：{e.Message}");
            return false;
        }

        if (payload == null || string.IsNullOrEmpty(payload.baseUrl))
        {
            Debug.LogError($"[接口地址] {source} 里 baseUrl 是空的");
            return false;
        }

        string normalized = Normalize(payload.baseUrl);
        if (normalized == null)
        {
            Debug.LogError($"[接口地址] {source} 里 baseUrl 不合法：{payload.baseUrl}（需要 http:// 或 https:// 开头）");
            return false;
        }

        baseUrl = normalized;
        try
        {
            p0Mode = P0DataModeParser.Parse(payload.p0DataMode);
        }
        catch (Exception e)
        {
            Debug.LogError($"[P0 数据模式] {source} 里的 p0DataMode 不合法：{e.Message}");
            return false;
        }

        try
        {
            certMode = P0DataModeParser.Parse(payload.certDataMode);
        }
        catch (Exception e)
        {
            Debug.LogError($"[证件数据模式] {source} 里的 certDataMode 不合法：{e.Message}");
            return false;
        }

        debugDefaultCompanyId = (payload.debugDefaultCompanyId ?? string.Empty).Trim();
        portalUsername = (payload.portalUsername ?? string.Empty).Trim();
        portalPwd = (payload.portalPwd ?? string.Empty).Trim();
        Source = source;
        Debug.Log($"[接口地址] 来源：{source}，采用 {baseUrl}；P0 数据模式：{p0Mode}；证件数据模式：{certMode}");
        if (debugDefaultCompanyId.Length > 0)
            Debug.LogWarning($"[接口地址] 已启用调试默认企业 {debugDefaultCompanyId}，交付前记得清空");

        // 只打账号名，绝不打 pwd 串本身 —— 写进 logcat 就等于泄出去了
        if (portalUsername.Length > 0 && portalPwd.Length == 0)
            Debug.LogError("[门户账号] 只配了 portalUsername 没配 portalPwd，两项必须成对，本次仍用内置账号");
        else if (portalUsername.Length == 0 && portalPwd.Length > 0)
            Debug.LogError("[门户账号] 只配了 portalPwd 没配 portalUsername，两项必须成对，本次仍用内置账号");
        else if (portalUsername.Length > 0)
            Debug.Log($"[门户账号] 配置覆盖生效，账号 {portalUsername}");

        return true;
    }

    /// <summary>
    /// 去空白、补结尾斜杠。少写一个斜杠拼出来就是 404，而现场改配置的人未必知道这个规矩，
    /// 所以在这儿兜住而不是要求配置写对。
    /// </summary>
    private static string Normalize(string url)
    {
        string trimmed = url.Trim();
        if (trimmed.Length == 0) return null;

        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return trimmed.EndsWith("/") ? trimmed : trimmed + "/";
    }

    private static void TryWriteOverride(string json)
    {
        try
        {
            File.WriteAllText(OverridePath, json);
            Debug.Log($"[接口地址] 已落地一份可现场编辑的配置：{OverridePath}");
        }
        catch (Exception e)
        {
            // 落地失败不影响本次运行，地址已经从随包那份读到了
            Debug.LogWarning($"[接口地址] 配置落地失败（本次仍用随包那份）：{e.Message}");
        }
    }
}
