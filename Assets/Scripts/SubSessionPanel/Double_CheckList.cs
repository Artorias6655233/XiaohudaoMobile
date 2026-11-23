using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Double_CheckList : SubSessionPanel
{
    public CheckObject checkObjectPrefab;
    public Transform checkContainer;
    [Tooltip("旧隐患详情，仅用于兼容场景；运行效果检查不再使用它。")]
    public CheckDetailPanel detailPanel;

    [Header("执法巡查运行效果检查")]
    public bool useMockBackend = false;
    public PatrolDetailPanel patrolDetailPanelPrefab;

    public override SubSessionType subSessionType => SubSessionType.Double_CheckList;

    private Coroutine currentCoroutine;
    private VirtualScrollList<PatrolRecord, CheckObject> rows;
    private readonly List<PatrolRecord> allPatrols = new List<PatrolRecord>();
    private bool isEventSubscribed;
    private int requestGeneration;
    private PatrolInspectionService patrolService;
    private InspectionAnnotationService annotationService;

    public override IEnumerator Start()
    {
        var mock = DoublePreventionMockLoader.Load();
        patrolService = new PatrolInspectionService(useMockBackend, mock);
        annotationService = new InspectionAnnotationService();
        if (patrolDetailPanelPrefab == null)
            patrolDetailPanelPrefab = PatrolDetailPanel.Create(
                transform, patrolService, annotationService);
        patrolDetailPanelPrefab.ConfigureListView(ResolveListPanelRect(), ReturnToList);
        yield return base.Start();
        SubscribeToCompanyIDChanged();
    }

    private void Update()
    {
        if (Session.Instance == null || BNG.InputBridge.Instance == null ||
            Session.Instance.subSessionType != SubSessionType.Double_CheckList ||
            patrolDetailPanelPrefab == null) return;

        InspectionAnnotationPanel annotation = patrolDetailPanelPrefab.annotationPanel;
        if (annotation != null && annotation.IsOpen)
        {
            if (BNG.InputBridge.Instance.YButtonDown) annotation.Save();
            return;
        }
        if (patrolDetailPanelPrefab.IsOpen &&
            !string.IsNullOrEmpty(patrolDetailPanelPrefab.CurrentPatrolRecordId) &&
            BNG.InputBridge.Instance.XButtonDown)
            patrolDetailPanelPrefab.OpenAnnotation();
    }

    private void SubscribeToCompanyIDChanged()
    {
        if (isEventSubscribed || Session.Instance == null) return;
        Session.Instance.OnCompanyIDChanged += OnCompanySelected;
        isEventSubscribed = true;
    }

    private void OnDestroy()
    {
        if (Session.Instance != null) Session.Instance.OnCompanyIDChanged -= OnCompanySelected;
    }

    public override void OnSubSessionTypeChanged()
    {
        base.OnSubSessionTypeChanged();
        SubscribeToCompanyIDChanged();
        if (currentCoroutine != null) StopCoroutine(currentCoroutine);
        if (Session.Instance.subSessionType == SubSessionType.Double_CheckList)
            currentCoroutine = StartCoroutine(GetListCoroutine());
        else
        {
            requestGeneration++;
            HideObjects();
        }
    }

    private void OnCompanySelected()
    {
        if (Session.Instance.subSessionType != SubSessionType.Double_CheckList) return;
        requestGeneration++;
        if (currentCoroutine != null) StopCoroutine(currentCoroutine);
        allPatrols.Clear();
        rows?.SetData(allPatrols);
        detailPanel?.Hide();
        ReturnToList();
        currentCoroutine = StartCoroutine(GetListCoroutine());
    }

    private IEnumerator GetListCoroutine()
    {
        int generation = ++requestGeneration;
        allPatrols.Clear();
        var result = new ServiceResult<PatrolPage>();
        string companyName = ResolveCompanyName();
        Debug.Log($"[运行效果检查] 请求巡查列表，企业：{companyName}，数据源：{(useMockBackend ? "Mock" : "真实接口")}");
        yield return patrolService.LoadPage(companyName, result);
        if (generation != requestGeneration) yield break;
        if (result.ok && result.value?.rows != null) allPatrols.AddRange(result.value.rows);
        else if (!result.ok) Debug.LogError("[运行效果检查] 巡查列表加载失败：" + result.error);
        EnsureRows();
        rows.SetData(allPatrols);
        ShowContainer();
        currentCoroutine = null;
        Debug.Log($"[运行效果检查] 巡查记录 {allPatrols.Count} 条，行实例 {rows.RowCount} 个");
    }

    private string ResolveCompanyName()
    {
        if (Session.Instance == null || string.IsNullOrEmpty(Session.Instance.companyID))
            return string.Empty;
        if (Session.Instance.companyObjects.TryGetValue(
                Session.Instance.companyID, out CompanyObject company) &&
            company?.companyDetails != null)
            return company.companyDetails.companyName;
        return string.Empty;
    }

    private void EnsureRows()
    {
        if (rows != null) return;
        rows = new VirtualScrollList<PatrolRecord, CheckObject>(
            checkObjectPrefab,
            checkContainer,
            (row, patrol, index) =>
            {
                row.index = index + 1;
                row.patrolData = patrol;
                row.OnPatrolDetailClick = OpenDetail;
            });
    }

    private void OpenDetail(PatrolRecord patrol)
    {
        detailPanel?.Hide();
        SetListVisible(false);
        patrolDetailPanelPrefab?.ConfigureListView(ResolveListPanelRect(), ReturnToList);
        patrolDetailPanelPrefab?.Open(patrol);
    }

    private void HideObjects()
    {
        SetListVisible(false);
        detailPanel?.Hide();
        patrolDetailPanelPrefab?.Close();
    }

    private void ShowContainer()
    {
        SetListVisible(true);
    }

    private void ReturnToList()
    {
        patrolDetailPanelPrefab?.Close();
        if (Session.Instance == null ||
            Session.Instance.subSessionType == SubSessionType.Double_CheckList)
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
