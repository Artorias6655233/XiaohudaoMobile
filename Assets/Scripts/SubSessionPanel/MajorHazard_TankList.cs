using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

public class MajorHazard_TankList : SubSessionPanel
{
    private const int CatalogPageSize = 300;
    private const int RequestTimeoutSeconds = 15;
    private const float PollIntervalSeconds = 5f;

    /// <summary>
    /// 目录反序列化设置，必须显式给，不能用 JsonConvert 的默认值。
    ///
    /// NullValueHandling：Company.longitude84 这类字段声明成非空 float，接口没配置时给 null，
    /// 不忽略会让整包反序列化直接失败。
    /// DateParseHandling：理由同 BackgroundJson —— 模型里时间全是 string，
    /// 默认值会把它们先转成 DateTime 再转回来，显示格式就变了。
    /// </summary>
    private static readonly JsonSerializerSettings CatalogJson = new JsonSerializerSettings
    {
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
        MissingMemberHandling = MissingMemberHandling.Ignore,
    };

    public override SubSessionType subSessionType => SubSessionType.MajorHazard_TankList;
    public GameObject tankMonitorPrefab;

    /// <summary>储罐列表卡片，与详情卡片并排，不是同一张，见 TankListPanel。</summary>
    public GameObject tankListPrefab;
    public GameObject p0MonitoringPanelPrefab;
    private readonly Dictionary<string, Company> companies = new Dictionary<string, Company>();
    private readonly Dictionary<string, Tank> tanks = new Dictionary<string, Tank>();
    private readonly HashSet<string> locatedTanks = new HashSet<string>();

    /// <summary>企业组织编码 -> 该企业在沙盘上的那一个图标及其代表的全部罐，见 TankGrouping。</summary>
    private readonly Dictionary<string, TankGrouping.Group> groups =
        new Dictionary<string, TankGrouping.Group>();
    private TankMonitorPanel monitor;
    private TankListPanel listPanel;
    private Coroutine polling;
    private UnityWebRequest catalogRequest;
    private UnityWebRequest readingRequest;
    private bool subscribed, ready, catalogFailed;
    private readonly Dictionary<string, CompanyObject> mockCompanyObjects = new Dictionary<string, CompanyObject>();
    private readonly Dictionary<string, P0TankArea> mockAreas = new Dictionary<string, P0TankArea>();
    private readonly Dictionary<string, P0AreaSnapshot> mockSnapshots = new Dictionary<string, P0AreaSnapshot>();
    private readonly Dictionary<string, List<P0ParameterState>> mockStates = new Dictionary<string, List<P0ParameterState>>();
    private readonly P0ActivationGuard mockGuard = new P0ActivationGuard();
    private IP0DataSource p0Source;
    private P0TankAreaMonitorPanel p0Monitor;
    private Coroutine mockPolling;
    private bool mockReady;

    /// <summary>目录取到了数据但分页与 total 对不上：降级展示 + 明示遗漏，不清空已取到的部分。</summary>
    private bool catalogIncomplete;

    /// <summary>有储罐坐标能解析、但落在无意义的位置被本入口刷掉了，见 <see cref="CreateTank"/>。</summary>
    private bool skippedPlaceableTank;
    private bool IsCurrent => Session.Instance != null && Session.Instance.subSessionType == subSessionType;

    public override IEnumerator Start()
    {
        yield return base.Start();
        Subscribe();
    }

    private void Subscribe()
    {
        if (subscribed || Session.Instance == null) return;
        Session.Instance.OnCompanyIDChanged += OnCompanySelected;
        Session.Instance.OnTankIDChanged += OnTankSelected;
        Session.Instance.OnTankAreaIDChanged += OnTankAreaSelected;
        subscribed = true;
    }

    public override void BeforeSubSessionTypeChanged() { }

