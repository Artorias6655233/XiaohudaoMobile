using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class CheckDetailPanel : MonoBehaviour
{
    public GameObject panelRoot;

    [Header("UI Components")]
    public TextMeshProUGUI nameText;            // 隐患名称
    public TextMeshProUGUI typeText;            // 隐患分类
    public TextMeshProUGUI sourceText;          // 隐患来源
    public TextMeshProUGUI locationText;        // 隐患位置
    public TextMeshProUGUI levelText;           // 隐患等级
    public TextMeshProUGUI riskPointIdText;     // 对应的风险点的id
    public TextMeshProUGUI reportedPersonText;  // 被报送人
    public TextMeshProUGUI descriptionText;     // 隐患描述
    public TextMeshProUGUI verificationImage;          // 核实图片
    public TextMeshProUGUI assignmentStatusText;// 指派情况
    public TextMeshProUGUI verificationStatusText;// 核实情况
    public TextMeshProUGUI verificationResultText;// 核实结果
    public TextMeshProUGUI rectificationDeptText;// 整改部门
    public TextMeshProUGUI assignedPersonText;  // 指派整改人
    public TextMeshProUGUI deadlineText;        // 整改期限
    public TextMeshProUGUI suggestionText;      // 整改建议

    private DoubleDanger currentData;

    private void Start()
    {
        Hide();
    }

    public void Show(DoubleDanger data)
    {
        Debug.Log($"CheckDetailPanel Show called for: {data?.hiddenDangerName}");
        if (panelRoot != null)
        {
            panelRoot.SetActive(true);
        }
        else
        {
            Debug.LogError("CheckDetailPanel: panelRoot is null!");
            // Fallback: try to show self if panelRoot is not assigned
            gameObject.SetActive(true);
        }

        currentData = data;
        if (data == null) return;

        // Populate fields
        SetText(nameText, data.hiddenDangerName);
        SetText(typeText, data.hiddenDangerType);
        SetText(sourceText, data.hiddenDangerSource);
        SetText(locationText, data.hiddenDangerLocation);
        SetText(levelText, data.hiddenDangerLevel);
        SetText(riskPointIdText, data.relevanceRiskPoint);
        SetText(reportedPersonText, data.reportedPerson);
        SetText(descriptionText, data.hiddenDangerDescription);
        
        // Fields from VerifyInfo
        if (data.verifyInfo != null)
        {
            SetText(assignmentStatusText, data.verifyInfo.submitStatus);
            SetText(verificationStatusText, data.verifyInfo.hiddenDangerVerifyResult);
            SetText(verificationResultText, data.verifyInfo.hiddenDangerVerifyInfo);
            
            string dept = !string.IsNullOrEmpty(data.verifyInfo.rectificationDept) ? data.verifyInfo.rectificationDept : data.deptOrgCode;
            SetText(rectificationDeptText, dept);
            SetText(assignedPersonText, data.verifyInfo.hiddenDangerVerifyChangePerson);
            SetText(deadlineText, data.verifyInfo.rectificationDeadline);
            SetText(suggestionText, data.verifyInfo.hiddenDangerVerifyInfo);
        }
        else
        {
            SetText(assignmentStatusText, ""); 
            SetText(verificationStatusText, "");
            SetText(verificationResultText, "");
            SetText(rectificationDeptText, data.deptOrgCode);
            SetText(assignedPersonText, "");
            SetText(deadlineText, "");
            SetText(suggestionText, "");
        }

        // Handle Image
        if (verificationImage != null)
        {
            SetText(verificationImage, data.hiddenDangerImgUrls);
        }

        // Rebuild layout
        StartCoroutine(RebuildLayout());
    }

    public void Hide()
    {
        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    private IEnumerator RebuildLayout()
    {
        // Wait for end of frame to ensure TMP has updated its mesh
        yield return new WaitForEndOfFrame();
        
        RectTransform target = panelRoot != null ? panelRoot.GetComponent<RectTransform>() : GetComponent<RectTransform>();
        if (target != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(target);
        }
    }

    private void SetText(TextMeshProUGUI textComp, string value)
    {
        if (textComp != null)
        {
            textComp.text = value ?? "";
        }
    }

}
