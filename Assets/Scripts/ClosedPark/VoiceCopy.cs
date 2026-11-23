/// <summary>
/// 园区语音发布的全部上屏文案，集中在这里方便统一改口径。
///
/// 【字形】字体是 Static 图集（见 Assets/Fonts），●✓ⓘ▶■ 这类符号都不在图集里，会显示成豆腐块，
/// 所以状态一律用文字表达，录音红点用 Image 画。新增文案里的汉字也要确认在图集里。
/// </summary>
public static class VoiceCopy
{
    public const string Title = "语音发布";
    public const string InfoButton = "说明";
    public const string InfoClose = "知道了";
    public const string InfoBody =
        "按住手柄 X 键说话，松开结束。一次录音同时得到两样东西：\n" +
        "1. 语音消息：原声录音，可以试听后发送；\n" +
        "2. 文字草稿：眼镜离线识别的文字，可以修改后发送。\n\n" +
        "离线识别 · 普通话 · 单次最长 30 秒\n" +
        "人名、专业术语可能识别错误，请核对后再发送\n" +
        "发布内容仅保存在这副眼镜上，不会发到其他设备";

    public const string VoiceCardTitle = "语音消息";
    public const string VoiceEmpty = "暂无录音";
    public const string Play = "播放";
    public const string Stop = "停止";
    public const string Delete = "删除";
    public const string SendVoice = "发送语音";

    public const string TextCardTitle = "文字草稿";
    public const string TextHintRecognized = "离线识别结果，可能有错字，请核对";
    public const string TextHintRecognizing = "识别中";
    public const string TextHintManual = "可用按键修改，或点左侧短语追加";
    public const string TextHintUnavailable = "语音识别不可用，可点左侧短语或按键输入";
    public const string SendText = "发送文字";

    public const string HistoryTitle = "已发布（本机）";
    public const string HistoryEmpty = "暂无发布记录";
    public const string HistoryVoice = "语音";
    public const string HistoryText = "文字";
    public const string PrevPage = "上一页";
    public const string NextPage = "下一页";

    public const string HintIdle = "按住 X 说话 · 松开后同时生成「语音消息」和「文字草稿」";
    public const string SubModelLoading = "语音识别准备中…（可以先录音，识别稍后完成）";
    public const string SubModelUnavailable = "语音识别不可用，仅可发送语音消息";
    public const string HintRecordingFormat = "录音中 {0} / {1} · 松开 X 结束";
    public const string HintCountdownFormat = "还剩 {0} 秒… {1} / {2}";
    public const string HintTranscribing = "语音已录好，可以先试听 · 正在转文字…";
    public const string HintTranscribingVoiceDeleted = "语音已删除 · 正在转文字…";
    public const string HintDraftReady = "请核对文字后发送 · 再次按住 X 会替换当前语音和文字";
    public const string HintVoiceOnly = "语音可以发送 · 再次按住 X 会替换当前语音和文字";
    public const string HintRecognitionEmpty = "未识别到文字，可以重录或手动输入；语音仍可发送";
    public const string HintKeyboard = "正在键盘输入，X 录音暂停";

    public const string ToastTooShort = "录音太短，已忽略。请按住 X 说话";
    public const string ToastSilent = "没有听到说话声，请靠近麦克风后重录";
    public const string ToastMaxLength = "已达最长 30 秒，自动结束";
    public const string ToastPublished = "已发布到本机（仅保存在这副眼镜上）";
    public const string ToastStillTranscribing = "上一段还在转文字，请稍候";
    public const string ToastConfirmOverwrite = "将覆盖已编辑的文字：再次按住 X 继续录音";
    public const string ToastPermissionAsked = "请在弹窗中允许使用麦克风，然后再按住 X";
    public const string ToastTextEmpty = "文字草稿为空";
    public const string ToastTextTooLong = "文字不能超过 500 字";
    public const string MissingGlyphsFormat = "有 {0} 个字无法显示，请核对";

    public const string ErrorNoPermission = "未获得麦克风权限，无法录音。请在系统设置中允许后重试";
    public const string ErrorNoMicrophone = "未检测到麦克风";
    public const string ErrorSaveFailed = "保存失败，存储空间可能不足";
}
