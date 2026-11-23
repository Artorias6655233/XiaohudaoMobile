using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 语音发布卡片的控件树，运行时搭出来（SandTable.unity 不能重新保存，见
/// <see cref="EntryExitHoverShortcut"/> 的说明）。只负责摆控件，不含任何状态逻辑。
///
/// 按 1000×1080 的逻辑尺寸摆，沿用 <see cref="RuntimeWorldUi"/> 的字号；
/// 放进园区发布面板时整体乘 <see cref="Scale"/>，和面板上 120 号字的消息按钮视觉上同一量级。
/// </summary>
public sealed class ParkVoiceComposerView
{
    public static readonly Vector2 LogicalSize = new Vector2(1000, 1080);
    public const float Scale = 2.4f;
    public const int HistoryRows = 3;

    private static readonly Color RecordingRed = new Color(0.92f, 0.26f, 0.26f, 1f);

    public RectTransform root;
    public Button infoButton;

    public CanvasGroup voiceCard;
    public TextMeshProUGUI voiceDuration;
    public Button playButton;
    public TextMeshProUGUI playLabel;
    public Button deleteButton;
    public Button sendVoiceButton;

    public CanvasGroup textCard;
    public TextMeshProUGUI textSubLabel;
    public TextMeshProUGUI textCounter;
    public TMP_InputField textInput;
    public Button sendTextButton;

    public Image recordingDot;
    public TextMeshProUGUI hintText;
    public TextMeshProUGUI subHintText;
    public RectTransform levelFill;

    public TextMeshProUGUI historyPage;
    public readonly List<TextMeshProUGUI> historyLabels = new List<TextMeshProUGUI>();
    public readonly List<Button> historyPlayButtons = new List<Button>();
    public readonly List<TextMeshProUGUI> historyPlayLabels = new List<TextMeshProUGUI>();
    public Button prevButton;
    public Button nextButton;

    public GameObject infoOverlay;
    public Button infoCloseButton;

    public static Color HintNormalColor => Color.white;
    public static Color HintWarningColor => RecordingRed;

    public static ParkVoiceComposerView Build(Transform parent, Vector2 position, TMP_FontAsset font)
    {
        var view = new ParkVoiceComposerView();
        view.root = RuntimeWorldUi.Panel(parent, "RuntimeParkVoiceComposer", LogicalSize, position);
        view.root.localScale = Vector3.one * Scale;
        Transform r = view.root;

        RuntimeWorldUi.Card(r, "HeaderBar", new Vector2(0.02f, 0.915f), new Vector2(0.98f, 0.99f),
            RuntimeWorldUi.HeaderColor);
        RuntimeWorldUi.Text(r, "Title", VoiceCopy.Title, new Vector2(0.04f, 0.92f),
            new Vector2(0.70f, 0.985f), 40, TextAlignmentOptions.MidlineLeft);
        view.infoButton = SecondaryButton(r, "Info", VoiceCopy.InfoButton,
            new Vector2(0.80f, 0.922f), new Vector2(0.97f, 0.983f));

        BuildVoiceCard(view, r);
        BuildTextCard(view, r);
        BuildHintBar(view, r);
        BuildHistory(view, r);
        BuildInfoOverlay(view, r);

        // RuntimeWorldUi 新建的 TMP 默认是 LiberationSans，一个中文都没有；统一换成面板现成的字体
        if (font != null)
        {
            foreach (TMP_Text text in view.root.GetComponentsInChildren<TMP_Text>(true))
                text.font = font;
        }
        return view;
    }

