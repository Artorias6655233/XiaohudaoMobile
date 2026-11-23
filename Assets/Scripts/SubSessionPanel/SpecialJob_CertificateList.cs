using System.Collections;
using System.Collections.Generic;
using BNG;
using UnityEngine;

/// <summary>
/// 特殊作业 - 企业证件管理。
///
/// 选中沙盘上的企业 → 看这家企业的持证人数、人员信息和证件有效期，手柄翻页。
///
/// 【数据源】走 <see cref="ISpecialJobCertSource"/>，当前是随包演示数据。
/// 原来接的 <c>specialJob/certificate/table/list</c> 顶不上：全量 5 条、personName 里存的是
/// 人员 ID、没有持证人数口径。正式接口（缺口 C1–C4）到位后把 <c>certDataMode</c> 改成
/// network 即可，本页一行不用动。
///
/// 【为什么从虚拟滚动改成固定行槽分页】需求要的是手柄翻页，而手柄射线拖滚动条很难用。
/// 分页照 <see cref="TankListPanel"/> 那套：行槽数量 = 一页几行，Bind/Clear 驱动，
/// 页数 ≤ 1 时隐藏翻页按钮。
/// </summary>
public class SpecialJob_CertificateList : SubSessionPanel
{
    [Tooltip("场景里 Content 下那个关掉的行模板，行实例照它克隆")]
    public CertificateObject certificateObjectPrefab;

    [Tooltip("行的父节点。留空则回退到行模板自己的父节点（场景里就是 Content）")]
    public Transform listContainer;

    public CertificateDetailPanel detailPanel;

    [Tooltip("统计 + 翻页工具栏，由 Tools/企业证件/生成工具栏 生成的 prefab")]
    public CertListToolbar toolbarPrefab;

    [Tooltip("工具栏挂在哪，接列表卡片的底板（场景里 GameObject/Image）")]
    public Transform toolbarParent;

    [Tooltip("一页几行。滚动区高 747.5、行高 100，超过 7 行就会溢出到要滚动")]
    public int rowsPerPage = 7;

    public override SubSessionType subSessionType => SubSessionType.SpecialJob_CertificateList;

    private ISpecialJobCertSource source;
    private CertListToolbar toolbar;
    private readonly List<CertificateObject> rows = new List<CertificateObject>();

    private CompanyCertSummary summary = CompanyCertSummary.Empty;
    private string companyName = string.Empty;
    private int page;
    private bool isEventSubscribed;
    private Coroutine loadCoroutine;

    private Dictionary<string, CompanyObject> companyObjects => Session.Instance.companyObjects;
    private CesiumForUnity.CesiumGlobeAnchor companyPrefab => Session.Instance.companyPrefab;
    private GameObject companyRoot => Session.Instance.companyRoot;

    private int PageSize => Mathf.Max(1, rowsPerPage);

    private int PageCount
    {
        get
        {
            int count = summary.certCount;
            return count <= 0 ? 0 : (count + PageSize - 1) / PageSize;
        }
    }

    private bool IsCurrent =>
        Session.Instance != null &&
        Session.Instance.subSessionType == SubSessionType.SpecialJob_CertificateList;

    public override IEnumerator Start()
    {
        yield return base.Start();
        SubscribeToCompanyIDChanged();
    }

    private void SubscribeToCompanyIDChanged()
    {
        if (isEventSubscribed || Session.Instance == null) return;

        Session.Instance.OnCompanyIDChanged += OnCompanySelected;
        isEventSubscribed = true;
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
        if (Session.Instance.companyRoot != null)
        {
            Session.Instance.companyRoot.SetActive(false);
        }
    }

    public override void OnSubSessionTypeChanged()
    {
        base.OnSubSessionTypeChanged();
        SubscribeToCompanyIDChanged();

        if (loadCoroutine != null)
        {
            StopCoroutine(loadCoroutine);
            loadCoroutine = null;
        }

        if (!IsCurrent)
        {
            HideObjects();
            return;
        }

        if (companyRoot != null) companyRoot.SetActive(true);
        loadCoroutine = StartCoroutine(LoadCoroutine());
    }

