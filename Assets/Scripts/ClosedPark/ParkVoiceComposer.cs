using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public enum VoiceDraftState
{
    Idle,
    Recording,
    Transcribing,
    Draft,
}

/// <summary>
/// 园区信息发布的「按住 X 说话」：一次录音同时产出语音消息草稿和文字草稿，分别发布到本机。
///
/// 交互口径（需求方要求"让用户清楚自己在做什么、能做到什么程度"）：
///   - 底部提示条常驻，文案随 <see cref="VoiceDraftState"/> 变，是主要反馈来源；
///   - 临时提示（太短、已发布、出错）盖住常驻文案 3 秒后自动退回；
///   - 录音时两张草稿卡片置灰，只剩提示条和音量条在动，注意力集中在"正在录"；
///   - 能力边界写在「说明」里，首次打开自动展开一次。
///
/// 【为什么不区分短按 / 长按】按下那一刻分不出是哪种，要么丢掉字头、要么按下就得开录；
/// 统一成按住即录，一段录音两用，X 键只有一种含义。
///
/// 【X 键和其他面板】本组件只在园区发布面板激活时 Update，和其他五处 X 快捷键按
/// subSessionType 天然互斥；文字框有焦点（正在打字）时不录音。
/// </summary>
public sealed class ParkVoiceComposer : MonoBehaviour
{
    private const float ToastSeconds = 3f;
    private const float CountdownSeconds = 5f;
    private const string InfoSeenKey = "park_voice_info_seen";

    private ParkVoiceComposerView view;
    private readonly VoiceRecorder recorder = new VoiceRecorder();
    private AudioSource audioSource;
    private BNG.VRKeyboardPro keyboard;
    private GameObject keyboardRoot;

    private VoiceDraftState state = VoiceDraftState.Idle;
    private float[] voiceDraft;
    private int voiceFrequency;
    private AudioClip draftClip;
    private AudioClip historyClip;
    private int playingHistoryIndex = -1;

    private bool recognitionEmpty;
    private bool textEdited;
    private bool settingTextFromCode;
    private bool overwriteArmed;
    private bool permissionAsked;
    private int transcribeTicket;
    private string missingGlyphHint;

    private string toast;
    private bool toastIsWarning;
    private float toastUntil;
    private int historyPage;

    public static ParkVoiceComposer Create(Transform panel, Vector2 position, TMP_FontAsset font,
        GameObject keyboardPrefab)
    {
        ParkVoiceComposerView view = ParkVoiceComposerView.Build(panel, position, font);
        ParkVoiceComposer composer = view.root.gameObject.AddComponent<ParkVoiceComposer>();
        composer.view = view;
        composer.audioSource = view.root.gameObject.AddComponent<AudioSource>();
        composer.audioSource.playOnAwake = false;
        composer.audioSource.spatialBlend = 0f;
        composer.AttachKeyboard(keyboardPrefab);
        composer.Bind();
        composer.RefreshAll();
        return composer;
    }

    // ------------------------------------------------------------------ 对外（原面板的按钮）

