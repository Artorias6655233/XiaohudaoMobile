using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using TMPro;

public class ClosedPark_EntryExitList : SubSessionPanel
{
    public override SubSessionType subSessionType => SubSessionType.ClosedPark_EntryExitList;

    [Header("UI Components")]
    public TextMeshProUGUI titleText;
    public CarAccessRecordObject carRecordPrefab;
    public Transform carRecordContainer;
    public PersonAccessRecordObject personRecordPrefab;
    public Transform personRecordContainer;
    public Button carButton;
    public Button personButton;
    public RectTransform carLabel;
    public RectTransform personLabel;

    [Header("审核 / 备注（只存本机，不回传生产环境）")]
    [Tooltip("行最右侧「操作」列的 prefab，由 Tools/出入园审核/生成 prefab 产出")]
    public EntryExitActionCell actionCellPrefab;
    [Tooltip("并排浮在面板右侧的审核卡片 prefab，由同一个菜单产出")]
    public EntryExitReviewCard reviewCardPrefab;

    private EntryExitReviewCard reviewCard;
    private bool operationHeaderAdded;
    private bool filterButtonAdded;
    private Image filterImage;
    private readonly Color filterIdleColor = new Color(0.18f, 0.26f, 0.38f, 0.75f);
    private readonly Color filterOnColor = new Color(0.20f, 0.52f, 0.78f, 0.95f);
    private EntryExitHoverShortcut shortcut;

    /// <summary>「只看待审核」是否开着。</summary>
    private bool onlyPending;

    /// <summary>
    /// 本次筛选期间刚被审批掉的记录。
    ///
    /// 开着「只看待审核」时，一审完那行就从列表里消失，在 MR 里非常突兀——刚点完确认，
    /// 视线落点上的东西没了，人会以为点错了。所以这些 key 暂时豁免过滤，留在列表上，
    /// 等下一次切车/人 tab、换企业或重进这个 tab 时才真正隐去。
    /// </summary>
    private readonly HashSet<string> justHandled = new HashSet<string>();

    // 行复用时每次都要重设回调，缓存成字段免得每帧新建委托
    private System.Action<object> onCarReview;
    private System.Action<object> onCarRemark;
    private System.Action<object> onPersonReview;
    private System.Action<object> onPersonRemark;

    private Coroutine currentCoroutine;
    private VirtualScrollList<CarAccessRecord, CarAccessRecordObject> carRows;
    private List<CarAccessRecord> allCarRecords = new List<CarAccessRecord>();

    private VirtualScrollList<PersonAccessRecord, PersonAccessRecordObject> personRows;
    private List<PersonAccessRecord> allPersonRecords = new List<PersonAccessRecord>();

    private static readonly CarAccessRecord[] NoCars = new CarAccessRecord[0];
    private static readonly PersonAccessRecord[] NoPersons = new PersonAccessRecord[0];

    private bool isShowingCars = true; // 当前是否显示车辆列表

    // 复用 Security_LicenseList 的企业显示逻辑
    private Dictionary<string, CompanyObject> companyObjects => Session.Instance.companyObjects;
    private GameObject companyRoot => Session.Instance.companyRoot;
    private CesiumForUnity.CesiumGlobeAnchor companyPrefab => Session.Instance.companyPrefab;
    private Dictionary<string, string> sysOrgCodeToID => Session.Instance.sysOrgCodeToID;

    private bool isEventSubscribed = false;

    public override IEnumerator Start()
    {
        if (carButton != null)
            carButton.onClick.AddListener(OnCarButtonClicked);
        
        if (personButton != null)
            personButton.onClick.AddListener(OnPersonButtonClicked);

        yield return base.Start();
        SubscribeToCompanyIDChanged();

        // 快捷键组件运行时挂上，不进场景（SandTable.unity 不能重新保存）
        if (shortcut == null)
        {
            shortcut = gameObject.AddComponent<EntryExitHoverShortcut>();
            shortcut.onOpenCard = ShowReviewCard;
            shortcut.cardProvider = () => reviewCard;
        }
    }

    private void OnCarButtonClicked()
    {
        if (isShowingCars) return;
        isShowingCars = true;
        HideReviewCard();
        justHandled.Clear();
        RefreshView();
    }

