using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 特殊作业 · 审批流程列表。
///
/// 【数据源换过一次】原先走跳板后端 <c>specialJob/jobTicket/list</c>（一次拉 1000 条再
/// 客户端筛企业）。那套是门户的只读镜像，没有流程能力，也就不可能做审批。
/// 现在走门户 <see cref="PortalJobTicketService"/>：
///
///   - 列表主体 = 该企业的作业票台账，**按企业在服务端筛**，不再整表拉回来客户端筛；
///   - 待办只用来标记哪几张轮到当前账号审批（理由见 PortalJobTicketService 的类注释）。
///
/// 换企业会重新发请求。以前是本地过滤所以切企业不花钱，现在每次切都要一个往返 ——
/// 用 <see cref="loadedUnitId"/> 挡住重复请求（同一家企业连点不会重发）。
///
/// 【列表与票面是二选一，不是并排】两块面板原先并排摆在同一块画布上，列表本身就占满视野，
/// 票面整个落在视野之外，连审批按钮都点不到。现在票面搬到列表这个位置，打开票面就把列表
/// 整块藏掉，票面关了再放回来 —— 同一时刻只有一块面板在视野里。
/// </summary>
public class SpecialJob_JobTicket : SubSessionPanel
{
    public JobTicketObject jobTicketObjectPrefab;
    public Transform listContainer;

    /// <summary>
    /// 列表面板整块的根节点（背景 + 表头 + 滚动区）。
    ///
    /// 和 <see cref="listContainer"/> 不是一回事：那个是滚动区里放行的容器，藏了它只是行没了，
    /// 背景和表头还占着视野。票面要顶上来，得把这一整块藏掉。
    /// </summary>
    public Transform listPanelRoot;

    public JobTicketDetailPanel detailPanel;
    public override SubSessionType subSessionType => SubSessionType.SpecialJob_JobTicket;

    private static readonly JobTicket[] NoItems = new JobTicket[0];

    private Coroutine currentCoroutine;

    private VirtualScrollList<JobTicket, JobTicketObject> rows;
    private PortalJobTicketService.Snapshot snapshot;

    /// <summary>已经加载过的企业编码。null 表示还没加载过任何一次。</summary>
    private string loadedUnitId;

    private bool isEventSubscribed = false;

    /// <summary>票面正开着。开着的时候列表不能被别的路径（比如审批后的 Reload）放回来。</summary>
    private bool detailOpen;

    private void OnEnable()
    {
        if (detailPanel != null)
        {
            detailPanel.OnPassAction -= OnTicketPassed; // Prevent duplicate subscription
            detailPanel.OnPassAction += OnTicketPassed;
            detailPanel.OnBackAction -= OnDetailClosed;
            detailPanel.OnBackAction += OnDetailClosed;
        }
    }

    private void OnDisable()
    {
        if (detailPanel != null)
        {
            detailPanel.OnPassAction -= OnTicketPassed;
            detailPanel.OnBackAction -= OnDetailClosed;
        }
    }

    public override IEnumerator Start()
    {
        yield return base.Start();
        SubscribeToCompanyIDChanged();
    }

