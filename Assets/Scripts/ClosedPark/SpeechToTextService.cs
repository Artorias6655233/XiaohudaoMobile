using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SherpaOnnx;
using UnityEngine;
using Debug = UnityEngine.Debug;

public enum SpeechModelState
{
    NotLoaded,
    Loading,
    Ready,
    Unavailable,
}

/// <summary>
/// 眼镜端离线语音转文字（sherpa-onnx + SenseVoice-Small int8，纯 CPU）。
///
/// 【模型不进 APK】约定由 adb 推到 <c>persistentDataPath/SenseVoice/</c>，需要
/// <see cref="ModelFile"/> 和 <see cref="TokensFile"/> 两个文件，缺一个都不可用。
/// 缺模型不影响录语音，只是没有文字草稿。
///
/// 【为什么是静态单例】模型常驻内存两百多 MB，面板开关多次也只加载一次。
///
/// 【为什么从 whisper.unity 换过来】2026-09-24 同一套园区用语测试（TTS 干净 / 加噪 / 压低音量）：
/// SenseVoice 字错率 0.7%，PC 单线程 RTF 0.095；whisper small-q5_1 加领域提示词 2.2%，4 线程 RTF 约 0.94。
/// 真机上 whisper base 一次要 24~34 秒，还会复读提示词、重复"抱歉抱歉抱歉"。SenseVoice 是非自回归模型，
/// 不补 30 秒窗口，耗时随音频长短走，也没有提示词可复读。
/// </summary>
public static class SpeechToTextService
{
    public const string ModelFolder = "SenseVoice";
    public const string ModelFile = "model.int8.onnx";
    public const string TokensFile = "tokens.txt";
    private const string DisplayName = "SenseVoice-int8";

    // PC 上 1 → 4 线程只快一倍（RTF 0.095 → 0.050）。识别时眼镜还在渲染，只给 2 个核
    private const int NumThreads = 2;

    // 纯噪声时 SenseVoice 会吐出单个「嗯」「我」（编辑器实测白噪 1 秒 / 3 秒都是这样），
    // 不足两个字就当没听出内容。代价是单说一个「好」也会被当成没识别到，语音消息仍可发送
    private const int MinMeaningfulChars = 2;

    private static OfflineRecognizer recognizer;
    private static Task loading;

    // 识别器不保证多线程同时 Decode 安全，排队串行；排队和识别分开计时，免得日志里的耗时混进等待
    private static readonly SemaphoreSlim inference = new SemaphoreSlim(1, 1);

    public static SpeechModelState State { get; private set; } = SpeechModelState.NotLoaded;

    public static string ModelDirectory => Path.Combine(Application.persistentDataPath, ModelFolder);

    public static Task EnsureLoadedAsync()
    {
        if (loading == null) loading = LoadAsync();
        return loading;
    }

    private static async Task LoadAsync()
    {
        string model = Path.Combine(ModelDirectory, ModelFile);
        string tokens = Path.Combine(ModelDirectory, TokensFile);
        if (!File.Exists(model) || !File.Exists(tokens))
        {
            Debug.LogWarning($"[园区语音] 未找到识别模型，请 adb push {ModelFile} 和 {TokensFile} 到 {ModelDirectory}");
            State = SpeechModelState.Unavailable;
            return;
        }

        State = SpeechModelState.Loading;
        var watch = Stopwatch.StartNew();
        try
        {
            recognizer = await Task.Run(() => new OfflineRecognizer(CreateConfig(model, tokens)));
        }
        catch (Exception e)
        {
            // 编辑器里没有 Windows 版原生库时是 DllNotFoundException，属于预期：编辑器只能录语音
            Debug.LogError($"[园区语音] 模型加载异常：{e}");
            recognizer = null;
        }

        State = recognizer != null ? SpeechModelState.Ready : SpeechModelState.Unavailable;
        Debug.Log($"[园区语音] 模型 {DisplayName} 加载{(recognizer != null ? "完成" : "失败")}，耗时 {watch.ElapsedMilliseconds} ms");
    }

    // 结构体没有默认构造（见 Plugins/SherpaOnnx/README.md），这里只填用到的字段，其余 0 / null 由 C API 落到默认值
    private static OfflineRecognizerConfig CreateConfig(string model, string tokens)
    {
        var config = new OfflineRecognizerConfig();
        config.FeatConfig.SampleRate = 16000;
        config.FeatConfig.FeatureDim = 80;
        config.ModelConfig.SenseVoice.Model = model;
        config.ModelConfig.SenseVoice.Language = "zh";
        // 逆文本规整：输出带标点，数字写成「3号」「下午3点」
        config.ModelConfig.SenseVoice.UseInverseTextNormalization = 1;
        config.ModelConfig.Tokens = tokens;
        config.ModelConfig.NumThreads = NumThreads;
        config.ModelConfig.Provider = "cpu";
        config.DecodingMethod = "greedy_search";
        return config;
    }

    /// <summary>
    /// 把一段单声道样本转成文字。模型不可用或识别失败返回 null；没听出内容返回空串。
    /// 采样率不是 16k 时 sherpa-onnx 内部会重采样。
    /// </summary>
    public static async Task<string> TranscribeAsync(float[] samples, int frequency)
    {
        await EnsureLoadedAsync();
        if (State != SpeechModelState.Ready) return null;

        var watch = Stopwatch.StartNew();
        long waitedMs;
        string text;
        await inference.WaitAsync();
        try
        {
            waitedMs = watch.ElapsedMilliseconds;
            text = await Task.Run(() => Decode(samples, frequency));
        }
        catch (Exception e)
        {
            Debug.LogError($"[园区语音] 识别异常：{e}");
            return null;
        }
        finally
        {
            inference.Release();
        }

        float audioSeconds = samples.Length / (float)frequency;
        Debug.Log($"[园区语音] 模型 {DisplayName} 识别 {audioSeconds:F1}s 音频耗时 {watch.ElapsedMilliseconds - waitedMs} ms" +
            $"（排队 {waitedMs} ms）：{text}");

        text = (text ?? string.Empty).Trim();
        if (CountMeaningfulChars(text) < MinMeaningfulChars)
        {
            if (text.Length > 0) Debug.LogWarning($"[园区语音] 识别结果「{text}」不足 {MinMeaningfulChars} 个字，按未识别处理");
            return string.Empty;
        }
        return text;
    }

    private static string Decode(float[] samples, int frequency)
    {
        using (OfflineStream stream = recognizer.CreateStream())
        {
            stream.AcceptWaveform(frequency, samples);
            recognizer.Decode(stream);
            return stream.Result.Text;
        }
    }

    private static int CountMeaningfulChars(string text)
    {
        int count = 0;
        foreach (char c in text)
            if (char.IsLetterOrDigit(c)) count++;
        return count;
    }
}
