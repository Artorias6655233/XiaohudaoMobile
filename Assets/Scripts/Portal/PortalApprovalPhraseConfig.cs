using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 作业票审批意见的常用短语。
///
/// 【为什么审批意见只能选、不能自由输入】服务端下发的 <c>audit</c> 按钮带
/// <c>opinionRequired=true</c>，意见是必填的。眼镜端自由输入要拉起 VR 键盘
/// （工程里有，见 <c>P0AnnotationPanel</c>），但手柄打一段中文的代价远大于点一个短语，
/// 而审批意见本来就高度模板化。先只做短语；真需要自由输入再叠上去，两者不冲突。
///
/// 读两处，优先级从高到低，与 <see cref="EntryExitPhraseConfig"/> 同一套约定：
///   1. <c>Application.persistentDataPath/approval_phrases.json</c> —— 现场 adb push 就能改；
///   2. 随包的 <c>Assets/Resources/Config/approval_phrases</c>。
///
/// 这份配置是新加的，设备上不存在旧格式，所以【不】做版本迁移那一套。
/// 本机那份读不出来就退回随包那份并打 Warning，不猜、不合并。
///
/// 刻意不在代码里留一份兜底短语：那样就有两处真相，改了 json 却没生效时最难查。
/// 加载失败就是失败，卡片上一条短语都没有，问题当场可见。
/// </summary>
public static class PortalApprovalPhraseConfig
{
    public const string FileName = "approval_phrases.json";

    /// <summary>Resources 下的相对路径（不带扩展名，<c>Resources.Load</c> 的规矩）。</summary>
    public const string ResourcePath = "Config/approval_phrases";

    /// <summary>【每组】最多摆下的短语数。卡片一列一条，超出的丢掉并打日志。</summary>
    public const int MaxPhrases = 6;

    /// <summary>「通过」时可选的意见。加载失败时是空数组，不是 null。</summary>
    public static string[] PassPhrases { get; private set; } = new string[0];

    /// <summary>「不通过」时可选的意见。</summary>
    public static string[] RejectPhrases { get; private set; } = new string[0];

    /// <summary>是否已经尝试过加载（不代表成功）。</summary>
    public static bool Loaded { get; private set; }

    public static string OverridePath => Path.Combine(Application.persistentDataPath, FileName);

    [Serializable]
    private class Payload
    {
        public int version;
        public string[] passPhrases;
        public string[] rejectPhrases;
    }

    /// <summary>按结论取该显示哪一组。</summary>
    public static string[] For(bool passed)
    {
        EnsureLoaded();
        return passed ? PassPhrases : RejectPhrases;
    }

    public static void EnsureLoaded()
    {
        if (Loaded) return;
        Loaded = true;

        string json = ReadOverride();
        string source = "设备本机";

        if (json == null)
        {
            TextAsset packaged = Resources.Load<TextAsset>(ResourcePath);
            if (packaged == null)
            {
                Debug.LogError($"[审批短语] 随包配置缺失：Resources/{ResourcePath}.json，审批意见将无可选项");
                return;
            }

            json = packaged.text;
            source = "随包";
        }

        Payload payload;
        try
        {
            payload = JsonUtility.FromJson<Payload>(json);
        }
        catch (Exception e)
        {
            Debug.LogError($"[审批短语] {source}配置解析失败：{e.Message}");
            return;
        }

        if (payload == null)
        {
            Debug.LogError($"[审批短语] {source}配置解析结果为空");
            return;
        }

        PassPhrases = Trim(payload.passPhrases, "通过");
        RejectPhrases = Trim(payload.rejectPhrases, "不通过");

        Debug.Log($"[审批短语] 来源{source}，通过 {PassPhrases.Length} 条、" +
                  $"不通过 {RejectPhrases.Length} 条");
    }

    private static string ReadOverride()
    {
        try
        {
            string path = OverridePath;
            if (!File.Exists(path)) return null;
            return File.ReadAllText(path);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[审批短语] 本机配置读取失败，改用随包那份：{e.Message}");
            return null;
        }
    }

    /// <summary>去掉空条目并截到上限。丢了东西一定打日志，不静默。</summary>
    private static string[] Trim(string[] raw, string group)
    {
        List<string> kept = new List<string>();
        if (raw != null)
        {
            foreach (string item in raw)
            {
                if (!string.IsNullOrWhiteSpace(item)) kept.Add(item.Trim());
            }
        }

        if (kept.Count == 0)
        {
            Debug.LogError($"[审批短语] 「{group}」一条短语都没有，该结论将无法提交（意见必填）");
            return new string[0];
        }

        if (kept.Count > MaxPhrases)
        {
            Debug.LogWarning($"[审批短语] 「{group}」有 {kept.Count} 条，超出上限 {MaxPhrases}，" +
                             $"丢弃末尾 {kept.Count - MaxPhrases} 条");
            kept.RemoveRange(MaxPhrases, kept.Count - MaxPhrases);
        }

        return kept.ToArray();
    }
}
