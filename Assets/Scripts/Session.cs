using System;
using System.Collections;
using System.Collections.Generic;
using BNG;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public enum SessionType
{
    None,
    Security,
    MajorHazard,
    DoubleDanger,
    SpecialJob,
    ClosedPark
}

public enum WeatherType
{
    Sunny,    // 晴天
    Cloudy,   // 多云
    Rainy,    // 下雨
}

public enum TimeType
{
    Morning,   // 上午
    Noon,      // 中午
    Afternoon, // 下午
    Night,     // 夜晚
}

public enum PanelType
{
    None,
    Weather,
    Time,
}

public enum SubSessionType
{
    None,
    Security_Member = 3,
    Security_LicenseList,
    Security_StateList,
    Security_OverhaulList,
    Security_ThirdPartyList,
    Security_PatrolOverview,

    MajorHazard_TankList,
    MajorHazard_Management,
    MajorHazard_AssessmentReportList,
    MajorHazard_CompanyManagement,

    Double_DangerList,
    Double_ReportList,
    Double_CheckList,

    SpecialJob_JobTicket,
    SpecialJob_CertificateList,

    ClosedPark_MessageBoard,
    ClosedPark_EntryExitList,
    ClosedPark_CameraMonitor,
}

public class Session : MonoBehaviour
{
    public static Session Instance { get; private set; }

    private SessionType _sessionType = SessionType.None;
    private SubSessionType _subSessionType = SubSessionType.None;
    private string _companyID = string.Empty;
    private string _tankID = string.Empty;
    private string _tankAreaID = string.Empty;
    private string _hazardSourceID = string.Empty;
    private string _cameraID = string.Empty;
    private WeatherType _weatherType = WeatherType.Sunny;
    private TimeType _timeType = TimeType.Morning;
    private PanelType _panelType = PanelType.None;
    
    public event System.Action BeforeSessionTypeChanged;
    public event System.Action BeforeSubSessionTypeChanged;
    public event System.Action BeforeCompanyIDChanged;
    public event System.Action BeforeTankIDChanged;
    public event System.Action BeforeCameraIDChanged;

    public event System.Action OnSessionTypeChanged;
    public event System.Action OnSubSessionTypeChanged;
    public event System.Action OnCompanyIDChanged;
    public event System.Action OnTankIDChanged;
    public event System.Action OnTankAreaIDChanged;
    public event System.Action OnHazardSourceIDChanged;
    public event System.Action OnCameraIDChanged;
    public event System.Action OnWeatherTypeChanged;
    public event System.Action OnTimeTypeChanged;
    public event System.Action OnPanelTypeChanged;

    public CesiumForUnity.CesiumGlobeAnchor companyPrefab;
    public CesiumForUnity.CesiumGlobeAnchor tankPrefab;
    public CesiumForUnity.CesiumGlobeAnchor cameraPrefab;

    public GameObject companyRoot => companyPrefab.transform.parent.gameObject;
    public GameObject tankRoot => tankPrefab.transform.parent.gameObject;
    public GameObject cameraRoot => cameraPrefab.transform.parent.gameObject;

    public SessionType sessionType
    {
        get { return _sessionType; }
        set
        {
            if (sessionPanels.TryGetValue(_sessionType, out SessionPanel currentPanel))
                currentPanel.gameObject.SetActive(false);
            if (sessionPanels.TryGetValue(value, out SessionPanel newPanel))
                newPanel.gameObject.SetActive(true);
            _sessionType = value;
            OnSessionTypeChanged?.Invoke();
        }
    }
    public SubSessionType subSessionType
    {
        get { return _subSessionType; }
        set
        {
            if (subSessionPanels.TryGetValue(_subSessionType, out SubSessionPanel currentPanel))
                currentPanel.gameObject.SetActive(false);
            if (subSessionPanels.TryGetValue(value, out SubSessionPanel newPanel))
                newPanel.gameObject.SetActive(true);
            _subSessionType = value;
            OnSubSessionTypeChanged?.Invoke();
        }
    }

    public string companyID
    {
        get { return _companyID; }
        set
        {
            _companyID = value;
            OnCompanyIDChanged?.Invoke();
        }
    }

    public string tankID
    {
        get { return _tankID; }
        set
        {
            _tankID = value;
            OnTankIDChanged?.Invoke();
        }
    }

    public string tankAreaID
    {
        get { return _tankAreaID; }
        set
        {
            if (string.Equals(_tankAreaID, value, StringComparison.Ordinal)) return;
            _tankAreaID = value ?? string.Empty;
            OnTankAreaIDChanged?.Invoke();
        }
    }

