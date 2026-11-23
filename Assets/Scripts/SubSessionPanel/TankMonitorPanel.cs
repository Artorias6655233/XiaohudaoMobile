using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 所选储罐的罐区监测卡片：液位 / 储量 / 温度 / 压力四个参数的最新实测值、单位与报警阈值。
///
/// 结构照 CameraAlarmDetailPanel —— 纯 MonoBehaviour，不继承 SubSessionPanel、
/// 不参与 SubSessionType 注册，由宿主面板（MajorHazard_Management）持引用调用。
///
/// 所有字段允许为空：场景接线漏一个不该让整块面板炸掉，SetText 里统一判空。
/// </summary>
public class TankMonitorPanel : MonoBehaviour
{
    public GameObject panelRoot;

    [Header("基础信息")]
    public TextMeshProUGUI tankNameText;    // 储罐名称
    public TextMeshProUGUI deviceCodeText;  // 监测设备编码（= Tank.equipCode）
    public TextMeshProUGUI tankTypeText;    // 罐型
    public TextMeshProUGUI tankVolText;     // 设计容积
    public TextMeshProUGUI liquidTopText;   // 液位上限

    [Header("参数行（顺序：液位 / 储量 / 温度 / 压力）")]
    public TankMonitorRow liquidLevelRow;
    public TankMonitorRow storageRow;
    public TankMonitorRow temperatureRow;
    public TankMonitorRow pressureRow;

    [Header("状态")]
    public TextMeshProUGUI updateTimeText;  // 最新采样时间
    public TextMeshProUGUI statusHintText;  // 加载中 / 取数失败 / 离线镜像提示

    [Header("返回")]
    public Button backButton;

    /// <summary>
    /// 点"返回"时的回调，由宿主设置。
    ///
    /// 详情卡片和 [储罐列表 + 管理员信息] 是互斥的两态：进详情要把那两张收起来，
    /// 返回时再放出来。卡片自己不知道该放出什么，所以只负责把这一下转交给宿主。
    /// </summary>
    public System.Action onBack;

    /// <summary>
    /// ShowLoading 时记下罐名，供 tankDetail 取不到时兜底显示
    /// （罐名在 tank/list 里本来就有，没必要因为详情接口失败就显示空白）。
    /// </summary>
    private string pendingTankName;

    /// <summary>Show/Hide 是否已经被宿主驱动过，见 Start 里的说明。</summary>
    private bool driven;

    private void Awake()
    {
        if (backButton != null) backButton.onClick.AddListener(() =>
        {
            if (onBack != null) onBack();
        });
    }

    private void Start()
    {
        // 这张卡片是运行时 Instantiate 出来的，Start 会晚于宿主的 ShowLoading 执行；
        // 无条件 Hide 会把刚撑起来的卡片又关掉。只在还没被驱动过时才默认隐藏。
        if (!driven) Hide();
    }

    /// <summary>选中储罐、请求还没回来时先把卡片撑起来，避免一片空白。</summary>
    public void ShowLoading(string tankName, string deviceCode)
    {
        pendingTankName = tankName;

        SetVisible(true);

        SetText(tankNameText, tankName);
        SetText(deviceCodeText, deviceCode);
        SetText(tankTypeText, string.Empty);
        SetText(tankVolText, string.Empty);
        SetText(liquidTopText, string.Empty);
        SetText(updateTimeText, string.Empty);
        SetText(statusHintText, "加载中…");

        ClearRow(liquidLevelRow);
        ClearRow(storageRow);
        ClearRow(temperatureRow);
        ClearRow(pressureRow);

        Rebuild();
    }

