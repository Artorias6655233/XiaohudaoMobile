using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 出入园审核卡片的常用备注短语配置。
///
/// 短语按审核意图分成两组：<see cref="ApprovePhrases"/> 给「审批通过」用，
/// <see cref="RejectPhrases"/> 给「审批不通过」用。卡片一次只显示其中一组，
/// 所以 <see cref="MaxPhrases"/> 是【每组】的上限，不是总数。
///
/// 短语不写死在代码里，读两处，优先级从高到低：
///   1. <c>Application.persistentDataPath/entry_exit_phrases.json</c>
///      —— 现场 adb push 就能改，不用重新出包；
///   2. 随包的 <c>Assets/Resources/Config/entry_exit_phrases.json</c>
///      —— 仓库里那份默认值，首次运行会自动落地一份到 (1)，方便现场直接改。
///
/// 【版本号的意义】(1) 的优先级高于 (2)，而现场设备上已经落地过一份只有 phrases 的旧格式。
/// 光按优先级读的话，升级后现场会一条短语都不显示，且看不出是配置的问题。所以加了
/// <c>version</c>：本机那份比随包那份旧就判定为过期，改用随包的并覆盖落地，同时打 Warning。
/// 现场手改本机配置时记得把 version 一并带上，否则下次启动会被随包那份覆盖掉。
///
/// 为什么默认值放 Resources 而不是 StreamingAssets：<c>sandtable/.gitignore</c> 把
/// <c>Assets/StreamingAssets/</c> 整个忽略了（那儿放的是几百 KB 的接口镜像），配置扔进去
/// 就进不了版本控制，新克隆一份代码就没有短语可用。Resources 没被忽略，而且
/// <c>Resources.Load</c> 是同步的，省掉了 StreamingAssets 在 Android 上必须走
/// UnityWebRequest 的那套协程。
///
/// 刻意【不】在代码里再留一份默认短语兜底：那样就有两处真相，改了 json 却发现没生效的时候
/// 最难查。加载失败就是失败，打 Error 并让卡片上一条短语都不显示，问题当场可见。
/// 同理，旧格式里那个不分组的 phrases 也【不】猜着拆成两组——直接走版本升级路径。
/// </summary>
public static class EntryExitPhraseConfig
{
    public const string FileName = "entry_exit_phrases.json";

    /// <summary>Resources 下的相对路径（不带扩展名，Resources.Load 的规矩）。</summary>
    public const string ResourcePath = "Config/entry_exit_phrases";

    /// <summary>
    /// 【每组】最多能摆下的短语数（4 列 x 3 行）。配置里超出的部分会被丢掉，
    /// 但不静默——加载时会打日志说明丢了几条。
    /// </summary>
    public const int MaxPhrases = 12;

    /// <summary>「审批通过」意图下可点的短语。加载失败时是空数组，不是 null。</summary>
    public static string[] ApprovePhrases { get; private set; } = new string[0];

    /// <summary>「审批不通过」意图下可点的短语，也是判定「驳回原因是否填了」的依据。</summary>
    public static string[] RejectPhrases { get; private set; } = new string[0];

    /// <summary>是否已经尝试过加载（不代表成功）。</summary>
    public static bool Loaded { get; private set; }

    public static string OverridePath => Path.Combine(Application.persistentDataPath, FileName);

    [Serializable]
    private class Payload
    {
        public int version;
        public string[] approvePhrases;
        public string[] rejectPhrases;
    }

    /// <summary>
    /// 保证短语已加载。重复调用是安全的：已经加载过就直接返回，不重复读盘。
    /// 现场刚 push 了新配置想立刻生效用 <see cref="Reload"/>。
    /// </summary>
    public static void Ensure()
    {
        if (Loaded) return;
        Reload();
    }