    /// <summary>
    /// 手柄快捷键翻页。X 上一页、Y 下一页。
    ///
    /// X 键 <see cref="P0AnnotationShortcut"/> 也在用，但那边只在「重大危险源管控」页面
    /// 且选中危险源时才响应，和本页互斥，不会同时触发。B 键被 SandTableMainController
    /// 占了（瞬移菜单），不能用。
    ///
    /// 主交互仍然是射线点底部的翻页按钮，快捷键只是省一次瞄准。
    /// </summary>
    private void Update()
    {
        if (!IsCurrent || PageCount <= 1) return;
        if (InputBridge.Instance == null) return;

        if (InputBridge.Instance.XButtonDown) Turn(-1);
        else if (InputBridge.Instance.YButtonDown) Turn(1);
    }

    private IEnumerator LoadCoroutine()
    {
        if (companyObjects.Count == 0)
        {
            yield return GetCompanyListCoroutine();
        }
        else
        {
            foreach (KeyValuePair<string, CompanyObject> kvp in companyObjects)
            {
                if (kvp.Value != null) kvp.Value.gameObject.SetActive(true);
            }
        }

        if (source == null)
        {
            source = SpecialJobCertSourceFactory.Create(ApiConfig.CertMode);
            yield return source.LoadAsync();
        }

        ShowContainer();
        Render();
        loadCoroutine = null;
    }

    private void OnCompanySelected()
    {
        if (!IsCurrent) return;

        // 换企业一律回第一页：留在第 3 页但新企业只有 1 页，看到的会是空白
        page = 0;
        Render();
    }

    // ------------------------------------------------------------------ 渲染

    private void Render()
    {
        summary = ResolveSummary(out companyName);

        int pages = PageCount;
        page = pages <= 0 ? 0 : Mathf.Clamp(page, 0, pages - 1);

        EnsureRows();
        EnsureToolbar();

        for (int slot = 0; slot < rows.Count; slot++)
        {
            CertificateObject row = rows[slot];
            if (row == null) continue;

            int index = page * PageSize + slot;
            if (index < summary.certCount) row.Bind(summary.rows[index], index + 1, OnDetailClick);
            else row.Clear();
        }

        if (toolbar != null) toolbar.Render(BuildSummaryText(), page, pages);

        // 翻页后原来那张详情卡说的可能已经不在当前页了，直接收起来
        if (detailPanel != null) detailPanel.Hide();

        // 每次刷新打一行：联调时选企业和翻页都在头显里做，不打日志的话
        // 外面只能靠人念屏幕上的数字，对不上也说不清是哪一步错的
        Debug.Log($"[企业证件] 企业「{(string.IsNullOrEmpty(companyName) ? "未选" : companyName)}」" +
            $"持证 {summary.holderCount} 人 / {summary.certCount} 本，第 {page + 1}/{Mathf.Max(pages, 1)} 页");
    }

    /// <summary>
    /// 统计文案。人数和证件数分开写——一人多证的情况下拿证件数当人数是错的。
    /// 演示数据必须常驻角标，不能让现场把它当成真实台账。
    /// </summary>
    private string BuildSummaryText()
    {
        string demo = source != null && source.IsDemo ? "　【演示数据】" : string.Empty;

        if (string.IsNullOrEmpty(Session.Instance == null ? null : Session.Instance.companyID))
            return "请在沙盘上选择企业" + demo;

        string name = string.IsNullOrEmpty(companyName) ? "该企业" : companyName;

        if (source != null && !source.Ready)
            return $"{name}　证件数据加载失败，退出本页重进可重试{demo}";

        if (summary.IsEmpty)
            return $"{name}　无特殊作业持证人员数据{demo}";

        return $"{name}　持证人员 {summary.holderCount} 人，证件 {summary.certCount} 本{demo}";
    }