    public override void OnSubSessionTypeChanged()
    {
        base.OnSubSessionTypeChanged();
        Subscribe();
        if (!IsCurrent) return;
        StopAllCoroutines();
        CancelRequests();
        StopMockMode();
        ready = false;
        companies.Clear();
        tanks.Clear();
        locatedTanks.Clear();
        groups.Clear();
        if (ApiConfig.P0Mode == P0DataMode.Mock)
        {
            StartMockMode();
            return;
        }
        if (listPanel == null && tankListPrefab != null)
            listPanel = Instantiate(tankListPrefab, transform).GetComponent<TankListPanel>();
        if (monitor == null && tankMonitorPrefab != null)
            monitor = Instantiate(tankMonitorPrefab, transform).GetComponent<TankMonitorPanel>();
        if (monitor == null)
        {
            Debug.LogError("监测预警未配置监测卡片");
            return;
        }
        Session.Instance.companyRoot.SetActive(true);
        Session.Instance.tankRoot.SetActive(true);
        foreach (var item in Session.Instance.companyObjects.Values)
            if (item != null) item.gameObject.SetActive(false);
        foreach (var item in Session.Instance.tankObjects.Values)
            if (item != null) item.gameObject.SetActive(false);
        monitor.ShowLoading("监测预警", "");
        StartCoroutine(LoadCatalog());
    }

    private void StartMockMode()
    {
        mockReady = false;
        var session = Session.Instance;
        if (session == null) return;

        if (p0Monitor == null && p0MonitoringPanelPrefab != null)
            p0Monitor = Instantiate(p0MonitoringPanelPrefab, transform).GetComponent<P0TankAreaMonitorPanel>();
        if (p0Monitor == null)
        {
            Debug.LogError("[P0 Mock] 监测预警未配置罐区监测卡片");
            return;
        }

        try
        {
            p0Source = P0DataSourceFactory.Create(P0DataMode.Mock);
            p0Source.Load();
        }
        catch (Exception exception)
        {
            Debug.LogError("[P0 Mock] 演示数据包不可用：" + exception.Message);
            p0Monitor.ShowHint("监测预警", "演示数据包不可用");
            return;
        }

        session.companyRoot.SetActive(true);
        session.tankRoot.SetActive(true);
        foreach (CompanyObject item in session.companyObjects.Values)
            if (item != null) item.gameObject.SetActive(false);
        foreach (TankObject item in session.tankObjects.Values)
            if (item != null) item.gameObject.SetActive(false);

        ClearMockCompanies();
        foreach (P0Company company in p0Source.GetCompanies()) CreateMockCompany(company);

        mockReady = true;
        session.tankAreaID = string.Empty;
        OnMockCompanySelected();
    }

    private void CreateMockCompany(P0Company company)
    {
        if (!ValidCoordinates(company.longitude, company.latitude)) return;
        Session session = Session.Instance;
        var anchor = Instantiate(session.companyPrefab, session.companyRoot.transform);
        anchor.longitudeLatitudeHeight = new Unity.Mathematics.double3(
            company.longitude, company.latitude, company.height);
        CompanyObject item = anchor.GetComponent<CompanyObject>();
        item.companyID = company.companyId;
        item.companyDetails = new Company
        {
            id = company.companyId,
            sysOrgCode = company.sysOrgCode,
            companyName = company.name,
            companyShortName = company.name,
            longitude = (float)company.longitude,
            latitude = (float)company.latitude
        };
        item.gameObject.SetActive(true);
        mockCompanyObjects[company.companyId] = item;
    }

    private void OnMockCompanySelected()
    {
        if (!mockReady || !IsCurrent) return;
        StopMockPolling();
        ClearMockAreas();

        string companyId = Session.Instance.companyID ?? string.Empty;
        int generation = mockGuard.Activate(companyId);
        P0Company company = p0Source.GetCompanies().FirstOrDefault(item => item.companyId == companyId);
        if (company == null)
        {
            p0Monitor.ShowHint("监测预警", "请选择企业查看罐区监测");
            return;
        }

        foreach (P0TankArea area in p0Source.GetTankAreas(companyId)) CreateMockArea(area);
        p0Monitor.ShowHint(company.name,
            mockAreas.Count == 0 ? "该企业暂无可定位罐区" : $"共 {mockAreas.Count} 个罐区，请选择图标查看");
        RefreshMockFrame(company, 0, generation);
        mockPolling = StartCoroutine(PollMock(company, generation));
    }

