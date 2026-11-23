using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class CompanyObject : MonoBehaviour
{
    public string companyID;
    public Company companyDetails;
    private Image buttonImage;
    private Button button;
    private TextMeshProUGUI companyNameText;

    void Start()
    {
        buttonImage = GetComponentInChildren<Image>();
        button = GetComponentInChildren<Button>();
        companyNameText = GetComponentInChildren<TextMeshProUGUI>();


        if (button == null)
        {
            Debug.LogError($"[CompanyObject] Button not found for company {companyID}");
            return;
        }

        button.onClick.AddListener(() =>
        {
            Debug.Log("Company button clicked: " + companyID);

            // 如果点击的是已选中的企业，则取消选择
            if (Session.Instance.companyID == companyID)
            {
                Session.Instance.companyID = string.Empty;
            }
            else
            {
                Session.Instance.companyID = companyID;
            }
        });

        // 设置企业名称（优先用简称，没有简称则用全称）
        if (companyNameText != null && companyDetails != null)
        {
            companyNameText.text = string.IsNullOrEmpty(companyDetails.companyShortName)
                ? companyDetails.companyName
                : companyDetails.companyShortName;
        }

        // 订阅企业选择变化事件
        if (Session.Instance != null)
        {
            Session.Instance.OnCompanyIDChanged += UpdateButtonAppearance;
        }

        // 初始化按钮外观
        UpdateButtonAppearance();
    }

    void OnDestroy()
    {
        // 取消订阅事件
        if (Session.Instance != null)
        {
            Session.Instance.OnCompanyIDChanged -= UpdateButtonAppearance;
        }
    }

    private void UpdateButtonAppearance()
    {
        if (buttonImage == null || button == null)
        {
            Debug.LogWarning($"[CompanyObject] UpdateButtonAppearance - buttonImage or button is null for company {companyID}");
            return;
        }

        if (Session.Instance == null)
        {
            Debug.LogWarning($"[CompanyObject] UpdateButtonAppearance - Session.Instance is null");
            return;
        }

        // 安全地获取 sprite，处理 null 情况
        Sprite selectedSprite = button.spriteState.pressedSprite;
        Sprite deselectedSprite = button.spriteState.disabledSprite;

        // 如果 spriteState 中的 sprite 为 null，使用当前 sprite 作为备用
        if (selectedSprite == null)
            selectedSprite = buttonImage.sprite;
        if (deselectedSprite == null)
            deselectedSprite = buttonImage.sprite;

        if (Session.Instance.companyID == companyID)
        {
            if (selectedSprite != null)
                buttonImage.sprite = selectedSprite;
        }
        else
        {
            if (deselectedSprite != null)
                buttonImage.sprite = deselectedSprite;
        }
        
        if (gameObject.activeInHierarchy)
        {
            StartCoroutine(RefreshLayout());
        }
    }

    private IEnumerator RefreshLayout()
    {
        // 1. 强制更新文本网格
        if (companyNameText != null)
        {
            companyNameText.ForceMeshUpdate();
            // 确保 Text 自身的 ContentSizeFitter 生效
            LayoutRebuilder.ForceRebuildLayoutImmediate(companyNameText.rectTransform);
        }

        yield return null;

        // 2. 强制刷新 Button/Image 的布局
        // 注意：LayoutRebuilder 必须对挂载了 LayoutGroup 或 ContentSizeFitter 的物体调用才有效
        // 之前的 GetComponent<RectTransform>() 是获取的根节点(Company)，它可能没有布局组件
        if (button != null)
        {
            RectTransform buttonRect = button.GetComponent<RectTransform>();
            
            // 临时禁用再启用 ContentSizeFitter 以强制标记为脏
            var fitter = button.GetComponent<ContentSizeFitter>();
            if (fitter != null)
            {
                fitter.enabled = false;
                fitter.enabled = true;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(buttonRect);
        }
        
        // 3. 强制全局 Canvas 更新 (作为最后一道防线)
        Canvas.ForceUpdateCanvases();
    }
}
