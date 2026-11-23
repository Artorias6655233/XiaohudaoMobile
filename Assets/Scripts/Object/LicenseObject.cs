using UnityEngine;

public class LicenseObject : MonoBehaviour
{
    public int licenseNo
    {
        set
        {
            licenseNoText.text = value.ToString();
        }
    }
    public License licenseDetails
    {
        set
        {
            licenseNameText.text = value.licenseName2;
            licenseExpiryDateText.text = value.licenseValidityPeriod.Split(' ')[0];
            licensingOrganizationText.text = value.issuingAuthority;
            licenseCodeText.text = value.certificateNumber;
            licenseTimeText.text = value.issueDate.Split(' ')[0];
        }
    }
    private TMPro.TextMeshProUGUI licenseNoText => transform.GetChild(0).GetComponent<TMPro.TextMeshProUGUI>();
    private TMPro.TextMeshProUGUI licenseNameText => transform.GetChild(1).GetComponent<TMPro.TextMeshProUGUI>();
    private TMPro.TextMeshProUGUI licenseExpiryDateText => transform.GetChild(2).GetComponent<TMPro.TextMeshProUGUI>();
    private TMPro.TextMeshProUGUI licensingOrganizationText => transform.GetChild(3).GetComponent<TMPro.TextMeshProUGUI>();
    private TMPro.TextMeshProUGUI licenseCodeText => transform.GetChild(4).GetComponent<TMPro.TextMeshProUGUI>();
    private TMPro.TextMeshProUGUI licenseTimeText => transform.GetChild(5).GetComponent<TMPro.TextMeshProUGUI>();

}