    private void CreateMockArea(P0TankArea area)
    {
        if (!ValidCoordinates(area.longitude, area.latitude)) return;
        Session session = Session.Instance;
        var anchor = Instantiate(session.tankPrefab, session.tankRoot.transform);
        anchor.longitudeLatitudeHeight = new Unity.Mathematics.double3(
            area.longitude, area.latitude, area.height);

        TankObject tank = anchor.GetComponent<TankObject>();
        ImageSelection selection = PrepareTankAreaVisual(tank);
        if (tank != null) tank.enabled = false;

        TankAreaObject item = anchor.gameObject.AddComponent<TankAreaObject>();
        item.areaId = area.areaId;
        item.details = area;
        item.alarmImage = selection.alarm;
        item.selectionImage = selection.selection;
        item.nameText = tank != null && tank.labelImage != null
            ? tank.labelImage.GetComponentInChildren<TMPro.TextMeshProUGUI>(true)
            : anchor.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
        item.Apply(P0AlarmLevel.Unknown, false);
        item.gameObject.SetActive(true);
        mockAreas[area.areaId] = area;
        session.tankAreaObjects[area.areaId] = item;
    }

    private static ImageSelection PrepareTankAreaVisual(TankObject tank)
    {
        var result = new ImageSelection();
        if (tank == null) return result;
        result.alarm = tank.hitpointImage;
        if (tank.labelImage != null) tank.labelImage.gameObject.SetActive(true);
        if (tank.normalStateImage != null) tank.normalStateImage.gameObject.SetActive(false);
        if (tank.belowNormalStateImage != null) tank.belowNormalStateImage.gameObject.SetActive(false);
        if (tank.aboveNormalStateImage != null) tank.aboveNormalStateImage.gameObject.SetActive(false);

        if (result.alarm != null)
        {
            GameObject selectionObject = new GameObject(
                "P0Selection",
                typeof(RectTransform),
                typeof(UnityEngine.UI.Image));
            RectTransform source = result.alarm.rectTransform;
            RectTransform target = selectionObject.GetComponent<RectTransform>();
            target.SetParent(source.parent, false);
            target.anchorMin = source.anchorMin;
            target.anchorMax = source.anchorMax;
            target.pivot = source.pivot;
            target.anchoredPosition = source.anchoredPosition;
            target.sizeDelta = source.sizeDelta * 1.5f;
            result.selection = selectionObject.GetComponent<UnityEngine.UI.Image>();
            result.selection.sprite = result.alarm.sprite;
            result.selection.raycastTarget = false;
            selectionObject.SetActive(false);
        }
        return result;
    }

    private IEnumerator PollMock(P0Company company, int generation)
    {
        int frame = 1;
        while (mockGuard.IsCurrent(generation, company.companyId) && IsCurrent)
        {
            yield return new WaitForSecondsRealtime(PollIntervalSeconds);
            if (!mockGuard.IsCurrent(generation, company.companyId) || !IsCurrent) yield break;
            RefreshMockFrame(company, frame++, generation);
        }
    }

    private void RefreshMockFrame(P0Company company, int frame, int generation)
    {
        if (!mockGuard.IsCurrent(generation, company.companyId) || !IsCurrent) return;
        IReadOnlyList<P0AreaSnapshot> snapshots = p0Source.GetAreaSnapshots(
            company.companyId, frame, DateTimeOffset.Now);
        foreach (P0AreaSnapshot snapshot in snapshots)
        {
            if (!mockAreas.TryGetValue(snapshot.areaId, out P0TankArea area)) continue;
            var states = new List<P0ParameterState>();
            foreach (P0SensorDefinition sensor in area.sensors)
            {
                P0SensorReading reading = snapshot.readings.FirstOrDefault(
                    item => item.sensorId == sensor.sensorId);
                states.Add(P0AlarmEvaluator.Evaluate(sensor, reading, DateTimeOffset.Now));
            }
            mockSnapshots[area.areaId] = snapshot;
            mockStates[area.areaId] = states;
            if (Session.Instance.tankAreaObjects.TryGetValue(area.areaId, out TankAreaObject marker) && marker != null)
                marker.Apply(P0AlarmEvaluator.Aggregate(states), Session.Instance.tankAreaID == area.areaId);
        }
        ShowSelectedMockArea(company);
    }

