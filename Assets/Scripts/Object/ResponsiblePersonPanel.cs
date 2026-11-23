using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class ResponsiblePersonPanel : MonoBehaviour
{
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI phoneText;
    public TextMeshProUGUI roleText;
    public TextMeshProUGUI positionText;
    public TextMeshProUGUI companyNameText;
    public TextMeshProUGUI tankNameText;
    public Button callButton;

    private string currentPhone;

    void Start()
    {
        if (callButton != null)
        {
            callButton.onClick.AddListener(OnCallButtonClicked);
        }
    }
    
    /// <param name="companyLevel">
    /// true 表示这个人是【按企业】兜底匹配来的，不是这个罐登记的责任人。
    /// 必须让用户看出来：责任人表里 scopeCoverage 只覆盖 45/264 个罐，其余靠企业兜底，
    /// 不标注的话会被当成"这个罐的责任人就是他"，出事时找错人。
    /// </param>
    public void SetData(
        CompanyHazardSourcesResponsiblePerson person,
        string companyName,
        string tankName,
        bool companyLevel = false)
    {
        if (person == null) 
        {
            Debug.LogError("[ResponsiblePersonPanel] Person data is null");
            return;
        }
        
        Debug.Log($"[ResponsiblePersonPanel] SetData called. Name: {person.name}, Company: {companyName}, Tank: {tankName}");
        
        currentPhone = person.phone;

        // 粒度标在姓名后面：它紧挨着"这个人是谁"，是用户第一眼看的地方。
        // 卡片上没有多余的文本槽，加新槽要改 prefab，不值得为一个角标动它。
        if (nameText != null)
            nameText.text = companyLevel ? person.name + "（企业级）" : person.name;
        else Debug.LogWarning("[ResponsiblePersonPanel] nameText is not assigned!");

        if (phoneText != null) phoneText.text = person.phone;
        else Debug.LogWarning("[ResponsiblePersonPanel] phoneText is not assigned!");

        if (roleText != null) roleText.text = person.role;
        else Debug.LogWarning("[ResponsiblePersonPanel] roleText is not assigned!");

        if (positionText != null) positionText.text = person.position;
        else Debug.LogWarning("[ResponsiblePersonPanel] positionText is not assigned!");

        if (companyNameText != null) companyNameText.text = companyName;
        else Debug.LogWarning("[ResponsiblePersonPanel] companyNameText is not assigned!");

        if (tankNameText != null) tankNameText.text = tankName;
        else Debug.LogWarning("[ResponsiblePersonPanel] tankNameText is not assigned!");
    }

    /// <summary>
    /// 匹配不到负责人时的占位。卡片仍然显示——直接藏掉的话用户会以为面板坏了。
    /// 走到这里意味着这个罐既没按 dangesrcCode 登记责任人，它所属企业也一个人都没登记。
    /// </summary>
    public void ShowEmpty(string companyName, string tankName)
    {
        currentPhone = null;

        if (nameText != null) nameText.text = "暂无登记";
        if (phoneText != null) phoneText.text = "—";
        if (roleText != null) roleText.text = "—";
        if (positionText != null) positionText.text = "—";
        if (companyNameText != null) companyNameText.text = companyName;
        if (tankNameText != null) tankNameText.text = tankName;
    }

    private void OnCallButtonClicked()
    {
        if (!string.IsNullOrEmpty(currentPhone))
        {
            Application.OpenURL($"sip:{currentPhone}");
        }
        else
        {
            Debug.LogWarning("No phone number available to call.");
        }
    }
}