    /// <summary>
    /// 取当前选中企业的汇总。没选企业、查不到 sysOrgCode、数据源没就绪，一律给空汇总——
    /// 这三种情况都不能显示别家企业的数据，也不能显示「0 人」以外的推断值。
    /// </summary>
    private CompanyCertSummary ResolveSummary(out string name)
    {
        name = string.Empty;

        if (Session.Instance == null || string.IsNullOrEmpty(Session.Instance.companyID))
            return CompanyCertSummary.Empty;

        if (!companyObjects.TryGetValue(Session.Instance.companyID, out CompanyObject companyObj) ||
            companyObj == null || companyObj.companyDetails == null)
        {
            return CompanyCertSummary.Empty;
        }

        name = companyObj.companyDetails.companyName;

        if (source == null || !source.Ready) return CompanyCertSummary.Empty;

        string sysOrgCode = companyObj.companyDetails.sysOrgCode;
        return string.IsNullOrWhiteSpace(sysOrgCode)
            ? CompanyCertSummary.Empty
            : source.GetByCompany(sysOrgCode.Trim());
    }

    private void Turn(int delta)
    {
        int pages = PageCount;
        if (pages <= 1) return;

        int next = Mathf.Clamp(page + delta, 0, pages - 1);
        if (next == page) return;

        page = next;
        Render();
    }

    private void OnDetailClick(CertRow row)
    {
        if (detailPanel != null) detailPanel.Show(row);
    }

    // ------------------------------------------------------------------ 行与底部条

    private Transform RowParent =>
        listContainer != null
            ? listContainer
            : (certificateObjectPrefab != null ? certificateObjectPrefab.transform.parent : null);

    /// <summary>
    /// 行实例建一次就固定下来，翻页只换数据。模板本身留在场景里且保持关闭状态。
    /// </summary>
    private void EnsureRows()
    {
        if (rows.Count >= PageSize) return;

        if (certificateObjectPrefab == null || RowParent == null)
        {
            Debug.LogError("[企业证件] 没接行模板或行父节点，列表建不出来");
            return;
        }

        while (rows.Count < PageSize)
        {
            // 三参重载、worldPositionStays=false：默认的 true 会按世界坐标反算，
            // UI 元素的 scale 和 anchoredPosition 会被搅乱
            CertificateObject row = Instantiate(certificateObjectPrefab, RowParent, false);
            row.name = $"CertRow {rows.Count + 1}";
            row.gameObject.SetActive(false);
            rows.Add(row);
        }

        Debug.Log($"[企业证件] 行实例 {rows.Count} 个（一页 {PageSize} 行）");
    }

    private void EnsureToolbar()
    {
        if (toolbar != null || toolbarPrefab == null) return;

        Transform parent = toolbarParent != null ? toolbarParent : RowParent;
        if (parent == null) return;

        toolbar = Instantiate(toolbarPrefab, parent, false);
        toolbar.name = "CertListToolbar";
        toolbar.BindTurn(Turn);
    }

    private void HideObjects()
    {
        Transform parent = RowParent;
        if (parent != null) parent.gameObject.SetActive(false);
        if (toolbar != null) toolbar.gameObject.SetActive(false);
        if (detailPanel != null) detailPanel.Hide();
    }

    private void ShowContainer()
    {
        Transform parent = RowParent;
        if (parent != null) parent.gameObject.SetActive(true);
        if (toolbar != null) toolbar.gameObject.SetActive(true);
    }

    // ------------------------------------------------------------------ 企业目录

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
        foreach (Company company in companies)
        {
            CreateCompanyInstance(company);
        }
    }

    private void CreateCompanyInstance(Company company)
    {
        CesiumForUnity.CesiumGlobeAnchor instance = Instantiate(companyPrefab, companyRoot.transform);

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

        if (Session.Instance.sysOrgCodeToID != null &&
            !Session.Instance.sysOrgCodeToID.ContainsKey(company.sysOrgCode))
        {
            Session.Instance.sysOrgCodeToID[company.sysOrgCode] = company.id;
        }
    }
}
