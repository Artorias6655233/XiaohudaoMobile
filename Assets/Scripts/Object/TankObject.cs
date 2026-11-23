using UnityEngine;
using UnityEngine.UI;
using TMPro;

public enum TankLabelState
{
    Label,
    Normal,
    BelowNormal,
    AboveNormal,
    Hidden, // 隐藏所有标签
    Unavailable,
    SelectedUnavailable
}

public class TankObject : MonoBehaviour
{
    public string tankID;
    public Tank tankDetails;

    public Image labelImage;
    public Image normalStateImage;
    public Image belowNormalStateImage;
    public Image aboveNormalStateImage;
    public Image hitpointImage; // 点击点图标，根据状态显示不同颜色

    private Button button;
    private Button labelButton; // 新增：Label 上的按钮
    private TextMeshProUGUI tankNameText;
    private TankLabelState currentLabelState = TankLabelState.Label;
    private bool hasInitialized = false;

    void Start()
    {
        button = hitpointImage != null ? hitpointImage.GetComponentInChildren<Button>(true) : null;
        if (button == null) button = GetComponentInChildren<Button>(true);
        
        // 尝试获取 Label 上的按钮
        if (labelImage != null)
        {
            labelButton = labelImage.GetComponentInChildren<Button>(true);
        }

        tankNameText = labelImage != null ? labelImage.GetComponentInChildren<TextMeshProUGUI>(true) : GetComponentInChildren<TextMeshProUGUI>(true);

        // 定义点击事件处理函数
        UnityEngine.Events.UnityAction onClickAction = () =>
        {
            Debug.Log("Tank button clicked: " + tankID);

            // 如果点击的是已选中的 tank，则取消选择
            if (Session.Instance.tankID == tankID)
            {
                Session.Instance.tankID = string.Empty;
            }
            else
            {
                Session.Instance.tankID = tankID;
            }
        };

        // 为主按钮添加监听
        if (button != null)
        {
            button.onClick.AddListener(onClickAction);
        }

        // 为 Label 按钮添加监听，防止重复添加
        if (labelButton != null && labelButton != button)
        {
            labelButton.onClick.AddListener(onClickAction);
        }

        // 设置 tank 名称
        if (tankNameText != null && tankDetails != null)
        {
            tankNameText.text = tankDetails.tankName;
        }

        // 初始化时隐藏所有图像，等待 SetDisplayState 调用
        if (!hasInitialized)
        {
            HideAllImages();
            UpdateHitpointColor();
        }
    }

    /// <summary>
    /// 隐藏所有状态图像
    /// </summary>
    private void HideAllImages()
    {
        if (labelImage != null)
            labelImage.gameObject.SetActive(false);
        if (normalStateImage != null)
            normalStateImage.gameObject.SetActive(false);
        if (belowNormalStateImage != null)
            belowNormalStateImage.gameObject.SetActive(false);
        if (aboveNormalStateImage != null)
            aboveNormalStateImage.gameObject.SetActive(false);
        // hitpointImage 不隐藏，始终显示但颜色会改变
    }

    /// <summary>
    /// 设置 Tank 的显示状态并更新对应的图像显示
    /// </summary>
    /// <param name="state">要设置的显示状态</param>
    public void SetLabelState(TankLabelState state)
    {
        currentLabelState = state;
        hasInitialized = true; // 标记为已初始化，防止 Start 覆盖
        UpdateStateImages();
        UpdateHitpointColor();
    }

    /// <summary>
    /// 获取当前 Tank 的显示状态
    /// </summary>
    public TankLabelState GetLabelState()
    {
        return currentLabelState;
    }

    /// <summary>
    /// 根据当前显示状态更新图像显示
    /// </summary>
    private void UpdateStateImages()
    {
        // 隐藏所有状态图像
        if (labelImage != null)
            labelImage.gameObject.SetActive(false);
        if (normalStateImage != null)
            normalStateImage.gameObject.SetActive(false);
        if (belowNormalStateImage != null)
            belowNormalStateImage.gameObject.SetActive(false);
        if (aboveNormalStateImage != null)
            aboveNormalStateImage.gameObject.SetActive(false);

        // 根据当前显示状态显示对应的图像
        switch (currentLabelState)
        {
            case TankLabelState.Label:
            case TankLabelState.SelectedUnavailable:
                if (labelImage != null)
                    labelImage.gameObject.SetActive(true);
                break;
            case TankLabelState.Normal:
                if (normalStateImage != null)
                    normalStateImage.gameObject.SetActive(true);
                break;
            case TankLabelState.BelowNormal:
                if (belowNormalStateImage != null)
                    belowNormalStateImage.gameObject.SetActive(true);
                break;
            case TankLabelState.AboveNormal:
                if (aboveNormalStateImage != null)
                    aboveNormalStateImage.gameObject.SetActive(true);
                break;
            case TankLabelState.Hidden:
                // 所有图像已经隐藏，不需要额外操作
                break;
        }
    }
    
    /// <summary>
    /// 根据当前显示状态更新 hitpoint 的颜色
    /// </summary>
    private void UpdateHitpointColor()
    {
        if (hitpointImage == null)
            return;
            
        switch (currentLabelState)
        {
            case TankLabelState.Unavailable:
                hitpointImage.color = Color.gray;
                break;
            case TankLabelState.SelectedUnavailable:
                hitpointImage.color = Color.cyan;
                break;
            case TankLabelState.BelowNormal:
                // 橙色
                hitpointImage.color = new Color(1f, 0.5f, 0f); // RGB: 255, 128, 0
                break;
            case TankLabelState.AboveNormal:
                // 红色
                hitpointImage.color = Color.red;
                break;
            default:
                // 绿色（所有其他状态）
                hitpointImage.color = Color.green;
                break;
        }
    }
}