    /// <summary>把预设短语追加进文字草稿，算作手动编辑。</summary>
    public void AppendText(string phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase) || state == VoiceDraftState.Recording) return;
        string current = view.textInput.text ?? string.Empty;
        string separator = string.IsNullOrWhiteSpace(current) ? string.Empty : "；";
        if (current.Length + separator.Length + phrase.Length > ParkMessageStore.MaximumTextLength)
        {
            ShowToast(VoiceCopy.ToastTextTooLong, true);
            return;
        }
        view.textInput.text = current + separator + phrase;
    }

    /// <summary>发布文字草稿。返回是否真的写进了本机。</summary>
    public bool SendText()
    {
        string text = view.textInput.text;
        if (string.IsNullOrWhiteSpace(text))
        {
            ShowToast(VoiceCopy.ToastTextEmpty, true);
            return false;
        }
        if (state == VoiceDraftState.Transcribing) return false;
        if (!TryPublish(() => ParkMessageStore.PublishText(text))) return false;
        SetTextFromCode(string.Empty);
        textEdited = false;
        recognitionEmpty = false;
        missingGlyphHint = null;
        AfterPublish();
        return true;
    }

    public void ClearDrafts()
    {
        if (state == VoiceDraftState.Recording) return;
        ClearVoiceDraft();
        SetTextFromCode(string.Empty);
        textEdited = false;
        recognitionEmpty = false;
        missingGlyphHint = null;
        transcribeTicket++;
        state = VoiceDraftState.Idle;
        RefreshAll();
    }

    // ------------------------------------------------------------------ 生命周期

    private void OnEnable()
    {
        // AddComponent 时也会进来一次，那时 view 还没赋值、面板也还没真正显示；
        // 模型上百 MB，等用户第一次打开面板再加载，不在开机时占内存
        if (view == null) return;
        _ = PreloadModel();
        if (PlayerPrefs.GetInt(InfoSeenKey, 0) == 0)
        {
            view.infoOverlay.SetActive(true);
            PlayerPrefs.SetInt(InfoSeenKey, 1);
            PlayerPrefs.Save();
        }
    }

    private void OnDisable()
    {
        if (state == VoiceDraftState.Recording)
        {
            recorder.Cancel();
            state = HasAnyDraft() ? VoiceDraftState.Draft : VoiceDraftState.Idle;
        }
        StopPlayback();
        ReleaseHistoryClip();
        CloseKeyboard();
    }

    private void OnDestroy()
    {
        recorder.Cancel();
        if (draftClip != null) Destroy(draftClip);
        ReleaseHistoryClip();
        if (keyboardRoot != null) Destroy(keyboardRoot);
    }

    private void Update()
    {
        if (view == null) return;
        HandleXButton();
        if (audioSource.clip != null && !audioSource.isPlaying && (draftClip != null || historyClip != null))
            StopPlayback();
        RefreshHintBar();
    }

    private async System.Threading.Tasks.Task PreloadModel()
    {
        try
        {
            await SpeechToTextService.EnsureLoadedAsync();
        }
        catch (Exception e)
        {
            Debug.LogError($"[园区语音] 预加载模型异常：{e}");
        }
    }

    // ------------------------------------------------------------------ 录音

    private void HandleXButton()
    {
        BNG.InputBridge bridge = BNG.InputBridge.Instance;
        if (bridge == null) return;

        if (state == VoiceDraftState.Recording)
        {
            if (recorder.ElapsedSeconds >= VoiceRecorder.MaxSeconds || recorder.StoppedBySelf)
                FinishRecording(true);
            else if (!bridge.XButton) FinishRecording(false);
            return;
        }

        if (bridge.XButtonDown) BeginRecording();
    }

    private void BeginRecording()
    {
        if (view.infoOverlay.activeSelf) view.infoOverlay.SetActive(false);
        if (IsTyping()) return; // 提示条常驻显示 HintKeyboard，不必再弹
        if (state == VoiceDraftState.Transcribing)
        {
            ShowToast(VoiceCopy.ToastStillTranscribing, true);
            return;
        }
        if (textEdited && !string.IsNullOrWhiteSpace(view.textInput.text) && !overwriteArmed)
        {
            overwriteArmed = true;
            ShowToast(VoiceCopy.ToastConfirmOverwrite, true);
            return;
        }
        if (!VoiceRecorder.HasPermission && !permissionAsked)
        {
            permissionAsked = true;
            VoiceRecorder.RequestPermission();
            ShowToast(VoiceCopy.ToastPermissionAsked, false);
            return;
        }

        string error = recorder.TryStart();
        if (error != null)
        {
            ShowToast(error, true);
            return;
        }

        overwriteArmed = false;
        StopPlayback();
        state = VoiceDraftState.Recording;
        toast = null;
        Buzz();
        RefreshAll();
    }

    private void FinishRecording(bool reachedLimit)
    {
        int frequency = recorder.Frequency;
        float[] raw = recorder.Stop();
        float heldSeconds = raw.Length / (float)frequency;
        state = HasAnyDraft() ? VoiceDraftState.Draft : VoiceDraftState.Idle;

        if (heldSeconds < VoiceRecorder.MinSeconds)
        {
            ShowToast(VoiceCopy.ToastTooShort, true);
            RefreshAll();
            return;
        }

        float[] trimmed = VoiceRecorder.TrimSilence(raw, frequency);
        if (trimmed == null)
        {
            ShowToast(VoiceCopy.ToastSilent, true);
            RefreshAll();
            return;
        }

        if (reachedLimit)
        {
            ShowToast(VoiceCopy.ToastMaxLength, false);
            Buzz();
            Invoke(nameof(Buzz), 0.15f);
        }
        else
        {
            Buzz();
        }

        // 只有拿到一段有效录音才替换旧草稿；太短 / 没声音时旧草稿原样保留
        SetVoiceDraft(trimmed, frequency);
        SetTextFromCode(string.Empty);
        textEdited = false;
        recognitionEmpty = false;
        missingGlyphHint = null;
        state = VoiceDraftState.Transcribing;
        RefreshAll();
        _ = Transcribe(trimmed, frequency, ++transcribeTicket);
    }

    private async System.Threading.Tasks.Task Transcribe(float[] samples, int frequency, int ticket)
    {
        string text = null;
        try
        {
            text = await SpeechToTextService.TranscribeAsync(samples, frequency);
        }
        catch (Exception e)
        {
            Debug.LogError($"[园区语音] 转文字异常：{e}");
        }

        // 等待期间面板可能被销毁、草稿可能被清掉或已开始下一段录音
        if (this == null || ticket != transcribeTicket || state != VoiceDraftState.Transcribing) return;

        recognitionEmpty = string.IsNullOrEmpty(text);
        if (!recognitionEmpty)
        {
            if (text.Length > ParkMessageStore.MaximumTextLength)
                text = text.Substring(0, ParkMessageStore.MaximumTextLength);
            SetTextFromCode(text);
            missingGlyphHint = CheckGlyphs(text);
        }
        state = VoiceDraftState.Draft;
        RefreshAll();
    }

    /// <summary>
    /// 识别结果是任意汉字，而字体是 Static 图集，不在图集里的字会显示成豆腐块。
    /// 提前告诉用户"有几个字显示不了"，免得以为识别出了乱码。
    /// </summary>
    private string CheckGlyphs(string text)
    {
        TMP_FontAsset font = view.textInput.textComponent.font;
        if (font == null) return null;
        if (font.HasCharacters(text, out uint[] missing, true) || missing == null || missing.Length == 0)
            return null;
        Debug.LogWarning($"[园区语音] 识别结果有 {missing.Length} 个字不在字体图集里");
        return string.Format(VoiceCopy.MissingGlyphsFormat, missing.Length);
    }

    // ------------------------------------------------------------------ 草稿与发布

    private void SetVoiceDraft(float[] samples, int frequency)
    {
        ClearVoiceDraft();
        voiceDraft = samples;
        voiceFrequency = frequency;
        draftClip = AudioClip.Create("ParkVoiceDraft", samples.Length, 1, frequency, false);
        draftClip.SetData(samples, 0);
    }

    private void ClearVoiceDraft()
    {
        if (draftClip != null && audioSource.clip == draftClip) StopPlayback();
        if (draftClip != null) Destroy(draftClip);
        draftClip = null;
        voiceDraft = null;
    }

    private void SendVoice()
    {
        if (voiceDraft == null) return;
        float[] samples = voiceDraft;
        int frequency = voiceFrequency;
        if (!TryPublish(() => ParkMessageStore.PublishVoice(samples, frequency))) return;
        ClearVoiceDraft();
        AfterPublish();
    }

    private void DeleteVoice()
    {
        ClearVoiceDraft();
        // 识别中只删语音，文字照样等它回来；以前这里直接回 Idle，识别没跑完就能开下一段录音
        if (state != VoiceDraftState.Transcribing && !HasAnyDraft()) state = VoiceDraftState.Idle;
        RefreshAll();
    }

    private bool TryPublish(Func<ParkMessage> publish)
    {
        if (state == VoiceDraftState.Recording) return false;
        try
        {
            ParkMessage message = publish();
            Debug.Log($"[园区发布] 已发布 {message.kind} {message.id}");
            return true;
        }
        catch (ArgumentException e)
        {
            ShowToast(e.Message, true);
        }
        catch (Exception e)
        {
            Debug.LogError($"[园区发布] 写入本机失败：{e}");
            ShowToast(VoiceCopy.ErrorSaveFailed, true);
        }
        RefreshAll();
        return false;
    }

    private void AfterPublish()
    {
        historyPage = 0;
        if (state != VoiceDraftState.Transcribing)
            state = HasAnyDraft() ? VoiceDraftState.Draft : VoiceDraftState.Idle;
        ShowToast(VoiceCopy.ToastPublished, false);
        RefreshAll();
    }

    private bool HasAnyDraft() =>
        voiceDraft != null || !string.IsNullOrWhiteSpace(view.textInput.text);

    private void SetTextFromCode(string text)
    {
        settingTextFromCode = true;
        view.textInput.text = text;
        settingTextFromCode = false;
    }

    // ------------------------------------------------------------------ 播放

    private void ToggleDraftPlayback()
    {
        if (draftClip == null) return;
        bool wasPlayingDraft = audioSource.isPlaying && audioSource.clip == draftClip;
        StopPlayback();
        if (wasPlayingDraft) return;
        audioSource.clip = draftClip;
        audioSource.Play();
        RefreshAll();
    }

    private void ToggleHistoryPlayback(int row)
    {
        int index = historyPage * ParkVoiceComposerView.HistoryRows + row;
        IReadOnlyList<ParkMessage> items = ParkMessageStore.All;
        if (index >= items.Count || !items[index].IsVoice) return;

        bool wasPlaying = playingHistoryIndex == index && audioSource.isPlaying;
        StopPlayback();
        if (wasPlaying) return;

        try
        {
            float[] samples = WavFile.Read(ParkMessageStore.VoicePath(items[index]), out int frequency);
            ReleaseHistoryClip();
            historyClip = AudioClip.Create("ParkVoiceHistory", samples.Length, 1, frequency, false);
            historyClip.SetData(samples, 0);
        }
        catch (Exception e)
        {
            Debug.LogError($"[园区发布] 读取语音 {items[index].voiceFile} 失败：{e.Message}");
            ShowToast("语音文件缺失或已损坏", true);
            return;
        }
        playingHistoryIndex = index;
        audioSource.clip = historyClip;
        audioSource.Play();
        RefreshAll();
    }

    private void StopPlayback()
    {
        if (audioSource == null) return;
        audioSource.Stop();
        audioSource.clip = null;
        playingHistoryIndex = -1;
        if (view != null) RefreshAll();
    }

    private void ReleaseHistoryClip()
    {
        if (historyClip == null) return;
        if (audioSource != null && audioSource.clip == historyClip) audioSource.clip = null;
        Destroy(historyClip);
        historyClip = null;
        playingHistoryIndex = -1;
    }

    // ------------------------------------------------------------------ 键盘

    private void AttachKeyboard(GameObject keyboardPrefab)
    {
        if (keyboardPrefab == null)
        {
            Debug.LogWarning("[园区语音] 场景上没接 keyboardPrefab，文字草稿只能靠识别和短语填写");
            return;
        }
        // 键盘不挂在面板下：VRTextInputPro 选中时会把它挪到镜头正前方，挂在 2.4 倍缩放的卡片里会跟着放大
        keyboardRoot = Instantiate(keyboardPrefab);
        keyboard = keyboardRoot.GetComponentInChildren<BNG.VRKeyboardPro>(true);
        if (keyboard == null)
        {
            Debug.LogWarning("[园区语音] keyboardPrefab 上没有 VRKeyboardPro");
            return;
        }
        // 键盘的显隐全靠它自己 GameObject 上的 CanvasGroup（VRTextInputPro 选中时、Close 键都直接
        // GetComponent 取），但 VR Keyboard Variant 上并没有挂，不补上一按就空引用
        CanvasGroup group = keyboard.GetComponent<CanvasGroup>();
        if (group == null) group = keyboard.gameObject.AddComponent<CanvasGroup>();
        BNG.VRKeyboardPro.SetCGActive(group, false);

        // VRTextInputPro.Awake 里直接用 AttachedKeyboard，必须先赋值再激活
        GameObject input = view.textInput.gameObject;
        input.SetActive(false);
        input.AddComponent<BNG.VRTextInputPro>().AttachedKeyboard = keyboard;
        input.SetActive(true);
    }

    private bool IsTyping() => view.textInput.isFocused;

    private void CloseKeyboard()
    {
        if (view == null) return;
        view.textInput.DeactivateInputField();
        if (keyboard == null) return;
        CanvasGroup group = keyboard.GetComponent<CanvasGroup>();
        if (group != null) BNG.VRKeyboardPro.SetCGActive(group, false);
    }

    // ------------------------------------------------------------------ 绑定与刷新

    private void Bind()
    {
        view.infoButton.onClick.AddListener(() => view.infoOverlay.SetActive(!view.infoOverlay.activeSelf));
        view.infoCloseButton.onClick.AddListener(() => view.infoOverlay.SetActive(false));
        view.playButton.onClick.AddListener(ToggleDraftPlayback);
        view.deleteButton.onClick.AddListener(DeleteVoice);
        view.sendVoiceButton.onClick.AddListener(SendVoice);
        view.sendTextButton.onClick.AddListener(() => SendText());
        view.prevButton.onClick.AddListener(() => TurnPage(-1));
        view.nextButton.onClick.AddListener(() => TurnPage(1));
        for (int i = 0; i < view.historyPlayButtons.Count; i++)
        {
            int row = i;
            view.historyPlayButtons[i].onClick.AddListener(() => ToggleHistoryPlayback(row));
        }
        view.textInput.onValueChanged.AddListener(_ =>
        {
            if (!settingTextFromCode)
            {
                textEdited = true;
                missingGlyphHint = null;
            }
            overwriteArmed = false;
            if (state == VoiceDraftState.Idle && HasAnyDraft()) state = VoiceDraftState.Draft;
            RefreshTextCard();
        });
    }

    private void TurnPage(int delta)
    {
        int pages = PageCount();
        historyPage = Mathf.Clamp(historyPage + delta, 0, Mathf.Max(0, pages - 1));
        RefreshHistory();
    }

    private int PageCount()
    {
        int rows = ParkVoiceComposerView.HistoryRows;
        return (ParkMessageStore.All.Count + rows - 1) / rows;
    }

    private void RefreshAll()
    {
        RefreshVoiceCard();
        RefreshTextCard();
        RefreshHistory();
        RefreshHintBar();
    }

    private void RefreshVoiceCard()
    {
        bool recording = state == VoiceDraftState.Recording;
        SetCardEnabled(view.voiceCard, !recording);
        bool hasVoice = voiceDraft != null;
        view.voiceDuration.text = hasVoice
            ? FormatSeconds(voiceDraft.Length / (float)voiceFrequency)
            : VoiceCopy.VoiceEmpty;
        view.playButton.interactable = hasVoice;
        view.deleteButton.interactable = hasVoice;
        view.sendVoiceButton.interactable = hasVoice;
        view.playLabel.text = audioSource.isPlaying && audioSource.clip == draftClip && draftClip != null
            ? VoiceCopy.Stop : VoiceCopy.Play;
    }

    private void RefreshTextCard()
    {
        SetCardEnabled(view.textCard, state != VoiceDraftState.Recording);
        string text = view.textInput.text ?? string.Empty;
        view.textCounter.text = text.Length + "/" + ParkMessageStore.MaximumTextLength;
        view.sendTextButton.interactable = !string.IsNullOrWhiteSpace(text) &&
            state != VoiceDraftState.Transcribing;
        view.textSubLabel.text = TextSubLabel(text);
    }

    private string TextSubLabel(string text)
    {
        if (state == VoiceDraftState.Transcribing)
            return VoiceCopy.TextHintRecognizing + new string('.', 1 + (int)(Time.time * 2f) % 3);
        if (missingGlyphHint != null) return missingGlyphHint;
        if (!textEdited && !string.IsNullOrEmpty(text)) return VoiceCopy.TextHintRecognized;
        if (SpeechToTextService.State == SpeechModelState.Unavailable) return VoiceCopy.TextHintUnavailable;
        return VoiceCopy.TextHintManual;
    }

    private void RefreshHistory()
    {
        IReadOnlyList<ParkMessage> items = ParkMessageStore.All;
        int pages = PageCount();
        historyPage = Mathf.Clamp(historyPage, 0, Mathf.Max(0, pages - 1));
        view.historyPage.text = items.Count == 0 ? VoiceCopy.HistoryEmpty
            : pages <= 1 ? $"共 {items.Count} 条"
            : $"{historyPage + 1} / {pages}　共 {items.Count} 条";
        view.prevButton.gameObject.SetActive(pages > 1);
        view.nextButton.gameObject.SetActive(pages > 1);

        for (int row = 0; row < ParkVoiceComposerView.HistoryRows; row++)
        {
            int index = historyPage * ParkVoiceComposerView.HistoryRows + row;
            ParkMessage item = index < items.Count ? items[index] : null;
            view.historyLabels[row].text = item == null ? string.Empty : HistoryLine(item);
            bool voice = item != null && item.IsVoice;
            view.historyPlayButtons[row].gameObject.SetActive(voice);
            if (voice)
                view.historyPlayLabels[row].text = playingHistoryIndex == index && audioSource.isPlaying
                    ? VoiceCopy.Stop : VoiceCopy.Play;
        }
    }

    private static string HistoryLine(ParkMessage item)
    {
        string time = item.createdAt != null && item.createdAt.Length >= 16
            ? item.createdAt.Substring(5, 11) : item.createdAt;
        return item.IsVoice
            ? $"{time}　{VoiceCopy.HistoryVoice} {FormatSeconds(item.durationSeconds)}"
            : $"{time}　{VoiceCopy.HistoryText}　{item.text}";
    }

    private void RefreshHintBar()
    {
        bool recording = state == VoiceDraftState.Recording;
        view.recordingDot.gameObject.SetActive(recording);
        view.levelFill.parent.gameObject.SetActive(recording);
        view.subHintText.text = SubHint();

        if (recording)
        {
            float elapsed = recorder.ElapsedSeconds;
            float remaining = VoiceRecorder.MaxSeconds - elapsed;
            bool countdown = remaining <= CountdownSeconds;
            view.hintText.text = countdown
                ? string.Format(VoiceCopy.HintCountdownFormat, Mathf.CeilToInt(remaining),
                    FormatSeconds(elapsed), FormatSeconds(VoiceRecorder.MaxSeconds))
                : string.Format(VoiceCopy.HintRecordingFormat,
                    FormatSeconds(elapsed), FormatSeconds(VoiceRecorder.MaxSeconds));
            view.hintText.color = countdown ? ParkVoiceComposerView.HintWarningColor : ParkVoiceComposerView.HintNormalColor;
            view.levelFill.anchorMax = new Vector2(recorder.CurrentLevel(), 1f);
            Color dot = view.recordingDot.color;
            dot.a = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.time * 4f));
            view.recordingDot.color = dot;
            return;
        }

        if (toast != null && Time.time < toastUntil)
        {
            view.hintText.text = toast;
            view.hintText.color = toastIsWarning ? RuntimeWorldUi.AccentColor : ParkVoiceComposerView.HintNormalColor;
            return;
        }
        toast = null;
        view.hintText.color = ParkVoiceComposerView.HintNormalColor;
        view.hintText.text = StateHint();
        if (state == VoiceDraftState.Transcribing) RefreshTextCard(); // 让"识别中..."的点动起来
    }

    private string StateHint()
    {
        if (IsTyping()) return VoiceCopy.HintKeyboard;
        switch (state)
        {
            case VoiceDraftState.Transcribing:
                return voiceDraft != null ? VoiceCopy.HintTranscribing : VoiceCopy.HintTranscribingVoiceDeleted;
            case VoiceDraftState.Draft:
                if (voiceDraft != null && recognitionEmpty && string.IsNullOrWhiteSpace(view.textInput.text))
                    return VoiceCopy.HintRecognitionEmpty;
                if (voiceDraft != null && string.IsNullOrWhiteSpace(view.textInput.text))
                    return VoiceCopy.HintVoiceOnly;
                return HasAnyDraft() ? VoiceCopy.HintDraftReady : VoiceCopy.HintIdle;
            default:
                return VoiceCopy.HintIdle;
        }
    }

    private string SubHint()
    {
        switch (SpeechToTextService.State)
        {
            case SpeechModelState.Loading:
            case SpeechModelState.NotLoaded:
                return VoiceCopy.SubModelLoading;
            case SpeechModelState.Unavailable:
                return VoiceCopy.SubModelUnavailable;
            default:
                return string.Empty;
        }
    }

    private void ShowToast(string message, bool warning)
    {
        toast = message;
        toastIsWarning = warning;
        toastUntil = Time.time + ToastSeconds;
    }

    private static void SetCardEnabled(CanvasGroup card, bool enabled)
    {
        card.alpha = enabled ? 1f : 0.4f;
        card.interactable = enabled;
    }

    private static string FormatSeconds(float seconds)
    {
        int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
        return $"{total / 60}:{total % 60:00}";
    }

    /// <summary>X 在左手柄，震左手。手柄没接 / 不支持震动时静默跳过。</summary>
    private void Buzz()
    {
        BNG.InputBridge bridge = BNG.InputBridge.Instance;
        if (bridge != null) bridge.VibrateController(0.3f, 0.5f, 0.05f, BNG.ControllerHand.Left);
    }
}