    /// <summary>
    /// 详情面板报告"这张票处理过了"，把列表行上的本机处置标记刷出来。
    ///
    /// 【不再本地删记录】最早那版是把这条从缓存和列表里抹掉，列表看起来变了、服务端毫无动作，
    /// 纯粹是障眼法。这张票该不该还在列表里，由服务端说了算。
    ///
    /// 【也不再重新拉一次】处置只写本机、一个字节都不回传门户，所以服务端的状态**不可能**变，
    /// 重拉除了多一个往返没有任何新信息；而且有两个实打实的坏处：拉失败会按既定策略清空列表
    /// （见 <see cref="GetListCoroutine"/>），用户刚处置完一张票就看见列表空了；
    /// 就算成功，<c>SetData</c> 会把滚动位置拉回顶部，在第 180 行处置完得重新滚回去。
    /// 原地重绑就够了。
    /// </summary>
    private void OnTicketPassed(JobTicket ticket)
    {
        if (ticket == null) return;
        rows?.Rebind();
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
        if (detailPanel != null)
        {
            detailPanel.OnPassAction -= OnTicketPassed;
            detailPanel.OnBackAction -= OnDetailClosed;
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
        
        if (Session.Instance.subSessionType == SubSessionType.SpecialJob_JobTicket)
        {
            EnsureLoaded();
        }
        else
        {
            HideObjects();
        }
    }

    private void OnCompanySelected()
    {
        if (Session.Instance.subSessionType != SubSessionType.SpecialJob_JobTicket)
            return;

        EnsureLoaded();
    }

    /// <summary>
    /// 当前该显示哪家企业的票，和已加载的不一致就重新拉。
    ///
    /// 换企业必须走网络，所以这里要挡重复：选中企业的事件在一次选择里可能触发多次，
    /// 不挡就会连发几个同样的请求。
    /// </summary>
    private void EnsureLoaded()
    {
        string unitId = ResolveUnitId(out string reason);

        if (reason != null)
        {
            ShowEmpty(reason);
            return;
        }

        if (currentCoroutine == null && loadedUnitId == unitId && snapshot != null)
        {
            UpdateDisplay();
            return;
        }

        Reload();
    }

    private void Reload()
    {
        if (currentCoroutine != null)
        {
            StopCoroutine(currentCoroutine);
            currentCoroutine = null;
        }
        currentCoroutine = StartCoroutine(GetListCoroutine());
    }

    /// <summary>
    /// 选中企业 → 门户要的 <c>applyingUnitId</c>。
    ///
    /// 没选企业不算错：此时拉全园区台账，和沙盘上"还没点任何企业"的状态一致。
    /// 其余失败原因各自返回一句人话，不要一律显示空列表 —— 空列表和"这家企业确实没票"
    /// 长得一样，用户没法判断该等一下、该重选、还是该找人。
    /// </summary>
    private string ResolveUnitId(out string reason)
    {
        reason = null;

        if (Session.Instance == null || string.IsNullOrEmpty(Session.Instance.companyID))
        {
            return null; // 全园区
        }

        if (PortalCompany.TryResolveCurrent(out string unitId, out PortalCompany.Failure failure))
        {
            return unitId;
        }

        reason = PortalCompany.Describe(failure);
        return null;
    }

    /// <summary>
    /// 把已拉到的数据喂给虚拟滚动列表。
    ///
    /// 【不再在这里筛企业】筛选移到服务端了（<c>applyingUnitId</c> 入参）。以前是整表
    /// 拉回来客户端按 <c>sysOrgCode</c> 比对，既多传几百条用不上的数据，又要自己维护
    /// 「用 sysOrgCode 还是 deptOrgCode 比」这种口径 —— 而服务端的筛选口径实测是准的。
    /// </summary>
    private void UpdateDisplay()
    {
        IList<JobTicket> visible = snapshot != null ? (IList<JobTicket>)snapshot.Tickets : NoItems;

        EnsureRows();
        rows.SetData(visible);
        ShowContainer();

        Debug.Log($"[作业票] 显示 {visible.Count} 条，行实例 {rows.RowCount} 个");
    }

    /// <summary>
    /// 显示一个带原因的空列表。
    ///
    /// 空列表本身不携带信息 —— "这家企业没有作业票"、"企业目录还没加载完"、"网络断了"
    /// 看起来完全一样。所以原因要打出来，后续接上空态文案时直接用这一句。
    /// </summary>
    private void ShowEmpty(string reason)
    {
        snapshot = null;
        loadedUnitId = null;

        EnsureRows();
        rows.SetData(NoItems);
        ShowContainer();

        Debug.Log($"[作业票] 列表为空：{reason}");
    }

    private void EnsureRows()
    {
        if (rows != null)
        {
            return;
        }

        rows = new VirtualScrollList<JobTicket, JobTicketObject>(
            jobTicketObjectPrefab,
            listContainer,
            (row, ticket, index) =>
            {
                // 序号按数据下标算，滚到第 200 行显示的就是 200，不是池里的位置。
                row.Bind(ticket, index, snapshot != null && snapshot.IsActionable(ticket));
                // 复用行必须每次重设回调，否则第 3 行会拿着第 40 行的那份
                row.OnDetailClick = OnDetailClick;
            });
    }

    private void HideObjects()
    {
        detailOpen = false;

        Transform parent = RowsParent();
        if (parent != null)
        {
            parent.gameObject.SetActive(false);
        }

        if (listPanelRoot != null)
        {
            listPanelRoot.gameObject.SetActive(false);
        }

        if (detailPanel != null)
        {
            detailPanel.Hide();
        }
    }

    /// <summary>
    /// 把列表显示出来 —— 但票面开着的时候不动列表面板。
    ///
    /// 审批提交成功会触发 <see cref="Reload"/>，而票面这时还要停留 3 秒显示「操作完成」。
    /// 不挡这一下，列表会在票面后面冒出来，两块面板叠在同一个位置上。
    /// </summary>
    private void ShowContainer()
    {
        Transform parent = RowsParent();
        if (parent != null)
        {
            parent.gameObject.SetActive(true);
        }

        if (listPanelRoot != null)
        {
            listPanelRoot.gameObject.SetActive(!detailOpen);
        }
    }

    /// <summary>行实例挂在哪。<c>listContainer</c> 场景里没接，退回用行模板自己的父节点。</summary>
    private Transform RowsParent()
    {
        if (listContainer != null) return listContainer;
        return jobTicketObjectPrefab != null ? jobTicketObjectPrefab.transform.parent : null;
    }

    private IEnumerator GetListCoroutine()
    {
        string unitId = ResolveUnitId(out string reason);
        if (reason != null)
        {
            ShowEmpty(reason);
            currentCoroutine = null;
            yield break;
        }

        PortalJobTicketService.Snapshot fresh = new PortalJobTicketService.Snapshot();
        yield return PortalJobTicketService.Fetch(unitId, fresh);

        if (!fresh.Ok)
        {
            // 拉失败不保留上一次的数据：选了乙企业却还显示着甲企业的票，比空列表危险得多
            ShowEmpty(fresh.Error ?? "作业票获取失败");
            currentCoroutine = null;
            yield break;
        }

        snapshot = fresh;
        loadedUnitId = unitId;
        UpdateDisplay();

        currentCoroutine = null;
    }
    private void OnDetailClick(JobTicket ticket)
    {
        if (detailPanel == null) return;

        detailOpen = true;
        if (listPanelRoot != null)
        {
            listPanelRoot.gameObject.SetActive(false);
        }

        detailPanel.Show(ticket);
    }

    /// <summary>票面关了（用户点返回，或者提交完成后自动返回），把列表放回来。</summary>
    private void OnDetailClosed()
    {
        detailOpen = false;
        if (listPanelRoot != null)
        {
            listPanelRoot.gameObject.SetActive(true);
        }
    }
}