    private void OnPersonButtonClicked()
    {
        if (!isShowingCars) return;
        isShowingCars = false;
        HideReviewCard();
        justHandled.Clear();
        RefreshView();
    }

    private void RefreshView()
    {
        if (titleText != null)
        {
            titleText.text = isShowingCars ? "车辆可进入查询" : "人员可进入查询";
        }

        UpdateRecordDisplay();
        
        // 切过去就保证那一侧的行是完整的：没数据取数据，有数据重建行。
        // 不能只在 Count == 0 时才做——分帧建可能被上一次切换打断，
        // 那时数据是全的、行只有一半。
        if (currentCoroutine != null) StopCoroutine(currentCoroutine);
        currentCoroutine = StartCoroutine(isShowingCars ? EnsureCarRows() : EnsurePersonRows());
    }

    /// <summary>
    /// 保证车辆一侧有数据：没有就取，有就直接刷显示。
    ///
    /// 以前这里要"用缓存重建行"，因为分帧建可能被切换打断留下半截。虚拟滚动下
    /// 行数固定是一屏、换数据只是重绑，不存在半截状态，所以不用再重建。
    /// </summary>
    private IEnumerator EnsureCarRows()
    {
        if (allCarRecords.Count == 0)
        {
            yield return GetCarListCoroutine();
        }
        else
        {
            UpdateRecordDisplay();
        }

        currentCoroutine = null;
    }

    /// <summary>保证人员一侧有数据，理由同 EnsureCarRows。</summary>
    private IEnumerator EnsurePersonRows()
    {
        if (allPersonRecords.Count == 0)
        {
            yield return GetPersonListCoroutine();
        }
        else
        {
            UpdateRecordDisplay();
        }

        currentCoroutine = null;
    }