    private void OnTankAreaSelected()
    {
        if (!mockReady || !IsCurrent || ApiConfig.P0Mode != P0DataMode.Mock) return;
        foreach (var pair in Session.Instance.tankAreaObjects)
        {
            if (pair.Value == null) continue;
            P0AlarmLevel level = mockStates.TryGetValue(pair.Key, out List<P0ParameterState> states)
                ? P0AlarmEvaluator.Aggregate(states)
                : P0AlarmLevel.Unknown;
            pair.Value.Apply(level, pair.Key == Session.Instance.tankAreaID);
        }
        P0Company company = p0Source.GetCompanies().FirstOrDefault(
            item => item.companyId == Session.Instance.companyID);
        if (company != null && string.IsNullOrEmpty(Session.Instance.tankAreaID))
        {
            p0Monitor.ShowHint(company.name,
                mockAreas.Count == 0 ? "该企业暂无可定位罐区" : $"共 {mockAreas.Count} 个罐区，请选择图标查看");
            return;
        }
        ShowSelectedMockArea(company);
    }

    private void ShowSelectedMockArea(P0Company company)
    {
        string areaId = Session.Instance.tankAreaID ?? string.Empty;
        if (company == null || !mockAreas.TryGetValue(areaId, out P0TankArea area) ||
            !mockSnapshots.TryGetValue(areaId, out P0AreaSnapshot snapshot) ||
            !mockStates.TryGetValue(areaId, out List<P0ParameterState> states))
            return;
        p0Monitor.Show(company.name, area, snapshot, states);
    }

    private void StopMockPolling()
    {
        if (mockPolling != null) StopCoroutine(mockPolling);
        mockPolling = null;
    }

    private void ClearMockAreas()
    {
        if (Session.Instance != null)
        {
            foreach (TankAreaObject item in Session.Instance.tankAreaObjects.Values)
                if (item != null)
                {
                    item.gameObject.SetActive(false);
                    Destroy(item.gameObject);
                }
            Session.Instance.tankAreaObjects.Clear();
            Session.Instance.tankAreaID = string.Empty;
        }
        mockAreas.Clear();
        mockSnapshots.Clear();
        mockStates.Clear();
    }

    private void ClearMockCompanies()
    {
        foreach (CompanyObject item in mockCompanyObjects.Values)
            if (item != null)
            {
                item.gameObject.SetActive(false);
                Destroy(item.gameObject);
            }
        mockCompanyObjects.Clear();
    }

    private void StopMockMode()
    {
        mockReady = false;
        StopMockPolling();
        mockGuard.Deactivate();
        ClearMockAreas();
        ClearMockCompanies();
        if (p0Monitor != null) p0Monitor.Hide();
    }

    private static bool ValidCoordinates(double longitude, double latitude)
    {
        return !double.IsNaN(longitude) && !double.IsNaN(latitude) &&
               !double.IsInfinity(longitude) && !double.IsInfinity(latitude) &&
               Math.Abs(longitude) <= 180 && Math.Abs(latitude) <= 90 &&
               longitude != 0 && latitude != 0;
    }

    private sealed class ImageSelection
    {
        public UnityEngine.UI.Image alarm;
        public UnityEngine.UI.Image selection;
    }

