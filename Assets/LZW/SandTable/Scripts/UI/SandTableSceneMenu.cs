using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class SandTableSceneMenu : MonoBehaviour
{
    public Button timeButton;

    public Button soundButton;

    public Button moveSpeedButton;

    public Button instructionButton;

    public Button backButton;

    public Button currentSituationButton;

    public Button featurePlanButton;

    public Button sceneDaytimeButton;

    public Button sceneEveningButton;

    public GameObject domainNameImage;

    public GameObject actionInstructionImage;

    public GameObject sceneChoicePanel;

    public GameObject introductionPanel;

    public UnityAction onCurrentSituationButtonClicked;
    public UnityAction onFeaturePlanButtonClicked;
    public UnityAction onSceneDaytimeButtonClicked;
    public UnityAction onSceneEveningButtonClicked;
    public UnityAction<bool> onTimeButtonClicked;
    public UnityAction<bool> onInstructionButtonClicked;

    // Start is called before the first frame update
    private void Start()
    {
        Init();
    }

    private void Init()
    {
        timeButton.onClick.AddListener(OnTimeButton);
        soundButton.onClick.AddListener(OnSoundButton);
        moveSpeedButton.onClick.AddListener(OnMoveSpeedButton);
        instructionButton.onClick.AddListener(OnInstructionButton);
        backButton.onClick.AddListener(OnBackButton);
        currentSituationButton.onClick.AddListener(OnCurrentSituationButton);
        featurePlanButton.onClick.AddListener(OnFeaturePlanButton);
        sceneDaytimeButton.onClick.AddListener(OnSceneDaytimeButton);
        sceneEveningButton.onClick.AddListener(OnSceneEveningButton);

        SetSceneChoicePanelState(false);
        SetActionInstructionImageState(false);
    }

    private void OnTimeButton()
    {
        ToggleSceneChoicePanelState();
        onTimeButtonClicked?.Invoke(sceneChoicePanel.activeSelf);
    }

    private void OnSoundButton()
    {

    }

    private void OnMoveSpeedButton()
    {

    }

    private void OnInstructionButton()
    {
        ToggleActionInstructionImageState();
        onInstructionButtonClicked?.Invoke(actionInstructionImage.activeSelf);
    }

    private void OnBackButton()
    {

    }

    private void OnCurrentSituationButton()
    {
        onCurrentSituationButtonClicked?.Invoke();
    }

    private void OnFeaturePlanButton()
    {
        onFeaturePlanButtonClicked?.Invoke();
    }

    private void OnSceneDaytimeButton()
    {
        SetSceneChoicePanelState(false);
        onSceneDaytimeButtonClicked?.Invoke();
    }

    private void OnSceneEveningButton()
    {
        SetSceneChoicePanelState(false);
        onSceneEveningButtonClicked?.Invoke();
    }

    // 

    public void SetSceneChoicePanelState(bool state)
    {
        sceneChoicePanel.SetActive(state);
    }

    private void ToggleSceneChoicePanelState()
    {
        SetSceneChoicePanelState(!sceneChoicePanel.activeSelf);
    }

    public void SetActionInstructionImageState(bool state)
    {
        actionInstructionImage.SetActive(state);
    }

    private void ToggleActionInstructionImageState()
    {
        SetActionInstructionImageState(!actionInstructionImage.activeSelf);
    }
}
