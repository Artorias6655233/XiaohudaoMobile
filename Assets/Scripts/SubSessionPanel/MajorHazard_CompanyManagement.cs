using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 「重大危险源管控信息」——纯信息看板，【不在沙盘上建任何图标】。
///
/// 沙盘图标那一套（企业 icon、罐体 icon、危险源 icon）已经全部归拢到
/// 「重大风险管控信息」（<see cref="MajorHazard_Management"/>）：那一页负责选企业、
/// 渲染罐体、开罐体列表和管理员信息面板。这一页只读 <see cref="Session.companyID"/>，
/// 把当前选中企业的负责人和罐数显示出来。
///
/// 【为什么连企业 icon 也不建】两页都建企业图标的话，同一个 companyRoot 下会出现两套
/// 实例，切页时谁隐藏谁就成了竞态——之前危险源图标残留、罐体图标堆在一个点上，都是
/// 这么来的。企业选择只留一个入口。
///
/// 数据只取不画：企业列表和罐体列表都拉成本地 List，不 Instantiate、不写
/// Session.companyObjects / tankObjects，所以这一页对其他页没有任何副作用。
///
/// 【P0 危险源那套还在，只是这一页不再用】按真实接口取危险源点位的
/// <see cref="NetworkP0DataSource"/> / <see cref="P0HazardMapper"/> 保留着（riskTagRecord
/// 实测能拿到 514 个逐罐真实坐标），等哪一页要画危险源分布时直接接。
/// </summary>
public class MajorHazard_CompanyManagement : SubSessionPanel
{
    public override SubSessionType subSessionType => SubSessionType.MajorHazard_CompanyManagement;

    [Header("UI Bindings")]
    public GameObject infoPanel;
    public TextMeshProUGUI leaderNameText;
    public TextMeshProUGUI tankCountText;

    /// <summary>
    /// 危险源详情卡片 / 批注卡片的 prefab 引用。
    ///
    /// 这一页已经不建危险源图标，两个字段都用不到了，但它们是场景里连好的序列化引用——
    /// 删掉就要动 SandTable.unity，而那个文件一保存会重排全部 fileID。留着不碰。
    /// </summary>
    public GameObject p0HazardDetailPrefab;
    public GameObject p0AnnotationPrefab;

    private Coroutine currentCoroutine;
    private bool isEventSubscribed;

    /// <summary>企业档案，只用于查负责人；不建图标。</summary>
    private List<P0CompanyRecord> companies;

    /// <summary>罐体，只用于按 sysOrgCode 数个数；不建图标。</summary>
    private List<Tank> tanks;

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
        StopLoading();
        if (infoPanel != null) infoPanel.SetActive(false);
    }

    public override void OnSubSessionTypeChanged()
    {
        base.OnSubSessionTypeChanged();
        SubscribeToCompanyIDChanged();
        StopLoading();

        if (Session.Instance.subSessionType != subSessionType) return;

        // 进来先把沙盘上的图标收掉：这一页不渲染任何 icon，上一页留下的图标
        // 继续挂在屏幕上只会让人以为还能点。
        if (Session.Instance.companyRoot != null) Session.Instance.companyRoot.SetActive(false);
        if (Session.Instance.tankRoot != null) Session.Instance.tankRoot.SetActive(false);

        currentCoroutine = StartCoroutine(LoadBoard());
    }

    private void StopLoading()
    {
        if (currentCoroutine == null) return;
        StopCoroutine(currentCoroutine);
        currentCoroutine = null;
    }

    private IEnumerator LoadBoard()
    {
        if (companies == null) yield return LoadCompanies();
        if (tanks == null) yield return LoadTanks();
        Refresh();
    }

    private IEnumerator LoadCompanies()
    {
        string url = Api.SecurityCompanyList(0, 1000);
        var fetch = new OfflineMock.Result();
        yield return OfflineMock.Fetch(url, fetch);
        if (!fetch.ok)
        {
            Debug.LogError("[重大危险源管控] 企业列表取数失败：" + fetch.error);
            yield break;
        }

        companies = SandTableJson.Rows(
            SandTableJson.Deserialize<ResponseList<P0CompanyRecord>>(
                fetch.json, "security/company/list"));
    }

    private IEnumerator LoadTanks()
    {
        string url = Api.MajorHazardTankList(1, 1000);
        var fetch = new OfflineMock.Result();
        yield return OfflineMock.Fetch(url, fetch);
        if (!fetch.ok)
        {
            Debug.LogError("[重大危险源管控] 罐体列表取数失败：" + fetch.error);
            yield break;
        }

        // Tank 复用的是老模型，tankVol 一类可空数值列声明成了 double，
        // 接口返回 null 时必须靠 SandTableJson 跳过，否则整份响应都解析不出来。
        tanks = SandTableJson.Rows(
            SandTableJson.Deserialize<ResponseList<Tank>>(
                fetch.json, "majorHazard/tank/list"));
    }

    private void OnCompanySelected()
    {
        if (Session.Instance.subSessionType != subSessionType) return;
        Refresh();
    }

    private void Refresh()
    {
        string companyId = Session.Instance.companyID;
        if (string.IsNullOrEmpty(companyId) || companies == null)
        {
            if (infoPanel != null) infoPanel.SetActive(false);
            return;
        }

        P0CompanyRecord company = companies.FirstOrDefault(item => item.id == companyId);
        if (company == null)
        {
            if (infoPanel != null) infoPanel.SetActive(false);
            return;
        }

        // 罐数按 sysOrgCode 数，和「重大风险管控信息」那页筛罐体图标用的是同一个口径
        int tankCount = tanks == null
            ? 0
            : tanks.Count(item => !string.IsNullOrEmpty(item.sysOrgCode) &&
                                  item.sysOrgCode.Trim() == company.sysOrgCode?.Trim());

        if (infoPanel == null) return;
        infoPanel.SetActive(true);
        if (tankCountText != null) tankCountText.text = tankCount.ToString();
        if (leaderNameText != null) leaderNameText.text = LeaderOf(company);
        LayoutRebuilder.ForceRebuildLayoutImmediate(infoPanel.GetComponent<RectTransform>());

        Debug.Log($"[重大危险源管控] 企业 {company.DisplayName}（{company.sysOrgCode}）：罐 {tankCount} 个");
    }

    /// <summary>负责人优先取实际控制人，缺失时退回法定代表人——和旧实现一致。</summary>
    private static string LeaderOf(P0CompanyRecord company)
    {
        return !string.IsNullOrEmpty(company.companyControllerName)
            ? company.companyControllerName
            : company.legalRepresentative;
    }
}
