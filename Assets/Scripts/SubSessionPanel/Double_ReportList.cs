using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Double_ReportList : SubSessionPanel
{
    public CheckObject checkObjectPrefab;
    public Transform checkContainer;
    public ReportDetailPanel detailPanel;
    [Header("整改督办")]
    public bool useMockBackend = true;
    public SupervisionPanel supervisionPanelPrefab;
    public RemediationDetailPanel remediationDetailPanelPrefab;
    public override SubSessionType subSessionType => SubSessionType.Double_ReportList;

    private Coroutine currentCoroutine;
    private VirtualScrollList<DoubleDanger, CheckObject> rows;
    private List<DoubleDanger> allDangers = new List<DoubleDanger>();
    private bool isEventSubscribed = false;
    private int requestGeneration;
    private SupervisionService supervisionService;
    private DoubleDanger currentDanger;
    private bool supervisionResultSubscribed;
    private Dictionary<string, string> sysOrgCodeToID => Session.Instance.sysOrgCodeToID;

    public override IEnumerator Start()
    {
        supervisionService = new SupervisionService(
            useMockBackend, DoublePreventionMockLoader.Load());
        EnsureRuntimeUi();
        yield return base.Start();
        SubscribeToCompanyIDChanged();
    }

    private void Update()
    {
        if (Session.Instance == null || BNG.InputBridge.Instance == null ||
            Session.Instance.subSessionType != SubSessionType.Double_ReportList) return;
        if (supervisionPanelPrefab != null && supervisionPanelPrefab.IsOpen)
        {
            if (BNG.InputBridge.Instance.YButtonDown)
                supervisionPanelPrefab.Submit();
            return;
        }
        if (currentDanger != null && BNG.InputBridge.Instance.XButtonDown) OpenSupervision();
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
        
        if (currentCoroutine != null)
        {
            StopCoroutine(currentCoroutine);
            currentCoroutine = null;
        }
        
        if (Session.Instance.subSessionType == SubSessionType.Double_ReportList)
            currentCoroutine = StartCoroutine(GetListCoroutine());
        else
        {
            requestGeneration++;
            HideObjects();
        }
    }

    private void OnCompanySelected()
    {
        if (Session.Instance.subSessionType != SubSessionType.Double_ReportList)
            return;

        requestGeneration++;
        if (currentCoroutine != null) StopCoroutine(currentCoroutine);
        allDangers.Clear();
        rows?.SetData(allDangers);
        detailPanel?.Hide();
        remediationDetailPanelPrefab?.Close();
        supervisionPanelPrefab?.CloseImmediately();
        currentDanger = null;
        SetListVisible(true);
        currentCoroutine = StartCoroutine(GetListCoroutine());
    }

    private void UpdateDisplay()
    {
        EnsureRows();
        rows.SetData(allDangers);
        ShowContainer();
        Debug.Log($"[整改督办] 数据 {allDangers.Count} 条，行实例 {rows.RowCount} 个");
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

        rows = new VirtualScrollList<DoubleDanger, CheckObject>(
            checkObjectPrefab,
            checkContainer,
            (row, danger, index) =>
            {
                row.data = danger;
                // 序号按数据下标算，滚到第 200 行显示的就是 200，不是池里的位置。
                row.index = index + 1;
                // 复用行必须每次重设回调，否则第 3 行会拿着第 40 行的那份
                row.OnDetailClick = OnDetailClick;
            });
    }

    private void HideObjects()
    {
        SetListVisible(false);
        if (detailPanel != null)
        {
            detailPanel.Hide();
        }
        remediationDetailPanelPrefab?.Close();
        supervisionPanelPrefab?.CloseImmediately();
        currentDanger = null;
    }

    private void ShowContainer()
    {
        SetListVisible(true);
    }

    private IEnumerator GetListCoroutine()
    {
        int generation = ++requestGeneration;
        allDangers.Clear();
        string topOrgCode = ResolveSysOrgCode(Session.Instance.companyID);
        var result = new ServiceResult<List<DoubleDanger>>();
        yield return supervisionService.LoadUnfinished(topOrgCode, result);
        if (generation != requestGeneration) yield break;
        if (result.ok && result.value != null) allDangers.AddRange(result.value);
        else if (!result.ok) Debug.LogError("[整改督办] 加载失败：" + result.error);
        UpdateDisplay();
        currentCoroutine = null;
    }

    private void OnDetailClick(DoubleDanger danger)
    {
        if (danger == null) return;
        currentDanger = danger;
        detailPanel?.Hide();
        RectTransform listRect = ResolveListPanelRect();
        remediationDetailPanelPrefab?.ConfigureListView(
            listRect, ReturnToList, OpenSupervision);
        SetListVisible(false);
        remediationDetailPanelPrefab?.Open(danger);
    }

    private void EnsureRuntimeUi()
    {
        if (supervisionPanelPrefab == null)
            supervisionPanelPrefab = SupervisionPanel.Create(transform, supervisionService);
        if (remediationDetailPanelPrefab == null)
            remediationDetailPanelPrefab = RemediationDetailPanel.Create(
                transform, supervisionService);
        if (!supervisionResultSubscribed && supervisionPanelPrefab != null)
        {
            supervisionPanelPrefab.Sent += OnSupervisionSent;
            supervisionResultSubscribed = true;
        }
        remediationDetailPanelPrefab.ConfigureListView(
            ResolveListPanelRect(), ReturnToList, OpenSupervision);
    }

    private void OpenSupervision()
    {
        if (currentDanger != null) supervisionPanelPrefab?.Open(currentDanger);
    }

    private void OpenSupervision(DoubleDanger danger)
    {
        currentDanger = danger;
        OpenSupervision();
    }

    private void OnSupervisionSent(SupervisionRecord record)
    {
        remediationDetailPanelPrefab?.ShowLatestSupervision(record);
    }

    private void ReturnToList()
    {
        remediationDetailPanelPrefab?.Close();
        currentDanger = null;
        if (Session.Instance == null ||
            Session.Instance.subSessionType == SubSessionType.Double_ReportList)
            SetListVisible(true);
    }

    private void SetListVisible(bool visible)
    {
        RectTransform listPanel = ResolveListPanelRect();
        if (listPanel != null) listPanel.gameObject.SetActive(visible);
    }

    private RectTransform ResolveListPanelRect()
    {
        Transform current = checkContainer != null ? checkContainer :
            (checkObjectPrefab != null ? checkObjectPrefab.transform.parent : null);
        while (current != null && current.parent != transform)
            current = current.parent;
        return current as RectTransform;
    }
}
