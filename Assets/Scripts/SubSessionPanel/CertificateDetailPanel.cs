using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 证件详情卡。
///
/// 【字段改过名，注意和场景对账】原来的 <c>documentNameText</c> / <c>documentAcquisitionTimeText</c>
/// 这两个名字和它们在卡片上的位置对不上：场景里 <c>documentNameText</c> 挂在「状态:」旁边，
/// <c>documentAcquisitionTimeText</c> 挂在「证件名称:」旁边，所以旧版详情卡是把
/// 「证件名称」显示在「状态」那一栏、把「获取时间」显示在「证件名称」那一栏的。
/// 这一版按卡片上实际的标签把字段改名成 <see cref="statusText"/> / <see cref="certNameText"/>，
/// 场景里那两行 YAML 的 key 同步改了。改这两个名字的时候必须连场景一起改，否则引用会丢。
/// </summary>
public class CertificateDetailPanel : MonoBehaviour
{
    public TextMeshProUGUI personNameText;
    public TextMeshProUGUI certNumberText;
    public TextMeshProUGUI phoneText;
    public TextMeshProUGUI certTypeNameText;

    /// <summary>卡片上「状态:」那一栏。</summary>
    public TextMeshProUGUI statusText;

    /// <summary>卡片上「工作年限:」那一栏。</summary>
    public TextMeshProUGUI workYearsText;

    /// <summary>卡片上「证件名称:」那一栏，放准操项目。</summary>
    public TextMeshProUGUI certNameText;

    public Button closeButton;

    private void Start()
    {
        if (closeButton != null) closeButton.onClick.AddListener(Hide);
    }

    public void Show(CertRow data)
    {
        if (data == null)
        {
            Hide();
            return;
        }

        gameObject.SetActive(true);

        SetText(personNameText, data.personName);
        SetText(certNumberText, data.certNumber);
        SetText(phoneText, data.phone);
        SetText(certTypeNameText, data.certTypeName);
        SetText(statusText, string.IsNullOrEmpty(data.statusText) ? "无数据" : data.statusText);
        SetText(workYearsText, data.workYears > 0 ? $"{data.workYears} 年" : "未提供");
        SetText(certNameText, data.certName);

        LayoutRebuilder.ForceRebuildLayoutImmediate(transform as RectTransform);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private static void SetText(TextMeshProUGUI textComp, string value)
    {
        if (textComp != null) textComp.text = value ?? string.Empty;
    }
}