    private static void BuildVoiceCard(ParkVoiceComposerView view, Transform r)
    {
        RectTransform card = RuntimeWorldUi.Card(r, "VoiceCard", new Vector2(0.02f, 0.54f),
            new Vector2(0.36f, 0.905f), RuntimeWorldUi.CardColor);
        view.voiceCard = card.gameObject.AddComponent<CanvasGroup>();
        Muted(RuntimeWorldUi.Text(card, "Title", VoiceCopy.VoiceCardTitle,
            new Vector2(0f, 0.82f), new Vector2(1f, 0.98f), 28));
        view.voiceDuration = RuntimeWorldUi.Text(card, "Duration", VoiceCopy.VoiceEmpty,
            new Vector2(0f, 0.50f), new Vector2(1f, 0.82f), 44, TextAlignmentOptions.Center);
        view.playButton = SecondaryButton(card, "Play", VoiceCopy.Play,
            new Vector2(0.02f, 0.28f), new Vector2(0.50f, 0.48f));
        view.playLabel = view.playButton.GetComponentInChildren<TextMeshProUGUI>(true);
        view.deleteButton = RuntimeWorldUi.Button(card, "Delete", VoiceCopy.Delete,
            new Vector2(0.50f, 0.28f), new Vector2(0.98f, 0.48f));
        RuntimeWorldUi.StyleButton(view.deleteButton, RuntimeWorldUi.DestructiveColor);
        view.sendVoiceButton = RuntimeWorldUi.Button(card, "SendVoice", VoiceCopy.SendVoice,
            new Vector2(0.02f, 0.03f), new Vector2(0.98f, 0.25f), 28);
        RuntimeWorldUi.StyleButton(view.sendVoiceButton, RuntimeWorldUi.PrimaryColor);
    }

    private static void BuildTextCard(ParkVoiceComposerView view, Transform r)
    {
        RectTransform card = RuntimeWorldUi.Card(r, "TextCard", new Vector2(0.36f, 0.54f),
            new Vector2(0.98f, 0.905f), RuntimeWorldUi.CardColor);
        view.textCard = card.gameObject.AddComponent<CanvasGroup>();
        Muted(RuntimeWorldUi.Text(card, "Title", VoiceCopy.TextCardTitle,
            new Vector2(0f, 0.82f), new Vector2(0.6f, 0.98f), 28));
        view.textCounter = Muted(RuntimeWorldUi.Text(card, "Counter", "0/" + ParkMessageStore.MaximumTextLength,
            new Vector2(0.6f, 0.82f), new Vector2(1f, 0.98f), 24, TextAlignmentOptions.TopRight));
        view.textSubLabel = RuntimeWorldUi.Text(card, "SubLabel", VoiceCopy.TextHintManual,
            new Vector2(0f, 0.70f), new Vector2(1f, 0.84f), 22);
        view.textSubLabel.color = RuntimeWorldUi.AccentColor;
        view.textInput = RuntimeWorldUi.Input(card, "Content", new Vector2(0f, 0.27f), new Vector2(1f, 0.71f));
        view.textInput.characterLimit = ParkMessageStore.MaximumTextLength;
        view.sendTextButton = RuntimeWorldUi.Button(card, "SendText", VoiceCopy.SendText,
            new Vector2(0.45f, 0.03f), new Vector2(0.99f, 0.25f), 28);
        RuntimeWorldUi.StyleButton(view.sendTextButton, RuntimeWorldUi.PrimaryColor);
    }

    private static void BuildHintBar(ParkVoiceComposerView view, Transform r)
    {
        RectTransform bar = RuntimeWorldUi.Card(r, "HintBar", new Vector2(0.02f, 0.40f),
            new Vector2(0.98f, 0.535f), RuntimeWorldUi.HeaderColor);
        RectTransform dot = RuntimeWorldUi.Card(bar, "RecordingDot", new Vector2(0.015f, 0.52f),
            new Vector2(0.045f, 0.86f), RecordingRed);
        dot.offsetMin = dot.offsetMax = Vector2.zero;
        view.recordingDot = dot.GetComponent<Image>();
        view.hintText = RuntimeWorldUi.Text(bar, "Hint", VoiceCopy.HintIdle,
            new Vector2(0.04f, 0.42f), new Vector2(1f, 1f), 28, TextAlignmentOptions.MidlineLeft);

        RectTransform levelBack = RuntimeWorldUi.Card(bar, "LevelBack", new Vector2(0.06f, 0.36f),
            new Vector2(0.97f, 0.42f), RuntimeWorldUi.SecondaryColor);
        levelBack.offsetMin = levelBack.offsetMax = Vector2.zero;
        view.levelFill = RuntimeWorldUi.Card(levelBack, "LevelFill", Vector2.zero, new Vector2(0f, 1f),
            RecordingRed);
        view.levelFill.offsetMin = view.levelFill.offsetMax = Vector2.zero;

        view.subHintText = Muted(RuntimeWorldUi.Text(bar, "SubHint", string.Empty,
            new Vector2(0.04f, 0f), new Vector2(1f, 0.38f), 22, TextAlignmentOptions.MidlineLeft));
    }

