using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Security_ThirdPartyList : SubSessionPanel
{
    public ThirdPartyObject thirdPartyObjectPrefab;
    public Transform thirdPartyContainer; // 用于放置 ThirdPartyObject 的容器
    public override SubSessionType subSessionType => SubSessionType.Security_ThirdPartyList;

    private static readonly ThirdParty[] NoThirdParties = new ThirdParty[0];

    private Coroutine currentCoroutine;
    private VirtualScrollList<ThirdParty, ThirdPartyObject> rows;
    private List<ThirdParty> allThirdParties = new List<ThirdParty>(); // 保存所有第三方单位数据
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

        if (Session.Instance.subSessionType == SubSessionType.Security_ThirdPartyList)
        {
            // 显示企业标记
            ShowCompanyObjects();

            if (allThirdParties.Count > 0)
            {
                // 虚拟滚动下不存在「行建到一半被切 tab 打断」这回事——行数固定是一屏，
                // 换数据只是重绑。所以不用再像以前那样整份重建来自愈。
                UpdateThirdPartyDisplay();
            }
            else
            {
                currentCoroutine = StartCoroutine(GetThirdPartyListCoroutine());
            }
        }
        else
        {
            HideThirdPartyObjects();
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
        // 只在当前是 Security_ThirdPartyList 模式时处理
        if (Session.Instance.subSessionType != SubSessionType.Security_ThirdPartyList)
            return;

        // 如果已有第三方单位数据，更新显示
        if (allThirdParties.Count > 0)
        {
            UpdateThirdPartyDisplay();
        }
    }

    /// <summary>
    /// 按当前选中的企业筛出要显示的数据交给虚拟滚动列表。
    ///
    /// 过滤全在数据层做了——以前是「行全建出来，再逐行 SetActive」，那套写法出过
    /// 一个 bug：查不到 sysOrgCode 时提前 return，容器一直是关的，面板永远空白。
    /// 现在只有一条出口，容器由这一处统一打开，结构上不会再漏。
    /// </summary>
    private void UpdateThirdPartyDisplay()
    {
        string selectedCompanyID = Session.Instance.companyID;
        IList<ThirdParty> visible;

        if (string.IsNullOrEmpty(selectedCompanyID))
        {
            // 没有选中企业，显示所有第三方单位
            visible = allThirdParties;
        }
        else
        {
            string sysOrgCode = ResolveSysOrgCode(selectedCompanyID);
            // 查不到 sysOrgCode 就给空列表：和以前「把所有行都关掉」的表现一致，
            // 但不用再去碰 GameObject。
            visible = string.IsNullOrEmpty(sysOrgCode)
                ? NoThirdParties
                : allThirdParties.FindAll(t => t.sysOrgCode == sysOrgCode);
        }

        EnsureRows();
        rows.SetData(visible);
        ShowThirdPartyObjects();

        Debug.Log($"[第三方单位] 数据 {visible.Count} 条，行实例 {rows.RowCount} 个");
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

        rows = new VirtualScrollList<ThirdParty, ThirdPartyObject>(
            thirdPartyObjectPrefab,
            thirdPartyContainer,
            (row, thirdParty, index) =>
            {
                // 序号按数据下标算，滚到第 500 行显示的就是 500，不是池里的位置。
                row.no = index + 1;
                row.thirdPartyDetails = thirdParty;
            });
    }

    private void HideThirdPartyObjects()
    {
        // 隐藏容器或父对象
        Transform parent = thirdPartyContainer != null ? thirdPartyContainer :
                          (thirdPartyObjectPrefab != null ? thirdPartyObjectPrefab.transform.parent : null);

        if (parent != null)
        {
            parent.gameObject.SetActive(false);
        }
    }

    private void ShowThirdPartyObjects()
    {
        // 显示容器或父对象
        Transform parent = thirdPartyContainer != null ? thirdPartyContainer :
                          (thirdPartyObjectPrefab != null ? thirdPartyObjectPrefab.transform.parent : null);

        if (parent != null)
        {
            parent.gameObject.SetActive(true);
        }
    }

    private IEnumerator GetThirdPartyListCoroutine()
    {
        string url = Api.SpecialJobThirdPartyList(0, 1000);

        // 请求第三方单位列表
        yield return DataProvider.Instance.Get<ThirdParty>(url);
        var result = DataProvider.Instance.GetLastResult<ThirdParty>(url);

        if (result != null && result.success)
        {
            ResponseList<ThirdParty> response = result.data as ResponseList<ThirdParty>;
            if (response?.result?.data != null)
            {
                Debug.Log($"获取 {response.result.total} 个第三方单位");

                // 保存所有第三方单位数据
                allThirdParties = response.result.data;

                // 根据当前选择更新显示。不再需要先把整份数据实例化成行——
                // 虚拟滚动只建一屏，SetData 就够了。
                UpdateThirdPartyDisplay();
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