    private IEnumerator LoadCatalog()
    {
        catalogFailed = false;
        catalogIncomplete = false;
        skippedPlaceableTank = false;
        // 返回值只回答"这一页有没有带来新 key"，覆盖语义保持原样（后到的覆盖先到的），
        // 免得在 tankId 重复的问题定下来之前又改一遍谁赢。
        yield return LoadPages<Company>("企业目录", Api.SecurityCompanyList, company =>
        {
            if (string.IsNullOrWhiteSpace(company.id)) return false;
            bool isNew = !companies.ContainsKey(company.id);
            companies[company.id] = company;
            return isNew;
        });
        if (catalogFailed) yield break;
        yield return LoadPages<Tank>("储罐目录", Api.MajorHazardTankList, tank =>
        {
            string key = tank.MarkerKey;
            if (key == null) return false;
            bool isNew = !tanks.ContainsKey(key);
            tanks[key] = tank;
            return isNew;
        });
        if (catalogFailed) yield break;
        // 其他面板用 companyObjects.Count > 0 判断企业目录已经加载完。
        // 这里不能分帧让出控制权，否则用户在创建中途切页会留下非空但不完整的共享缓存。
        PublishCompanyMarkers();
        yield return FrameBudget.Spread(new List<Tank>(tanks.Values), CreateTank);

        groups.Clear();
        foreach (var group in TankGrouping.Build(tanks.Values)) groups[group.sysOrgCode] = group;

        // 替 _Management / _CompanyManagement 置位，省掉它们再整拉一遍 264 条。
        // 前提是两边建出来的集合一致：目录完整，且没有被本入口独有的坐标合理性检查刷掉过
        // （坐标解析不出来的那些，_Management 同样会跳过，不构成差异）。
        if (!catalogIncomplete && !skippedPlaceableTank)
            Session.Instance.tanksLoaded = true;

        ready = true;
        if (!SelectDebugDefaultCompany()) OnCompanySelected();
    }

    /// <summary>
    /// 联调用：按配置里的 <see cref="ApiConfig.DebugDefaultCompanyId"/> 替用户选中企业，
    /// 省掉在沙盘里从 51 个图标中找目标那一步。返回是否已经代为选中。
    ///
    /// 走的是和手柄点击完全相同的入口（给 <c>Session.companyID</c> 赋值），
    /// 所以企业图标的选中态、储罐过滤、卡片刷新都跟真点一次一模一样，不是另一条支路。
    /// 配置留空时本方法直接返回 false，行为与没有这个开关时一致。
    /// </summary>
    private bool SelectDebugDefaultCompany()
    {
        string id = ApiConfig.DebugDefaultCompanyId;
        if (string.IsNullOrWhiteSpace(id)) return false;
        if (!companies.ContainsKey(id))
        {
            Debug.LogWarning($"[监测预警] 调试默认企业 {id} 不在企业目录里，按未选中处理");
            return false;
        }

        Debug.Log($"[监测预警] 已按配置默认选中企业 {companies[id].companyName}（{id}）");
        // setter 无条件触发 OnCompanyIDChanged，所以这里不用再手动调 OnCompanySelected
        Session.Instance.companyID = id;
        return true;
    }

    /// <summary>
    /// 分页拉一个目录。<paramref name="accept"/> 收下一条并返回它是不是新 key。
    ///
    /// 光看累计条数不够：这套后端出现过忽略 pageNum、每页都回同样前 N 条的情况
    /// （docs 里 G4 对气体点位接口的实测），那样只按 received &gt;= total 判断会在
    /// 不报错的情况下只拿到第一页。所以再看本页有没有带来新 key，一条都没有就停。
    /// </summary>
    private IEnumerator LoadPages<T>(string name, Func<int, int, string> url, Func<T, bool> accept)
    {
        int received = 0;
        int total = 0;
        for (int page = 1; page <= 100; page++)
        {
            catalogRequest = UnityWebRequest.Get(url(page, CatalogPageSize));
            catalogRequest.timeout = RequestTimeoutSeconds;
            yield return catalogRequest.SendWebRequest();

            // 先把正文取出来再释放请求：后面解析要等好几帧，不该把连接一直挂着
            string body = null;
            string networkError = null;
            if (catalogRequest.result == UnityWebRequest.Result.Success)
                body = catalogRequest.downloadHandler.text;
            else
                networkError = $"{catalogRequest.result} {catalogRequest.error}";
            catalogRequest.Dispose();
            catalogRequest = null;

            if (networkError != null)
            {
                FailCatalog($"{name}第 {page} 页请求失败：{networkError}");
                yield break;
            }

            // 200KB 量级的响应在主线程解析会掉帧，实测数据见 BackgroundJson 的注释。
            // 没复用 BackgroundJson.Parse：它对小响应走 JsonUtility，而 Company.longitude
            // 声明成 float、接口给的是字符串 "113.55"，JsonUtility 取不出来。
            Task<ResponseList<T>> parse = Task.Run(
                () => JsonConvert.DeserializeObject<ResponseList<T>>(body, CatalogJson));
            while (!parse.IsCompleted) yield return null;

            if (parse.Status != TaskStatus.RanToCompletion)
            {
                FailCatalog($"{name}第 {page} 页解析失败：{parse.Exception?.GetBaseException().Message}");
                yield break;
            }

            ResponseList<T> response = parse.Result;
            if (response == null || !response.success || response.result?.data == null)
            {
                FailCatalog($"{name}第 {page} 页响应不完整：{response?.message}");
                yield break;
            }

            total = response.result.total;
            int fresh = 0;
            foreach (T item in response.result.data)
                if (accept(item)) fresh++;
            received += response.result.data.Count;

            if (received >= total) yield break;          // 服务端自报的条数已取满，正常结束
            if (response.result.data.Count == 0) break;  // 服务端说没有更多了
            if (fresh == 0) break;                       // 整页都是重复，分页参数多半没生效
        }

        // 还有数据没拿到，但已经取到的那部分仍然可用：降级展示并在卡片上明示，
        // 而不是把几百条一起丢掉、整个入口不可用。
        catalogIncomplete = true;
        Debug.LogWarning($"[监测预警] {name}分页不自洽：服务端 total={total}，实际取到 {received} 条");
    }

