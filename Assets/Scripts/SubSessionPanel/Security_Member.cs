using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Security_Member : SubSessionPanel
{
    public override SubSessionType subSessionType => SubSessionType.Security_Member;
    public TMPro.TextMeshProUGUI totalCount;
    
    private CesiumForUnity.CesiumGlobeAnchor companyPrefab => Session.Instance.companyPrefab;
    private GameObject companyRoot => Session.Instance.companyRoot;
    private Dictionary<string, CompanyObject> companyObjects => Session.Instance.companyObjects;
    private Dictionary<string, string> sysOrgCodeToID => Session.Instance.sysOrgCodeToID;
    
    private Coroutine currentCoroutine;
    private bool isEventSubscribed = false;

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
        
        if (Session.Instance.subSessionType == SubSessionType.Security_Member)
        {
            // 隐藏 tankRoot，只显示 companyRoot
            if (Session.Instance.tankRoot != null)
            {
                Session.Instance.tankRoot.SetActive(false);
            }
            
            // 显示企业标记
            ShowCompanyObjects();
            
            // 如果已有数据，直接显示；否则获取数据
            if (companyObjects.Count > 0)
            {
                ShowCompanyObjects();
            }
            else
            {
                currentCoroutine = StartCoroutine(GetCompanyListCoroutine());
            }
            
            // 初始时如果没有选中企业，隐藏所有子对象
            if (string.IsNullOrEmpty(Session.Instance.companyID))
            {
                HideAllChildren();
            }
        }
        else
        {
            // 离开模式时不隐藏 companyObjects，让其他模式决定是否需要显示
            // HideCompanyObjects();
        }
    }

    private void HideCompanyObjects()
    {
        if (companyRoot != null)
        {
            companyRoot.SetActive(false);
        }
    }

    private void ShowCompanyObjects()
    {
        if (companyRoot != null)
        {
            companyRoot.SetActive(true);
        }
    }

    private IEnumerator GetCompanyListCoroutine()
    {
        string url = Api.SecurityCompanyList(0, 1000);

        // 「点园区基础信息渲染很慢」目前定不了责：日志里只有大类切换（PanelController），
        // 子页切换没有埋点，所以"进面板到企业出现"那段时间里混着用户在菜单上的操作。
        // 这里把取数和建图标分别计时，下次真机跑一遍就能直接读出瓶颈在哪一段。
        //
        // 企业目录是全局共享的（Session.companyObjects），谁先进谁付这笔钱，
        // 所以这条日志只在本面板是第一个被打开时才会出现。
        var watch = System.Diagnostics.Stopwatch.StartNew();

        // 请求企业列表
        yield return DataProvider.Instance.Get<Company>(url);
        var result = DataProvider.Instance.GetLastResult<Company>(url);
        long fetchMs = watch.ElapsedMilliseconds;

        if (result != null && result.success)
        {
            ResponseList<Company> response = result.data as ResponseList<Company>;
            if (response?.result?.data != null)
            {
                // 创建企业实例
                watch.Restart();
                CreateCompanyInstances(response.result.data);

                Debug.Log($"[园区基础信息] 企业 {response.result.data.Count}/{response.result.total} 家：" +
                          $"取数+解析 {fetchMs} ms，建图标 {watch.ElapsedMilliseconds} ms");
            }
        }
        else
        {
            Debug.LogError($"请求失败: {result?.error}");
        }
        
        // coroutine 完成后清空引用
        currentCoroutine = null;
    }

    private void CreateCompanyInstances(System.Collections.Generic.List<Company> companies)
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
        // 只在当前是 Security_Member 模式时处理
        if (Session.Instance.subSessionType != SubSessionType.Security_Member)
            return;
            
        string selectedCompanyID = Session.Instance.companyID;
        
        // 如果没有选中企业，隐藏所有子对象
        if (string.IsNullOrEmpty(selectedCompanyID))
        {
            HideAllChildren();
            return;
        }
        
        // 选中了企业，显示所有子对象并更新人员数量
        ShowAllChildren();
        
        if (companyObjects.TryGetValue(selectedCompanyID, out CompanyObject selectedCompany))
        {
            // 直接从 Company 对象获取员工数量
            if (totalCount != null && selectedCompany.companyDetails != null)
            {
                totalCount.text = selectedCompany.companyDetails.employeeCount.ToString();
            }
        }
    }

    private void HideAllChildren()
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            transform.GetChild(i).gameObject.SetActive(false);
        }
    }

    private void ShowAllChildren()
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            transform.GetChild(i).gameObject.SetActive(true);
        }
    }
}
