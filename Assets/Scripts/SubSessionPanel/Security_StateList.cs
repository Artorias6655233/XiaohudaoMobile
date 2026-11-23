using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Security_StateList : SubSessionPanel
{
    public StateObject stateObjectPrefab;
    public Transform stateContainer; // 用于放置 StateObject 的容器
    public override SubSessionType subSessionType => SubSessionType.Security_StateList;

    private static readonly State[] NoStates = new State[0];

    private Coroutine currentCoroutine;
    private VirtualScrollList<State, StateObject> rows;
    private List<State> allStates = new List<State>(); // 保存所有开停车数据
    private bool isEventSubscribed = false;
    private Dictionary<string, string> sysOrgCodeToID => Session.Instance.sysOrgCodeToID;
    private Dictionary<string, CompanyObject> companyObjects => Session.Instance.companyObjects;
    private GameObject companyRoot => Session.Instance.companyRoot;
    private CesiumForUnity.CesiumGlobeAnchor companyPrefab => Session.Instance.companyPrefab;

    public override IEnumerator Start()
    {
        yield return base.Start();
        
        // 订阅企业选择事件
        SubscribeToCompanyIDChanged();
    }

    private void SubscribeToCompanyIDChanged()
    {
        if (isEventSubscribed)
            return;
            
        if (Session.Instance != null)
        {
            Session.Instance.OnCompanyIDChanged += OnCompanySelected;
            isEventSubscribed = true;
        }
    }

    private void OnDestroy()
    {
        if (Session.Instance != null)
        {
            Session.Instance.OnCompanyIDChanged -= OnCompanySelected;
        }
    }

    public override void BeforeSubSessionTypeChanged()
    {
        // 不调用 base，避免隐藏 companyRoot
        // base.BeforeSubSessionTypeChanged();
    }

    public override void OnSubSessionTypeChanged()
    {
        base.OnSubSessionTypeChanged();
        
        // 确保事件已订阅
        SubscribeToCompanyIDChanged();
        
        // 停止当前正在运行的 coroutine
        if (currentCoroutine != null)
        {
            StopCoroutine(currentCoroutine);
            currentCoroutine = null;
        }
        
        if (Session.Instance.subSessionType == SubSessionType.Security_StateList)
        {
            // 显示企业标记
            ShowCompanyObjects();
            
            if (allStates.Count > 0)
            {
                // 虚拟滚动下不存在「行建到一半被切 tab 打断」这回事——行数固定是一屏，
                // 换数据只是重绑。所以不用再像以前那样整份重建来自愈。
                UpdateStateDisplay();
            }
            else
            {
                currentCoroutine = StartCoroutine(GetStateListCoroutine());
            }
        }
        else
        {
            HideStateObjects();
            // 离开模式时不隐藏 companyObjects，让其他模式决定是否需要显示
            // HideCompanyObjects();
        }
    }

    private void ShowCompanyObjects()
    {
        if (companyRoot != null)
        {
            companyRoot.SetActive(true);
        }
        
        // 如果企业对象还没有创建，创建它们
        if (companyObjects.Count == 0)
        {
            StartCoroutine(GetCompanyListCoroutine());
        }
    }

    private void HideCompanyObjects()
    {
        if (companyRoot != null)
        {
            companyRoot.SetActive(false);
        }
    }

    private IEnumerator GetCompanyListCoroutine()
    {
        string url = Api.SecurityCompanyList(0, 1000);
        
        // 请求企业列表
        yield return DataProvider.Instance.Get<Company>(url);
        var result = DataProvider.Instance.GetLastResult<Company>(url);
        
        if (result != null && result.success)
        {
            ResponseList<Company> response = result.data as ResponseList<Company>;
            if (response?.result?.data != null)
            {
                Debug.Log($"获取 {response.result.total} 个企业");
                
                // 创建企业实例
                CreateCompanyInstances(response.result.data);
            }
        }
        else
        {
            Debug.LogError($"获取企业列表失败: {result?.error}");
        }
    }

    private void CreateCompanyInstances(List<Company> companies)
    {
        foreach (var company in companies)
        {
            CreateCompanyInstance(company);
        }
    }

    private void CreateCompanyInstance(Company company)
    {
        // 实例化企业 prefab
        CesiumForUnity.CesiumGlobeAnchor instance = Instantiate(
            companyPrefab,
            companyRoot.transform
        );
        
        // 设置坐标 (经度, 纬度, 高度)
        instance.longitudeLatitudeHeight = new Unity.Mathematics.double3(
            company.longitude, 
            company.latitude, 
            100
        );
        
        // 设置企业信息
        CompanyObject companyObj = instance.GetComponent<CompanyObject>();
        if (companyObj != null)
        {
            companyObj.companyID = company.id;
            companyObj.companyDetails = company;
        }
        
        // 保存到字典
        companyObjects[company.id] = companyObj;
        sysOrgCodeToID[company.sysOrgCode] = company.id;
    }

    private void OnCompanySelected()
    {
        // 只在当前是 Security_StateList 模式时处理
        if (Session.Instance.subSessionType != SubSessionType.Security_StateList)
            return;
        
        // 如果已有开停车数据，更新显示
        if (allStates.Count > 0)
        {
            UpdateStateDisplay();
        }
    }

    /// <summary>
    /// 按当前选中的企业筛出要显示的数据交给虚拟滚动列表。
    ///
    /// 过滤全在数据层做了——以前是「行全建出来，再逐行 SetActive」。没选企业时那套会
    /// 把上百行一次性 SetActive(true)，全部行的 TMP mesh 挤在同一次 CanvasUpdate 里，
    /// 这就是「不先选企业点 tab 才卡」的来源。
    /// </summary>
    private void UpdateStateDisplay()
    {
        string selectedCompanyID = Session.Instance.companyID;
        IList<State> visible;

        if (string.IsNullOrEmpty(selectedCompanyID))
        {
            visible = allStates;
        }
        else
        {
            string sysOrgCode = ResolveSysOrgCode(selectedCompanyID);
            // 查不到 sysOrgCode 就给空列表：和以前「把所有行都关掉」的表现一致，
            // 但不用再去碰 GameObject。
            visible = string.IsNullOrEmpty(sysOrgCode)
                ? NoStates
                : allStates.FindAll(x => x.sysOrgCode == sysOrgCode);
        }

        EnsureRows();
        rows.SetData(visible);
        ShowStateObjects();

        Debug.Log($"[开停车] 数据 {visible.Count} 条，行实例 {rows.RowCount} 个");
    }

    /// <summary>companyID 反查 sysOrgCode。字典是 sysOrgCode -> companyID，只能反着找。</summary>
    private string ResolveSysOrgCode(string companyID)
    {
        foreach (var kvp in sysOrgCodeToID)
        {
            if (kvp.Value == companyID)
            {
                return kvp.Key;
            }
        }
        return null;
    }

    private void EnsureRows()
    {
        if (rows != null)
        {
            return;
        }

        rows = new VirtualScrollList<State, StateObject>(
            stateObjectPrefab,
            stateContainer,
            (row, state, index) =>
            {
                // 序号按数据下标算，滚到第 100 行显示的就是 100，不是池里的位置。
                row.stateNo = index + 1;
                row.stateDetails = state;
            });
    }

    private void HideStateObjects()
    {
        // 隐藏容器或父对象
        Transform parent = stateContainer != null ? stateContainer : 
                          (stateObjectPrefab != null ? stateObjectPrefab.transform.parent : null);
        
        if (parent != null)
        {
            parent.gameObject.SetActive(false);
        }
    }

    private void ShowStateObjects()
    {
        // 显示容器或父对象
        Transform parent = stateContainer != null ? stateContainer : 
                          (stateObjectPrefab != null ? stateObjectPrefab.transform.parent : null);
        
        if (parent != null)
        {
            parent.gameObject.SetActive(true);
        }
    }

    private IEnumerator GetStateListCoroutine()
    {
        string url = Api.SecurityCompanyStateList(0, 1000);
        
        // 请求开停车列表
        yield return DataProvider.Instance.Get<State>(url);
        var result = DataProvider.Instance.GetLastResult<State>(url);
        
        if (result != null && result.success)
        {
            ResponseList<State> response = result.data as ResponseList<State>;
            if (response?.result?.data != null)
            {
                Debug.Log($"获取 {response.result.total} 个开停车记录");
                
                // 保存所有开停车数据
                allStates = response.result.data;
                
                // 根据当前选择更新显示
                UpdateStateDisplay();
            }
        }
        else
        {
            Debug.LogError($"请求失败: {result?.error}");
        }
        
        // coroutine 完成后清空引用
        currentCoroutine = null;
    }
}
