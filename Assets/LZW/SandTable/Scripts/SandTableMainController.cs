using BNG;
using UnityEngine;

public class SandTableMainController : MonoBehaviour
{
    public SandTableSceneManager sandTableSceneManager;

    public SandTableLoadedModelContainer sandTableLoadedModelContainer;

    public SandTableSceneMenu sandTableSceneMenu;

    private int index, index2;
    private bool visible, visible2;

    public new Camera camera;

    // Start is called before the first frame update
    void Start()
    {
        OnChangeScene(0, 0);

        sandTableSceneManager.onChangeSceneStateLocal += (index, index2, visible, visible2) => {
            this.index = index;
            this.index2 = index2;
            this.visible = visible;
            this.visible2 = visible2;
            OnChangeScene(index, index2);
            OnChangePanel(visible, visible2);
        };

        sandTableSceneMenu.onCurrentSituationButtonClicked += () => {
            index = 0;
            sandTableSceneManager.ChangeSceneStateNet(index, index2, visible, visible2);
        };

        sandTableSceneMenu.onFeaturePlanButtonClicked += () => {
            index = 1;
            sandTableSceneManager.ChangeSceneStateNet(index, index2, visible, visible2);
        };

        sandTableSceneMenu.onSceneDaytimeButtonClicked += () => {
            index2 = 0;
            visible = false;
            sandTableSceneManager.ChangeSceneStateNet(index, index2, visible, visible2);
        };

        sandTableSceneMenu.onSceneEveningButtonClicked += () => {
            index2 = 1;
            visible = false;
            sandTableSceneManager.ChangeSceneStateNet(index, index2, visible, visible2);
        };

        sandTableSceneMenu.onTimeButtonClicked += value => {
            visible = value;
            sandTableSceneManager.ChangeSceneStateNet(index, index2, visible, visible2);
        };

        sandTableSceneMenu.onInstructionButtonClicked += value => {
            visible2 = value;
            sandTableSceneManager.ChangeSceneStateNet(index, index2, visible, visible2);
        };
    }

    private void Update()
    {
        if (InputBridge.Instance != null)
        {
            if (InputBridge.Instance.BButtonDown || (Application.isEditor && Input.GetKeyDown(KeyCode.B)))
            {
                PlaceUIInFrontOfCamera(camera, sandTableSceneMenu.transform, 6);
            }
        }
    }

    public void OnChangeScene(int index, int index2)
    {
        var loadedModel = sandTableLoadedModelContainer.GetLoadedModel();
        if (loadedModel == null) return;

        for (int i = 0; i < loadedModel.transform.childCount && i < 2; i++)
        {
            var t = loadedModel.transform.GetChild(i);
            t.gameObject.SetActive(i == index);
            for (int j = 0; j < t.childCount; j++)
            {
                var t2 = t.GetChild(j);
                t2.gameObject.SetActive(j == index2);
            }
        }
    }

    public void OnChangePanel(bool visible, bool visible2)
    {
        sandTableSceneMenu.SetSceneChoicePanelState(visible);
        sandTableSceneMenu.SetActionInstructionImageState(visible2);
    }

    public void PlaceUIInFrontOfCamera(Camera camera, Transform ui, float distance = 6)
    {
        var cameraForward = camera.transform.forward;
        var cameraForwardX0Z = new Vector3(cameraForward.x, 0, cameraForward.z);
        var cameraForwardX0ZNormalized = cameraForwardX0Z.normalized;
        var placeTargetPos = camera.transform.position + cameraForwardX0ZNormalized * distance;
        ui.position = placeTargetPos;
        ui.rotation = Quaternion.LookRotation(cameraForwardX0ZNormalized);
    }
}
