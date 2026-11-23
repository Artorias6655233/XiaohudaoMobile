using UnityEngine;

public class StateObject : MonoBehaviour
{
    public int stateNo
    {
        set
        {
            stateNoText.text = value.ToString();
        }
    }
    public State stateDetails
    {
        set
        {
            if(!string.IsNullOrEmpty(value.sysOrgCode) && 
               Session.Instance.sysOrgCodeToID.TryGetValue(value.sysOrgCode, out var companyId))
            {
                stateOrgText.text = Session.Instance.companyObjects[companyId].companyDetails.companyName;
            }
            else
            {
                stateOrgText.text = value.sysOrgCode ?? "未知";
            }
            stateStateText.text = value.operationalStatusName;
            stateDeviceNameText.text = value.deviceName;
            stateIsMajorHazardText.text = value.isMajorHazardName;
            stateManagerText.text = value.deviceResponsiblePerson;
            stateManagerPhoneText.text = value.contactPhone;
            stateStartTimeText.text = value.plannedStartTime;
            stateEndTimeText.text = value.plannedEndTime;
        }
    }

    private TMPro.TextMeshProUGUI stateNoText => transform.GetChild(0).GetComponent<TMPro.TextMeshProUGUI>();
    private TMPro.TextMeshProUGUI stateOrgText => transform.GetChild(1).GetComponent<TMPro.TextMeshProUGUI>();
    private TMPro.TextMeshProUGUI stateStateText => transform.GetChild(2).GetComponent<TMPro.TextMeshProUGUI>();
    private TMPro.TextMeshProUGUI stateDeviceNameText => transform.GetChild(3).GetComponent<TMPro.TextMeshProUGUI>();
    private TMPro.TextMeshProUGUI stateIsMajorHazardText => transform.GetChild(4).GetComponent<TMPro.TextMeshProUGUI>();
    private TMPro.TextMeshProUGUI stateManagerText => transform.GetChild(5).GetComponent<TMPro.TextMeshProUGUI>();
    private TMPro.TextMeshProUGUI stateManagerPhoneText => transform.GetChild(6).GetComponent<TMPro.TextMeshProUGUI>();
    private TMPro.TextMeshProUGUI stateStartTimeText => transform.GetChild(7).GetComponent<TMPro.TextMeshProUGUI>();
    private TMPro.TextMeshProUGUI stateEndTimeText => transform.GetChild(8).GetComponent<TMPro.TextMeshProUGUI>();
}
