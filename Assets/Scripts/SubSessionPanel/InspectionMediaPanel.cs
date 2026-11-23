using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.Video;

public sealed class InspectionMediaPanel : MonoBehaviour
{
    public RawImage display;
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI statusText;
    public Slider progressSlider;
    public TextMeshProUGUI durationText;
    public Button playPauseButton;

    private InspectionAttachmentService service;
    private List<InspectionAttachment> attachments = new List<InspectionAttachment>();
    private AudioSource audioSource;
    private VideoPlayer videoPlayer;
    private Texture2D imageTexture;
    private int index;
    private int generation;

    public bool IsOpen => gameObject.activeSelf;

    public static InspectionMediaPanel Create(
        Transform parent, InspectionAttachmentService service)
    {
        RectTransform root = RuntimeWorldUi.Panel(parent, "RuntimeInspectionMediaPanel",
            new Vector2(1080, 760), Vector2.zero);
        TextMeshProUGUI title = RuntimeWorldUi.Text(root, "Title", "附件查看",
            new Vector2(0.03f, 0.91f), new Vector2(0.97f, 0.99f), 30,
            TextAlignmentOptions.Center);

        var displayGo = new GameObject("Display", typeof(RectTransform), typeof(RawImage));
        displayGo.transform.SetParent(root, false);
        RectTransform displayRect = displayGo.GetComponent<RectTransform>();
        displayRect.anchorMin = new Vector2(0.05f, 0.2f);
        displayRect.anchorMax = new Vector2(0.95f, 0.9f);
        displayRect.offsetMin = displayRect.offsetMax = Vector2.zero;
        RawImage display = displayGo.GetComponent<RawImage>();
        display.color = Color.white;

        Slider progress = Slider(root, "Progress", new Vector2(0.16f, 0.13f), new Vector2(0.7f, 0.18f));
        TextMeshProUGUI status = RuntimeWorldUi.Text(root, "Status", "",
            new Vector2(0.1f, 0.06f), new Vector2(0.9f, 0.13f), 22,
            TextAlignmentOptions.Center);
        TextMeshProUGUI duration = RuntimeWorldUi.Text(root, "Duration", "00:00 / 00:00",
            new Vector2(0.72f, 0.13f), new Vector2(0.96f, 0.19f), 20,
            TextAlignmentOptions.Center);
        Button previous = RuntimeWorldUi.Button(root, "Previous", "上一项",
            new Vector2(0.01f, 0.01f), new Vector2(0.14f, 0.11f));
        Button play = RuntimeWorldUi.Button(root, "Play", "播放/暂停",
            new Vector2(0.15f, 0.01f), new Vector2(0.31f, 0.11f));
        Button zoomOut = RuntimeWorldUi.Button(root, "ZoomOut", "缩小",
            new Vector2(0.32f, 0.01f), new Vector2(0.44f, 0.11f));
        Button zoomIn = RuntimeWorldUi.Button(root, "ZoomIn", "放大",
            new Vector2(0.45f, 0.01f), new Vector2(0.57f, 0.11f));
        Button next = RuntimeWorldUi.Button(root, "Next", "下一项",
            new Vector2(0.58f, 0.01f), new Vector2(0.72f, 0.11f));
        Button close = RuntimeWorldUi.Button(root, "Close", "关闭",
            new Vector2(0.84f, 0.01f), new Vector2(0.99f, 0.11f));

        InspectionMediaPanel panel = root.gameObject.AddComponent<InspectionMediaPanel>();
        panel.service = service;
        panel.display = display;
        panel.titleText = title;
        panel.statusText = status;
        panel.progressSlider = progress;
        panel.durationText = duration;
        panel.playPauseButton = play;
        panel.audioSource = root.gameObject.AddComponent<AudioSource>();
        panel.videoPlayer = root.gameObject.AddComponent<VideoPlayer>();
        panel.videoPlayer.playOnAwake = false;
        panel.videoPlayer.renderMode = VideoRenderMode.APIOnly;
        panel.videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
        panel.videoPlayer.controlledAudioTrackCount = 1;
        panel.videoPlayer.SetTargetAudioSource(0, panel.audioSource);
        panel.videoPlayer.prepareCompleted += source => panel.OnVideoPrepared(source);
        panel.videoPlayer.errorReceived += (source, message) => panel.OnVideoError(message);
        previous.onClick.AddListener(() => panel.Turn(-1));
        next.onClick.AddListener(() => panel.Turn(1));
        play.onClick.AddListener(panel.TogglePlayback);
        zoomOut.onClick.AddListener(() => panel.Zoom(0.8f));
        zoomIn.onClick.AddListener(() => panel.Zoom(1.25f));
        close.onClick.AddListener(panel.Close);
        progress.onValueChanged.AddListener(panel.Seek);
        root.gameObject.SetActive(false);
        return panel;
    }

