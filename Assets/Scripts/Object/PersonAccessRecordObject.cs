using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PersonAccessRecordObject : MonoBehaviour
{
    public int index
    {
        set
        {
            indexText.text = value.ToString();
        }
    }

    private PersonAccessRecord _recordDetails;
    public PersonAccessRecord recordDetails
    {
        get => _recordDetails;
        set
        {
            _recordDetails = value;
            if (value == null) return;
            companyNameText.text = value.ENTERPRISENAME;
            personNameText.text = value.PERSONNAME;
            contactPhoneText.text = value.PHONE;
            idCardNumText.text = value.IDCARDNUM;
        }
    }

    private EntryExitActionCell actionCell;

    /// <summary>挂上最右侧的「操作」列。说明同 <see cref="CarAccessRecordObject.BindActions"/>。</summary>
    public void BindActions(EntryExitActionCell prefab, Action<object> onReview, Action<object> onRemark)
    {
        if (prefab == null) return;

        if (actionCell == null)
        {
            actionCell = Instantiate(prefab, transform);
            actionCell.rowBackground = GetComponent<Image>();
            actionCell.gameObject.SetActive(true);
        }

        actionCell.Bind(EntryExitReviewStore.KeyFor(_recordDetails), _recordDetails, onReview, onRemark);
    }

    private TextMeshProUGUI indexText => transform.GetChild(0).GetComponent<TextMeshProUGUI>();
    private TextMeshProUGUI companyNameText => transform.GetChild(1).GetComponent<TextMeshProUGUI>();
    private TextMeshProUGUI personNameText => transform.GetChild(2).GetComponent<TextMeshProUGUI>();
    private TextMeshProUGUI contactPhoneText => transform.GetChild(3).GetComponent<TextMeshProUGUI>();
    private TextMeshProUGUI idCardNumText => transform.GetChild(4).GetComponent<TextMeshProUGUI>();
}
