using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ClosedPark_MessageBoard : SubSessionPanel
{
    public override SubSessionType subSessionType => SubSessionType.ClosedPark_MessageBoard;

    // 语音发布卡片挂在面板右侧。面板内容区实测 x -2481~2481、y -1355~1227（含底部按钮行），
    // 卡片 2400×2592（1000×1080 × 2.4），左边留 100 空隙，竖直居中于内容区。
    private static readonly Vector2 ComposerPosition = new Vector2(3781f, -64f);

    [Header("UI Components")]
    public Button cancelButton;
    public Button sendButton;
    public Button callButton;
    public GameObject successImage;

    [Header("Message Selection")]
    public Button[] messageButtons;
    private Sprite[] highlightSprites;
    private Sprite[] normalSprites;
    private int selectedMessageIndex = -1;

    [Header("语音发布")]
    [Tooltip("VR Keyboard Variant，给文字草稿用；不接也能用，只是不能按键修改")]
    public GameObject keyboardPrefab;

    private ParkVoiceComposer composer;

    public override IEnumerator Start()
    {
         if (cancelButton != null)
            cancelButton.onClick.AddListener(OnCancelClicked);

        if (sendButton != null)
            sendButton.onClick.AddListener(OnSendClicked);

        if (callButton != null)
            callButton.onClick.AddListener(OnCallClicked);

        if (successImage != null)
            successImage.SetActive(false);

        if (messageButtons != null)
        {
            normalSprites = new Sprite[messageButtons.Length];
            highlightSprites = new Sprite[messageButtons.Length];
            for (int i = 0; i < messageButtons.Length; i++)
            {
                int index = i;
                messageButtons[i].onClick.AddListener(() => OnMessageButtonClicked(index));
                if (messageButtons[i].image != null)
                {
                    normalSprites[i] = messageButtons[i].image.sprite;
                    // Try to get selected sprite, fallback to highlighted sprite
                    highlightSprites[i] = messageButtons[i].spriteState.selectedSprite;
                    if (highlightSprites[i] == null)
                    {
                        highlightSprites[i] = messageButtons[i].spriteState.highlightedSprite;
                    }
                }
            }
        }

        composer = ParkVoiceComposer.Create(transform, ComposerPosition, BorrowFont(), keyboardPrefab);

        yield return base.Start();
    }

    private void OnMessageButtonClicked(int index)
    {
        selectedMessageIndex = index;
        UpdateMessageButtonsState();
        if (composer != null) composer.AppendText(PhraseOf(index));
    }

    /// <summary>预设消息的文案就在按钮的 TMP 子节点上，去掉两端的引号再用。</summary>
    private string PhraseOf(int index)
    {
        if (messageButtons == null || index < 0 || index >= messageButtons.Length) return null;
        TextMeshProUGUI label = messageButtons[index].GetComponentInChildren<TextMeshProUGUI>(true);
        return label == null ? null : label.text.Trim().Trim('“', '”', '"').Trim();
    }

    /// <summary>新建的 TMP 默认是 LiberationSans，没有中文；借面板上现成的字体。</summary>
    private TMP_FontAsset BorrowFont()
    {
        TextMeshProUGUI text = GetComponentInChildren<TextMeshProUGUI>(true);
        return text != null ? text.font : null;
    }

    private void UpdateMessageButtonsState()
    {
        if (messageButtons == null) return;

        for (int i = 0; i < messageButtons.Length; i++)
        {
            if (messageButtons[i] == null || messageButtons[i].image == null) continue;

            if (highlightSprites != null && i < highlightSprites.Length && normalSprites != null && i < normalSprites.Length)
            {
                messageButtons[i].image.sprite = (i == selectedMessageIndex) ? highlightSprites[i] : normalSprites[i];
            }
        }
    }

    private void OnCancelClicked()
    {
        selectedMessageIndex = -1;
        UpdateMessageButtonsState();
        if (composer != null) composer.ClearDrafts();

        if (successImage != null)
        {
            successImage.SetActive(false);
        }
    }

    // 原来只弹一张成功图、什么都没存；现在发的是右侧卡片里的文字草稿，真正写进本机
    private void OnSendClicked()
    {
        if (composer == null || !composer.SendText()) return;

        selectedMessageIndex = -1;
        UpdateMessageButtonsState();
        if (successImage != null)
        {
            successImage.SetActive(true);
            StartCoroutine(HideSuccessImageAfterDelay());
        }
    }

    private IEnumerator HideSuccessImageAfterDelay()
    {
        yield return new WaitForSeconds(2f);
        if (successImage != null)
        {
            successImage.SetActive(false);
        }
    }

    private void OnCallClicked()
    {
        Application.OpenURL("sip:");
    }

    public override void OnSubSessionTypeChanged()
    {
        base.OnSubSessionTypeChanged();
        // Reset UI when panel is shown
        if (gameObject.activeSelf)
        {
            if (successImage != null) successImage.SetActive(false);

            selectedMessageIndex = -1;
            UpdateMessageButtonsState();
        }
    }
}