    private void FailCatalog(string reason)
    {
        catalogFailed = true;
        Debug.LogError($"[监测预警] {reason}");
        monitor.ShowError("企业或储罐目录获取失败，请重新进入重试");
    }

    private void CreateCompany(Company company)
    {
        var session = Session.Instance;
        if (float.IsNaN(company.longitude) || float.IsNaN(company.latitude)
            || Math.Abs(company.longitude) > 180 || Math.Abs(company.latitude) > 90
            || company.longitude == 0 || company.latitude == 0) return;
        if (!session.companyObjects.TryGetValue(company.id, out var item) || item == null)
        {
            var anchor = Instantiate(session.companyPrefab, session.companyRoot.transform);
            item = anchor.GetComponent<CompanyObject>();
            session.companyObjects[company.id] = item;
        }
        item.GetComponent<CesiumForUnity.CesiumGlobeAnchor>().longitudeLatitudeHeight =
            new Unity.Mathematics.double3(company.longitude, company.latitude, 100);
        item.companyID = company.id;
        item.companyDetails = company;
        item.gameObject.SetActive(true);
        if (!string.IsNullOrWhiteSpace(company.sysOrgCode)) session.sysOrgCodeToID[company.sysOrgCode] = company.id;
    }

    private void PublishCompanyMarkers()
    {
        foreach (Company company in companies.Values)
            CreateCompany(company);
    }

    private void CreateTank(Tank tank)
    {
        var session = Session.Instance;

        // 坐标压根不是数字。随包镜像里 264 条有 111 条直接把字段名 "longitude"/"latitude"
        // 当值返回，_Management 的建法同样解析失败会跳过，两边一致，不影响 tanksLoaded。
        if (!double.TryParse(tank.longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)
            || !double.TryParse(tank.latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude))
            return;

        if (double.IsNaN(longitude) || double.IsNaN(latitude)
            || Math.Abs(longitude) > 180 || Math.Abs(latitude) > 90 || longitude == 0 || latitude == 0)
        {
            // 能解析但落在无意义的位置。_Management 没有这道检查，会把它摆到几内亚湾 0°N 0°E，
            // 两边建出来的集合就不一样了，这时不能替它置 tanksLoaded。
            skippedPlaceableTank = true;
            return;
        }

        string key = tank.MarkerKey;
        if (key == null) return;

        if (!session.tankObjects.TryGetValue(key, out var item) || item == null)
        {
            var anchor = Instantiate(session.tankPrefab, session.tankRoot.transform);
            item = anchor.GetComponent<TankObject>();
            session.tankObjects[key] = item;
        }
        item.GetComponent<CesiumForUnity.CesiumGlobeAnchor>().longitudeLatitudeHeight =
            new Unity.Mathematics.double3(longitude, latitude, 0);
        item.tankID = key;
        item.tankDetails = tank;
        locatedTanks.Add(key);
        item.SetLabelState(TankLabelState.Unavailable);
        item.gameObject.SetActive(false);
    }

