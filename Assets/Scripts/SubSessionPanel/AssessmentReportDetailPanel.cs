using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class AssessmentReportDetailPanel : MonoBehaviour
{
    public TextMeshProUGUI reportNameText;
    public TextMeshProUGUI assessmentTimeText;
    public TextMeshProUGUI assessorText;
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI assessorPhoneText;
    public TextMeshProUGUI hazardIdText;
    public TextMeshProUGUI qualificationCertificateText;
    public TextMeshProUGUI agencyText;

    private void Start()
    {
    }

    public void Show(AssessmentReport report)
    {
        gameObject.SetActive(true);
        
        if (reportNameText != null) reportNameText.text = report.reportName;
        if (assessmentTimeText != null) assessmentTimeText.text = !string.IsNullOrEmpty(report.assessmentTime) ? report.assessmentTime.Split(' ')[0] : "";
        if (assessorText != null) assessorText.text = report.assessor;
        if (statusText != null) statusText.text = report.statusName;
        if (assessorPhoneText != null) assessorPhoneText.text = report.assessorPhone;
        if (hazardIdText != null) hazardIdText.text = report.companyMajorHazardId;
        if (qualificationCertificateText != null) qualificationCertificateText.text = report.classificationOfQualificationCertificate;
        if (agencyText != null) agencyText.text = report.nameOfEvaluationAgency;

        // 强制刷新布局
        LayoutRebuilder.ForceRebuildLayoutImmediate(GetComponent<RectTransform>());
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }
}
