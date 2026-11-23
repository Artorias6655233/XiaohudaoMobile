using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class AssessmentReportObject : MonoBehaviour
{
    public Button detailButton;
    private AssessmentReport _currentReport;
    public System.Action<AssessmentReport> OnDetailClick;

    private void Start()
    {
        if (detailButton == null) detailButton = GetComponent<Button>();
        if (detailButton != null) detailButton.onClick.AddListener(() => OnDetailClick?.Invoke(_currentReport));
    }

    public int reportNo
    {
        set
        {
            reportNoText.text = value.ToString();
        }
    }

   
    public AssessmentReport reportDetails
    {
        set
        {
            _currentReport = value;
            reportNameText.text = value.reportName;
            companyText.text = value.sysOrgCode;
            hazardIdText.text = value.companyMajorHazardId;
            assessmentTimeText.text = !string.IsNullOrEmpty(value.assessmentTime) ? value.assessmentTime.Split(' ')[0] : "";
            assessorText.text = value.assessor;
            statusText.text = value.statusName;

            // 强制刷新布局，解决 LayoutGroup 不自动刷新的问题
            LayoutRebuilder.ForceRebuildLayoutImmediate(GetComponent<RectTransform>());
        }
    }

    private TextMeshProUGUI reportNoText => transform.GetChild(0).GetComponent<TextMeshProUGUI>();
    private TextMeshProUGUI companyText => transform.GetChild(1).GetComponent<TextMeshProUGUI>();
    private TextMeshProUGUI reportNameText => transform.GetChild(2).GetComponent<TextMeshProUGUI>();
    private TextMeshProUGUI hazardIdText => transform.GetChild(3).GetComponent<TextMeshProUGUI>();
    private TextMeshProUGUI assessmentTimeText => transform.GetChild(4).GetComponent<TextMeshProUGUI>();
    private TextMeshProUGUI assessorText => transform.GetChild(5).GetComponent<TextMeshProUGUI>();
    private TextMeshProUGUI statusText => transform.GetChild(6).GetComponent<TextMeshProUGUI>();
}