    private void SubscribeToCompanyIDChanged()
    {
        if (isEventSubscribed) return;
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

    // 订阅/退订成对写在 OnEnable/OnDisable：面板在切 tab 时会被 SetActive(false)，
    // 关着的时候不需要跟着审核结论动，也避免静态事件一直攥着一个隐藏面板。
    private void OnEnable()
    {
        EntryExitReviewStore.OnChanged += OnReviewChanged;
    }

    private void OnDisable()
    {
        EntryExitReviewStore.OnChanged -= OnReviewChanged;
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

        if (Session.Instance.subSessionType == subSessionType)
        {
            isShowingCars = true;
            EnsureOperationHeader();
            EnsureFilterButton();

            // 重进这个 tab 等于重新开工，上次留在列表上的已处置记录该隐去了
            justHandled.Clear();

            ShowCompanyObjects();
            
            if (titleText != null)
            {
                titleText.text = isShowingCars ? "车辆可进入查询" : "人员可进入查询";
            }

            // 默认显示车辆，或者保持上次状态？这里假设保持状态
            if (currentCoroutine != null) StopCoroutine(currentCoroutine);
            currentCoroutine = StartCoroutine(isShowingCars ? EnsureCarRows() : EnsurePersonRows());
        }
        else
        {
            HideRecordObjects();
            EntryExitReviewStore.Hover(null);
            // 离开模式时不隐藏 companyObjects
        }
    }

    // ------------------------------------------------------------------ 审核 / 备注

    /// <summary>
    /// 给车/人两个表头各补一个「操作」列。
    ///
    /// 克隆表头已有的最后一个列头再改文案，样式（字体/字号/颜色/对齐）自然就对上了；
    /// 位置交给表头自带的 HorizontalLayoutGroup，和行里那一列用的是同一套参数，天然对齐。
    /// </summary>
    private void EnsureOperationHeader()
    {
        if (operationHeaderAdded) return;
        operationHeaderAdded = true;

        AppendOperationHeader(carLabel);
        AppendOperationHeader(personLabel);
    }

    private void AppendOperationHeader(RectTransform header)
    {
        if (header == null || header.childCount == 0) return;

        RectTransform template = header.GetChild(header.childCount - 1) as RectTransform;
        if (template == null) return;

        RectTransform cell = Instantiate(template, header);
        cell.name = "OperationHeader";
        cell.sizeDelta = new Vector2(EntryExitActionCell.ColumnWidth, template.sizeDelta.y);

        TextMeshProUGUI text = cell.GetComponent<TextMeshProUGUI>();
        if (text != null) text.text = "操作";
    }

    /// <summary>
    /// 补一个「只看待审核」按钮，排在「人员」按钮右边。
    ///
    /// 【不能克隆 carButton】：场景里那两个切换按钮是 SpriteSwap 的纯图标按钮，连文字子节点
    /// 都没有（文案其实在 titleText 上），克隆出来是个没法标注含义的图标，而且 SpriteSwap
    /// 会和这里要做的选中态染色打架。所以整个按钮运行时搭出来。
    ///
    /// 搭而不是摆进场景，理由和 <see cref="AppendOperationHeader"/> 一样：
    /// SandTable.unity 保存一次会重编号全部 fileID。
    ///
    /// 位置不写死：步长取现有两个按钮的实际间距，以后那两个按钮在场景里挪了，这个跟着走。
    /// </summary>
    private void EnsureFilterButton()
    {
        if (filterButtonAdded) return;
        filterButtonAdded = true;

        RectTransform carRect = carButton != null ? carButton.transform as RectTransform : null;
        if (carRect == null)
        {
            Debug.LogWarning("[出入园] 未接 carButton，「只看待审核」按钮建不出来");
            return;
        }

        RectTransform personRect = personButton != null ? personButton.transform as RectTransform : null;
        float step = personRect != null
            ? personRect.anchoredPosition.x - carRect.anchoredPosition.x
            : carRect.sizeDelta.x + 60f;
        Vector2 basePos = personRect != null ? personRect.anchoredPosition : carRect.anchoredPosition;

        GameObject go = new GameObject("OnlyPendingButton",
            typeof(RectTransform), typeof(Image), typeof(Button));

        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(carRect.parent, false);
        // 锚点/轴心/尺寸全抄现有按钮，父节点没有布局组，全靠这几项对齐
        rect.anchorMin = carRect.anchorMin;
        rect.anchorMax = carRect.anchorMax;
        rect.pivot = carRect.pivot;
        rect.sizeDelta = carRect.sizeDelta;
        rect.anchoredPosition = basePos + new Vector2(step, 0f);
        rect.localScale = Vector3.one;

        filterImage = go.GetComponent<Image>();
        filterImage.color = filterIdleColor;

        Button button = go.GetComponent<Button>();
        button.targetGraphic = filterImage;
        button.transition = Selectable.Transition.ColorTint;

        Navigation nav = button.navigation;
        nav.mode = Navigation.Mode.None;
        button.navigation = nav;

        button.onClick.AddListener(OnFilterButtonClicked);

        MakeFilterLabel(rect);
        RefreshFilterButton();
    }

    /// <summary>
    /// 按钮上的文字。字体必须从场景里现成的 TMP 抄过来——新建的 TextMeshProUGUI 用的是
    /// TMP 默认字体（LiberationSans），一个中文都没有。
    /// </summary>
    private void MakeFilterLabel(RectTransform parent)
    {
        GameObject go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));

        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;

        TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();

