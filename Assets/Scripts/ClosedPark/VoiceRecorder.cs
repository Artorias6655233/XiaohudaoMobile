using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

/// <summary>
/// 按住说话的录音器：只管麦克风的开、停、取样本，不碰 UI。
///
/// 【为什么录音缓冲比上限多几秒】非循环录音跑到缓冲末尾后 <c>Microphone.GetPosition</c>
/// 会归零，取不到已录长度。所以缓冲留余量，由调用方在 <see cref="MaxSeconds"/> 时主动停。
/// 万一卡顿太久让它自己跑到了头，<see cref="StoppedBySelf"/> 会变真，<see cref="Stop"/> 按整段缓冲取样本。
/// </summary>
public sealed class VoiceRecorder
{
    public const int MaxSeconds = 30;
    public const float MinSeconds = 0.5f;

    // 识别模型的原生采样率；设备不支持时退到它支持的范围，识别时 sherpa-onnx 会自己重采样
    private const int PreferredFrequency = 16000;
    private const int LevelWindow = 512;
    private const int BufferMarginSeconds = 5;

    // 裁首尾静音用：20ms 一窗，RMS 低于阈值算静音，两端各留 0.2 秒免得切掉字头字尾
    private const float SilenceRms = 0.006f;
    private const float EdgePaddingSeconds = 0.2f;

    private string device;
    private AudioClip clip;
    private int lastPosition;
    private readonly float[] levelBuffer = new float[LevelWindow];

    public bool IsRecording => clip != null;
    public int Frequency { get; private set; }

    /// <summary>缓冲已被录满、麦克风自己停了（调用方没来得及在上限时停）。</summary>
    public bool StoppedBySelf => clip != null && !Microphone.IsRecording(device);

    public float ElapsedSeconds
    {
        get
        {
            if (clip == null) return 0f;
            int position = Microphone.GetPosition(device);
            if (position > 0) lastPosition = position;
            return lastPosition / (float)Frequency;
        }
    }

    public static bool HasPermission
    {
        get
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return Permission.HasUserAuthorizedPermission(Permission.Microphone);
#else
            return true;
#endif
        }
    }

    public static void RequestPermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        Permission.RequestUserPermission(Permission.Microphone);
#endif
    }

    /// <summary>开始录音。失败时返回给用户看的原因，成功返回 null。</summary>
    public string TryStart()
    {
        if (clip != null) return null;
        if (!HasPermission) return VoiceCopy.ErrorNoPermission;
        if (Microphone.devices.Length == 0) return VoiceCopy.ErrorNoMicrophone;

        device = Microphone.devices[0];
        Microphone.GetDeviceCaps(device, out int min, out int max);
        Frequency = min == 0 && max == 0 ? PreferredFrequency : Mathf.Clamp(PreferredFrequency, min, max);
        lastPosition = 0;
        clip = Microphone.Start(device, false, MaxSeconds + BufferMarginSeconds, Frequency);
        if (clip == null)
        {
            Debug.LogError($"[园区语音] Microphone.Start 失败，设备 {device}，采样率 {Frequency}");
            return VoiceCopy.ErrorNoMicrophone;
        }
        Debug.Log($"[园区语音] 开始录音，设备 {device}，采样率 {Frequency}");
        return null;
    }

    /// <summary>最近一小段的音量（0~1），给音量条用，证明麦克风确实在收音。</summary>
    public float CurrentLevel()
    {
        if (clip == null) return 0f;
        int position = Microphone.GetPosition(device);
        if (position < LevelWindow) return 0f;
        clip.GetData(levelBuffer, position - LevelWindow);
        float sum = 0f;
        for (int i = 0; i < LevelWindow; i++) sum += levelBuffer[i] * levelBuffer[i];
        // RMS 很少超过 0.3，放大一下让说话时条能明显跳动
        return Mathf.Clamp01(Mathf.Sqrt(sum / LevelWindow) * 4f);
    }

    /// <summary>停止并取出本次录到的原始样本（未裁剪）。</summary>
    public float[] Stop()
    {
        if (clip == null) return new float[0];
        int position = Microphone.GetPosition(device);
        // 位置归零有两种情况：刚开录还没数据，或缓冲已录满自己停了——后者整段都是有效录音
        if (position <= 0) position = StoppedBySelf ? clip.samples : lastPosition;
        Microphone.End(device);

        // 超出上限的部分只可能来自卡顿后缓冲录满，截回上限，和提示里说的"最长 30 秒"一致
        position = Mathf.Min(position, MaxSeconds * Frequency);
        var samples = new float[Mathf.Max(0, position)];
        if (samples.Length > 0) clip.GetData(samples, 0);
        Object.Destroy(clip);
        clip = null;
        return samples;
    }

    public void Cancel()
    {
        if (clip == null) return;
        Microphone.End(device);
        Object.Destroy(clip);
        clip = null;
    }

    /// <summary>
    /// 裁掉首尾静音。整段都低于阈值时返回 null，表示"没听到说话声"。
    /// </summary>
    public static float[] TrimSilence(float[] samples, int frequency)
    {
        int window = Mathf.Max(1, frequency / 50);
        int first = -1, last = -1;
        float peak = 0f;
        for (int start = 0; start + window <= samples.Length; start += window)
        {
            float sum = 0f;
            for (int i = start; i < start + window; i++) sum += samples[i] * samples[i];
            float rms = Mathf.Sqrt(sum / window);
            peak = Mathf.Max(peak, rms);
            if (rms < SilenceRms) continue;
            if (first < 0) first = start;
            last = start + window;
        }

        // 阈值是拍脑袋定的，真机麦克风增益未知；把峰值打出来，误判时能照着调
        Debug.Log($"[园区语音] 录音峰值 RMS {peak:F4}，静音阈值 {SilenceRms}");
        if (first < 0) return null;

        int padding = Mathf.RoundToInt(EdgePaddingSeconds * frequency);
        int from = Mathf.Max(0, first - padding);
        int to = Mathf.Min(samples.Length, last + padding);
        var trimmed = new float[to - from];
        System.Array.Copy(samples, from, trimmed, 0, trimmed.Length);
        return trimmed;
    }
}
