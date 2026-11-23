using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 单个摄像头的周界入侵告警详情。
///
/// 结构照 CheckDetailPanel（panelRoot + Show/Hide + 强制重排），但去掉了那边
/// 给每个字段 AddComponent&lt;TMP_InputField&gt; 的可编辑逻辑 —— 这里是纯只读展示。
///
/// 字段全部允许为空：场景接线漏一个不该让整块面板炸掉，SetText 里统一判空。
/// </summary>
public class CameraAlarmDetailPanel : MonoBehaviour
{
    public GameObject panelRoot;

    [Header("摄像头")]
    public TextMeshProUGUI cameraNameText;      // 摄像头名称（CSV name）
    public TextMeshProUGUI cameraLocationText;  // 安装位置（CSV location）

    [Header("告警")]
    public TextMeshProUGUI deviceNameText;      // 报警设备（ICC deviceName）
    public TextMeshProUGUI alarmContentText;    // 报警内容
    public TextMeshProUGUI alarmTypeText;       // 报警类型
    public TextMeshProUGUI alarmGradeText;      // 报警等级
    public TextMeshProUGUI alarmDateText;       // 报警时间
    public TextMeshProUGUI alarmDurationText;   // 持续时长
    public TextMeshProUGUI alarmStatusText;     // 处理状态
    public TextMeshProUGUI alarmCountText;      // 该摄像头共有几条告警

    private void Start()
    {
        Hide();
    }

    /// <summary>
    /// 展示一条告警。
    /// </summary>
    /// <param name="camera">被点中的摄像头标记，可为 null（只显示告警侧字段）。</param>
    /// <param name="alarm">要展示的那条告警。</param>
    /// <param name="totalForCamera">该摄像头匹配到的告警总条数，用于提示"共 N 条"。</param>
    public void Show(CameraObject camera, IccAlarm alarm, int totalForCamera)
    {
        if (alarm == null)
        {
            Hide();
            return;
        }

        if (panelRoot != null)
        {
            panelRoot.SetActive(true);
        }
        else
        {
            // 没接 panelRoot 时退化成显示自身，和 CheckDetailPanel 的兜底一致
            gameObject.SetActive(true);
        }

        CameraInfo info = camera != null ? camera.cameraDetails : null;
        SetText(cameraNameText, info != null ? info.name : string.Empty);
        SetText(cameraLocationText, info != null ? info.location : string.Empty);

        SetText(deviceNameText, alarm.deviceName);
        SetText(alarmContentText, alarm.alarmContent);
        SetText(alarmTypeText, alarm.alarmType);
        SetText(alarmGradeText, alarm.alarmGrade);
        SetText(alarmDateText, alarm.alarmDate);
        SetText(alarmDurationText, alarm.alarmDuration);
        SetText(alarmStatusText, alarm.alarmStatus.ToString());

        // 一个地点可能有多条告警，面板只展示最新那条，条数得让人看得见
        SetText(alarmCountText, totalForCamera > 1 ? $"共 {totalForCamera} 条，显示最近一条" : string.Empty);

        if (gameObject.activeInHierarchy)
        {
            StartCoroutine(RebuildLayout());
        }
    }

    public void Hide()
    {
        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    private IEnumerator RebuildLayout()
    {
        // 等 TMP 把 mesh 更新完再重排，否则按旧尺寸算
        yield return new WaitForEndOfFrame();

        RectTransform target = panelRoot != null
            ? panelRoot.GetComponent<RectTransform>()
            : GetComponent<RectTransform>();

        if (target != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(target);
        }
    }

    private static void SetText(TextMeshProUGUI textComp, string value)
    {
        if (textComp != null)
        {
            textComp.text = value ?? string.Empty;
        }
    }
}