    public void Show(TankMonitorSnapshot snapshot)
    {
        if (snapshot == null)
        {
            ShowError("未取到监测数据");
            return;
        }

        SetVisible(true);

        TankDetailInfo detail = snapshot.detail;

        SetText(tankNameText, detail != null && !string.IsNullOrEmpty(detail.tankName)
            ? detail.tankName
            : pendingTankName);
        SetText(deviceCodeText, snapshot.deviceCode);
        SetText(tankTypeText, detail != null ? detail.tankType : string.Empty);
        SetText(tankVolText, detail != null ? WithUnit(detail.tankVol, detail.tankVolUnit) : string.Empty);
        SetText(liquidTopText, detail != null ? WithUnit(detail.liquidTop, detail.liquidTopUnit) : string.Empty);

        // readings 由 TankMonitorService 保证恒为 4 项、顺序固定；这里仍然按下标判长度，
        // 免得将来参数表增删时静默越界。
        List<TankParamReading> readings = snapshot.readings;
        SetRow(liquidLevelRow, readings, 0);
        SetRow(storageRow, readings, 1);
        SetRow(temperatureRow, readings, 2);
        SetRow(pressureRow, readings, 3);

        SetText(updateTimeText, FormatTime(NewestTs(readings)));

        // 回退到随包镜像时必须说出来：数值看着正常，其实不是实时的
        SetText(statusHintText, snapshot.source == "离线镜像"
            ? "离线镜像数据，非实时"
            : snapshot.statusHint ?? string.Empty);

        Rebuild();
    }

    public void ShowError(string message)
    {
        SetVisible(true);

        SetText(tankNameText, pendingTankName);
        SetText(tankTypeText, string.Empty);
        SetText(tankVolText, string.Empty);
        SetText(liquidTopText, string.Empty);
        SetText(updateTimeText, string.Empty);
        SetText(statusHintText, message);

        ClearRow(liquidLevelRow);
        ClearRow(storageRow);
        ClearRow(temperatureRow);
        ClearRow(pressureRow);

        Rebuild();
    }

    public void Hide()
    {
        SetVisible(false);
    }

    private void SetVisible(bool visible)
    {
        driven = true;

        // panelRoot 是子物体时，自身没激活的话它也显示不出来
        if (visible && panelRoot != null && !gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        if (panelRoot != null)
        {
            panelRoot.SetActive(visible);
        }
        else
        {
            // 没接 panelRoot 时退化成控制自身，和 CameraAlarmDetailPanel 的兜底一致
            gameObject.SetActive(visible);
        }
    }

    private void Rebuild()
    {
        if (gameObject.activeInHierarchy)
        {
            StartCoroutine(RebuildLayout());
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

    private static void SetRow(TankMonitorRow row, List<TankParamReading> readings, int index)
    {
        if (row == null) return;

        if (readings != null && index < readings.Count)
        {
            row.SetData(readings[index]);
        }
        else
        {
            row.Clear();
        }
    }

    private static void ClearRow(TankMonitorRow row)
    {
        if (row != null) row.Clear();
    }

    /// <summary>四个参数的采样时间可能不同（各自取的是自己那列最新的非空样本），取最新的那个。</summary>
    private static string NewestTs(List<TankParamReading> readings)
    {
        if (readings == null) return null;

        string newest = null;
        foreach (TankParamReading reading in readings)
        {
            if (reading == null || !reading.hasValue || string.IsNullOrEmpty(reading.ts)) continue;

            // ISO8601 定长同格式，字符串比较就够，不必先解析
            if (newest == null || string.CompareOrdinal(reading.ts, newest) > 0)
            {
                newest = reading.ts;
            }
        }

        return newest;
    }

    /// <summary>服务端给的是 UTC（...+00:00），转成本地时间再显示。</summary>
    private static string FormatTime(string ts)
    {
        if (string.IsNullOrEmpty(ts)) return string.Empty;

        System.DateTimeOffset parsed;
        if (!System.DateTimeOffset.TryParse(ts, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out parsed))
        {
            return ts;
        }

        return parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static string WithUnit(string value, string unit)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        return string.IsNullOrEmpty(unit) ? value : $"{value} {unit}";
    }

    private static void SetText(TextMeshProUGUI textComp, string value)
    {
        if (textComp != null)
        {
            textComp.text = value ?? string.Empty;
        }
    }
}
