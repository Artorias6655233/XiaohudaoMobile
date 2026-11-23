using UnityEngine;

public class ThirdPartyObject : MonoBehaviour
{
    public int no
    {
        set
        {
            noText.text = value.ToString();
        }
    }

    public ThirdParty thirdPartyDetails
    {
        set
        {
            orgText.text = value.thirdPartyName;
            codeText.text = value.thirdPartyCreditCode;
            typeText.text = value.thirdPartyType;
            dateText.text = value.createTime.Split(' ')[0];
            statusText.text = "否";
        }
    }

    private TMPro.TextMeshProUGUI noText => transform.GetChild(0).GetComponent<TMPro.TextMeshProUGUI>();
    private TMPro.TextMeshProUGUI orgText => transform.GetChild(1).GetComponent<TMPro.TextMeshProUGUI>();
    private TMPro.TextMeshProUGUI codeText => transform.GetChild(2).GetComponent<TMPro.TextMeshProUGUI>();
    private TMPro.TextMeshProUGUI typeText => transform.GetChild(3).GetComponent<TMPro.TextMeshProUGUI>();
    private TMPro.TextMeshProUGUI dateText => transform.GetChild(4).GetComponent<TMPro.TextMeshProUGUI>();
    private TMPro.TextMeshProUGUI statusText => transform.GetChild(5).GetComponent<TMPro.TextMeshProUGUI>();
}
