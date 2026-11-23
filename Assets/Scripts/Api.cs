public static class Api
{
    /// <summary>
    /// 后端根地址。实际值来自 <see cref="ApiConfig"/>：设备上
    /// <c>persistentDataPath/api_config.json</c> 优先，其次随包的
    /// <c>Resources/Config/api_config.json</c>。现场换 IP 改那个 json 即可，不用重新出包。
    ///
    /// 本地调 mock：把 json 里的 baseUrl 改成 "http://127.0.0.1:8082/"，
    /// 再 adb reverse tcp:8082 tcp:8082 把端口转到开发机。
    /// </summary>
    public static string API_URL => ApiConfig.BaseUrl;

    #region 一、安全基础管理 - 园区基础信息管理
    /// <summary>企业列表</summary>
    public static string SecurityCompanyList(int pageNum = 1, int pageSize = 10)
    {
        return $"{API_URL}security/company/list?pageNum={pageNum}&pageSize={pageSize}";
    }

    /// <summary>企业危险化学品数量（待确认）</summary>
    public static string SecurityCompanyChemicalCount(string companyId)
    {
        return $"{API_URL}security/company/chemical/count?companyId={companyId}";
    }

    /// <summary>企业人员信息列表</summary>
    public static string SecurityCompanyEmployeeList(int pageNum = 1, int pageSize = 10)
    {
        return $"{API_URL}security/company/employee/list?pageNum={pageNum}&pageSize={pageSize}";
    }
    #endregion

    #region 一、安全基础管理 - 安全生产行政许可管理
    /// <summary>企业许可证照信息</summary>
    public static string SecurityCompanyLicensingList(int pageNum = 1, int pageSize = 10)
    {
        return $"{API_URL}security/company/licensing/list?pageNum={pageNum}&pageSize={pageSize}";
    }

    /// <summary>企业装置开停车列表</summary>
    public static string SecurityCompanyStateList(int pageNum = 1, int pageSize = 10)
    {
        return $"{API_URL}security/company/state/list?pageNum={pageNum}&pageSize={pageSize}";
    }
    #endregion

    #region 一、安全基础管理 - 装置开停车 & 大检修管理
    /// <summary>大检修备案列表</summary>
    public static string SecurityCompanyOverhaulList(int pageNum = 1, int pageSize = 10)
    {
        return $"{API_URL}security/company/overhaul/list?pageNum={pageNum}&pageSize={pageSize}";
    }
    #endregion

    #region 一、安全基础管理 - 第三方单位管理
    /// <summary>承包商信息列表</summary>
    public static string SpecialJobThirdPartyList(int pageNum = 1, int pageSize = 10)
    {
        return $"{API_URL}specialJob/thirdParty/list?pageNum={pageNum}&pageSize={pageSize}";
    }
    #endregion

    #region 一、安全基础管理 - 检查管理
    /// <summary>巡检列表</summary>
    public static string SecurityPatrolList(int pageNum = 1, int pageSize = 10)
    {
        return $"{API_URL}security/patrol/list?pageNum={pageNum}&pageSize={pageSize}";
    }

    /// <summary>巡检详情</summary>
    public static string SecurityPatrol(string sid)
    {
        return $"{API_URL}security/patrol?sid={sid}";
    }
    #endregion

    #region 二、重大危险源管理 - 监测预警
    /// <summary>罐区监测列表</summary>
    public static string MajorHazardTankList(int pageNum = 1, int pageSize = 10)
    {
        return $"{API_URL}majorHazard/tank/list?pageNum={pageNum}&pageSize={pageSize}";
    }

    /// <summary>
    /// 指定设备的最新监测值（液位 / 储量 / 温度 / 压力）。
    ///
    /// deviceIds 传 <see cref="Tank.equipCode"/>（tank/list 里的 provinceDeviceCode 恒为 null，用不了）。
    /// 响应的 body 是按设备编码键控的对象，不能按位置或 sid 取数，解析见 TankWarningData.ParseLatest。
    /// </summary>
    public static string ExternalDeviceLatestData(string deviceIds)
    {
        string escaped = UnityEngine.Networking.UnityWebRequest.EscapeURL(deviceIds);
        return $"{API_URL}api/external/derviceData/bestNewData?deviceIds={escaped}";
    }

    /// <summary>
    /// 罐区实时监测数据（SocketIO接口，暂不可用）。
    ///
    /// 注意：这里的 IP 是写死的，【没有】走 <see cref="ApiConfig"/>。它跟 API_URL 不同端口
    /// （80，不是 8082），当前又用不上，就没一并配置化。哪天这个接口启用了、现场又换了 IP，
    /// 记得把它也挪到 api_config.json，否则改了配置这条还是指向旧地址。
    /// </summary>
    public static string MajorHazardSocketIo(string token)
    {
        return $"http://192.168.3.20/socket.io/?channel=iot-data&token={token}";
    }
    #endregion

    #region 二、重大危险源管理 - 评估报告管理
    /// <summary>评估报告列表</summary>
    public static string MajorHazardAssessmentReportList(int pageNum = 1, int pageSize = 10)
    {
        return $"{API_URL}majorHazard/assessmentReport/list?pageNum={pageNum}&pageSize={pageSize}";
    }
    #endregion

    #region 二、重大危险源管理 - 重大风险管控（页面改版，待确认）
    /// <summary>企业重大危险源分布（待确认）</summary>
    public static string MajorHazardSourceList(string companyId, int pageNum = 1, int pageSize = 10)
    {
        return $"{API_URL}majorHazard/source/list?companyId={companyId}&pageNum={pageNum}&pageSize={pageSize}";
    }
    #endregion

    #region 三、双重预防机制 - 风险隐患管控
    /// <summary>隐患台账列表</summary>
    public static string DoubleDangerList(int pageNum = 1, int pageSize = 10)
    {
        return $"{API_URL}api/external/hiddenDangerReportTaskReview/find?checkTaskTimeStart=&checkTaskTimeEnd=&page={pageNum}&pageSize={pageSize}";
    }

    /// <summary>隐患整改分页列表</summary>
    public static string DoubleDangerChangeList(int pageNum = 1, int pageSize = 10)
    {
        return $"{API_URL}double/danger/change/list?pageNum={pageNum}&pageSize={pageSize}";
    }
    #endregion

    #region 三、双重预防机制 - 整改督办（待确认）
    /// <summary>按状态和企业查询隐患整改记录（POST JSON，token 请求头）。</summary>
    public static string DoubleDangerReviewFind()
    {
        return $"{ICC_URL}double-prevention/hiddenDangerReportTaskReview/find";
    }

    /// <summary>下达督办通知（POST JSON，token 请求头）。</summary>
    public static string DoubleDangerRemind()
    {
        return $"{ICC_URL}double-prevention/hiddenDangerReport/remind";
    }

    /// <summary>督办历史（POST JSON，token 请求头）。</summary>
    public static string DoubleDangerRemindFind()
    {
        return $"{ICC_URL}double-prevention/hiddenDangerReport/remind/find";
    }
    #endregion

    #region 三、双重预防机制 - 运行效果检查
    public static string PatrolFindByPage()
    {
        return $"{ICC_URL}enforce-client/patrol/findByPage";
    }

    public static string PatrolFindBySid()
    {
        return $"{ICC_URL}enforce-client/patrol/findBySid";
    }

    public static string PatrolAttachmentFind()
    {
        return $"{ICC_URL}enforce-client/attach/find";
    }

    public static string PatrolAttachmentDownload(string attachmentSid)
    {
        string escaped = UnityEngine.Networking.UnityWebRequest.EscapeURL(attachmentSid ?? string.Empty);
        return $"{ICC_URL}enforce-client/attach/downloadPreview?sid={escaped}";
    }

    #endregion

    #region 四、特殊作业 - 特殊作业审批流程
    /// <summary>作业票列表</summary>
    public static string SpecialJobTicketList(int pageNum = 1, int pageSize = 10)
    {
        return $"{API_URL}specialJob/jobTicket/list?pageNum={pageNum}&pageSize={pageSize}";
    }
    #endregion

    #region 四、特殊作业 - 企业证件管理
    /// <summary>
    /// 企业证件表列表。
    ///
    /// 【当前没有调用方】页面已经改走 <see cref="ISpecialJobCertSource"/>：这个接口全量只有
    /// 5 条、<c>personName</c> 里存的是人员 ID 而不是姓名、也没有持证人数口径，撑不起这一页。
    /// 地址留着是因为它仍是唯一已知的证件端点，后端补齐缺口 C1–C4 后大概率从这里扩。
    /// </summary>
    public static string SpecialJobCertificateTableList(int pageNum = 1, int pageSize = 10)
    {
        return $"{API_URL}specialJob/certificate/table/list?pageNum={pageNum}&pageSize={pageSize}";
    }
    #endregion

    #region 五、封闭园区管理 - 出入园管理（待确认）
    /// <summary>车辆入园申请列表</summary>
    public static string ClosedParkCarInApplyList(int pageNo = 1, int pageSize = 10)
    {
        return $"{API_URL}api/closedPark/carInApply/list?pageNo={pageNo}&pageSize={pageSize}";
    }

    /// <summary>人员入园申请列表</summary>
    public static string ClosedParkPersonInApplyList(int pageNo = 1, int pageSize = 10)
    {
        return $"{API_URL}api/closedPark/personInApply/list?pageNo={pageNo}&pageSize={pageSize}";
    }

    /// <summary>车辆出园申请列表</summary>
    public static string ClosedParkCarOutApplyList(int pageNo = 1, int pageSize = 10)
    {
        return $"{API_URL}api/closedPark/carOutApply/list?pageNo={pageNo}&pageSize={pageSize}";
    }

    /// <summary>人员出园申请列表</summary>
    public static string ClosedParkPersonOutApplyList(int pageNo = 1, int pageSize = 10)
    {
        return $"{API_URL}api/closedPark/personOutApply/list?pageNo={pageNo}&pageSize={pageSize}";
    }
    #endregion

    #region External APIs
    /// <summary>企业重大危险源负责人列表</summary>
    public static string CompanyHazardSourcesResponsiblePersonList(int pageNo = 1, int pageSize = 10)
    {
        return $"{API_URL}api/external/companyHazardSourcesResponsiblePerson/list?pageNo={pageNo}&pageSize={pageSize}";
    }

    /// <summary>获取被指派人信息列表</summary>
    public static string GetReportPersonList()
    {
        return $"{API_URL}api/external/companyEmployeeInfo/getReportPerson";
    }
    #endregion

    #region 六、封闭园区管理 - 周界入侵告警（ICC）
    // 这套是公网直连，不走 adb reverse，所以不能挂在 API_URL 上。
    public const string ICC_URL = "https://www.nsxhcip.cn/";

    /// <summary>登录取 Token（POST multipart 表单）</summary>
    public static string IccLogin()
    {
        return $"{ICC_URL}portal/login/tel/sms";
    }

    /// <summary>告警明细查询（POST JSON，需要 Token 头）</summary>
    public static string IccDataEventDetailFind()
    {
        return $"{ICC_URL}icc/dataEventDetail/find";
    }
    #endregion

    #region 七、特殊作业 - 作业票（门户 iflow-task，需 token 头）
    // 与 ICC 同源：同一个 ICC_URL、同一个 token。作业票这套在门户后端，
    // 和跳板的 specialJob/jobTicket/list 不是一回事 —— 那边是镜像，没有流程能力。

    /// <summary>
    /// 待办作业票（POST JSON，需 token 头）。
    ///
    /// 请求体 <c>{"body":{"job":{…筛选条件…},"operateType":"audit"},"page":1,"pageSize":n}</c>。
    /// <c>operateType</c> 三种：<c>audit</c> 待审批、<c>record</c> 待记录、<c>acceptance</c> 待验收。
    ///
    /// 【它是「我的待办」，不是「这家企业的票」】待办按登录账号的岗位分派，
    /// 与沙盘选中哪家企业无关。实测 2026-09-22：xhdAdmin 的 2 条待办都挂在
    /// 政府单位（广州市南沙区化工应急管理中心）名下，不在园区任何一家企业下 ——
    /// 拿选中企业的编码去筛待办会恒定得到 0 条。所以列表主体用
    /// <see cref="JobTicketFind"/>（台账，按企业筛得准），待办只用来标记哪几张能审批。
    /// </summary>
    public static string JobTicketTodo()
    {
        return $"{ICC_URL}iflow-task/task/jobTicket/todo";
    }

    /// <summary>
    /// 作业票台账（POST JSON，需 token 头）。请求体同 <see cref="JobTicketTodo"/>，
    /// 但没有 <c>operateType</c>。按 <c>applyingUnitId</c> 筛企业实测精确可靠。
    /// </summary>
    public static string JobTicketFind()
    {
        return $"{ICC_URL}iflow-task/task/jobTicket/find";
    }

    /// <summary>
    /// 票面第 1 步：流程头（GET，需 token 头）。
    ///
    /// 用 <c>view</c> 而不是文档里的 <c>handle</c> —— 两者都能取到票面，但 handle 是
    /// 「进入办理」的语义，只读查看不该在服务端留办理痕迹。
    ///
    /// 返回的是 <c>body.tasks</c>【数组】，不是文档写的 <c>body.task</c> 单数。
    /// </summary>
    /// <param name="flowKey">取作业票行的 <c>processDefinitionKey</c>，不要自己按类型查表。</param>
    public static string FlowView(string flowKey, string sid)
    {
        return $"{ICC_URL}iflow-task/flow/view/{flowKey}/{sid}";
    }

    /// <summary>
    /// 票面第 2 步：段定义 + 表单定义（POST，需 token 头）。
    ///
    /// **请求体是裸字符串 formKey**（文档没写）。传 <c>{"sid":…}</c> 之类的对象一律返回
    /// <c>{"success":false,"code":"700","message":"JSON转换异常"}</c>。
    /// </summary>
    public static string FormBaseInfoDetail()
    {
        return $"{ICC_URL}idaf-server/formBaseInfo/detail";
    }

    /// <summary>
    /// 票面第 3 步：取某一段的数据（POST，需 token 头）。
    ///
    /// 两个坑：<c>params</c> 入参是**字符串化的 JSON**；响应的 <c>body</c> 也是 JSON 字符串，
    /// 要再 parse 一次。文档里那个写成 base64 常量的 <c>key</c> 实测可以不传。
    /// </summary>
    public static string FormDataGet()
    {
        return $"{ICC_URL}idaf-server/formData/get";
    }

    /// <summary>
    /// 审批提交第 1 步：把审批结果和意见写进表单段（POST，需 token 头）。
    ///
    /// 【路径两段都和接口文档不一样，以服务端 swagger 为准】
    /// 文档写的是 <c>saveall/{formKey}/{key}</c>，服务端实际声明是
    /// <c>saveall/{formId}/{key}</c>，且 <c>formId</c> 的类型是 <b>integer</b>。
    /// 取值就是 <c>flow/view</c> 里当前 task 的 <c>formKey</c>（表单 sid，雪花号）。
    ///
    /// <paramref name="authorizeKey"/> 即文档里那个「写死的 base64 常量」。它的真身是
    /// **表单授权 key**：表单自己的 <c>formDefine.formParam</c> 里第三项就叫
    /// <c>key</c> / 标题「表单授权key」，同一个 <c>{formId}/{key}</c> 组合也出现在
    /// <c>formAuthorize/loadPromis</c>、<c>deleteByPromisKey</c> 上，对应实体
    /// <c>FormPromis.promisKey</c>。实测整套授权数据是空的（<c>FormScene</c> 全表 0 行，
    /// <c>loadPromis</c> 对任意 key 都回 <c>{}</c>），所以它当前不构成约束。
    /// </summary>
    public static string FormDataSaveAll(string formId, string authorizeKey)
    {
        return $"{ICC_URL}idaf-server/formData/saveall/{formId}/{authorizeKey}";
    }

    /// <summary>
    /// 审批提交第 2 步：推进工作流（POST，需 token 头）。
    ///
    /// **必须在 <see cref="FormDataSaveAll"/> 之后**。顺序颠倒的话流程推过去了，
    /// 而审批结果还没落库。
    /// </summary>
    public static string FlowHandle()
    {
        return $"{ICC_URL}iflow-task/flow/handle";
    }
    #endregion

    #region 二、重大危险源管理 - 罐区实时监测（公网直连，需 Token 头）
    // 与周界入侵告警同源：同一个 ICC_URL、同一个 Token，所以不挂在 API_URL 上，
    // 也不能走 DataProvider（那边不支持 POST，也不支持自定义头）。

    /// <summary>
    /// 储罐基础信息。deviceCode 取 <see cref="Tank.equipCode"/>
    /// （provinceDeviceCode 在 tank/list 里恒为 null，不能用）。
    /// </summary>
    public static string TankDetail(string deviceCode)
    {
        return $"{ICC_URL}jeecg/ext/dataDangerDeviceInfo/tankDetail?deviceCode={deviceCode}";
    }

    /// <summary>储罐实时/历史监测数据（POST JSON，需要 Token 头）</summary>
    public static string DeviceDataFindAll()
    {
        return $"{ICC_URL}aiot-admin/api/derviceData/findAll";
    }

    /// <summary>传感器报警阈值（POST JSON，需要 Token 头，每个监测参数单独查一次）</summary>
    public static string DataSensorDetail()
    {
        return $"{ICC_URL}jeecg/ext/dataSensor/detail";
    }

    /// <summary>
    /// 风险标注点位（POST，一次返回全量，无需分页）。
    ///
    /// 这是目前唯一能按坐标拿到重大危险源/罐体点位的接口。tank/list 里也有经纬度，
    /// 但那份数据是坏的（264 条里 111 条字面值就是 "longitude"/"latitude"、79 条是 "1"），
    /// 所以危险源图标一律用这里的 geometry，不要回头去 join tank/list。
    ///
    /// 三个实测出来、写错任何一个都拿不到数据的点：
    ///   1. coordinateType=84 指 WGS84，Cesium 要的就是这个，别改；
    ///   2. token 放 query 或请求头都认，这里用 query（不用给 OfflineMock 传 headers）；
    ///   3. 【必须带 Content-Type: application/json 和一段 JSON 正文】。正文为空时
    ///      服务端直接回 {"status":"error","message":"系统异常"}，不是 401 也不是空集，
    ///      光看响应根本看不出是请求形态错了。
    ///
    /// 还要注意它是【分页】的：正文不给 page/pageSize 时只回 10 条，而 body.total 照样
    /// 是全量数（实测 871）。差点就把 10 条当成全部了。
    ///
    /// 响应壳是 double-prevention 那套 {status, message, body:{data, total}}。
    /// </summary>
    public static string RiskTagRecordFind(string token)
    {
        string escaped = UnityEngine.Networking.UnityWebRequest.EscapeURL(token ?? string.Empty);
        return $"{ICC_URL}double-prevention/riskTagRecord/find?token={escaped}&coordinateType=84";
    }
    #endregion
}