        if (titleText != null)
        {
            label.font = titleText.font;
            label.color = titleText.color;
            // 标题字号是按整块面板定的，套到 353 宽的按钮上会溢出，封一个上限
            label.fontSize = Mathf.Min(titleText.fontSize, 48f);
        }
        else
        {
            Debug.LogWarning("[出入园] 未接 titleText，「只看待审核」按钮会用 TMP 默认字体（不含中文）");
            label.fontSize = 44f;
        }

        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Overflow;
        label.raycastTarget = false;   // 文字挡住射线的话按钮就点不到了
        label.text = "只看待审核";
    }

    private void OnFilterButtonClicked()
    {
        onlyPending = !onlyPending;
        justHandled.Clear();
        RefreshFilterButton();
        UpdateRecordDisplay();
    }

    private void RefreshFilterButton()
    {
        if (filterImage == null) return;

        filterImage.color = onlyPending ? filterOnColor : filterIdleColor;
    }

    /// <summary>
    /// 审批结论变了。只在「只看待审核」开着时才记豁免——关着的时候列表本来就全显示，
    /// 攒这个名单没有意义，还会让下次开筛选时莫名多出一批已处置的行。
    /// </summary>
    private void OnReviewChanged(string key)
    {
        if (!onlyPending || string.IsNullOrEmpty(key)) return;
        if (EntryExitReviewStore.StateOf(key) == EntryExitReviewState.Pending) return;

        justHandled.Add(key);
    }

    /// <summary>这条记录在「只看待审核」下是否该显示。</summary>
    private bool PassesPendingFilter(string key)
    {
        if (!onlyPending) return true;
        if (string.IsNullOrEmpty(key)) return true;   // 取不到 key 的记录本来就审不了，别再藏起来

        return EntryExitReviewStore.StateOf(key) == EntryExitReviewState.Pending
            || justHandled.Contains(key);
    }

    /// <summary>懒实例化审核卡片，之后只 SetActive 复用（与罐区监测卡片同一套写法）。</summary>
    private bool EnsureReviewCard()
    {
        if (reviewCard != null) return true;

        if (reviewCardPrefab == null)
        {
            Debug.LogWarning("[出入园] 未接 reviewCardPrefab，审核卡片不会显示");
            return false;
        }

        // 卡片作为面板根的子物体加入，由面板已有的 HorizontalLayoutGroup 自动排到列表右侧
        reviewCard = Instantiate(reviewCardPrefab, transform);
        return true;
    }

    private void ShowReviewCard(object payload, bool focusRemark)
    {
        if (!EnsureReviewCard()) return;

        CarAccessRecord car = payload as CarAccessRecord;
        if (car != null)
        {
            reviewCard.ShowCar(car, focusRemark);
            return;
        }

        PersonAccessRecord person = payload as PersonAccessRecord;
        if (person != null)
        {
            reviewCard.ShowPerson(person, focusRemark);
        }
    }

    private void HideReviewCard()
    {
        if (reviewCard != null)
        {
            reviewCard.Hide();
        }
        else
        {
            // 卡片还没建出来时也要把选中态清掉，否则行高亮会一直挂着
            EntryExitReviewStore.Select(null);
        }
    }

    private void ShowCompanyObjects()
    {
        if (companyRoot != null) companyRoot.SetActive(true);
        if (companyObjects.Count == 0)
        {
            StartCoroutine(GetCompanyListCoroutine());
        }
    }

    private IEnumerator GetCompanyListCoroutine()
    {
        // 复用 Security_LicenseList 的逻辑
        string url = Api.SecurityCompanyList(0, 1000);
        yield return DataProvider.Instance.Get<Company>(url);
        var result = DataProvider.Instance.GetLastResult<Company>(url);
        
        if (result != null && result.success)
        {
            ResponseList<Company> response = result.data as ResponseList<Company>;
            if (response?.result?.data != null)
            {
                foreach (var company in response.result.data)
                {
                    CreateCompanyInstance(company);
                }
            }
        }
    }

    private void CreateCompanyInstance(Company company)
    {
        if (companyObjects.ContainsKey(company.id)) return;

        CesiumForUnity.CesiumGlobeAnchor instance = Instantiate(companyPrefab, companyRoot.transform);
        instance.longitudeLatitudeHeight = new Unity.Mathematics.double3(company.longitude, company.latitude, 100);
        
        CompanyObject companyObj = instance.GetComponent<CompanyObject>();
        if (companyObj != null)
        {
            companyObj.companyID = company.id;
            companyObj.companyDetails = company;
        }
        
        companyObjects[company.id] = companyObj;
        sysOrgCodeToID[company.sysOrgCode] = company.id;
    }

    private void HideRecordObjects()
    {
        Transform carParent = carRecordContainer != null ? carRecordContainer : 
                          (carRecordPrefab != null ? carRecordPrefab.transform.parent : null);
        if (carParent != null) carParent.gameObject.SetActive(false);

        Transform personParent = personRecordContainer != null ? personRecordContainer : 
                          (personRecordPrefab != null ? personRecordPrefab.transform.parent : null);
        if (personParent != null) personParent.gameObject.SetActive(false);

        if (carLabel != null) carLabel.gameObject.SetActive(false);
        if (personLabel != null) personLabel.gameObject.SetActive(false);

        HideReviewCard();
    }

    private void ShowRecordObjects()
    {
        Transform carParent = carRecordContainer != null ? carRecordContainer : 
                          (carRecordPrefab != null ? carRecordPrefab.transform.parent : null);
        
        Transform personParent = personRecordContainer != null ? personRecordContainer : 
                          (personRecordPrefab != null ? personRecordPrefab.transform.parent : null);

        if (carParent != null) carParent.gameObject.SetActive(isShowingCars);
        if (personParent != null) personParent.gameObject.SetActive(!isShowingCars);

        if (carLabel != null) carLabel.gameObject.SetActive(isShowingCars);
        if (personLabel != null) personLabel.gameObject.SetActive(!isShowingCars);
    }

    private IEnumerator GetCarListCoroutine()
    {
        string url = Api.ClosedParkCarInApplyList(1, 1000);

        // 必须走 OfflineMock.Fetch，不能直接 UnityWebRequest：
        // 直连会绕过离线镜像回退，没有 adb reverse 时这里永远是空列表。
        // 成功/失败/空集的日志由 Fetch 统一打，这里不再重复打整段 JSON（约 26KB）。
        var fetch = new OfflineMock.Result();
        yield return OfflineMock.Fetch(url, fetch);

        if (fetch.ok)
        {
            // 这两份镜像各 264KB / 59KB，走统一入口：超过阈值的丢后台线程解析，
            // 否则点 tab 时这笔开销会顶在同一帧上
            var parsed = new BackgroundJson.Result();
            yield return BackgroundJson.Parse(typeof(FlatCarResponse), fetch.json, parsed);
            FlatCarResponse response = parsed.value as FlatCarResponse;

            if (response != null && response.records != null)
            {
                allCarRecords = response.records;

                // 不再需要先把整份数据实例化成行——虚拟滚动只建一屏，SetData 就够了
                UpdateRecordDisplay();
            }
        }

        currentCoroutine = null;
    }

    private IEnumerator GetPersonListCoroutine()
    {
        string url = Api.ClosedParkPersonInApplyList(1, 1000);

        // 同上：必须走统一取数入口才能享受离线镜像回退
        var fetch = new OfflineMock.Result();
        yield return OfflineMock.Fetch(url, fetch);

        if (fetch.ok)
        {
            // 这两份镜像各 264KB / 59KB，走统一入口：超过阈值的丢后台线程解析，
            // 否则点 tab 时这笔开销会顶在同一帧上
            var parsed = new BackgroundJson.Result();
            yield return BackgroundJson.Parse(typeof(FlatPersonResponse), fetch.json, parsed);
            FlatPersonResponse response = parsed.value as FlatPersonResponse;

            if (response != null && response.records != null)
            {
                allPersonRecords = response.records;

                // 不再需要先把整份数据实例化成行——虚拟滚动只建一屏，SetData 就够了
                UpdateRecordDisplay();
            }
        }

        currentCoroutine = null;
    }

    private void OnCompanySelected()
    {
        if (Session.Instance.subSessionType != subSessionType) return;

        // 换了企业就是换了一批数据，上一批的豁免名单没有意义了
        justHandled.Clear();
        UpdateRecordDisplay();
    }

    private void UpdateRecordDisplay()
    {
        string selectedCompanyID = Session.Instance.companyID;
        
        // 先根据模式显示/隐藏对应的容器
        ShowRecordObjects();

        if (isShowingCars)
        {
            UpdateCarListDisplay(selectedCompanyID);
        }
        else
        {
            UpdatePersonListDisplay(selectedCompanyID);
        }
    }

    /// <summary>
    /// 按企业筛出车辆记录交给虚拟滚动列表。
    ///
    /// 过滤全在数据层做了——以前是「行全建出来，再逐行 SetActive」。没选企业时那套会
    /// 把整份数据一次性 SetActive(true)，全部行的 TMP mesh 挤在同一次 CanvasUpdate 里。
    /// </summary>
    private void UpdateCarListDisplay(string companyID)
    {
        IList<CarAccessRecord> visible;

        if (string.IsNullOrEmpty(companyID))
        {
            visible = allCarRecords;
        }
        else
        {
            visible = allCarRecords.FindAll(
                r => r != null && (r.ENTERPRISEID == companyID || r.CARSHENBQY == companyID));
        }

        // 审核状态过滤放在企业过滤【之后】：两者都在数据层做，行池只拿到最终要显示的那批
        if (onlyPending)
        {
            List<CarAccessRecord> pending = new List<CarAccessRecord>();
            for (int i = 0; i < visible.Count; i++)
            {
                if (PassesPendingFilter(EntryExitReviewStore.KeyFor(visible[i]))) pending.Add(visible[i]);
            }
            visible = pending;
        }

        EnsureCarRowPool();
        carRows.SetData(visible ?? NoCars);

        Debug.Log($"[车辆出入] 数据 {visible.Count} 条，行实例 {carRows.RowCount} 个");
    }

    private void UpdatePersonListDisplay(string companyID)
    {
        IList<PersonAccessRecord> visible;

        if (string.IsNullOrEmpty(companyID))
        {
            visible = allPersonRecords;
        }
        else
        {
            // PersonAccessRecord 也有 ENTERPRISEID
            visible = allPersonRecords.FindAll(r => r != null && r.ENTERPRISEID == companyID);
        }

        if (onlyPending)
        {
            List<PersonAccessRecord> pending = new List<PersonAccessRecord>();
            for (int i = 0; i < visible.Count; i++)
            {
                if (PassesPendingFilter(EntryExitReviewStore.KeyFor(visible[i]))) pending.Add(visible[i]);
            }
            visible = pending;
        }

        EnsurePersonRowPool();
        personRows.SetData(visible ?? NoPersons);

        Debug.Log($"[人员出入] 数据 {visible.Count} 条，行实例 {personRows.RowCount} 个");
    }

    private void EnsureCarRowPool()
    {
        if (carRows != null)
        {
            return;
        }

        if (onCarReview == null)
        {
            onCarReview = payload => ShowReviewCard(payload, false);
            onCarRemark = payload => ShowReviewCard(payload, true);
        }

        carRows = new VirtualScrollList<CarAccessRecord, CarAccessRecordObject>(
            carRecordPrefab,
            carRecordContainer,
            (row, record, index) =>
            {
                // 序号按数据下标算，滚到第 300 行显示的就是 300，不是池里的位置。
                row.index = index + 1;
                row.recordDetails = record;
                // 复用行必须每次重设，否则第 3 行会拿着第 40 行的那条记录去审核
                row.BindActions(actionCellPrefab, onCarReview, onCarRemark);
            });
    }

    /// <summary>
    /// 人员行池。
    ///
    /// 注意场景里的接线：车/人两个 Content 是同一个 Viewport 下的兄弟
    /// （Scroll View/Viewport/Content 和 Content (1)），而 ScrollRect.content 只指向
    /// 车那个。也就是说拖动只会移动车的 Content，人员这侧的 anchoredPosition 恒为 0，
    /// 于是只看得到第一屏、滚不动。
    ///
    /// 这是改虚拟滚动之前就有的接线问题：原先整份建行 + ContentSizeFitter，多出来的行
    /// 同样被 Viewport 裁掉且拖不动，可见结果一致，所以这里没有顺手改动。要修的话是在
    /// 车/人切换时把 ScrollRect.content 一起换过去。
    /// </summary>
    private void EnsurePersonRowPool()
    {
        if (personRows != null)
        {
            return;
        }

        if (onPersonReview == null)
        {
            onPersonReview = payload => ShowReviewCard(payload, false);
            onPersonRemark = payload => ShowReviewCard(payload, true);
        }

        personRows = new VirtualScrollList<PersonAccessRecord, PersonAccessRecordObject>(
            personRecordPrefab,
            personRecordContainer,
            (row, record, index) =>
            {
                row.index = index + 1;
                row.recordDetails = record;
                row.BindActions(actionCellPrefab, onPersonReview, onPersonRemark);
            });
    }

    // 内部类用于解析扁平 JSON
    [System.Serializable]
    private class FlatCarResponse
    {
        public bool success;
        public int code;
        public string message;
        public int total;
        public List<CarAccessRecord> records;
    }

    [System.Serializable]
    private class FlatPersonResponse
    {
        public bool success;
        public int code;
        public string message;
        public int total;
        public List<PersonAccessRecord> records;
    }
}
