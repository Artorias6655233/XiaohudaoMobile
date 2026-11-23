using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ClosedPark_CameraMonitor : SubSessionPanel
{
    public override SubSessionType subSessionType => SubSessionType.ClosedPark_CameraMonitor;

    public TextAsset cameraCsvFile;

    [Header("告警详情")]
    public CameraAlarmDetailPanel detailPanel;

    /// <summary>告警查询的分页参数。接口上限未知，先按给的样例走 100。</summary>
    private const int AlarmPage = 1;
    private const int AlarmPageSize = 100;

    /// <summary>
    /// 告警查询请求体。所有值都是固定的，所以直接拼字符串。
    ///
    /// 不用 JsonUtility 序列化：那玩意会把 null 的 string 字段写成 ""，
    /// 而这个接口的日期区间字段必须是真 null 才表示"不限"。
    /// </summary>
    private const string AlarmQueryBodyFormat =
        "{{\"body\":{{\"alarmDateStart\":null,\"alarmDateEnd\":null,"
        + "\"alarmTimeStart\":null,\"alarmTimeEnd\":null,"
        + "\"isUrge\":\"0\",\"sid\":null,\"deviceType\":\"3\"}},"
        + "\"page\":{0},\"pageSize\":{1}}}";

    private CesiumForUnity.CesiumGlobeAnchor cameraPrefab => Session.Instance.cameraPrefab;
    private GameObject cameraRoot => Session.Instance.cameraRoot;
    private Dictionary<string, CameraObject> cameraObjects => Session.Instance.cameraObjects;
    private Dictionary<string, string> cameraNameToID => Session.Instance.cameraNameToID;

    /// <summary>CSV 全部建完过一次。建完就不再重复解析。</summary>
    private bool loadComplete;

    /// <summary>正在分帧建。防止一次 tab 切换里重复起协程。</summary>
    private bool loading;

    /// <summary>ICC 告警取过一次就不再取（不做定时刷新）。</summary>
    private bool alarmsLoaded;

    /// <summary>正在取告警。防止一次 tab 切换里重复起协程。</summary>
    private bool alarmsLoading;

    private List<IccAlarm> alarms = new List<IccAlarm>();

    /// <summary>cameraID -> 该摄像头最新的一条告警。</summary>
    private readonly Dictionary<string, IccAlarm> latestAlarmByCameraID =
        new Dictionary<string, IccAlarm>();

    /// <summary>cameraID -> 该摄像头匹配到的告警条数。</summary>
    private readonly Dictionary<string, int> alarmCountByCameraID = new Dictionary<string, int>();

    private bool isEventSubscribed;

    public override IEnumerator Start()
    {
        yield return base.Start();
        SubscribeToCameraIDChanged();
    }

    private void OnDisable()
    {
        // 面板被 SetActive(false) 时 Unity 会直接杀掉本对象上的协程，
        // 分帧加载可能停在半路。把 loading 清掉，下次进来才能续上；
        // CreateCameraInstance 会跳过已建好的 id，所以续建是幂等的。
        loading = false;

        // 告警协程同理：被打断就让下次重新取（alarmsLoaded 只有真拿到数据才置位）
        alarmsLoading = false;
    }

    private void OnDestroy()
    {
        if (Session.Instance != null)
        {
            Session.Instance.OnCameraIDChanged -= OnCameraSelected;
        }
    }

    private void SubscribeToCameraIDChanged()
    {
        if (isEventSubscribed || Session.Instance == null)
        {
            return;
        }

        Session.Instance.OnCameraIDChanged += OnCameraSelected;
        isEventSubscribed = true;
    }

    public override void OnSubSessionTypeChanged()
    {
        base.OnSubSessionTypeChanged();
        SubscribeToCameraIDChanged();

        if (Session.Instance.subSessionType == SubSessionType.ClosedPark_CameraMonitor)
        {
            // 隐藏其他 Root
            if (Session.Instance.tankRoot != null) Session.Instance.tankRoot.SetActive(false);
            if (Session.Instance.companyRoot != null) Session.Instance.companyRoot.SetActive(false);

            // 显示 Camera Root
            if (cameraRoot != null) cameraRoot.SetActive(true);

            // 如果没有数据则加载。499 个 CesiumGlobeAnchor 必须分帧建，
            // 否则整份实例化会压在点击当帧上，直接卡一下。
            if (!loadComplete && !loading)
            {
                StartCoroutine(LoadCameras());
            }

            if (!alarmsLoaded && !alarmsLoading)
            {
                StartCoroutine(LoadAlarms());
            }
        }
        else
        {
            // 离开时隐藏
            if (cameraRoot != null) cameraRoot.SetActive(false);

            if (detailPanel != null) detailPanel.Hide();

            // 选中态不该跨 tab 留着：下次进来时图标应该是未选中的
            if (Session.Instance.cameraID != string.Empty)
            {
                Session.Instance.cameraID = string.Empty;
            }
        }
    }

    private IEnumerator LoadCameras()
    {
        if (cameraCsvFile == null)
        {
            Debug.LogError("Camera CSV file not assigned in ClosedPark_CameraMonitor!");
            yield break;
        }

        loading = true;

        string[] lines = cameraCsvFile.text.Split('\n');
        int created = 0;

        // 跳过表头 (第一行)
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            string[] parts = ParseCsvLine(line);
            // 预期列: ---,name,location,ip,serial_number,latitude,longitude,type
            if (parts.Length < 8) continue;

            CameraInfo info = new CameraInfo();
            info.name = parts[1];
            info.location = parts[2];
            info.ip = parts[3];
            info.serialNumber = parts[4];
            double.TryParse(parts[5], out info.latitude);
            double.TryParse(parts[6], out info.longitude);
            info.type = parts[7];
            info.id = info.serialNumber; // 使用序列号作为 ID

            if (!CreateCameraInstance(info))
            {
                continue; // 已经建过，不占分帧配额
            }

            if (++created % FrameBudget.ItemsPerFrame == 0)
            {
                yield return null;
            }
        }

        loading = false;
        loadComplete = true;
        Debug.Log($"Loaded {cameraObjects.Count} cameras.");

        // 告警可能比 CSV 先到（网络快 / 走了离线镜像），那时标记还没建出来，
        // 高亮会全部落空。所以两条路径末尾都要再套一次，ApplyAlarmHighlight 是幂等的。
        ApplyAlarmHighlight();
    }

    /// <summary>
    /// 取 ICC 周界入侵告警。
    ///
    /// token 有效期未知，第一轮没拿到数据就重登一次再试。只重试一次，
    /// 免得 token 一直无效时在这里打转。
    /// </summary>
    private IEnumerator LoadAlarms()
    {
        alarmsLoading = true;

        yield return FetchAlarms(false);

        // 没拿到数据（401、登录失败、解析失败都算）就换一个新 token 再来一次
        if (!alarmsLoaded)
        {
            yield return FetchAlarms(true);
        }

        alarmsLoading = false;

        if (!alarmsLoaded)
        {
            Debug.LogError("[周界入侵] 告警取数失败，沙盘上不会有高亮摄像头");
        }
    }

    private IEnumerator FetchAlarms(bool forceRelogin)
    {
        yield return IccAuth.EnsureToken(forceRelogin);

        if (!IccAuth.HasToken)
        {
            yield break;
        }

        string body = string.Format(AlarmQueryBodyFormat, AlarmPage, AlarmPageSize);
        var headers = new Dictionary<string, string> { { "Token", IccAuth.Token } };

        var fetch = new OfflineMock.Result();
        yield return OfflineMock.PostJson(Api.IccDataEventDetailFind(), body, headers, fetch);

        // token 过期时 PostJson 会静默回退到随包镜像，从 fetch.ok 上看和成功没区别。
        // 第一轮撞上 401 先不收工，让 LoadAlarms 带 force 重登一次再来 ——
        // 否则演示时看到的是镜像里那几条，真实告警一条都没拿到，还看不出来。
        if (fetch.httpCode == 401 && !forceRelogin)
        {
            Debug.LogWarning("[周界入侵] 告警接口返回 401，重新登录后重试");
            yield break;
        }

        if (!fetch.ok)
        {
            yield break;
        }

        if (fetch.httpCode == 401)
        {
            Debug.LogWarning("[周界入侵] 重登后仍是 401，下面用的是随包镜像数据，不是实时告警");
        }

        // 与其它面板一致走统一入口：超过阈值的丢后台线程解析
        var parsed = new BackgroundJson.Result();
        yield return BackgroundJson.Parse(typeof(IccAlarmExternalResponse), fetch.json, parsed);
        IccAlarmExternalResponse response = parsed.value as IccAlarmExternalResponse;

        // 判空必须落到 Count 上，不能只判 null。
        //
        // BackgroundJson 在响应小于 256KB 时走 JsonUtility，而 JsonUtility 对声明过的
        // List 字段一律给一个空 List、不会留 null。第一版在这里判 body.data == null，
        // 结果信封写错时既没数据也没报错，静默过去了，真机上查了一轮才定位到。
        List<IccAlarm> records = response != null && response.body != null
            ? response.body.data
            : null;

        if (records == null || records.Count == 0)
        {
            // 只打长度不打内容：响应里带设备信息，整段进 logcat 既慢也不合适
            Debug.LogError($"[周界入侵] 响应里没取到告警记录（{fetch.json.Length} 字符，"
                + $"来源：{fetch.source}，status={response?.status}，total={response?.body?.total}"
                + $"，解析错误={parsed.error ?? "无"}）");
            yield break;
        }

        alarms = records;
        alarmsLoaded = true;
        ApplyAlarmHighlight();
    }

    /// <summary>
    /// 把告警按地点名匹配到摄像头，命中的高亮并开启点击。
    ///
    /// 必须幂等：CSV 分帧建约 25 帧、网络时序不定，两条路径完成的先后不确定，
    /// 所以两边末尾都会调一次。
    /// </summary>
    private void ApplyAlarmHighlight()
    {
        if (alarms == null || alarms.Count == 0)
        {
            // 这里以前是静默 return。信封写错那次就是卡在这儿：既没高亮也没有任何日志，
            // 只能靠"该出现的那行没出现"反推。留一行，别再让沉默藏问题。
            if (alarmsLoaded)
            {
                Debug.LogWarning("[周界入侵] 告警 0 条，没有摄像头会被高亮");
            }

            return;
        }

        latestAlarmByCameraID.Clear();
        alarmCountByCameraID.Clear();

        int unmatched = 0;

        for (int i = 0; i < alarms.Count; i++)
        {
            IccAlarm alarm = alarms[i];
            if (alarm == null) continue;

            string location = alarm.LocationName;
            if (string.IsNullOrEmpty(location)) continue;

            if (!cameraNameToID.TryGetValue(location, out string id))
            {
                unmatched++;
                continue;
            }

            if (!cameraObjects.TryGetValue(id, out CameraObject cam) || cam == null)
            {
                // 标记还没建出来（CSV 分帧建没跑完）。LoadCameras 结束时会再调一次本方法。
                continue;
            }

            alarmCountByCameraID.TryGetValue(id, out int count);
            alarmCountByCameraID[id] = count + 1;

            // 一个地点多条告警时只展示最新的那条
            if (!latestAlarmByCameraID.TryGetValue(id, out IccAlarm kept) || IsNewer(alarm, kept))
            {
                latestAlarmByCameraID[id] = alarm;
            }

            cam.SetState(CameraState.Alarm);
            cam.EnableClick();
        }

        Debug.Log($"[周界入侵] 告警 {alarms.Count} 条，匹配摄像头 {latestAlarmByCameraID.Count} 个" +
            $"，地点名对不上 {unmatched} 条（摄像头已建 {cameraObjects.Count} 个）");
    }

    /// <summary>
    /// alarmDate 谁更新。格式是 "yyyy-MM-dd HH:mm:ss"，定长且字典序等于时间序，
    /// 直接比字符串就够，不用为此引 DateTime.Parse（还得处理 null 和脏数据）。
    /// </summary>
    private static bool IsNewer(IccAlarm candidate, IccAlarm current)
    {
        string a = candidate != null ? candidate.alarmDate : null;
        string b = current != null ? current.alarmDate : null;

        if (string.IsNullOrEmpty(a)) return false;
        if (string.IsNullOrEmpty(b)) return true;

        return string.CompareOrdinal(a, b) > 0;
    }

    private void OnCameraSelected()
    {
        if (Session.Instance.subSessionType != subSessionType) return;
        if (detailPanel == null) return;

        string selected = Session.Instance.cameraID;

        // 取消选中，或点了没有告警的摄像头 —— 两种都不该留着上一条详情
        if (string.IsNullOrEmpty(selected)
            || !latestAlarmByCameraID.TryGetValue(selected, out IccAlarm alarm))
        {
            detailPanel.Hide();
            return;
        }

        cameraObjects.TryGetValue(selected, out CameraObject cam);
        alarmCountByCameraID.TryGetValue(selected, out int total);
        detailPanel.Show(cam, alarm, total);
    }

    private string[] ParseCsvLine(string line)
    {
        // 简单处理 CSV，去除引号
        string[] rawParts = line.Split(',');
        for(int i=0; i<rawParts.Length; i++)
        {
            rawParts[i] = rawParts[i].Trim('"');
        }
        return rawParts;
    }

    /// <summary>
    /// 建一个摄像头标记。返回 false 表示这个 id 已经建过、本次跳过。
    ///
    /// 去重检查必须在 Instantiate 之前：原先是先 Instantiate 再判
    /// ContainsKey，重复 id 会留下没人引用的孤儿 GameObject。分帧后
    /// 中途切走再回来会走续建路径，这个漏洞会被放大，所以一并修掉。
    /// </summary>
    private bool CreateCameraInstance(CameraInfo info)
    {
        if (cameraPrefab == null) return false;
        if (string.IsNullOrEmpty(info.id)) return false;
        if (cameraObjects.ContainsKey(info.id)) return false;

        CesiumForUnity.CesiumGlobeAnchor instance = Instantiate(cameraPrefab, cameraRoot.transform);

        // 设置经纬度和高度
        instance.longitudeLatitudeHeight = new Unity.Mathematics.double3(
            info.longitude,
            info.latitude,
            100 // 高度，参考 Security_Member 设置为 100
        );

        CameraObject camObj = instance.GetComponent<CameraObject>();
        if (camObj != null)
        {
            camObj.cameraID = info.id;
            camObj.cameraDetails = info;
            camObj.SetState(CameraState.Normal); // 默认显示为 Normal
        }

        cameraObjects.Add(info.id, camObj);

        // ICC 告警只带地点名，反查索引在这里一并建起来
        if (!string.IsNullOrEmpty(info.name))
        {
            cameraNameToID[info.name.Trim()] = info.id;
        }

        return true;
    }
}
