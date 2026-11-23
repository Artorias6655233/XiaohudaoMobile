using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 证件列表里的一行。
///
/// 【为什么从「每次 new 一行」改成 Bind/Clear】这一页改成了固定行槽分页
/// （见 <see cref="SpecialJob_CertificateList"/>），行实例在进页面时建一次就固定下来，
/// 翻页只是把不同的数据喂进同一批行。所以行必须支持「被复用」和「这一槽这页没数据」
/// 两种状态，照 <see cref="TankListRow"/> 的写法。
///
/// 列是场景里摆死的 8 个子物体，顺序和表头一一对应，改列要连表头一起改：
///   0 序号 1 人员姓名 2 证件类型 3 证件获取时间 4 证件有效期至 5 状态 6 第三方单位名称 7 管理单位
/// </summary>
public class CertificateObject : MonoBehaviour
{
    public Button detailButton;

    private static readonly Color ExpiredColor = new Color(1f, 0.32f, 0.32f);
    private static readonly Color ExpiringColor = new Color(1f, 0.72f, 0.2f);

    private CertRow bound;
    private Action<CertRow> detail;

    /// <summary>状态列的原始颜色。按状态染色前先记下来，恢复「有效」时要染回去。</summary>
    private Color? statusIdleColor;

    private void Awake()
    {
        if (detailButton == null) detailButton = GetComponent<Button>();
        if (detailButton != null) detailButton.onClick.AddListener(OnButtonClick);
    }

    private void OnButtonClick()
    {
        if (bound != null && detail != null) detail(bound);
    }

    /// <param name="displayIndex">列表里的序号，从 1 开始，跨页连续（第 2 页第 1 行是 9 不是 1）。</param>
    public void Bind(CertRow row, int displayIndex, Action<CertRow> onDetail)
    {
        bound = row;
        detail = onDetail;

        gameObject.SetActive(row != null);
        if (row == null) return;

        SetText(indexText, displayIndex.ToString());
        SetText(personNameText, row.personName);
        SetText(certTypeText, row.certTypeName);
        SetText(issueDateText, DatePart(row.issueDate));
        SetText(expireDateText, DatePart(row.expireDate));
        // 第三方为空表示自有员工，写清楚比留白好——留白会被当成数据没取到
        SetText(thirdPartyText, string.IsNullOrWhiteSpace(row.thirdParty) ? "自有员工" : row.thirdParty);
        SetText(managementUnitText, row.managementUnit);

        ApplyStatus(row.statusText);

        LayoutRebuilder.ForceRebuildLayoutImmediate(GetComponent<RectTransform>());
    }

    /// <summary>这一槽这页没数据。整行关掉，让布局收起来。</summary>
    public void Clear()
    {
        bound = null;
        detail = null;
        gameObject.SetActive(false);
    }

    private void ApplyStatus(string status)
    {
        TextMeshProUGUI target = statusText;
        if (target == null) return;

        if (statusIdleColor == null) statusIdleColor = target.color;

        // 状态为空表示有效期缺失或格式不认识，按「无数据」显示，不能当成有效
        target.text = string.IsNullOrEmpty(status) ? "无数据" : status;

        if (status == CertStatus.Expired) target.color = ExpiredColor;
        else if (status == CertStatus.Expiring) target.color = ExpiringColor;
        else target.color = statusIdleColor.Value;
    }

    /// <summary>只要日期那一截。演示包是 yyyy-MM-dd，真实接口常见 "yyyy-MM-dd HH:mm:ss"。</summary>
    private static string DatePart(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        string trimmed = value.Trim();
        int space = trimmed.IndexOf(' ');
        return space > 0 ? trimmed.Substring(0, space) : trimmed;
    }

    private static void SetText(TextMeshProUGUI textComp, string value)
    {
        if (textComp != null) textComp.text = value ?? string.Empty;
    }

    private TextMeshProUGUI Column(int index) =>
        index < transform.childCount ? transform.GetChild(index).GetComponent<TextMeshProUGUI>() : null;

    private TextMeshProUGUI indexText => Column(0);
    private TextMeshProUGUI personNameText => Column(1);
    private TextMeshProUGUI certTypeText => Column(2);
    private TextMeshProUGUI issueDateText => Column(3);
    private TextMeshProUGUI expireDateText => Column(4);
    private TextMeshProUGUI statusText => Column(5);
    private TextMeshProUGUI thirdPartyText => Column(6);
    private TextMeshProUGUI managementUnitText => Column(7);
}
