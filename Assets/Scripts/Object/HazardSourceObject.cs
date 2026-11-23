using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class HazardSourceObject : MonoBehaviour
{
    public string hazardId;
    public P0HazardSource details;
    public Image markerImage;
    public Image selectionImage;
    public TextMeshProUGUI nameText;

    private void Start()
    {
        if (nameText == null) nameText = GetComponentInChildren<TextMeshProUGUI>(true);
        if (nameText != null && details != null)
            nameText.text = $"危险源 {details.name}（{details.level}）";
        // 兜底和 TankObject.Start 对齐：markerImage 子树里没有 Button 时，
        // 退回整个物体再找一遍。少了这一步，图标看得见却点不动，
        // 而且没有任何报错——只能靠人肉试出来。
        Button button = markerImage != null
            ? markerImage.GetComponentInChildren<Button>(true)
            : null;
        if (button == null) button = GetComponentInChildren<Button>(true);

        if (button != null) button.onClick.AddListener(ToggleSelection);
        else Debug.LogWarning($"[P0 Mock] 危险源 {hazardId} 的图标上找不到 Button，点不动");
    }

    public void ApplySelection(bool selected)
    {
        if (markerImage != null) markerImage.color = ColorForLevel(details?.level);
        if (selectionImage != null)
        {
            selectionImage.color = Color.cyan;
            selectionImage.gameObject.SetActive(selected);
        }
    }

    private void ToggleSelection()
    {
        if (Session.Instance == null) return;
        Session.Instance.hazardSourceID = Session.Instance.hazardSourceID == hazardId
            ? string.Empty
            : hazardId;
        Debug.Log($"[P0 Mock] 危险源图标被点击 {hazardId}，" +
                  $"当前选中「{Session.Instance.hazardSourceID}」");
    }

    /// <summary>
    /// 危险源图标配色。
    ///
    /// 两套等级词并存：一级/二级/三级 是演示数据包的写法，重大/较大/一般/低风险 是
    /// riskTagRecord 真实返回的 riskLevelName。切数据源时两边都要认，所以都留着。
    /// 颜色取的是后端 riskColor 那四个值（#a30014 / #f59a23 / #ffff00 / #02a7f0），
    /// 保证沙盘和 Web 端看到的是同一套色。
    /// </summary>
    private static Color ColorForLevel(string level)
    {
        switch (level)
        {
            case "一级":
            case "重大风险": return new Color(0.639f, 0f, 0.078f);
            case "二级":
            case "较大风险": return new Color(0.961f, 0.604f, 0.137f);
            case "三级":
            case "一般风险": return Color.yellow;
            case "低风险": return new Color(0.008f, 0.655f, 0.941f);
            default: return new Color(0.7f, 0.3f, 1f);
        }
    }
}