    public static void Reload()
    {
        Loaded = true;
        ApprovePhrases = new string[0];
        RejectPhrases = new string[0];

        // --- 随包那份先读出来，它既是兜底也是版本基准 ---
        TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
        if (asset == null)
        {
            Debug.LogError($"[出入园短语] Resources 里找不到 {ResourcePath}.json，卡片上不会有短语可点");
            return;
        }

        int packagedVersion = VersionOf(asset.text);

        // --- 1. 设备本机的覆盖文件，版本不比随包旧才采用 ---
        string overridePath = OverridePath;
        if (File.Exists(overridePath))
        {
            try
            {
                string localJson = File.ReadAllText(overridePath);
                int localVersion = VersionOf(localJson);

                if (localVersion < packagedVersion)
                {
                    Debug.LogWarning(
                        $"[出入园短语] 本机配置版本 {localVersion} 旧于随包版本 {packagedVersion}，" +
                        $"改用随包那份并覆盖本机文件：{overridePath}");
                }
                else if (Apply(localJson, "本机配置 " + overridePath))
                {
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[出入园短语] 本机配置读取失败，改用随包默认：{e.Message}");
            }
        }

        // --- 2. 随包的默认配置 ---
        if (!Apply(asset.text, "随包配置 " + ResourcePath))
        {
            return;
        }

        // 落地一份，现场就能直接改这份而不用重新出包
        TryWriteOverride(asset.text);
    }

    /// <summary>只取 version 字段。解析不了或没这个字段一律算 0（旧格式）。</summary>
    private static int VersionOf(string json)
    {
        if (string.IsNullOrEmpty(json)) return 0;

        try
        {
            Payload payload = JsonUtility.FromJson<Payload>(json);
            return payload != null ? payload.version : 0;
        }
        catch
        {
            return 0;
        }
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
            Debug.LogError($"[出入园短语] {source} 解析失败：{e.Message}");
            return false;
        }

        if (payload == null)
        {
            Debug.LogError($"[出入园短语] {source} 解析出来是空的");
            return false;
        }

        string[] approve = Clean(payload.approvePhrases, source, "approvePhrases");
        string[] reject = Clean(payload.rejectPhrases, source, "rejectPhrases");

        if (approve.Length == 0 && reject.Length == 0)
        {
            Debug.LogError($"[出入园短语] {source} 里 approvePhrases 和 rejectPhrases 都是空的");
            return false;
        }

        ApprovePhrases = approve;
        RejectPhrases = reject;

        Debug.Log($"[出入园短语] 来源：{source}，通过组 {approve.Length} 条，驳回组 {reject.Length} 条");
        return true;
    }

    private static string[] Clean(string[] raw, string source, string field)
    {
        if (raw == null) return new string[0];

        List<string> kept = new List<string>();
        int blank = 0;

        foreach (string phrase in raw)
        {
            if (string.IsNullOrEmpty(phrase) || phrase.Trim().Length == 0)
            {
                blank++;
                continue;
            }

            if (kept.Count >= MaxPhrases) break;
            kept.Add(phrase.Trim());
        }

        int dropped = raw.Length - kept.Count - blank;
        if (blank > 0 || dropped > 0)
        {
            Debug.Log($"[出入园短语] {source} 的 {field}：采用 {kept.Count} 条" +
                (blank > 0 ? $"，跳过空白 {blank} 条" : string.Empty) +
                (dropped > 0 ? $"，超出每组上限 {MaxPhrases} 丢弃 {dropped} 条" : string.Empty));
        }

        return kept.ToArray();
    }

    private static void TryWriteOverride(string json)
    {
        try
        {
            File.WriteAllText(OverridePath, json);
            Debug.Log($"[出入园短语] 已落地一份可现场编辑的配置：{OverridePath}");
        }
        catch (Exception e)
        {
            // 落地失败不影响本次运行，短语已经从随包那份读到了
            Debug.LogWarning($"[出入园短语] 配置落地失败（本次仍用随包那份）：{e.Message}");
        }
    }
}