    private static void BuildHistory(ParkVoiceComposerView view, Transform r)
    {
        RectTransform card = RuntimeWorldUi.Card(r, "HistoryCard", new Vector2(0.02f, 0.01f),
            new Vector2(0.98f, 0.395f), RuntimeWorldUi.CardColor);
        Muted(RuntimeWorldUi.Text(card, "Title", VoiceCopy.HistoryTitle,
            new Vector2(0f, 0.84f), new Vector2(0.6f, 0.99f), 28));
        view.historyPage = Muted(RuntimeWorldUi.Text(card, "Page", VoiceCopy.HistoryEmpty,
            new Vector2(0.5f, 0.84f), new Vector2(1f, 0.99f), 24, TextAlignmentOptions.TopRight));

        for (int i = 0; i < HistoryRows; i++)
        {
            float top = 0.84f - i * 0.21f;
            view.historyLabels.Add(RuntimeWorldUi.Text(card, "Row" + i, string.Empty,
                new Vector2(0f, top - 0.21f), new Vector2(0.84f, top), 22,
                TextAlignmentOptions.MidlineLeft));
            view.historyLabels[i].enableWordWrapping = false;
            view.historyLabels[i].overflowMode = TextOverflowModes.Ellipsis;
            Button play = SecondaryButton(card, "RowPlay" + i, VoiceCopy.Play,
                new Vector2(0.84f, top - 0.20f), new Vector2(1f, top - 0.01f));
            view.historyPlayButtons.Add(play);
            view.historyPlayLabels.Add(play.GetComponentInChildren<TextMeshProUGUI>(true));
        }

        view.prevButton = SecondaryButton(card, "Prev", VoiceCopy.PrevPage,
            new Vector2(0.52f, 0.0f), new Vector2(0.76f, 0.19f));
        view.nextButton = SecondaryButton(card, "Next", VoiceCopy.NextPage,
            new Vector2(0.76f, 0.0f), new Vector2(1f, 0.19f));
    }

    private static void BuildInfoOverlay(ParkVoiceComposerView view, Transform r)
    {
        RectTransform overlay = RuntimeWorldUi.Card(r, "InfoOverlay", new Vector2(0.02f, 0.40f),
            new Vector2(0.98f, 0.905f), RuntimeWorldUi.PanelColor);
        RuntimeWorldUi.Text(overlay, "Body", VoiceCopy.InfoBody,
            new Vector2(0.02f, 0.22f), new Vector2(0.98f, 0.97f), 28);
        view.infoCloseButton = RuntimeWorldUi.Button(overlay, "Close", VoiceCopy.InfoClose,
            new Vector2(0.70f, 0.03f), new Vector2(0.98f, 0.20f), 28);
        RuntimeWorldUi.StyleButton(view.infoCloseButton, RuntimeWorldUi.PrimaryColor);
        view.infoOverlay = overlay.gameObject;
        view.infoOverlay.SetActive(false);
    }

    private static Button SecondaryButton(Transform parent, string name, string label,
        Vector2 anchorMin, Vector2 anchorMax)
    {
        Button button = RuntimeWorldUi.Button(parent, name, label, anchorMin, anchorMax, 24);
        RuntimeWorldUi.StyleButton(button, RuntimeWorldUi.SecondaryColor);
        return button;
    }

    private static TextMeshProUGUI Muted(TextMeshProUGUI text)
    {
        text.color = RuntimeWorldUi.MutedTextColor;
        return text;
    }
}