    public string hazardSourceID
    {
        get { return _hazardSourceID; }
        set
        {
            if (string.Equals(_hazardSourceID, value, StringComparison.Ordinal)) return;
            _hazardSourceID = value ?? string.Empty;
            OnHazardSourceIDChanged?.Invoke();
        }
    }

    public string cameraID
    {
        get { return _cameraID; }
        set
        {
            _cameraID = value;
            OnCameraIDChanged?.Invoke();
        }
    }

    public WeatherType weatherType
    {
        get { return _weatherType; }
        set
        {
            _weatherType = value;
            
            // 通过 Enviro 3 修改天气
            if (Enviro.EnviroManager.instance != null && Enviro.EnviroManager.instance.Weather != null)
            {
                string weatherName = GetEnviroWeatherName(value);
                Enviro.EnviroManager.instance.Weather.ChangeWeather(weatherName);
            }
            
            OnWeatherTypeChanged?.Invoke();
        }
    }

    public TimeType timeType
    {
        get { return _timeType; }
        set
        {
            _timeType = value;
            
            // 通过 Enviro 3 修改时间
            if (Enviro.EnviroManager.instance != null && Enviro.EnviroManager.instance.Time != null)
            {
                float timeOfDay = GetTimeOfDay(value);
                Enviro.EnviroManager.instance.Time.SetTimeOfDay(timeOfDay);
            }
            
            OnTimeTypeChanged?.Invoke();
        }
    }

    public PanelType panelType
    {
        get { return _panelType; }
        set
        {
            _panelType = value;
            OnPanelTypeChanged?.Invoke();
        }
    }

    public Dictionary<SessionType, SessionPanel> sessionPanels =
        new Dictionary<SessionType, SessionPanel>();
    public Dictionary<SubSessionType, SubSessionPanel> subSessionPanels =
        new Dictionary<SubSessionType, SubSessionPanel>();
    public Dictionary<string, CompanyObject> companyObjects =
        new Dictionary<string, CompanyObject>();
    public Dictionary<string, string> sysOrgCodeToID = new Dictionary<string, string>();
    public Dictionary<string, TankObject> tankObjects =
        new Dictionary<string, TankObject>();
    public readonly Dictionary<string, TankAreaObject> tankAreaObjects =
        new Dictionary<string, TankAreaObject>();
    public readonly Dictionary<string, HazardSourceObject> hazardSourceObjects =
        new Dictionary<string, HazardSourceObject>();
    public Dictionary<string, CameraObject> cameraObjects =
        new Dictionary<string, CameraObject>();

    /// <summary>
    /// cameras_4.csv 的 name -> cameraID（serialNumber）。
    ///
    /// ICC 告警只带 deviceName（"地点名_设备类型_日期"），拿不到摄像头的 serialNumber，
    /// 只能按地点名反查，所以建这张索引 —— 与 sysOrgCodeToID 同一个套路。
    /// </summary>
    public Dictionary<string, string> cameraNameToID = new Dictionary<string, string>();

    /// <summary>
    /// tankObjects 是否已经全部建完。MajorHazard_TankList / _Management /
    /// _CompanyManagement 三个面板都往 tankObjects 里建，原先各自用
    /// "tankObjects.Count == 0" 判断还没加载过。
    ///
    /// 罐区有 264 个，改成分帧建之后这个判断会被半截数据骗到：在第 20 个
    /// 建完时切走 tab，Unity 会把面板连协程一起停掉，再进来 Count 已经 > 0，
    /// 剩下 244 个永远补不回来。所以改用显式完成标志，不靠集合空不空推断。
    /// </summary>
    public bool tanksLoaded;
    
    // Weather 和 Time Panel 引用
    public GameObject weatherPanel;
    public GameObject timePanel;

    void Awake()
    {
        VRUISystem.Instance.AddPhysicsRaycaster = true;
        VRUISystem.Instance.PhysicsRaycasterEventMask = -1;
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    private string GetEnviroWeatherName(WeatherType weatherType)
    {
        switch (weatherType)
        {
            case WeatherType.Sunny:
                return "Clear Sky"; // Enviro 3 默认的晴天名称
            case WeatherType.Cloudy:
                return "Cloudy 1"; // Enviro 3 默认的多云名称
            case WeatherType.Rainy:
                return "Rain"; // Enviro 3 默认的下雨名称
            default:
                return "Clear Sky";
        }
    }
    
    private float GetTimeOfDay(TimeType timeType)
    {
        switch (timeType)
        {
            case TimeType.Morning:
                return 8.0f; // 上午 8:00
            case TimeType.Noon:
                return 12.0f; // 中午 12:00
            case TimeType.Afternoon:
                return 16.0f; // 下午 16:00 (4:00 PM)
            case TimeType.Night:
                return 22.0f; // 夜晚 22:00 (10:00 PM)
            default:
                return 12.0f;
        }
    }
}