    public void Open(List<InspectionAttachment> items, int startIndex = 0)
    {
        attachments = items ?? new List<InspectionAttachment>();
        index = Mathf.Clamp(startIndex, 0, Mathf.Max(0, attachments.Count - 1));
        gameObject.SetActive(true);
        if (attachments.Count == 0)
        {
            SetStatus("暂无附件");
            return;
        }
        StartCoroutine(LoadCurrent());
    }

    private IEnumerator LoadCurrent()
    {
        StopPlayback();
        service.CancelAndCleanup();
        int requestGeneration = ++generation;
        InspectionAttachment attachment = attachments[index];
        titleText.text = $"{index + 1}/{attachments.Count}  {attachment.attachName}";
        SetStatus("正在下载…");
        var result = new ServiceResult<string>();
        yield return service.Download(attachment,
            value =>
            {
                if (requestGeneration == generation && progressSlider != null)
                    progressSlider.SetValueWithoutNotify(value);
            }, result);
        if (requestGeneration != generation) yield break;
        if (!result.ok)
        {
            SetStatus(result.error);
            yield break;
        }

        switch (InspectionMediaKindResolver.Resolve(attachment.mimeType))
        {
            case InspectionMediaKind.Image:
                yield return LoadImage(result.value, requestGeneration);
                break;
            case InspectionMediaKind.Audio:
                if (attachment.mimeType == "audio/mp4")
                    PrepareVideoPlayer(result.value, "正在准备 M4A 音频…");
                else
                    yield return LoadAudio(result.value, attachment.mimeType, requestGeneration);
                break;
            case InspectionMediaKind.Video:
                PrepareVideoPlayer(result.value, "正在准备视频…");
                break;
            default:
                SetStatus("不支持的附件格式：" + (attachment.mimeType ?? "未提供 MIME"));
                break;
        }
    }

