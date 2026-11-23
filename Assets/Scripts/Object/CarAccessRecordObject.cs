using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CarAccessRecordObject : MonoBehaviour
{
    public int index
    {
        set
        {
            indexText.text = value.ToString();
        }
    }

    private CarAccessRecord _recordDetails;
    public CarAccessRecord recordDetails
    {
        get => _recordDetails;
        set
        {
            _recordDetails = value;
            if (value == null) return;
            carNumText.text = value.CARNUM;
            carTypeText.text = value.CARTYPE;
            contactNameText.text = value.CARLIANXR;
            contactPhoneText.text = value.CARPHONENUM;
        }
    }

    private EntryExitActionCell actionCell;

    /// <summary>
    /// 挂上最右侧的「操作」列（审核 / 备注）。
    ///
    /// 行是虚拟滚动复用的，本方法每次绑数据都会被调到，所以 key 和回调必须每次重设——
    /// 只在创建时设一次的话，第 3 行会拿着第 40 行的记录。
    ///
    /// 操作列作为最后一个子物体加进来，由行自带的 HorizontalLayoutGroup 自动排到右端，
    /// 不用手算坐标；也因为是追加在末尾，下面那几个按 GetChild(N) 取列的写法不受影响。
    /// </summary>
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
    private TextMeshProUGUI carNumText => transform.GetChild(1).GetComponent<TextMeshProUGUI>();
    private TextMeshProUGUI carTypeText => transform.GetChild(2).GetComponent<TextMeshProUGUI>();
    private TextMeshProUGUI contactNameText => transform.GetChild(3).GetComponent<TextMeshProUGUI>();
    private TextMeshProUGUI contactPhoneText => transform.GetChild(4).GetComponent<TextMeshProUGUI>();
}
