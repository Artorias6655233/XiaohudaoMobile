using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class TankAreaObject : MonoBehaviour
{
    public string areaId;
    public P0TankArea details;
    public Image alarmImage;
    public Image selectionImage;
    public TextMeshProUGUI nameText;

    public Color AlarmColor => alarmImage != null ? alarmImage.color : Color.clear;
    public bool SelectionVisible => selectionImage != null && selectionImage.gameObject.activeSelf;

    private void Start()
    {
        if (nameText == null) nameText = GetComponentInChildren<TextMeshProUGUI>(true);
        if (nameText != null && details != null) nameText.text = "罐区 " + details.name;

        Button button = alarmImage != null
            ? alarmImage.GetComponentInChildren<Button>(true)
            : GetComponentInChildren<Button>(true);
        if (button != null) button.onClick.AddListener(ToggleSelection);
    }

    public void Apply(P0AlarmLevel level, bool selected)
    {
        if (alarmImage != null) alarmImage.color = ColorFor(level);
        if (selectionImage != null)
        {
            selectionImage.color = Color.cyan;
            selectionImage.gameObject.SetActive(selected);
        }
    }

    private void ToggleSelection()
    {
        if (Session.Instance == null) return;
        Session.Instance.tankAreaID = Session.Instance.tankAreaID == areaId
            ? string.Empty
            : areaId;
    }

    private static Color ColorFor(P0AlarmLevel level)
    {
        switch (level)
        {
            case P0AlarmLevel.High:
            case P0AlarmLevel.HighHigh:
                return Color.red;
            case P0AlarmLevel.Low:
            case P0AlarmLevel.LowLow:
                return new Color(1f, 0.5f, 0f);
            case P0AlarmLevel.Normal:
                return Color.green;
            default:
                return Color.gray;
        }
    }
}