    private void OnCompanySelected()
    {
        if (ApiConfig.P0Mode == P0DataMode.Mock)
        {
            OnMockCompanySelected();
            return;
        }
        if (!IsCurrent || !ready) return;
        StopPolling();
        Session.Instance.tankID = string.Empty;
        companies.TryGetValue(Session.Instance.companyID ?? "", out var company);
        ShowGroupMarker(company);
        EnterListState(company);
    }

    /// <summary>
    /// 列表态：显示储罐列表，收起详情。
    ///
    /// 详情和列表是互斥的两态，不并排显示——列表要和管理员信息卡片占同一片位置
    /// （本面板没有管理员卡片，但两个面板的交互口径保持一致）。
    /// </summary>
    private void EnterListState(Company company)
    {
        monitor.onBack = null;
        monitor.Hide();

        if (listPanel == null) return;
        if (company == null)
        {
            listPanel.Hide();
            return;
        }
        listPanel.Show(CompanyTitle(company), CurrentGroupTanks(company), null, PickFromList);
    }

    /// <summary>详情态：收起列表，只留详情卡片，返回键切回列表态。</summary>
    private void EnterDetailState(Tank tank)
    {
        if (listPanel != null) listPanel.Hide();
        monitor.onBack = BackToList;
        monitor.ShowLoading(tank.tankName, tank.equipCode);
    }

    /// <summary>
    /// 详情态点"返回"。先清 tankID 让轮询停下（这会触发 OnTankSelected 把卡片关掉），
    /// 再把列表重新撑起来——顺序反了会被那次 Hide 盖掉。
    /// </summary>
    private void BackToList()
    {
        if (!IsCurrent) return;
        Session.Instance.tankID = string.Empty;
        companies.TryGetValue(Session.Instance.companyID ?? "", out var company);
        ShowGroupMarker(company);
        EnterListState(company);
    }

    /// <summary>
    /// 同企业只亮一个图标。tank/list 给的是企业级坐标，一罐一图标会严丝合缝地叠在一起，
    /// 射线只命中最上面那个，其余罐永远选不到——那些罐改从卡片的列表里选。
    /// 其它罐的 TankObject 仍留在 Session.tankObjects 里（别的面板要用），只是不激活。
    /// </summary>
    private void ShowGroupMarker(Company company)
    {
        var keep = new HashSet<string>();
        if (company != null && !string.IsNullOrWhiteSpace(company.sysOrgCode)
            && groups.TryGetValue(company.sysOrgCode.Trim(), out var group))
            keep = TankGrouping.MarkerKeys(new[] { group });

        foreach (var pair in Session.Instance.tankObjects)
        {
            if (pair.Value == null) continue;
            pair.Value.gameObject.SetActive(keep.Contains(pair.Key));
            pair.Value.SetLabelState(TankLabelState.Unavailable);
        }
    }

    private List<Tank> CurrentGroupTanks(Company company)
    {
        if (company != null && !string.IsNullOrWhiteSpace(company.sysOrgCode)
            && groups.TryGetValue(company.sysOrgCode.Trim(), out var group))
            return group.tanks;
        return new List<Tank>();
    }

    private string CompanyTitle(Company company)
    {
        string name = company?.companyName ?? "监测预警";
        return catalogIncomplete ? name + "（目录分页不完整，可能有遗漏）" : name;
    }

    /// <summary>
    /// 本次 tankID 变化是不是用户从列表里点的。
    ///
    /// 沙盘图标代表的是一整组罐而不是某一个罐，所以点图标应该先看列表；
    /// 只有从列表里点到具体一行才直接进详情。setter 是同步触发事件的，
    /// 所以这个标志在赋值前后置位就够，不需要排队。
    /// </summary>
    private bool pickedFromList;

    private void PickFromList(Tank tank)
    {
        if (tank == null || !IsCurrent) return;
        pickedFromList = true;
        Session.Instance.tankID = tank.MarkerKey;
        pickedFromList = false;
    }

