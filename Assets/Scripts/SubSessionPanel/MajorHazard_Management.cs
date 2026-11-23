using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class MajorHazard_Management : SubSessionPanel
{
    public override SubSessionType subSessionType => SubSessionType.MajorHazard_Management;

    private CesiumForUnity.CesiumGlobeAnchor tankPrefab => Session.Instance.tankPrefab;
    private GameObject tankRoot => Session.Instance.tankRoot;
    private Dictionary<string, TankObject> tankObjects => Session.Instance.tankObjects;
    
    // Prefab for displaying responsible person info
    public GameObject responsiblePersonPrefab; 
    private GameObject currentPersonDisplay;

    // 罐区监测卡片，挂在负责人卡片下方
    public GameObject tankMonitorPrefab;
    private GameObject currentMonitorDisplay;
    private TankMonitorPanel monitorPanel;

    /// <summary>储罐列表卡片。紧挨管理员信息卡片摆，和罐体详情卡片分开，见 TankListPanel。</summary>
    public GameObject tankListPrefab;
    private TankListPanel listPanel;

    /// <summary>
    /// 批注卡片。和演示页共用同一个 prefab，但数据源不同——这边跑真实罐数据，
    /// 目标键是 equipCode，见 <see cref="OpenAnnotation"/>。
    /// </summary>
    public GameObject annotationPrefab;
    private P0AnnotationPanel annotationPanel;

    private Coroutine monitorCoroutine;

    /// <summary>
    /// 当前正在请求的 deviceCode。响应回来时对不上就丢弃 ——
    /// 连点两个罐时慢的那个请求后到，不能拿它覆盖新选中的卡片。
    /// </summary>
    private string monitorRequestDeviceCode;

    private Coroutine currentCoroutine;

    /// <summary>企业组织编码 -> 该企业在沙盘上的那一个图标及其代表的全部罐，见 TankGrouping。</summary>
    private readonly Dictionary<string, TankGrouping.Group> groups =
        new Dictionary<string, TankGrouping.Group>();

    /// <summary>
    /// 本次 tankID 变化是不是用户从列表里点的。沙盘图标代表一整组罐，点它只亮列表；
    /// 只有从列表点到具体一行才去拉详情。setter 同步触发事件，赋值前后置位就够。
    /// </summary>
    private bool pickedFromList;

    /// <summary>列表态对应的那个罐（企业的代表罐）。详情态点返回时靠它切回来。</summary>
    private Tank listStateTank;

    /// <summary>
    /// 沙盘上罐图标的统一落点（福达石化那一簇的真实坐标，实测 30 个罐全在这个点）。
    ///
    /// 产品要求：不管选哪家企业，罐图标都出现在同一个位置，用户不必满沙盘找。
    /// 各家企业自己的坐标没有丢，仍在 Tank.longitude/latitude 里，
    /// 只是本页渲染时挪过来；离开本页时由 RestoreMovedMarker 还回去——
    /// TankObject 是三个面板共享的，不还的话别的页面会看到跑偏的图标。
    /// </summary>
    private const double MarkerSlotLongitude = 113.552026;
    private const double MarkerSlotLatitude = 22.835008;

    /// <summary>当前被挪到统一落点的那个罐，用于还原。没有就是 null。</summary>
    private string movedMarkerKey;
    private Unity.Mathematics.double3 movedMarkerOrigin;
    private bool isEventSubscribed = false;

    /// <summary>上一次事件时本页是不是当前页。企业图标只在「真的从本页离开」时收，见 else 分支。</summary>
    private bool wasCurrent;
    
    // Cache for responsible persons
    private List<CompanyHazardSourcesResponsiblePerson> allResponsiblePersons;
    // Cache for companies
    private List<Company> allCompanies;

    public override IEnumerator Start()
    {
        yield return base.Start();
        SubscribeToTankIDChanged();
    }

    private void SubscribeToTankIDChanged()
    {
        if (isEventSubscribed) return;
        if (Session.Instance != null)
        {
            Session.Instance.OnTankIDChanged += OnTankSelected;
            // 罐图标改成"选中企业后才渲染"，所以企业变化也要跟着重画，见 ShowCompanyMarker
            Session.Instance.OnCompanyIDChanged += OnCompanyChanged;
            isEventSubscribed = true;
        }
    }

    private void OnDestroy()
    {
        if (Session.Instance != null)
        {
            Session.Instance.OnTankIDChanged -= OnTankSelected;
            Session.Instance.OnCompanyIDChanged -= OnCompanyChanged;
        }
    }

    /// <summary>
    /// 刻意不调 base：基类会把 companyRoot 关掉，而本页要求"先点企业、再出罐图标"，
    /// 企业图标必须留着。离场时的隐藏放在 OnSubSessionTypeChanged 的 else 分支里。
    /// </summary>
    public override void BeforeSubSessionTypeChanged() { }

    public override void OnSubSessionTypeChanged()
    {
        base.OnSubSessionTypeChanged();
        SubscribeToTankIDChanged();

        if (Session.Instance.subSessionType == SubSessionType.MajorHazard_Management)
        {
            wasCurrent = true;

            if (Session.Instance.companyRoot != null)
                Session.Instance.companyRoot.SetActive(true);
            foreach (var item in Session.Instance.companyObjects.Values)
                if (item != null) item.gameObject.SetActive(true);

            ShowTankObjects();

            // 上一页可能留着别家企业的罐，不清掉的话会弹出一张和当前企业对不上的卡片
            Session.Instance.tankID = string.Empty;
            listStateTank = null;

            // 默认一个罐图标都不渲染，选中企业后才渲染那一个，见 ShowCompanyMarker。
            // MajorHazard_CompanyManagement 进来时会把不属于所选企业的 tank 直接
            // SetActive(false)，而 SetLabelState 只切换子 Image、不恢复 activeSelf，
            // 所以 activeSelf 必须在这里显式过一遍。
            ShowCompanyMarker();
            
            // Ensure tanks are loaded。用 tanksLoaded 而不是 tankObjects.Count——
            // 分帧建到一半被切走时集合非空但不完整。
            if (!Session.Instance.tanksLoaded)
            {
                 StartCoroutine(GetTankListCoroutine());
            }
            else
            {
                SetAllTanksLabelState(TankLabelState.Hidden);
            }

            // Load responsible persons data
            if (allResponsiblePersons == null)
            {
                StartCoroutine(GetResponsiblePersonsCoroutine());
            }

            // Load companies data if needed
            if (allCompanies == null)
            {
                StartCoroutine(GetCompanyListCoroutine());
            }
            
            // Handle current selection
            OnTankSelected();
        }
        else
        {
            // 企业图标只在「刚从本页离开」时收。OnSubSessionTypeChanged 是全局广播的，
            // 任何一个 tab 切换都会走到这个分支——不加 wasCurrent 判断的话，
            // 别的页面（作业票、运行效果检查等）刚点亮的企业 icon 会被这里顺手关掉。
            if (wasCurrent && Session.Instance.companyRoot != null)
                Session.Instance.companyRoot.SetActive(false);
            wasCurrent = false;

            // 罐图标被挪到过统一落点，走之前放回它自己的坐标（TankObject 是共享的）
            RestoreMovedMarker();

            // Hide person display when leaving
            if (currentPersonDisplay != null)
                currentPersonDisplay.SetActive(false);

            HideTankMonitor();
            if (listPanel != null) listPanel.Hide();
            // 批注卡片是运行时挂在本面板下的子物体，离场时不收起来会跟着别的页面一起留在视野里
            if (annotationPanel != null)
            {
                annotationPanel.onClosed = null;
                annotationPanel.CloseImmediately();
            }
        }
    }

    private void ShowTankObjects()
    {
        if (tankRoot != null) tankRoot.SetActive(true);
    }

    /// <summary>
    /// 只渲染【当前选中企业】的那一个罐图标；没选企业就一个都不渲染。
    ///
    /// 原先是 ActivateAllTanks——一次把 51 家企业的代表罐全亮出来。沙盘上罐图标密密麻麻，
    /// 用户以为点的是东曹化工，射线实际打到的是旁边福达石化的图标，表现就是
    /// "点哪个企业都出福达石化的罐体列表"（2026-09-21 真机日志：连续多次点击命中的
    /// 都是同一个 440100150001G0007）。
    ///
    /// 一企业只放一个图标的理由见 TankGrouping：tank/list 给的是企业级坐标，
    /// 同企业的罐常常共用一个点，一罐一图标会严丝合缝叠在一起。其余罐从列表卡片里选。
    ///
    /// SetLabelState / SetAllTanksLabelState 只控制子 Image，不影响 activeSelf，所以这里单独管。
    /// </summary>
    private void ShowCompanyMarker()
    {
        RebuildGroups();

        var keep = new HashSet<string>();
        string sysOrgCode = CurrentSysOrgCode();
        if (!string.IsNullOrEmpty(sysOrgCode) && groups.TryGetValue(sysOrgCode, out var group))
            keep = TankGrouping.MarkerKeys(new[] { group });

        RestoreMovedMarker();
        foreach (var pair in tankObjects)
        {
            if (pair.Value == null) continue;
            bool show = keep.Contains(pair.Key);
            pair.Value.gameObject.SetActive(show);
            if (show) MoveToMarkerSlot(pair.Key, pair.Value);
        }

        Debug.Log($"[重大风险管控] 企业 {(string.IsNullOrEmpty(sysOrgCode) ? "未选中" : sysOrgCode)}" +
                  $"：渲染罐图标 {keep.Count} 个 / 全部 {tankObjects.Count} 个罐");

        // 选了企业却一个罐都没有：提示写在【罐体列表卡片】上。
        //
        // 原先是去撑罐体详情卡（monitorPanel.ShowError），但那张卡和「监测预警」用的是
        // 同一个 prefab：用户在看罐体列表时点了别家企业，屏幕上弹出一张长得像监测预警的
        // 面板，看上去就是切错了页面。换成在原地那张列表卡上写提示，没有新面板冒出来。
        //
        // tanksLoaded 之前不提示——那时是"还没加载完"，不是"没有数据"。
        if (!string.IsNullOrEmpty(sysOrgCode) && keep.Count == 0 && Session.Instance.tanksLoaded)
        {
            HideTankMonitor();
            if (currentPersonDisplay != null) currentPersonDisplay.SetActive(false);
            listStateTank = null;
            if (EnsureListPanel()) listPanel.ShowEmpty(CurrentCompanyName(), "该企业无罐体数据");
        }
    }

    /// <summary>把这个罐图标挪到统一落点，并记下它原来的坐标以便还原。</summary>
    private void MoveToMarkerSlot(string key, TankObject tankObj)
    {
        var anchor = tankObj.GetComponent<CesiumForUnity.CesiumGlobeAnchor>();
        if (anchor == null) return;

        movedMarkerKey = key;
        movedMarkerOrigin = anchor.longitudeLatitudeHeight;
        anchor.longitudeLatitudeHeight = new Unity.Mathematics.double3(
            MarkerSlotLongitude, MarkerSlotLatitude, movedMarkerOrigin.z);
    }

    /// <summary>把上一次挪走的罐图标放回它自己的坐标。没挪过就什么都不做。</summary>
    private void RestoreMovedMarker()
    {
        if (string.IsNullOrEmpty(movedMarkerKey)) return;

        if (tankObjects.TryGetValue(movedMarkerKey, out var tankObj) && tankObj != null)
        {
            var anchor = tankObj.GetComponent<CesiumForUnity.CesiumGlobeAnchor>();
            if (anchor != null) anchor.longitudeLatitudeHeight = movedMarkerOrigin;
        }
        movedMarkerKey = null;
    }

    /// <summary>
    /// 当前选中企业的 sysOrgCode。罐按 sysOrgCode 分组，而 Session 存的是企业 id，
    /// 中间要过一道翻译：优先查沙盘上的企业图标，查不到再退回本页自己拉的企业目录。
    /// </summary>
    private string CurrentSysOrgCode()
    {
        string companyID = Session.Instance.companyID;
        if (string.IsNullOrWhiteSpace(companyID)) return null;

        if (Session.Instance.companyObjects.TryGetValue(companyID, out var item)
            && item != null && item.companyDetails != null
            && !string.IsNullOrWhiteSpace(item.companyDetails.sysOrgCode))
            return item.companyDetails.sysOrgCode.Trim();

        Company company = allCompanies == null
            ? null
            : allCompanies.FirstOrDefault(c => c.id == companyID);
        return company == null || string.IsNullOrWhiteSpace(company.sysOrgCode)
            ? null
            : company.sysOrgCode.Trim();
    }

    /// <summary>当前选中企业的名字，给列表卡片当标题用；查不到就退回通用标题。</summary>
    private string CurrentCompanyName()
    {
        string companyID = Session.Instance.companyID;
        if (string.IsNullOrWhiteSpace(companyID)) return "储罐列表";

        if (Session.Instance.companyObjects.TryGetValue(companyID, out var item)
            && item != null && item.companyDetails != null
            && !string.IsNullOrWhiteSpace(item.companyDetails.companyName))
            return item.companyDetails.companyName;

        Company company = allCompanies == null
            ? null
            : allCompanies.FirstOrDefault(c => c.id == companyID);
        return company == null || string.IsNullOrWhiteSpace(company.companyName)
            ? "储罐列表"
            : company.companyName;
    }

    /// <summary>
    /// 换企业：把上一家的卡片全收起来，再渲染新企业的罐图标。
    ///
    /// 清 tankID 会同步触发 OnTankSelected，由它关掉负责人卡片和详情卡片，
    /// 所以这里不用再手动关一遍。批注卡片要先摘掉 onClosed 再关——
    /// 那个回调会把 tankID 设回上一个罐，正好抵消这次清空。
    /// </summary>
    private void OnCompanyChanged()
    {
        if (Session.Instance.subSessionType != subSessionType) return;

        if (annotationPanel != null)
        {
            annotationPanel.onClosed = null;
            annotationPanel.CloseImmediately();
        }
        if (listPanel != null) listPanel.Hide();
        listStateTank = null;
        Session.Instance.tankID = string.Empty;
        ShowCompanyMarker();
    }

    private void RebuildGroups()
    {
        var details = new List<Tank>();
        foreach (var tankObj in tankObjects.Values)
        {
            if (tankObj != null && tankObj.tankDetails != null) details.Add(tankObj.tankDetails);
        }

        groups.Clear();
        foreach (var group in TankGrouping.Build(details)) groups[group.sysOrgCode] = group;
    }

    private TankGrouping.Group GroupOf(Tank tank)
    {
        if (tank == null || string.IsNullOrWhiteSpace(tank.sysOrgCode)) return null;
        groups.TryGetValue(tank.sysOrgCode.Trim(), out var group);
        return group;
    }

    private string CompanyNameOf(Tank tank)
    {
        if (tank != null && allCompanies != null)
        {
            var company = allCompanies.FirstOrDefault(c => c.sysOrgCode == tank.sysOrgCode);
            if (company != null && !string.IsNullOrWhiteSpace(company.companyName)) return company.companyName;
        }
        return "储罐列表";
    }

    private void PickFromList(Tank tank)
    {
        if (tank == null) return;
        pickedFromList = true;
        Session.Instance.tankID = tank.MarkerKey;
        pickedFromList = false;
    }

    /// <summary>
    /// 列表里点「批注」：批注卡片顶掉 [列表 + 管理员信息] 两张，关掉再放回来。
    ///
    /// 【为什么必须互斥】宿主根上是 HorizontalLayoutGroup(spacing=570)，卡片按各自
    /// rect.width 从左往右排。再激活一张 1880 宽的卡，它会落到已有卡片右边两米开外，
    /// 戴着眼镜正视列表时根本在视野外——表现就是"点了批注没反应"。
    ///
    /// 批注的目标键用 <see cref="Tank.MarkerKey"/>（equipCode，实测 264/264 唯一），
    /// 不是演示数据那套 hazardId。
    /// </summary>
    private void OpenAnnotation(Tank tank)
    {
        if (tank == null) return;
        string target = tank.MarkerKey;
        if (string.IsNullOrEmpty(target))
        {
            Debug.LogWarning("[批注] 该储罐没有 equipCode，没法作为批注目标");
            return;
        }
        if (!EnsureAnnotationPanel()) return;

        if (listPanel != null) listPanel.Hide();
        if (currentPersonDisplay != null) currentPersonDisplay.SetActive(false);
        annotationPanel.onClosed = BackFromAnnotation;
        annotationPanel.Open(target);
    }

    /// <summary>关掉批注：回到 [列表 + 管理员信息] 两张并排的那一态。</summary>
    private void BackFromAnnotation()
    {
        if (listStateTank == null) return;
        pickedFromList = false;
        Session.Instance.tankID = listStateTank.MarkerKey;
    }

    private bool EnsureAnnotationPanel()
    {
        if (annotationPanel != null) return true;
        if (annotationPrefab == null)
        {
            Debug.LogWarning("[批注] 场景上没接 annotationPrefab，批注开不出来");
            return false;
        }
        annotationPanel = Instantiate(annotationPrefab, transform).GetComponent<P0AnnotationPanel>();
        if (annotationPanel == null) return false;

        // 罐体批注跑在真实接口数据上，P0DataSourceFactory 对 Network 模式直接 throw，
        // 所以这里不走演示数据源，直连本地存储
        annotationPanel.Configure(new LocalAnnotationSource());
        annotationPanel.CloseImmediately();
        return true;
    }

    /// <summary>
    /// 懒实例化列表卡片，并把它排到管理员信息卡片右边。
    ///
    /// 宿主根上是 HorizontalLayoutGroup(ChildControlWidth=0)，子物体按兄弟顺序从左往右排，
    /// 所以位置靠 SetSiblingIndex 定，不用手算坐标。与管理员卡片之间的间隔做在列表卡片
    /// 自己的 RectTransform 右侧留白里（见 TankListPanelBuilder.RightGap），没有去动布局组的
    /// m_Spacing —— 那个值是所有卡片共用的，调它会连主面板一起撑开。
    /// </summary>
    private bool EnsureListPanel()
    {
        if (listPanel == null)
        {
            if (tankListPrefab == null) return false;
            listPanel = Instantiate(tankListPrefab, transform).GetComponent<TankListPanel>();
            if (listPanel == null) return false;
        }

        // 列表在左、管理员信息在右：插到管理员卡片当前的位置上，把它顶到右边
        if (currentPersonDisplay != null)
        {
            int slot = currentPersonDisplay.transform.GetSiblingIndex();
            if (listPanel.transform.GetSiblingIndex() > slot)
                listPanel.transform.SetSiblingIndex(slot);
        }
        return true;
    }
    
    private void SetAllTanksLabelState(TankLabelState state)
    {
        foreach (var tankObj in tankObjects.Values)
        {
            if (tankObj != null) tankObj.SetLabelState(state);
        }
    }

    private IEnumerator GetTankListCoroutine()
    {
        string url = Api.MajorHazardTankList(1, 1000);
        yield return DataProvider.Instance.Get<Tank>(url);
        var result = DataProvider.Instance.GetLastResult<Tank>(url);
        
        if (result != null && result.success)
        {
             ResponseList<Tank> response = result.data as ResponseList<Tank>;
             if (response?.result?.data != null)
             {
                 yield return CreateTankInstances(response.result.data);

                 // 只有真正走完才置标志，半途被打断时保持 false 以便下次续建
                 Session.Instance.tanksLoaded = true;
             }
        }
    }

    private IEnumerator CreateTankInstances(List<Tank> tanks)
    {
        // 264 个 CesiumGlobeAnchor 分帧建，见 FrameBudget
        yield return FrameBudget.Spread(tanks, CreateTankInstance);

        // 【必须在这里再渲染一次】入口处那次 ShowCompanyMarker 是在本协程启动【之前】
        // 跑的，那时 tankObjects 还是空的，等于没跑。从「园区基础信息」直接切过来
        // （tanksLoaded=false）就是这条路径：少了这一次，刚建出来的罐图标一个都不会亮
        // （CreateTankInstance 建出来默认是关的）。
        //
        // 建的过程中用户可能已经切走了，这时不要抢别的面板的图标显隐。
        if (Session.Instance.subSessionType == SubSessionType.MajorHazard_Management)
        {
            ShowCompanyMarker();
        }

        // 下面两个分支必须在分帧建完之后跑，否则只会作用到已建出来的那部分
        // If responsible persons are already loaded, update states immediately
        if (allResponsiblePersons != null)
        {
            UpdateTankDisplayStates();
        }
        else
        {
            SetAllTanksLabelState(TankLabelState.Hidden);
        }
    }

    private void CreateTankInstance(Tank tank)
    {
        // 已建过就跳过：分帧后续建路径会重跑整份列表，没有这道判断会重复
        // Instantiate，旧对象被字典覆盖后变成孤儿。
        string key = tank.MarkerKey;
        if (key == null || tankObjects.ContainsKey(key))
        {
            return;
        }

        if (!double.TryParse(tank.longitude, out double longitude) ||
            !double.TryParse(tank.latitude, out double latitude))
        {
            return;
        }

        CesiumForUnity.CesiumGlobeAnchor instance = Instantiate(
            tankPrefab,
            tankRoot.transform
        );
        
        instance.longitudeLatitudeHeight = new Unity.Mathematics.double3(
            longitude, 
            latitude, 0
        );
        
        TankObject tankObj = instance.GetComponent<TankObject>();
        if (tankObj != null)
        {
            tankObj.tankID = key;
            tankObj.tankDetails = tank;
            // 默认不激活，由 ActivateAllTanks 决定哪些亮（和
            // MajorHazard_CompanyManagement.CreateTankInstance 的口径一致）。
            // 不加这句的话，分帧建的十几帧里 264 个图标会先糊在沙盘上闪一下。
            tankObj.gameObject.SetActive(false);
        }
        
        tankObjects[key] = tankObj;
    }

    private IEnumerator GetResponsiblePersonsCoroutine()
    {
        string url = Api.CompanyHazardSourcesResponsiblePersonList(1, 1000);
        yield return DataProvider.Instance.Get<CompanyHazardSourcesResponsiblePerson>(url);
        var result = DataProvider.Instance.GetLastResult<CompanyHazardSourcesResponsiblePerson>(url);
        
        if (result != null && result.success)
        {
            ResponseList<CompanyHazardSourcesResponsiblePerson> response = result.data as ResponseList<CompanyHazardSourcesResponsiblePerson>;
            if (response?.result != null)
            {
                allResponsiblePersons = response.result.records ?? response.result.data;
                
                // Update tank display states based on responsible persons
                UpdateTankDisplayStates();
                
                // Refresh display if a tank is already selected
                OnTankSelected();
            }
        }
    }

    private void UpdateTankDisplayStates()
    {
        if (allResponsiblePersons == null || tankObjects.Count == 0) return;

        // Get all unique scopeCoverages from responsible persons
        HashSet<string> coveredScopes = new HashSet<string>();
        foreach (var person in allResponsiblePersons)
        {
            if (!string.IsNullOrEmpty(person.scopeCoverage))
            {
                coveredScopes.Add(person.scopeCoverage.Trim());
            }
        }

        foreach (var kvp in tankObjects)
        {
            TankObject tankObj = kvp.Value;
            if (tankObj != null && tankObj.tankDetails != null)
            {
                string tankScope = tankObj.tankDetails.dangesrcCode?.Trim();
                if (!string.IsNullOrEmpty(tankScope) && coveredScopes.Contains(tankScope))
                {
                    tankObj.SetLabelState(TankLabelState.Label);
                }
                else
                {
                    tankObj.SetLabelState(TankLabelState.Hidden);
                }
            }
        }
    }

    private IEnumerator GetCompanyListCoroutine()
    {
        string url = Api.SecurityCompanyList(0, 1000);
        yield return DataProvider.Instance.Get<Company>(url);
        var result = DataProvider.Instance.GetLastResult<Company>(url);
        
        if (result != null && result.success)
        {
            ResponseList<Company> response = result.data as ResponseList<Company>;
            if (response?.result?.data != null)
            {
                allCompanies = response.result.data;

                // 本页要先点企业，所以企业图标必须在。companyObjects 是「园区基础信息」
                // 和「监测预警」共建的共享集合，直接从这一页进来时它可能还是空的。
                foreach (Company company in allCompanies) CreateCompanyInstance(company);
                if (Session.Instance.subSessionType == subSessionType) ShowCompanyMarker();

                // Refresh display if a tank is already selected (to update company name)
                OnTankSelected();
            }
        }
    }

    /// <summary>
    /// 建（或复用）一个企业图标。和 MajorHazard_TankList.CreateCompany 同一套口径：
    /// 坐标不合法的跳过，已存在的只更新数据不重复 Instantiate。
    /// </summary>
    private void CreateCompanyInstance(Company company)
    {
        var session = Session.Instance;
        if (company == null || string.IsNullOrEmpty(company.id)) return;
        if (float.IsNaN(company.longitude) || float.IsNaN(company.latitude)
            || Mathf.Abs(company.longitude) > 180 || Mathf.Abs(company.latitude) > 90
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
        item.gameObject.SetActive(Session.Instance.subSessionType == subSessionType);
        if (!string.IsNullOrWhiteSpace(company.sysOrgCode))
            session.sysOrgCodeToID[company.sysOrgCode] = company.id;
    }

    private void OnTankSelected()
    {
        if (Session.Instance.subSessionType != SubSessionType.MajorHazard_Management) return;

        string selectedTankID = Session.Instance.tankID;
        
        if (string.IsNullOrEmpty(selectedTankID))
        {
            if (currentPersonDisplay != null) currentPersonDisplay.SetActive(false);
            HideTankMonitor();
            return;
        }

        if (tankObjects.TryGetValue(selectedTankID, out TankObject tankObj))
        {
            ShowResponsiblePerson(tankObj.tankDetails);

            // 与负责人卡片并列调用，不能嵌进 ShowResponsiblePerson ——
            // 后者在负责人数据还没加载好时会提前 return，监测卡片不该跟着一起消失。
            ShowTankMonitor(tankObj.tankDetails);
        }
    }

    /// <summary>
    /// 拉并展示所选储罐的罐区监测数据。
    ///
    /// deviceCode 取 equipCode：tank/list 里的 provinceDeviceCode 恒为 null，用不了。
    /// </summary>
    private void ShowTankMonitor(Tank tank)
    {
        if (tank == null) return;

        if (!EnsureMonitorPanel()) return;

        string deviceCode = tank.equipCode?.Trim();
        if (string.IsNullOrEmpty(deviceCode))
        {
            monitorRequestDeviceCode = null;
            monitorPanel.ShowError("该储罐未关联监测设备");
            return;
        }

        if (monitorCoroutine != null)
        {
            StopCoroutine(monitorCoroutine);
            monitorCoroutine = null;
        }

        // 【点沙盘图标一律进列表，绝不落到详情】
        // 原先这里写成 if (group != null && EnsureListPanel()) { ... }，条件不成立就穿透到
        // 下面的 ShowLoading——表现就是"点了 icon 直接弹罐体详情"。图标代表一整组罐，
        // 替用户挑一个去拉数据本身就是错的，所以列表拿不出来时宁可什么都不显示。
        if (!pickedFromList)
        {
            TankGrouping.Group iconGroup = GroupOf(tank);
            if (iconGroup == null || !EnsureListPanel())
            {
                Debug.LogWarning($"[重大风险管控] 罐 {tank.MarkerKey} 取不到分组或列表卡片" +
                                 $"（group={(iconGroup == null ? "null" : iconGroup.sysOrgCode)}），" +
                                 "不弹详情，请检查 tankListPrefab 接线");
                monitorRequestDeviceCode = null;
                monitorPanel.Hide();
                return;
            }

            // 列表态：[储罐列表][管理员信息] 并排，详情收起
            monitorRequestDeviceCode = null;
            monitorPanel.onBack = null;
            monitorPanel.Hide();
            if (annotationPanel != null) annotationPanel.CloseImmediately();
            listStateTank = tank;
            if (currentPersonDisplay != null) currentPersonDisplay.SetActive(true);
            listPanel.Show(CompanyNameOf(tank), iconGroup.tanks, null, PickFromList, OpenAnnotation);
            return;
        }

        // 详情态：列表和管理员卡片一起收起，只留详情，返回键切回列表态
        if (listPanel != null) listPanel.Hide();
        if (currentPersonDisplay != null) currentPersonDisplay.SetActive(false);
        monitorPanel.onBack = BackToListState;

        monitorRequestDeviceCode = deviceCode;
        monitorPanel.ShowLoading(tank.tankName, deviceCode);
        monitorCoroutine = StartCoroutine(LoadTankMonitor(deviceCode));
    }

    private IEnumerator LoadTankMonitor(string deviceCode)
    {
        var outcome = new TankMonitorService.Result();
        yield return TankMonitorService.Load(deviceCode, outcome);

        monitorCoroutine = null;

        // 期间用户已经切到别的罐（或取消选中），这份结果作废
        if (monitorRequestDeviceCode != deviceCode) yield break;

        if (outcome.snapshot != null)
        {
            monitorPanel.Show(outcome.snapshot);
        }
        else
        {
            monitorPanel.ShowError(outcome.error ?? "监测数据取数失败");
        }
    }

    /// <summary>懒实例化监测卡片，之后只 SetActive 复用（与负责人卡片同一套写法）。</summary>
    private bool EnsureMonitorPanel()
    {
        if (monitorPanel != null) return true;

        if (currentMonitorDisplay == null)
        {
            if (tankMonitorPrefab == null)
            {
                Debug.LogWarning("[重大风险管控] 未接 tankMonitorPrefab，罐区监测卡片不会显示");
                return false;
            }

            currentMonitorDisplay = Instantiate(tankMonitorPrefab, transform);
        }

        monitorPanel = currentMonitorDisplay.GetComponent<TankMonitorPanel>();
        if (monitorPanel == null)
        {
            Debug.LogError("[重大风险管控] tankMonitorPrefab 上没有 TankMonitorPanel 组件");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 详情态点"返回"：把 tankID 切回该企业的代表罐。
    ///
    /// 走的是和点沙盘图标完全相同的入口（pickedFromList 为假），所以列表卡片、
    /// 管理员卡片、详情卡片的显隐全部由 ShowTankMonitor 那一处决定，不在这里各写一遍。
    /// </summary>
    private void BackToListState()
    {
        if (listStateTank == null) return;
        pickedFromList = false;
        Session.Instance.tankID = listStateTank.MarkerKey;
    }

    private void HideTankMonitor()
    {
        if (monitorCoroutine != null)
        {
            StopCoroutine(monitorCoroutine);
            monitorCoroutine = null;
        }

        monitorRequestDeviceCode = null;

        if (monitorPanel != null)
        {
            monitorPanel.Hide();
        }
        else if (currentMonitorDisplay != null)
        {
            currentMonitorDisplay.SetActive(false);
        }
    }

    private void ShowResponsiblePerson(Tank tank)
    {
        if (tank == null) return;
        
        if (allResponsiblePersons == null)
        {
            Debug.LogWarning("Responsible persons data not loaded yet.");
            return;
        }

        var persons = allResponsiblePersons.Where(p => p.scopeCoverage == tank.dangesrcCode).ToList();
        Debug.Log($"Found {persons.Count} responsible persons for tank {tank.tankName} (code: {tank.dangesrcCode})");
        
        if (persons.Count == 0)
        {
            // Try to find by trimming whitespace if any
            string trimmedCode = tank.dangesrcCode?.Trim();
            if (!string.IsNullOrEmpty(trimmedCode) && trimmedCode != tank.dangesrcCode)
            {
                persons = allResponsiblePersons.Where(p => p.scopeCoverage?.Trim() == trimmedCode).ToList();
                Debug.Log($"Retry with trimmed code '{trimmedCode}': Found {persons.Count}");
            }
        }
        
        // 【企业级兜底】责任人表的 scopeCoverage 只覆盖 45/264 个罐（它只有 12 个值，
        // 而罐的 dangesrcCode 有 39 个），剩下 219 个罐精确匹配必然落空，卡片上就只有
        // "暂无登记 / — / — / —"。这些企业其实登记了责任人，只是没挂到具体危险源编码上，
        // 所以退一步按企业（sysOrgCode）匹配，覆盖率 17% -> 93%。
        //
        // 代价是显示的人从"这个罐的责任人"变成"这家企业的责任人"，粒度变粗了。
        // 必须让用户看出来，否则出事时会照着卡片找错人——所以传 companyLevel=true，
        // 卡片会在姓名后标"（企业级）"。
        bool companyLevel = false;
        if (persons.Count == 0)
        {
            string orgCode = tank.sysOrgCode?.Trim();
            if (!string.IsNullOrEmpty(orgCode))
            {
                persons = allResponsiblePersons
                    .Where(p => p.sysOrgCode?.Trim() == orgCode)
                    .ToList();
                companyLevel = persons.Count > 0;
                Debug.Log($"企业级兜底 sysOrgCode '{orgCode}'：Found {persons.Count}");
            }
        }

        // 【匹配不到负责人也要把卡片撑起来】
        // 原先 persons.Count == 0 就直接把整张卡片藏了，表现就是"打开罐体列表后
        // 管理员信息面板不见了"。摆占位文案，让用户看得出是"这个罐没登记负责人"，
        // 而不是面板坏了。
        if (currentPersonDisplay == null && responsiblePersonPrefab != null)
        {
            currentPersonDisplay = Instantiate(responsiblePersonPrefab, transform);
        }
        if (currentPersonDisplay == null) return;

        currentPersonDisplay.SetActive(true);
        var panel = currentPersonDisplay.GetComponent<ResponsiblePersonPanel>();
        if (panel == null)
        {
            Debug.LogError("ResponsiblePersonPanel component not found on prefab instance.");
            return;
        }

        if (persons.Count == 0)
        {
            panel.ShowEmpty(CompanyNameOf(tank), tank.tankName);
            return;
        }

        var person = persons[0];
        string companyName = "未知企业";
        if (allCompanies != null)
        {
            var company = allCompanies.FirstOrDefault(
                c => c.sysOrgCode == person.sysOrgCode || c.deptOrgCode == person.deptOrgCode);
            if (company != null) companyName = company.companyName;
        }
        else if (Session.Instance.companyObjects.Count > 0)
        {
            // allCompanies 还没到手时退回沙盘上已有的企业对象
            foreach (var co in Session.Instance.companyObjects.Values)
            {
                if (co.companyDetails != null
                    && (co.companyDetails.sysOrgCode == person.sysOrgCode
                        || co.companyDetails.deptOrgCode == person.deptOrgCode))
                {
                    companyName = co.companyDetails.companyName;
                    break;
                }
            }
        }

        panel.SetData(person, companyName, tank.tankName, companyLevel);
    }
}
