using UnityEngine;

public class OverhaulObject : MonoBehaviour
{
    public int OverhaulNo
    {
        set
        {
            stateNoText.text = value.ToString();
        }
    }
    public Overhaul OverhaulDetail
    {
        set
        {
            if (!string.IsNullOrEmpty(value.sysOrgCode) && 
                Session.Instance.sysOrgCodeToID.TryGetValue(value.sysOrgCode, out var companyId))
            {
                stateOrgText.text = Session.Instance.companyObjects[companyId].companyDetails.companyName;
            }
            else
            {
                stateOrgText.text = value.sysOrgCode ?? "未知";
            }
            stateIsMajorHazardText.text = value.isMajorHazard;
            stateManagerText.text = value.responsiblePerson;
            stateManagerPhoneText.text = value.contactPhone;
            stateStartTimeText.text = value.plannedStartTime;
            stateEndTimeText.text = value.plannedEndTime;
            stateText.text = value.maintenanceStatusName;
        }
    }

    private TMPro.TextMeshProUGUI stateNoText => transform.GetChild(0).GetComponent<TMPro.TextMeshProUGUI>();
    private TMPro.TextMeshProUGUI stateOrgText => transform.GetChild(1).GetComponent<TMPro.TextMeshProUGUI>();
    private TMPro.TextMeshProUGUI stateIsMajorHazardText => transform.GetChild(2).GetComponent<TMPro.TextMeshProUGUI>();
    private TMPro.TextMeshProUGUI stateManagerText => transform.GetChild(3).GetComponent<TMPro.TextMeshProUGUI>();
    private TMPro.TextMeshProUGUI stateManagerPhoneText => transform.GetChild(4).GetComponent<TMPro.TextMeshProUGUI>();
    private TMPro.TextMeshProUGUI stateStartTimeText => transform.GetChild(5).GetComponent<TMPro.TextMeshProUGUI>();
    private TMPro.TextMeshProUGUI stateEndTimeText => transform.GetChild(6).GetComponent<TMPro.TextMeshProUGUI>();
    private TMPro.TextMeshProUGUI stateText => transform.GetChild(6).GetComponent<TMPro.TextMeshProUGUI>();
}
