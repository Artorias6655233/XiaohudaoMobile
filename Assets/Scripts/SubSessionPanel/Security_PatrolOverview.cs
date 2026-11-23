
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Security_PatrolOverview : SubSessionPanel
{
    private bool dataFetched = false;
    private bool isEventSubscribed = false;
    private Coroutine fetchCoroutine;
    private Coroutine companyCoroutine;
    
    public override SubSessionType subSessionType => SubSessionType.Security_PatrolOverview;
    public UnityEngine.UI.Image percentage;
    public TMPro.TextMeshProUGUI totalCountText;
    public TMPro.TextMeshProUGUI completeCountText;
    public TMPro.TextMeshProUGUI incompleteCountText;
    
    private CesiumForUnity.CesiumGlobeAnchor companyPrefab => Session.Instance.companyPrefab;
    private GameObject companyRoot => Session.Instance.companyRoot;
    private Dictionary<string, CompanyObject> companyObjects => Session.Instance.companyObjects;
    private Dictionary<string, string> sysOrgCodeToID => Session.Instance.sysOrgCodeToID;
    
    private List<Patrol> allPatrols = new List<Patrol>();

    public override IEnumerator Start()
    {
        yield return base.Start();
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
        HideOverviewDisplay();
    }

    public override void OnSubSessionTypeChanged()
    {
        base.OnSubSessionTypeChanged();
        
        // 确保事件已订阅
        SubscribeToCompanyIDChanged();
        
        // 停止当前正在运行的 coroutines
        if (fetchCoroutine != null)
        {
            StopCoroutine(fetchCoroutine);
            fetchCoroutine = null;
        }
        
        if (companyCoroutine != null)
        {
            StopCoroutine(companyCoroutine);
            companyCoroutine = null;
        }
        
        if (Session.Instance.subSessionType == SubSessionType.Security_PatrolOverview)
        {
            ShowCompanyObjects();
            
            if (!dataFetched)
            {
                fetchCoroutine = StartCoroutine(GetPatrolListCoroutine());
            }
            else
            {
                UpdateDisplay();
            }
        }
        else
        {
            HideOverviewDisplay();
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
            companyCoroutine = StartCoroutine(GetCompanyListCoroutine());
        }
    }

    private void HideOverviewDisplay()
    {
        if (transform.childCount > 0)
        {
            transform.GetChild(0).gameObject.SetActive(false);
        }
    }

    private void ShowOverviewDisplay()
    {
        if (transform.childCount > 0)
        {
            transform.GetChild(0).gameObject.SetActive(true);
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
                CreateCompanyInstances(response.result.data);
            }
        }
        
        companyCoroutine = null;
    }

    private void CreateCompanyInstances(List<Company> companies)
    {
        foreach (var company in companies)
        {
            if (!companyObjects.ContainsKey(company.id))
            {
                CreateCompanyInstance(company);
            }
        }
    }

    private void CreateCompanyInstance(Company company)
    {
        CesiumForUnity.CesiumGlobeAnchor instance = Instantiate(
            companyPrefab,
            companyRoot.transform
        );
        
        instance.longitudeLatitudeHeight = new Unity.Mathematics.double3(
            company.longitude, 
            company.latitude, 
            100
        );
        
        CompanyObject companyObj = instance.GetComponent<CompanyObject>();
        if (companyObj != null)
        {
            companyObj.companyID = company.id;
            companyObj.companyDetails = company;
        }
        
        companyObjects[company.id] = companyObj;
        sysOrgCodeToID[company.sysOrgCode] = company.id;
    }

    private void OnCompanySelected()
    {
        if (Session.Instance.subSessionType != SubSessionType.Security_PatrolOverview)
            return;

        if (dataFetched)
        {
            UpdateDisplay();
        }
    }

    private void UpdateDisplay()
    {
        string selectedCompanyID = Session.Instance.companyID;
        
        if (string.IsNullOrEmpty(selectedCompanyID))
        {
            CalculateStatistics(allPatrols);
        }
        else
        {
            // 获取企业的 companyName
            string companyName = GetCompanyName(selectedCompanyID);
            if (!string.IsNullOrEmpty(companyName))
            {
                var filteredPatrols = allPatrols.FindAll(p => p.corpName == companyName);
                CalculateStatistics(filteredPatrols);
            }
            else
            {
                CalculateStatistics(allPatrols);
            }
        }
    }

    private string GetCompanyName(string companyID)
    {
        if (companyObjects.TryGetValue(companyID, out CompanyObject companyObj))
        {
            return companyObj.companyDetails?.companyName;
        }
        return null;
    }

    private void CalculateStatistics(List<Patrol> patrols)
    {
        int total = patrols.Count;
        int completeCount = 0;
        
        foreach (var patrol in patrols)
        {
            if (patrol.patrolStatus == 3)
                completeCount++;
        }

        percentage.fillAmount = total > 0 ? (float)completeCount / total : 0f;
        totalCountText.text = total.ToString();
        completeCountText.text = completeCount.ToString();
        incompleteCountText.text = (total - completeCount).ToString();

        ShowOverviewDisplay();
    }

    private IEnumerator GetPatrolListCoroutine()
    {
        dataFetched = true;
        allPatrols.Clear();
        var pageNum = 1;
        var pageSize = 1000;
        
        while (true)
        {
            string url = Api.SecurityPatrolList(pageNum, pageSize);
            yield return DataProvider.Instance.Get<Patrol>(url);
            
            var result = DataProvider.Instance.GetLastResult<Patrol>(url);
            
            if (result == null || !result.success)
            {
                Debug.LogError("Error fetching patrol list");
                yield break;
            }
            
            ResponseList<Patrol> patrolList = result.data as ResponseList<Patrol>;
            if (patrolList == null || patrolList.result == null)
            {
                Debug.LogError("Failed to parse patrol list");
                yield break;
            }
            
            allPatrols.AddRange(patrolList.result.data);
            
            if (patrolList.result.data.Count < pageSize) 
                break;
                
            pageNum++;
        }

        UpdateDisplay();
        
        fetchCoroutine = null;
        yield return null;
    }
}
