using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class UIPOIManager : MonoBehaviour
{
    public static UIPOIManager Ins;
    private void Awake()
    {
        Ins = this;
    }
    public Camera _camera;

    public Canvas[] _canvasPOIS;
    [SerializeField]
    private Button[][] _poisTransButtons;
    [SerializeField]
    private RectTransform[][] _poisTransPanels;

    // Start is called before the first frame update
    void Start()
    {
        _camera ??= Camera.main;

        InitPOIS();

        EventInit();

    }

    private void InitPOIS()
    {
        int canvasPOISCount = _canvasPOIS.Length;
        _poisTransButtons = new Button[canvasPOISCount][];
        for (int i = 0; i < canvasPOISCount; i++)
        {
            // index: 0 --> Buttons
            _poisTransButtons[i] = _canvasPOIS[i].transform.GetChild(0).GetComponentsInChildren<Button>();
            var cPOIButtons = _poisTransButtons[i];
            if (cPOIButtons != null)
            {
                for (int j = 0; j < cPOIButtons.Length; j++)
                {
                    int _i = i;
                    int _j = j;
                    cPOIButtons[j].onClick.AddListener(() => {
                        onButtonClick?.Invoke(_i, _j);
                    });
                }
            }
            // index: 1 --> Panels
            _poisTransPanels = new RectTransform[canvasPOISCount][];
            _poisTransPanels[i] = new RectTransform[_canvasPOIS[i].transform.GetChild(1).childCount];
            for (int k = 0; k < _canvasPOIS[i].transform.GetChild(1).childCount; k++)
            {
                _poisTransPanels[i][k] = _canvasPOIS[i].transform.GetChild(1).GetChild(k).GetComponent<RectTransform>();
                _poisTransPanels[i][k].gameObject.SetActive(false);
            }
        }
    }

    private void EventInit()
    {
        OnButtonClickEvent();
    }

    public UnityAction<int, int> onButtonClick;
    public void OnButtonClickEvent()
    {
        onButtonClick += new UnityAction<int, int>(OnButtonClick);
    }

    public void OnButtonClick(int index_i, int index_j)
    {
        if (index_i == 0) {
            if (index_j < 2) {
                var panel = _poisTransPanels[index_i][index_j].gameObject;
                Features.LookViewToCamera(panel.transform, _camera);
                panel.SetActive(!panel.activeSelf);
            }
        }

        Debug.Log($"OnButtonClick: {index_i} {index_j}");
    }

    // Update is called once per frame
    void Update()
    {
        OnLookToCamera();
    }

    private void OnLookToCamera()
    {
        foreach (Button[] b1 in _poisTransButtons)
        {
            foreach (Button b2 in b1)
            {
                var t2 = b2.transform;
                if (t2.gameObject.activeSelf)
                {
                    Features.LookViewToCamera(t2, _camera);
                }
            }
        }
    }

    /// <summary>
    /// display specific canvas.
    /// </summary>
    /// <param name="index"></param>
    public bool DisplayUIPOI(int index, bool status)
    {
        if (index < 0 || index > _canvasPOIS.Length - 1)
            return false;
        _canvasPOIS[index].gameObject.SetActive(status);
        return true;
    }
}