    private IEnumerator LoadImage(string path, int requestGeneration)
    {
        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(ToFileUrl(path)))
        {
            yield return request.SendWebRequest();
            if (requestGeneration != generation) yield break;
            if (request.result != UnityWebRequest.Result.Success)
            {
                SetStatus("图片文件损坏或无法读取");
                yield break;
            }
            imageTexture = DownloadHandlerTexture.GetContent(request);
            display.texture = imageTexture;
            display.rectTransform.localScale = Vector3.one;
            SetStatus("图片已加载");
        }
    }

    private IEnumerator LoadAudio(string path, string mimeType, int requestGeneration)
    {
        AudioType type = mimeType == "audio/wav" || mimeType == "audio/x-wav"
            ? AudioType.WAV : mimeType == "audio/mpeg" ? AudioType.MPEG : AudioType.UNKNOWN;
        using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(ToFileUrl(path), type))
        {
            yield return request.SendWebRequest();
            if (requestGeneration != generation) yield break;
            if (request.result != UnityWebRequest.Result.Success)
            {
                SetStatus("音频文件损坏或格式不受设备支持");
                yield break;
            }
            audioSource.clip = DownloadHandlerAudioClip.GetContent(request);
            SetStatus("音频已加载，点击播放");
        }
    }

    private void OnVideoPrepared(VideoPlayer source)
    {
        if (!IsOpen) return;
        if (source.texture != null) display.texture = source.texture;
        SetStatus(source.texture == null ? "音频已加载，点击播放" : "视频已加载，点击播放");
    }

    private void OnVideoError(string message)
    {
        if (!IsOpen) return;
        SetStatus("视频或 M4A 文件损坏，或格式不受设备支持");
    }

    private void Update()
    {
        if (!IsOpen || progressSlider == null) return;
        if (audioSource != null && audioSource.clip != null && audioSource.clip.length > 0)
        {
            progressSlider.SetValueWithoutNotify(audioSource.time / audioSource.clip.length);
            SetDuration(audioSource.time, audioSource.clip.length);
        }
        else if (videoPlayer != null && videoPlayer.isPrepared && videoPlayer.length > 0)
        {
            progressSlider.SetValueWithoutNotify((float)(videoPlayer.time / videoPlayer.length));
            SetDuration(videoPlayer.time, videoPlayer.length);
        }
    }

    private void TogglePlayback()
    {
        if (audioSource != null && audioSource.clip != null)
        {
            if (audioSource.isPlaying) audioSource.Pause(); else audioSource.Play();
            return;
        }
        if (videoPlayer != null && videoPlayer.isPrepared)
        {
            if (videoPlayer.isPlaying) videoPlayer.Pause(); else videoPlayer.Play();
        }
    }

    private void Seek(float value)
    {
        if (audioSource != null && audioSource.clip != null)
            audioSource.time = Mathf.Clamp01(value) * audioSource.clip.length;
        else if (videoPlayer != null && videoPlayer.isPrepared)
            videoPlayer.time = Mathf.Clamp01(value) * videoPlayer.length;
    }

    private void Turn(int delta)
    {
        if (attachments.Count == 0) return;
        index = (index + delta + attachments.Count) % attachments.Count;
        StartCoroutine(LoadCurrent());
    }

    private void Zoom(float factor)
    {
        if (imageTexture == null) return;
        float scale = Mathf.Clamp(display.rectTransform.localScale.x * factor, 0.5f, 3f);
        display.rectTransform.localScale = Vector3.one * scale;
    }

    public void Close()
    {
        generation++;
        StopPlayback();
        service.CancelAndCleanup();
        gameObject.SetActive(false);
    }

    private void StopPlayback()
    {
        if (audioSource != null) { audioSource.Stop(); audioSource.clip = null; }
        if (videoPlayer != null) { videoPlayer.Stop(); videoPlayer.url = string.Empty; }
        if (display != null) display.texture = null;
        if (imageTexture != null) Destroy(imageTexture);
        imageTexture = null;
        if (progressSlider != null) progressSlider.SetValueWithoutNotify(0);
        if (durationText != null) durationText.text = "00:00 / 00:00";
    }

    private void SetStatus(string message)
    {
        if (statusText != null) statusText.text = message ?? string.Empty;
    }

    private static string ToFileUrl(string path)
    {
        return path.Contains("://") ? path : "file:///" + path.Replace('\\', '/');
    }

    private void PrepareVideoPlayer(string path, string status)
    {
        videoPlayer.url = ToFileUrl(path);
        videoPlayer.Prepare();
        SetStatus(status);
    }

    private void SetDuration(double current, double total)
    {
        if (durationText == null) return;
        durationText.text = FormatTime(current) + " / " + FormatTime(total);
    }

    private static string FormatTime(double seconds)
    {
        int safeSeconds = Mathf.Max(0, Mathf.FloorToInt((float)seconds));
        return string.Format("{0:00}:{1:00}", safeSeconds / 60, safeSeconds % 60);
    }

    private static Slider Slider(
        Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
    {
        var root = new GameObject(name, typeof(RectTransform), typeof(Slider));
        root.transform.SetParent(parent, false);
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
        background.transform.SetParent(root.transform, false);
        RectTransform bg = background.GetComponent<RectTransform>();
        bg.anchorMin = Vector2.zero; bg.anchorMax = Vector2.one;
        bg.offsetMin = bg.offsetMax = Vector2.zero;
        background.GetComponent<Image>().color = new Color(1, 1, 1, 0.25f);
        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(background.transform, false);
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero; fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
        fill.GetComponent<Image>().color = new Color(0.08f, 0.65f, 0.95f, 1f);
        Slider slider = root.GetComponent<Slider>();
        slider.fillRect = fillRect;
        slider.minValue = 0; slider.maxValue = 1;
        return slider;
    }

    private void OnDisable() { StopPlayback(); }
    private void OnApplicationPause(bool paused) { if (paused) StopPlayback(); }
}