    private void OnTankSelected()
    {
        if (!IsCurrent || !ready) return;
        StopPolling();
        string id = Session.Instance.tankID ?? "";
        foreach (var pair in Session.Instance.tankObjects)
            if (pair.Value != null) pair.Value.SetLabelState(pair.Key == id
                ? TankLabelState.SelectedUnavailable : TankLabelState.Unavailable);
        if (!locatedTanks.Contains(id) || !tanks.TryGetValue(id, out var tank)
            || !companies.TryGetValue(Session.Instance.companyID ?? "", out var company)
            || !TankWarningData.SameCompany(company.sysOrgCode, tank.sysOrgCode))
        {
            monitor.Hide();
            return;
        }
        // 点沙盘图标进来的：图标代表一整组罐，回到列表让用户自己挑，不擅自替他选一个
        if (!pickedFromList)
        {
            EnterListState(company);
            return;
        }

        EnterDetailState(tank);
        if (string.IsNullOrWhiteSpace(tank.equipCode))
        {
            monitor.ShowError("储罐缺少监测设备编码");
            return;
        }
        polling = StartCoroutine(Poll(tank));
    }

    private IEnumerator Poll(Tank tank)
    {
        while (IsCurrent && Session.Instance.tankID == tank.MarkerKey)
        {
            readingRequest = UnityWebRequest.Get(Api.ExternalDeviceLatestData(tank.equipCode));
            readingRequest.timeout = RequestTimeoutSeconds;
            yield return readingRequest.SendWebRequest();

            string body = null;
            string networkError = null;
            if (readingRequest.result == UnityWebRequest.Result.Success)
                body = readingRequest.downloadHandler.text;
            else
                networkError = $"{readingRequest.result} {readingRequest.error}";
            readingRequest.Dispose();
            readingRequest = null;

            if (networkError != null)
            {
                Debug.LogError($"[监测预警] {tank.equipCode} 取数失败：{networkError}");
                monitor.ShowError($"监测数据不可用，{PollIntervalSeconds:0} 秒后重试");
            }
            else
            {
                try
                {
                    monitor.Show(TankWarningData.ParseLatest(body, tank));
                }
                catch (Exception ex)
                {
                    // 连同 UI 绑定缺失这类异常一起记全栈：只弹一句提示的话现场查不出原因
                    Debug.LogError($"[监测预警] {tank.equipCode} 解析或展示失败：{ex}");
                    monitor.ShowError($"监测数据不可用，{PollIntervalSeconds:0} 秒后重试");
                }
            }

            yield return new WaitForSecondsRealtime(PollIntervalSeconds);
        }
    }

    private void StopPolling()
    {
        if (polling != null) StopCoroutine(polling);
        polling = null;
        Cancel(ref readingRequest);
    }

    private static void Cancel(ref UnityWebRequest request)
    {
        if (request == null) return;
        request.Abort();
        request.Dispose();
        request = null;
    }

    private void CancelRequests()
    {
        StopPolling();
        Cancel(ref catalogRequest);
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        CancelRequests();
        ready = false;
        if (monitor != null) monitor.Hide();
        StopMockMode();
        if (IsCurrent)
        {
            Session.Instance.companyRoot.SetActive(false);
            Session.Instance.tankRoot.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        CancelRequests();
        if (Session.Instance == null) return;
        Session.Instance.OnCompanyIDChanged -= OnCompanySelected;
        Session.Instance.OnTankIDChanged -= OnTankSelected;
        Session.Instance.OnTankAreaIDChanged -= OnTankAreaSelected;
        Session.Instance.BeforeSubSessionTypeChanged -= BeforeSubSessionTypeChanged;
        Session.Instance.OnSubSessionTypeChanged -= OnSubSessionTypeChanged;
    }

    // Retain the entry points; this data source cannot substantiate an alarm yet.
    public void SetTankAlertState(string tankID, TankLabelState alertState)
    {
        Debug.LogWarning("监测预警尚缺传感器阈值契约，未应用告警状态");
    }

    public void ClearAllAlertStates()
    {
        if (!IsCurrent) return;
        foreach (var item in Session.Instance.tankObjects.Values)
            if (item != null) item.SetLabelState(TankLabelState.Unavailable);
    }
}
