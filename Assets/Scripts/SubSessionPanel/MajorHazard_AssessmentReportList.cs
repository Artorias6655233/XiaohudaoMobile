using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MajorHazard_AssessmentReportList : SubSessionPanel
{
    public AssessmentReportObject assessmentReportObjectPrefab;
    public Transform reportContainer; // 用于放置 AssessmentReportObject 的容器
    public AssessmentReportDetailPanel detailPanel;
    public override SubSessionType subSessionType => SubSessionType.MajorHazard_AssessmentReportList;

    private static readonly AssessmentReport[] NoReports = new AssessmentReport[0];

    private Coroutine currentCoroutine;
    private VirtualScrollList<AssessmentReport, AssessmentReportObject> rows;
    private List<AssessmentReport> allReports = new List<AssessmentReport>(); // 保存所有报告数据
    private bool isEventSubscribed = false;
    private Dictionary<string, string> sysOrgCodeToID => Session.Instance.sysOrgCodeToID;
    private Dictionary<string, CompanyObject> companyObjects => Session.Instance.companyObjects;
    private GameObject companyRoot => Session.Instance.companyRoot;
    private CesiumForUnity.CesiumGlobeAnchor companyPrefab => Session.Instance.companyPrefab;

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

    public override void OnSubSessionTypeChanged()
    {
        base.OnSubSessionTypeChanged();
        
        SubscribeToCompanyIDChanged();
        
        // 停止当前正在运行的 coroutine
        if (currentCoroutine != null)
        {
            StopCoroutine(currentCoroutine);
            currentCoroutine = null;
        }
        
        if (Session.Instance.subSessionType == SubSessionType.MajorHazard_AssessmentReportList)
        {
            ShowCompanyObjects();

            if (allReports.Count > 0)
            {
                // 虚拟滚动下不存在「行建到一半被切 tab 打断」这回事——行数固定是一屏，
                // 换数据只是重绑。所以不用再像以前那样整份重建来自愈。
                UpdateReportDisplay();
            }
            else
            {
                currentCoroutine = StartCoroutine(GetReportListCoroutine());
            }
        }
        else
        {
            HideReportObjects();
        }
    }

    private void ShowCompanyObjects()
    {
        if (companyRoot != null)
        {
            companyRoot.SetActive(true);
        }
        
        if (companyObjects.Count == 0)
        {
            StartCoroutine(GetCompanyListCoroutine());
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
        if (companyObjects.ContainsKey(company.id)) return;

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
        if (Session.Instance.subSessionType != SubSessionType.MajorHazard_AssessmentReportList)
            return;
        
        if (allReports.Count > 0)
        {
            UpdateReportDisplay();
        }
    }

    /// <summary>
    /// 按当前选中的企业筛出要显示的数据交给虚拟滚动列表。
    ///
    /// 过滤全在数据层做了——以前是「行全建出来，再逐行 SetActive」。没选企业时那套会
    /// 把上百行一次性 SetActive(true)，全部行的 TMP mesh 挤在同一次 CanvasUpdate 里，
    /// 这就是「不先选企业点 tab 才卡」的来源。
    /// </summary>
    private void UpdateReportDisplay()
    {
        string selectedCompanyID = Session.Instance.companyID;
        IList<AssessmentReport> visible;

        if (string.IsNullOrEmpty(selectedCompanyID))
        {
            visible = allReports;
        }
        else
        {
            string sysOrgCode = ResolveSysOrgCode(selectedCompanyID);
            // 查不到 sysOrgCode 就给空列表：和以前「把所有行都关掉」的表现一致，
            // 但不用再去碰 GameObject。
            visible = string.IsNullOrEmpty(sysOrgCode)
                ? NoReports
                : allReports.FindAll(r => r.sysOrgCode == sysOrgCode);
        }

        EnsureRows();
        rows.SetData(visible);
        ShowReportObjects();

        Debug.Log($"[评估报告] 数据 {visible.Count} 条，行实例 {rows.RowCount} 个");
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

        rows = new VirtualScrollList<AssessmentReport, AssessmentReportObject>(
            assessmentReportObjectPrefab,
            reportContainer,
            (row, report, index) =>
            {
                // 序号按数据下标算，滚到第 100 行显示的就是 100，不是池里的位置。
                row.reportNo = index + 1;
                row.reportDetails = report;
                // 复用行必须每次重设回调，否则第 3 行会拿着第 40 行的那份
                row.OnDetailClick = OnReportDetailClick;
            });
    }

    private void HideReportObjects()
    {
        // 隐藏容器或父对象
        Transform parent = reportContainer != null ? reportContainer : 
                          (assessmentReportObjectPrefab != null ? assessmentReportObjectPrefab.transform.parent : null);
        
        if (parent != null)
        {
            parent.gameObject.SetActive(false);
        }

        if (detailPanel != null)
        {
            detailPanel.Hide();
        }
    }

    private void ShowReportObjects()
    {
        // 显示容器或父对象
        Transform parent = reportContainer != null ? reportContainer : 
                          (assessmentReportObjectPrefab != null ? assessmentReportObjectPrefab.transform.parent : null);
        
        if (parent != null)
        {
            parent.gameObject.SetActive(true);
        }
    }

    private IEnumerator GetReportListCoroutine()
    {
        string url = Api.MajorHazardAssessmentReportList(0, 1000);
        
        // 请求报告列表
        yield return DataProvider.Instance.Get<AssessmentReport>(url);
        var result = DataProvider.Instance.GetLastResult<AssessmentReport>(url);
        
        if (result != null && result.success)
        {
            ResponseList<AssessmentReport> response = result.data as ResponseList<AssessmentReport>;
            if (response?.result?.data != null)
            {
                Debug.Log($"获取 {response.result.total} 个评估报告");
                
                allReports = response.result.data;

                // 根据当前选择更新显示。不再需要先把整份数据实例化成行——
                // 虚拟滚动只建一屏，SetData 就够了。
                UpdateReportDisplay();
            }
        }
        else
        {
            Debug.LogError($"请求失败: {result?.error}");
        }
        
        // coroutine 完成后清空引用
        currentCoroutine = null;
    }
    private void OnReportDetailClick(AssessmentReport report)
    {
        if (detailPanel != null)
        {
            detailPanel.Show(report);
        }
    }
}
