using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class DoubleDanger_Overview : SubSessionPanel
{
    private bool dataFetched = false;
    private bool isEventSubscribed = false;
    private Coroutine fetchCoroutine;
    private Coroutine companyCoroutine;
    
    public override SubSessionType subSessionType => SubSessionType.Double_DangerList;
    public UnityEngine.UI.Image percentage;
    public TMPro.TextMeshProUGUI totalCountText;
    public TMPro.TextMeshProUGUI percentageText;
    public bool showCompanyObjects = true;
    
    private CesiumForUnity.CesiumGlobeAnchor companyPrefab => Session.Instance.companyPrefab;
    private GameObject companyRoot => Session.Instance.companyRoot;
    private Dictionary<string, CompanyObject> companyObjects => Session.Instance.companyObjects;
    private Dictionary<string, string> sysOrgCodeToID => Session.Instance.sysOrgCodeToID;
    
    private List<DoubleDanger> allDangers = new List<DoubleDanger>();
    private List<DoubleDangerChange> allChanges = new List<DoubleDangerChange>();

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
        HideOverviewDisplay();
    }

    public override void OnSubSessionTypeChanged()
    {
        base.OnSubSessionTypeChanged();
        
        SubscribeToCompanyIDChanged();
        
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
        
        if (Session.Instance.subSessionType == SubSessionType.Double_DangerList)
        {
            if (!dataFetched)
            {
                fetchCoroutine = StartCoroutine(GetAllDataCoroutine());
            }
            else
            {
                UpdateDisplay();
                ShowCompanyObjects();
            }
        }
        else
        {
            HideOverviewDisplay();
        }
    }

    private void ShowCompanyObjects()
    {
        if (!showCompanyObjects) return;

        if (companyRoot != null)
        {
            companyRoot.SetActive(true);
        }
        
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
        if (Session.Instance.subSessionType != SubSessionType.Double_DangerList)
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
            CalculateStatistics(allDangers, allChanges);
        }
        else
        {
            string sysOrgCode = GetSysOrgCode(selectedCompanyID);
            if (!string.IsNullOrEmpty(sysOrgCode))
            {
                var filteredDangers = allDangers.FindAll(d => d.sysOrgCode == sysOrgCode || d.deptOrgCode == sysOrgCode);
                var filteredChanges = allChanges.FindAll(c => c.sysOrgCode == sysOrgCode);
                CalculateStatistics(filteredDangers, filteredChanges);
            }
            else
            {
                // Fallback or clear? Let's show 0 if company not found in mapping
                CalculateStatistics(new List<DoubleDanger>(), new List<DoubleDangerChange>());
            }
        }
    }

    private string GetSysOrgCode(string companyID)
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

    private void CalculateStatistics(List<DoubleDanger> dangers, List<DoubleDangerChange> changes)
    {
        int total = dangers.Count;
        int completeCount = changes.Count;
        
        float ratio = total > 0 ? (float)completeCount / total : 0f;
        if (ratio > 1f) ratio = 1f; // Cap at 100% just in case

        if (percentage != null) percentage.fillAmount = ratio;
        if (totalCountText != null) totalCountText.text = total.ToString();
        if (percentageText != null) percentageText.text = $"{(ratio * 100):F1}%";

        // 强制刷新布局
        LayoutRebuilder.ForceRebuildLayoutImmediate(totalCountText.transform.parent.GetComponent<RectTransform>());
        LayoutRebuilder.ForceRebuildLayoutImmediate(percentageText.transform.parent.GetComponent<RectTransform>());

        ShowOverviewDisplay();
    }

    private IEnumerator GetAllDataCoroutine()
    {
        // Reset display before fetching
        CalculateStatistics(new List<DoubleDanger>(), new List<DoubleDangerChange>());

        allDangers.Clear();
        allChanges.Clear();

        // Fetch Dangers (External API with string total)
        yield return FetchDoubleDangers();
        
        // Fetch Changes (Internal API)
        yield return FetchList<DoubleDangerChange>(Api.DoubleDangerChangeList, allChanges);

        dataFetched = true;
        UpdateDisplay();
        ShowCompanyObjects();
        
        fetchCoroutine = null;
    }

    private IEnumerator FetchDoubleDangers()
    {
        string url = Api.DoubleDangerList(1, 1000);
        yield return DataProvider.Instance.GetExternal<DoubleDanger>(url);
        
        var result = DataProvider.Instance.GetLastResult<DoubleDanger>(url);
        if (result != null && result.success)
        {
            var response = result.data as ExternalResponseList<DoubleDanger>;
            if (response?.body?.data != null)
            {
                allDangers.AddRange(response.body.data);
            }
        }
    }

    private IEnumerator FetchList<T>(System.Func<int, int, string> apiMethod, List<T> targetList)
    {
        var pageNum = 1;
        var pageSize = 1000;
        
        while (true)
        {
            string url = apiMethod(pageNum, pageSize);
            yield return DataProvider.Instance.Get<T>(url);
            
            var result = DataProvider.Instance.GetLastResult<T>(url);
            
            if (result == null || !result.success)
            {
                Debug.LogError($"Error fetching data from {url}");
                yield break;
            }
            
            ResponseList<T> list = result.data as ResponseList<T>;
            if (list == null)
            {
                Debug.LogError($"Failed to parse list from {url}");
                yield break;
            }
            
            // Handle both old (result) and new (body) formats
            ListResult<T> listResult = list.result ?? list.body;
            
            if (listResult == null)
            {
                Debug.LogError($"List result is null from {url}");
                yield break;
            }
            
            if (listResult.data != null)
            {
                targetList.AddRange(listResult.data);
            }
            
            if (listResult.data == null || listResult.data.Count < pageSize) 
                break;
                
            pageNum++;
        }
    }
}
