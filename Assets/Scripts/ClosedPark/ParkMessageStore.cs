using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public sealed class ParkMessage
{
    public const string KindText = "text";
    public const string KindVoice = "voice";

    public string id;
    public string kind;
    public string text;
    /// <summary>语音文件名，相对 <see cref="ParkMessageStore.VoiceDirectory"/>。</summary>
    public string voiceFile;
    public float durationSeconds;
    /// <summary>本机时间，<c>yyyy-MM-dd HH:mm:ss</c>。</summary>
    public string createdAt;

    public bool IsVoice => kind == KindVoice;
}

/// <summary>
/// 园区信息发布的本机存档。需求确认过：发布只落到眼镜本地，不接后端。
///
/// 索引是 <c>persistentDataPath/park_messages.json</c>，语音是
/// <c>persistentDataPath/park_voice/*.wav</c>。语音在【发布时】才写盘：
/// 草稿阶段只在内存里，放弃草稿不会留下孤儿文件。
///
/// 写入失败抛异常给调用方（UI 要告诉用户"保存失败"），不像审核存档那样吞掉——
/// 那边是标注，这边用户点的是"发布"，没存上必须当场知道。
/// </summary>
public static class ParkMessageStore
{
    public const string FileName = "park_messages.json";
    public const string VoiceFolder = "park_voice";
    public const int MaximumTextLength = 500;

    private static List<ParkMessage> cache;

    public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);
    public static string VoiceDirectory => Path.Combine(Application.persistentDataPath, VoiceFolder);

    /// <summary>全部已发布消息，新的在前。</summary>
    public static IReadOnlyList<ParkMessage> All
    {
        get
        {
            EnsureLoaded();
            return cache;
        }
    }

    public static string VoicePath(ParkMessage message) =>
        message == null || string.IsNullOrEmpty(message.voiceFile)
            ? null
            : Path.Combine(VoiceDirectory, message.voiceFile);

    public static ParkMessage PublishText(string text)
    {
        string content = text?.Trim();
        if (string.IsNullOrEmpty(content)) throw new ArgumentException("文字内容为空");
        if (content.Length > MaximumTextLength)
            throw new ArgumentException($"文字不能超过 {MaximumTextLength} 字");

        ParkMessage message = NewMessage(ParkMessage.KindText);
        message.text = content;
        Add(message);
        return message;
    }

    public static ParkMessage PublishVoice(float[] samples, int frequency)
    {
        if (samples == null || samples.Length == 0) throw new ArgumentException("语音为空");

        ParkMessage message = NewMessage(ParkMessage.KindVoice);
        message.voiceFile = message.id + ".wav";
        message.durationSeconds = samples.Length / (float)frequency;
        try
        {
            Directory.CreateDirectory(VoiceDirectory);
            WavFile.Write(VoicePath(message), samples, frequency);
            Add(message);
        }
        catch
        {
            // wav 写了一半，或索引没写进去，这个文件就再也找不到了，顺手删掉
            TryDelete(VoicePath(message));
            throw;
        }
        return message;
    }

    private static ParkMessage NewMessage(string kind)
    {
        DateTime now = DateTime.Now;
        return new ParkMessage
        {
            id = now.ToString("yyyyMMdd_HHmmss_fff"),
            kind = kind,
            createdAt = now.ToString("yyyy-MM-dd HH:mm:ss"),
        };
    }

    private static void Add(ParkMessage message)
    {
        EnsureLoaded();
        cache.Insert(0, message);
        try
        {
            Write();
        }
        catch
        {
            cache.RemoveAt(0);
            throw;
        }
    }

    private static void EnsureLoaded()
    {
        if (cache != null) return;
        cache = new List<ParkMessage>();
        if (!File.Exists(FilePath)) return;

        try
        {
            Wrapper wrapper = JsonUtility.FromJson<Wrapper>(File.ReadAllText(FilePath));
            if (wrapper?.items != null) cache.AddRange(wrapper.items);
        }
        catch (Exception e)
        {
            // 坏档挪到一边再从空开始，下一次写盘才不会把它覆盖掉，现场还能把旧记录捞回来
            string backup = FilePath + ".corrupt";
            Debug.LogError($"[园区发布] 本机存档读取失败，已另存为 {backup}：{e.Message}");
            TryDelete(backup);
            File.Move(FilePath, backup);
        }
    }

    private static void Write()
    {
        var wrapper = new Wrapper { items = cache };
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonUtility.ToJson(wrapper));
        TryDelete(FilePath);
        File.Move(temp, FilePath);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[园区发布] 删除 {path} 失败：{e.Message}");
        }
    }

    [Serializable]
    private sealed class Wrapper
    {
        public List<ParkMessage> items = new List<ParkMessage>();
    }
}
