#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 门户（<c>www.nsxhcip.cn</c>）特殊作业链路的**只读**连通性自检。
///
///   Unity.exe -batchmode -nographics -projectPath &lt;repo&gt;/sandtable \
///     -executeMethod PortalSmokeChecks.Run -quit -logFile -
///
/// 【为什么要有这个东西】审批链路要跨 iflow-task + idaf-server 两套系统调四五个接口，
/// 每个接口的 Content-Type 还不一样（见文档 §1.3）。如果等做完 UI 再出包上头显试，
/// 一轮反馈要三分钟出包 + 戴眼镜，而九成的坑（鉴权、响应壳、双重序列化、字段名）
/// 跟渲染毫无关系。先在这里把协议跑通，后面写 UI 才有地基。
///
/// 【为什么不用 UnityWebRequest】batchmode 下没有跑 Unity 的更新循环，
/// UnityWebRequest 的异步操作没人推进，主线程 busy-wait 会直接卡死。
/// 这里走纯 .NET HttpClient 同步调用，不依赖任何 Unity 生命周期。
/// 代价是没覆盖运行时那条 UnityWebRequest 代码路径 —— 这个自检验的是**协议**，
/// 不是客户端实现，两者的边界要分清。
///
/// 【只读】审批提交（saveall / flow/handle POST）会推动真实的作业票流程，客户端撤不回来。
/// 所以本脚本一律不发写请求，并且用 <see cref="ForbiddenFragments"/> 在发送前拦一道，
/// 靠代码挡而不是靠自觉。打开票也走只读的 <c>flow/view</c>，而不是会占用工作流任务的 <c>flow/handle</c>。
///
/// 【文档与实际不符的地方】以下都是跑通后按实际改的，改文档之前不要照文档写代码：
///   · <c>flow/view</c> 返回 <c>body.tasks</c>【数组】，文档写的是 <c>body.task</c> 单数；
///     当前环节靠 <c>endTime == null</c> 认，<c>active</c> 在 view 模式下恒为 false。
///   · <c>formBaseInfo/detail</c> 的请求体是【裸字符串 formKey】，文档没写；
///     传 <c>{"sid":…}</c> 之类的对象一律回「JSON转换异常」。
///   · <c>formData/get</c> 的 <c>params.key</c> 【可以不传】，文档里那个占位的 base64 常量非必需。
///   · <c>xhd_tszylx</c> / <c>xhd_tszyzt</c> 的 <c>dictKey</c> 与 <c>dictValue</c> 是同一个中文串，
///     所以不存在「传 key 还是传中文」的分歧；返回里混着一条字典自描述行，要剔掉。
///     **但字典的写法和作业票列表返回的写法有 4 类对不上**（受限空间 / 断路作业 /
///     动土作业 / 盲板抽堵），拿字典值去筛选会查出 0 条。一律以列表返回的写法为准，
///     对照表在 <c>PortalJobTicketCatalog</c>。
///   · 附件段的 <c>name</c> 会随表单版本变（同一环节见过 <c>_wj</c> 和 <c>_file</c> 两种后缀），
///     只能按 <c>title</c> 的「文件_」「图片_」前缀认。
///
/// 【凭据】账号密码不进仓库、不进日志。读仓库外的一个 JSON：
///   环境变量 PORTAL_SMOKE_CONFIG 指定的路径，缺省 %USERPROFILE%\.xhd\portal_smoke.json
///   { "username": "...", "pwd": "&lt;Base64(MD5hex(明文))&gt;", "companyKeyword": "小虎岛" }
/// pwd 那一串用 <see cref="MakePwd"/> 生成（读环境变量 PORTAL_SMOKE_PASSWORD，明文不落盘）。
/// </summary>
public static class PortalSmokeChecks
{
    private const string ConfigEnv = "PORTAL_SMOKE_CONFIG";
    private const string PasswordEnv = "PORTAL_SMOKE_PASSWORD";

    /// <summary>登录接口的固定参数，移动端写死的值，文档 §1.1。</summary>
    private const string DefaultApiKey = "a7d96edc-0fb9-4b23-9190-c3fdc8ad3c6f";

    /// <summary>
    /// 写接口片段。命中即抛，不发出去。
    ///
    /// <c>iflow-task/flow/handle</c> 单独在 <see cref="Guard"/> 里只拦 POST：
    /// 同名的 GET 是「打开票」，但它会占用工作流任务，本脚本也不用它（用 flow/view）。
    /// </summary>
    private static readonly string[] ForbiddenFragments =
    {
        "formData/saveall",
        "attach/upload",
        "updateBatchFile",
        "hiddenDangerReport/remind",
    };

    private static HttpClient http;
    private static string token;
    private static readonly List<Step> steps = new List<Step>();

    private class Step
    {
        public string name;
        public string state;     // 通过 / 失败 / 跳过 / 待后端
        public string detail;
    }

    private class SmokeConfig
    {
        public string username;
        public string pwd;
        public string apiKey;
        public string companyKeyword;
    }

    // ------------------------------------------------------------------ 入口

    [MenuItem("Tools/门户联调/接口连通性自检")]
    public static void RunFromMenu()
    {
        Run();
    }

    public static void Run()
    {
        steps.Clear();
        token = null;

        // 纯逻辑校验放最前面：不需要网络也不需要凭据，先跑完再去连服务器
        CheckEnvelope();

        http = new HttpClient();
        http.Timeout = TimeSpan.FromSeconds(25);

        try
        {
            SmokeConfig config = LoadConfig();
            if (config == null)
            {
                Report();
                Finish(false);
                return;
            }

            if (!Login(config)) { Report(); Finish(false); return; }
            if (!IsAuthed()) { Report(); Finish(false); return; }

            string companySid = OrganTree(config.companyKeyword);
            List<JObject> workTypes = LoadDict("xhd_tszylx", "作业类型");
            LoadDict("xhd_tszyzt", "作业状态");

            JObject firstTicket = TodoList(companySid);
            LedgerList(companySid);
            CheckVocabulary(workTypes);

            // 待办为空时不能就此收工：票面渲染链路和待办分派是两件事，
            // 从台账里挑票照样能验协议。两张票各验一半：
            //   审批中的票 —— 当前环节识别、服务端下发的按钮；
            //   已结束的票 —— 审批段的字段名（审批中的票那几段还是空的，看不到字段）。
            OpenTicket(firstTicket ?? SampleFromLedger("作业负责人审批"), "审批中");
            OpenTicket(SampleFromLedger("结束"), "已结束");

            CheckFormDefinition();
        }
        catch (Exception e)
        {
            Add("未捕获异常", "失败", e.Message);
        }
        finally
        {
            if (http != null) { http.Dispose(); http = null; }
            token = null;   // 不留在静态字段里
        }

        Report();
        Finish(steps.All(s => s.state != "失败"));
    }

    /// <summary>
    /// 把明文密码算成登录要用的 <c>Base64(MD5hex)</c>。
    ///
    /// 明文只从环境变量 PORTAL_SMOKE_PASSWORD 读，不落盘、不回显，只打印算好的结果：
    ///   $env:PORTAL_SMOKE_PASSWORD='明文'; Unity.exe … -executeMethod PortalSmokeChecks.MakePwd …
    /// </summary>
    [MenuItem("Tools/门户联调/生成密码串")]
    public static void MakePwd()
    {
        string plain = Environment.GetEnvironmentVariable(PasswordEnv);
        if (string.IsNullOrEmpty(plain))
        {
            Debug.LogError("[门户自检] 没读到环境变量 " + PasswordEnv + "，无法生成");
            return;
        }

        Debug.Log("[门户自检] pwd = " + EncodePwd(plain) +
                  "\n把这一串填进 portal_smoke.json 的 pwd 字段。明文不要写进任何文件。");
    }

    /// <summary>
    /// 先取明文的 MD5 十六进制小写字符串，再对这个**字符串本身**（不是它的字节摘要）
    /// 做 Base64。顺序不能错，文档 §1.1。
    /// </summary>
    private static string EncodePwd(string plain)
    {
        using (MD5 md5 = MD5.Create())
        {
            byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(plain));
            string hex = string.Concat(hash.Select(b => b.ToString("x2")));
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(hex));
        }
    }

    // ------------------------------------------------------------------ 响应壳（离线）

    /// <summary>
    /// <see cref="PortalEnvelope"/> 的纯逻辑校验，样本全部取自实测到的真实响应。
    ///
    /// 不连网，所以没有凭据、服务器挂了也能跑。盯的是三件会静默出错的事：
    /// HTTP 200 但业务失败、body 的双重序列化、total 缺失与 0 的区别。
    /// </summary>
    private static void CheckEnvelope()
    {
        int failed = 0;
        Action<bool, string> expect = (ok, what) =>
        {
            if (ok) return;
            failed++;
            Add("响应壳 " + what, "失败", "断言不成立");
        };

        PortalEnvelope env;
        string error;

        // 1. iflow-task 列表的成功响应
        expect(PortalEnvelope.TryParse(
            "{\"success\":true,\"code\":null,\"message\":\"操作成功\"," +
            "\"body\":[{\"sid\":\"1\"}],\"total\":246}", out env, out error), "列表响应能解析");
        expect(env != null && env.Success, "列表响应 Success=true");
        expect(env != null && env.Total == 246, "total 取自壳而不是 body");
        expect(env != null && env.BodyArray().Count == 1, "BodyArray 取到 1 条");

        // 2. HTTP 200 但业务失败 —— 这是最容易被当成功的一种
        expect(PortalEnvelope.TryParse(
            "{\"success\":false,\"code\":\"700\",\"message\":\"JSON转换异常\",\"body\":null}",
            out env, out error), "业务失败响应仍能解析出壳");
        expect(env != null && !env.Success, "业务失败必须 Success=false");
        expect(env != null && env.Code == "700", "失败码要带出来");
        expect(env != null && env.Message == "JSON转换异常", "服务端提示要原样带出来");
        expect(env != null && env.UnwrapBody() == null, "body 为 null 时 UnwrapBody 给 null");

        // 3. idaf-server formData/get：body 是一个 JSON 字符串，要再 parse 一次
        expect(PortalEnvelope.TryParse(
            "{\"success\":true,\"message\":null," +
            "\"body\":\"{\\\"APPROVAL_RESULT\\\":\\\"1\\\",\\\"APPROVAL_OPINION\\\":null}\"}",
            out env, out error), "双重序列化响应能解析");
        JObject section = env == null ? null : env.BodyObject();
        expect(section != null, "body 字符串要再 parse 成对象");
        expect(section != null && (string)section["APPROVAL_RESULT"] == "1", "取得到 APPROVAL_RESULT");
        expect(section != null && section.Property("APPROVAL_OPINION") != null, "空值字段也在");

        // 4. 缺 total 要能和「确实是 0 条」区分开
        expect(PortalEnvelope.TryParse("{\"success\":true,\"body\":[]}", out env, out error),
            "缺 total 的响应能解析");
        expect(env != null && env.Total == -1, "缺 total 时给 -1，不是 0");

        // 5. 不是这套壳的东西必须被拒，不能当成功
        //    登录响应就没有 success 字段，误当成功会一路错下去
        expect(!PortalEnvelope.TryParse("{\"token\":\"abc\",\"principal\":{}}", out env, out error),
            "没有 success 字段要拒绝");
        expect(error != null && error.Contains("success"), "拒绝理由要说清楚");
        expect(!PortalEnvelope.TryParse("<html>502 Bad Gateway</html>", out env, out error),
            "非 JSON 要拒绝");
        expect(!PortalEnvelope.TryParse("", out env, out error), "空响应要拒绝");

        // 6. 入参侧的同一个坑：params 要的是字符串化的 JSON
        JObject p = new JObject();
        p["form.id"] = "1815319581666611200";
        expect(PortalEnvelope.EmbedJson(p) == "{\"form.id\":\"1815319581666611200\"}",
            "EmbedJson 产出紧凑字符串");

        if (failed == 0)
        {
            Add("响应壳解析", "通过", "19 条断言全过（样本取自实测响应）");
        }
    }

    // ------------------------------------------------------------------ 配置

    private static SmokeConfig LoadConfig()
    {
        string path = Environment.GetEnvironmentVariable(ConfigEnv);
        if (string.IsNullOrEmpty(path))
        {
            path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".xhd", "portal_smoke.json");
        }

        if (!File.Exists(path))
        {
            Add("读取凭据", "失败",
                "找不到 " + path + "。新建这个文件（仓库外），内容：" +
                "{\"username\":\"<监管端账号>\",\"pwd\":\"<Base64(MD5hex(明文))>\",\"companyKeyword\":\"小虎岛\"}");
            return null;
        }

        try
        {
            JObject json = JObject.Parse(File.ReadAllText(path));
            SmokeConfig config = new SmokeConfig();
            config.username = (string)json["username"];
            config.pwd = (string)json["pwd"];
            config.apiKey = (string)json["apiKey"] ?? DefaultApiKey;
            config.companyKeyword = (string)json["companyKeyword"] ?? string.Empty;

            if (string.IsNullOrEmpty(config.username) || string.IsNullOrEmpty(config.pwd))
            {
                Add("读取凭据", "失败", path + " 里 username 或 pwd 是空的");
                return null;
            }

            // 账号名可以打（排查用），密码只打长度
            Add("读取凭据", "通过",
                "账号 " + config.username + "，pwd 串 " + config.pwd.Length + " 字符，来源 " + path);
            return config;
        }
        catch (Exception e)
        {
            Add("读取凭据", "失败", path + " 解析失败：" + e.Message);
            return null;
        }
    }

    // ------------------------------------------------------------------ 各步

    /// <summary>
    /// 文档 §1.1 写着 captcha 固定传 123，而工程里的 <c>IccAuth</c> 没传也能登录。
    /// 先按文档传，失败再去掉重试一次，把哪种能通记进报告 —— 换账号时这是第一个要看的。
    /// </summary>
    private static bool Login(SmokeConfig config)
    {
        bool[] attempts = { true, false };
        foreach (bool withCaptcha in attempts)
        {
            Dictionary<string, string> form = new Dictionary<string, string>();
            form["username"] = config.username;
            form["pwd"] = config.pwd;
            form["apiKey"] = config.apiKey;
            if (withCaptcha) form["captcha"] = "123";

            string label = withCaptcha ? "带 captcha=123" : "不带 captcha";
            try
            {
                JObject res = JObject.Parse(PostMultipart(Api.IccLogin(), form));
                string got = (string)res["token"];
                if (string.IsNullOrEmpty(got))
                {
                    if (!withCaptcha)
                    {
                        Add("登录换 token", "失败",
                            "两种传法都没拿到 token。message = " + (string)res["message"]);
                        return false;
                    }
                    continue;
                }

                token = got;

                JObject principal = res["principal"] as JObject;
                string who = principal == null
                    ? "响应里没有 principal"
                    : (string)principal["realName"] + "（" + (string)principal["orgName"] + "）" +
                      "，角色 " + principal["userRoles"] +
                      "，topOrgCode " + (principal["attr"] == null ? null : principal["attr"]["topOrgCode"]);

                // token 本体绝不打日志，只打长度
                Add("登录换 token", "通过", label + " 成功，token 长度 " + token.Length + "；" + who);

                // 文档 §2 坑一：后端 UserUtil.getPostId() 取 role[0]，
                // 审批岗位不在第一位会静默失败 —— 把顺序原样打出来给后端核对（A5）
                JArray roles = principal == null ? null : principal["userRoles"] as JArray;
                if (roles != null && roles.Count > 1)
                {
                    Add("A5 角色顺序", "待后端",
                        "账号挂了 " + roles.Count + " 个角色，第 0 个是 " + roles[0] +
                        "。工作流只认第 0 个角色，要后端确认审批岗位是不是它。");
                }
                return true;
            }
            catch (Exception e)
            {
                if (!withCaptcha)
                {
                    Add("登录换 token", "失败", "两种传法都失败。最后一次：" + e.Message);
                    return false;
                }
            }
        }
        return false;
    }

    private static bool IsAuthed()
    {
        try
        {
            JObject res = JObject.Parse(
                Post(Api.ICC_URL + "tacs/isAuthed", string.Empty, "application/json"));
            bool ok = (bool?)res["success"] ?? false;
            Add("校验 token", ok ? "通过" : "失败",
                "success=" + ok + "，message=" + (string)res["message"]);
            return ok;
        }
        catch (Exception e)
        {
            Add("校验 token", "失败", e.Message);
            return false;
        }
    }

    /// <summary>沙盘企业目录镜像，用来核对门户企业 sid 和跳板的 sysOrgCode 是不是同一套编码。</summary>
    private const string CompanyMirror = "Assets/StreamingAssets/mock/security_company_list.json";

    /// <summary>
    /// 组织机构树。请求体是裸字符串（文档 §3.1），实测裸 <c>-1</c> 和 <c>"-1"</c> 都通，
    /// 不是坑，所以只用裸的那种。
    ///
    /// 企业不在根节点上，要下钻两层：根 → 小虎岛化工园区 → 企业单位 → 52 家企业。
    /// </summary>
    private static string OrganTree(string keyword)
    {
        JArray roots = Children("-1");
        if (roots == null)
        {
            Add("组织机构树", "失败", "根节点取不到数据");
            return null;
        }

        Add("组织机构树 根", "通过",
            roots.Count + " 个：" + string.Join("、",
                roots.Select(n => (string)n["name"]).Where(n => n != null)));

        // 企业藏在「<园区> / 企业单位」下面，两层都按名字找，找不到就把这一层的名字打出来
        JToken park = roots.FirstOrDefault(n => ((string)n["name"] ?? string.Empty).Contains("园区"));
        if (park == null)
        {
            Add("组织机构树 企业层", "失败", "根节点里没有「园区」节点，下钻路径要重新确认");
            return null;
        }

        JArray level2 = Children((string)park["sid"]);
        JToken unitNode = level2 == null
            ? null
            : level2.FirstOrDefault(n => ((string)n["name"] ?? string.Empty).Contains("企业单位"));
        if (unitNode == null)
        {
            Add("组织机构树 企业层", "失败",
                "「" + (string)park["name"] + "」下没有「企业单位」节点，实际有：" +
                (level2 == null ? "(取不到)" : string.Join("、",
                    level2.Select(n => (string)n["name"]))));
            return null;
        }

        JArray companies = Children((string)unitNode["sid"]);
        if (companies == null || companies.Count == 0)
        {
            Add("组织机构树 企业层", "失败", "「企业单位」下一家企业都没有");
            return null;
        }

        Add("组织机构树 企业层", "通过", "企业单位下 " + companies.Count + " 家企业");
        CheckCompanyCodesMatchSite(companies);

        if (string.IsNullOrEmpty(keyword))
        {
            Add("定位企业", "跳过", "配置里没填 companyKeyword，后面按全园区查");
            return null;
        }

        JToken hit = companies.FirstOrDefault(
            n => ((string)n["name"] ?? string.Empty).Contains(keyword));
        if (hit == null)
        {
            Add("定位企业", "失败", "企业层里没有名字含「" + keyword + "」的企业");
            return null;
        }

        Add("定位企业", "通过", (string)hit["name"] + " → sid " + (string)hit["sid"]);
        return (string)hit["sid"];
    }

    private static JArray Children(string parentSid)
    {
        try
        {
            JObject res = JObject.Parse(Post(
                Api.ICC_URL + "portal/system/sys/organ/children", parentSid, "application/json;"));
            return res["body"] as JArray;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[门户自检] organ/children(" + parentSid + ") 失败：" + e.Message);
            return null;
        }
    }

    /// <summary>
    /// 核对门户企业 sid 与沙盘企业目录的 sysOrgCode 是不是同一套编码。
    ///
    /// 这条决定了「按企业筛作业票」要不要额外建一张映射表：同源就能直接把沙盘选中的
    /// 企业编码当 <c>applyingUnitId</c> 用；不同源则每加一家企业都要维护映射，
    /// 而映射漏了的表现是「这家企业永远 0 条待办」，排查起来很贵。
    /// </summary>
    private static void CheckCompanyCodesMatchSite(JArray companies)
    {
        string path = Path.Combine(Directory.GetCurrentDirectory(), CompanyMirror);
        if (!File.Exists(path))
        {
            Add("企业编码同源", "跳过", "找不到企业目录镜像 " + CompanyMirror);
            return;
        }

        HashSet<string> siteCodes = new HashSet<string>(
            System.Text.RegularExpressions.Regex
                .Matches(File.ReadAllText(path), "\"sysOrgCode\"\\s*:\\s*\"([^\"]+)\"")
                .Cast<System.Text.RegularExpressions.Match>()
                .Select(m => m.Groups[1].Value),
            StringComparer.Ordinal);

        HashSet<string> portalSids = new HashSet<string>(
            companies.Select(n => (string)n["sid"]).Where(s => s != null), StringComparer.Ordinal);

        int both = siteCodes.Count(c => portalSids.Contains(c));
        int siteOnly = siteCodes.Count - both;

        Add("企业编码同源", siteOnly == 0 ? "通过" : "失败",
            "跳板 " + siteCodes.Count + " 家 / 门户 " + portalSids.Count + " 家，交集 " + both +
            (siteOnly == 0
                ? "　→ 同一套编码，applyingUnitId 可以直接用沙盘的 sysOrgCode，不需要映射表"
                : "　→ 有 " + siteOnly + " 家只在跳板侧，这些企业查不到作业票，需要映射表"));
    }

    /// <summary>
    /// 取字典并剔除自描述行。
    ///
    /// 实测这两个字典的返回里都混着一条 <c>dictKey == 字典编码本身</c> 的行
    /// （如 <c>xhd_tszylx = 小虎岛-特殊作业-特殊作业类型</c>），那是字典自己的定义，
    /// 不是可选项。直接渲染成下拉框会多出一条永远查不到数据的垃圾项。
    /// </summary>
    private static List<JObject> LoadDict(string code, string label)
    {
        try
        {
            JObject res = JObject.Parse(
                Get(Api.ICC_URL + "portal/system/basic/dict/load/" + code));
            JArray body = res["body"] as JArray;
            List<JObject> all = body == null
                ? new List<JObject>()
                : body.OfType<JObject>().ToList();

            List<JObject> items = all
                .Where(i => !string.Equals((string)i["dictKey"], code, StringComparison.Ordinal))
                .ToList();

            if (all.Count == 0)
            {
                Add("字典 " + label, "失败", code + " 一条都没返回");
                return items;
            }

            Add("字典 " + label, "通过",
                "可选项 " + items.Count + " 条（已剔除 " + (all.Count - items.Count) +
                " 条自描述行）：" + string.Join("、", items.Select(i => (string)i["dictValue"])));
            return items;
        }
        catch (Exception e)
        {
            Add("字典 " + label, "失败", e.Message);
            return new List<JObject>();
        }
    }

    /// <summary>待办（审批）列表。返回第一条，供后面打开票用。</summary>
    private static JObject TodoList(string companySid)
    {
        JObject all = QueryTickets("todo", null, null, null);
        int allTotal = all == null ? -1 : ((int?)all["total"] ?? -1);

        // 全园区 vs 单企业：监管账号如果数据权限不对，这两个数会一样大、或者都为 0
        Add("待办列表（全园区）", allTotal >= 0 ? "通过" : "失败",
            allTotal >= 0 ? "operateType=audit，total=" + allTotal : "请求失败");

        JArray rows = all == null ? null : all["body"] as JArray;

        if (!string.IsNullOrEmpty(companySid))
        {
            JObject one = QueryTickets("todo", companySid, null, null);
            int oneTotal = one == null ? -1 : ((int?)one["total"] ?? -1);
            string note = oneTotal >= 0
                ? "applyingUnitId=" + companySid + "，total=" + oneTotal +
                  (oneTotal == allTotal && allTotal > 0
                      ? "　⚠ 和全园区一样多，applyingUnitId 可能没被服务端采纳"
                      : string.Empty)
                : "请求失败";
            Add("待办列表（按企业）", oneTotal >= 0 ? "通过" : "失败", note);

            JArray r = one == null ? null : one["body"] as JArray;
            if (r != null && r.Count > 0) rows = r;
        }

        if (rows == null || rows.Count == 0)
        {
            // 这是审批功能最硬的一道坎：接口通、台账有几百条，但这个账号一条待办都没有。
            // 待办按岗位（postId = 用户第 0 个角色）分派，账号不在审批岗位上就永远是 0，
            // 而且 total=0 不是错误响应，从客户端看不出区别。
            Add("待办为空", "失败",
                "audit 待办 0 条。接口本身是通的（台账能查到几百条），说明这个账号没有被分派审批任务。" +
                "要后端把测试票分派到该账号的审批岗位，或换一个在审批岗位上的账号，否则审批链路无法端到端验证。");
            return null;
        }

        // 待办挂在哪个单位名下 —— 这条决定了列表该按什么组织。
        // 实测 2026-09-22：xhdAdmin 的 2 条待办都挂在政府单位（广州市南沙区化工应急管理中心），
        // 园区 51 家企业一家都没有。所以「按选中企业筛待办」恒定 0 条，
        // PortalJobTicketService 才改成「台账为主、待办只作标记」。
        // 哪天后端把票分派到企业岗位上了，这一步会变成"通过"，那时可以回头简化客户端。
        JArray allRows = all == null ? null : all["body"] as JArray;
        if (allRows != null && allRows.Count > 0)
        {
            List<string> units = allRows.OfType<JObject>()
                .Select(r => (string)r["applyingUnit"] + "(" + (string)r["applyingUnitId"] + ")")
                .Distinct().ToList();
            bool underCompany = !string.IsNullOrEmpty(companySid) &&
                allRows.OfType<JObject>().Any(r => (string)r["applyingUnitId"] == companySid);

            Add("待办挂在哪个单位", underCompany ? "通过" : "注意",
                underCompany
                    ? "待办里有挂在园区企业名下的票，列表可以直接按选中企业筛待办"
                    : "全部待办都不在园区企业名下（" + string.Join("、", units) +
                      "）→ 按选中企业筛待办会恒定 0 条。" +
                      "客户端因此用「台账为主、待办只标记哪张能审批」，见 PortalJobTicketService。");
        }

        JObject first = rows.OfType<JObject>().FirstOrDefault();
        if (first == null)
        {
            Add("票据样本", "失败", "待办返回的元素不是对象");
            return null;
        }

        Add("票据样本", "通过",
            (string)first["jobTicket"] + "｜类型 " + (string)first["jobTicketType"] +
            "｜节点 " + (string)first["jobTicketStatus"] +
            "｜申请人 " + (string)first["applicant"] +
            "｜sid " + (string)first["sid"]);
        return first;
    }

    private static void LedgerList(string companySid)
    {
        JObject res = QueryTickets("find", companySid, null, null);
        int total = res == null ? -1 : ((int?)res["total"] ?? -1);
        Add("台账列表", total >= 0 ? "通过" : "失败",
            total >= 0 ? "total=" + total : "请求失败");
    }

    /// <summary>
    /// 词汇一致性校验 —— 这一页盯的是整条链路上最阴的一类错：查得到、不报错、但结果是错的。
    ///
    /// 这套后端同一个概念有三套写法：字典给一套、作业票列表返回一套、接口文档写一套。
    /// 客户端拿错哪一套，表现都是「查出来 0 条」，和「这家企业确实没有作业票」分不开。
    ///
    /// 所以不问字典，直接以**台账实际返回的值**为准：把几百条票的 jobTicketType /
    /// jobTicketStatus 全捞出来，逐个验证客户端认得出、且筛得到。
    /// </summary>
    /// <summary>
    /// 验 <see cref="PortalFormDefinition"/> 对真实响应的解析结果。
    ///
    /// 这一步验的是**运行时那个解析器本身**（自检其余部分只验协议），所以它是票面渲染
    /// 唯一的自动化保障 —— 上真机之前只有它能发现「中文标签抽不出来」「审批变量取错」。
    ///
    /// 两件事：
    ///   1. 中文标签能不能抽出来（抽不出来票面上会写「PIPELINE_MEDIUM：蒸汽」）；
    ///   2. 当前环节的 vars 键能不能从表单里读出来 —— 这条成立的话，缺口 A1
    ///      （文档只给了动火 6 个键）就不用等后端了。
    /// </summary>
    private static void CheckFormDefinition()
    {
        // --- 动火作业：拿文档给的对照表逐环节核对 ---
        Dictionary<string, string> documented = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "作业负责人审批", "zyfzr_AuditResult" },
            { "所在部门审批", "bmfzr_AuditResult" },
            { "安全部审批", "aqbmshr_AuditResult" },
            { "副总审批", "fzld_AuditResult" },
            { "企业领导审批", "qyld_AuditResult" },
            { "动火前验票情况", "yp_AuditResult" },
        };

        List<string> agreed = new List<string>();
        List<string> conflicted = new List<string>();
        List<string> noSample = new List<string>();

        foreach (KeyValuePair<string, string> pair in documented)
        {
            FormProbe probe = ProbeForm("1", pair.Key);
            if (probe == null) { noSample.Add(pair.Key); continue; }
            if (probe.Definition.Approval == null)
            {
                conflicted.Add(pair.Key + "：表单没声明审批变量");
                continue;
            }
            if (probe.Definition.Approval.VarsKey == pair.Value) agreed.Add(pair.Key);
            else conflicted.Add(pair.Key + "：表单说 " + probe.Definition.Approval.VarsKey +
                                "，文档说 " + pair.Value);
        }

        Add("审批变量与文档一致（动火）", conflicted.Count == 0 && agreed.Count > 0 ? "通过" : "失败",
            conflicted.Count == 0
                ? "取到样本的 " + agreed.Count + " 个环节（" + string.Join("、", agreed) +
                  "）全部与文档 §3.3 的 vars 键逐字一致" +
                  (noSample.Count > 0 ? "；没有样本的环节：" + string.Join("、", noSample) : "")
                : string.Join("；", conflicted));

        // --- 其余 7 类：文档没给键名，看能不能从表单自解（缺口 A1） ---
        string[][] others =
        {
            new[] { "2", "受限空间" }, new[] { "3", "断路作业" }, new[] { "4", "动土作业" },
            new[] { "5", "临时用电安全作业" }, new[] { "6", "吊装安全作业" },
            new[] { "7", "高处作业" }, new[] { "8", "盲板抽堵" },
        };

        List<string> resolved = new List<string>();
        List<string> unresolved = new List<string>();
        foreach (string[] item in others)
        {
            FormProbe probe = ProbeForm(item[0], "作业负责人审批");
            if (probe == null) { noSample.Add(item[1]); continue; }
            if (probe.Definition.Approval != null)
                resolved.Add(item[1] + "→" + probe.Definition.Approval.VarsKey);
            else
                unresolved.Add(item[1]);
        }

        Add("其余作业类型的审批变量（A1）", unresolved.Count == 0 && resolved.Count > 0 ? "通过" : "失败",
            unresolved.Count == 0
                ? "都能从表单定义里读出来，不必等后端给对照表：" + string.Join("、", resolved) +
                  "　注意吊装用的是 zyzh_ 而不是 zyfzr_，按命名规律猜必然猜错"
                : "这些类型读不出审批变量，只能等后端给键名：" + string.Join("、", unresolved));

        // --- 中文标签覆盖率：拿动火的基础段比对 ---
        FormProbe fire = ProbeForm("1", "作业负责人审批");
        if (fire == null)
        {
            Add("票面字段中文标签", "跳过", "没有动火作业的审批中样本");
            return;
        }

        JObject baseDef = fire.Defs == null ? null : fire.Defs.OfType<JObject>().FirstOrDefault(
            d => ((string)d["title"] ?? string.Empty).StartsWith("基础", StringComparison.Ordinal));
        if (baseDef == null)
        {
            Add("票面字段中文标签", "失败", "找不到基础信息段");
            return;
        }

        string sectionName = (string)baseDef["name"];
        JObject data = ReadSection(baseDef, fire) as JObject;
        if (data == null)
        {
            Add("票面字段中文标签", "失败", "基础信息段取不到数据");
            return;
        }

        List<string> labelled = new List<string>();
        List<string> unlabelled = new List<string>();
        List<string> fireBaseFields = new List<string>();
        foreach (JProperty property in data.Properties())
        {
            PortalFormDefinition.Field field = fire.Definition.GetField(sectionName, property.Name);
            if (field != null && !string.IsNullOrEmpty(field.Label)) labelled.Add(field.Label);
            else unlabelled.Add(property.Name);
        }

        // 表单声明了哪些字段（用来反向查"声明了但数据里没有"）
        foreach (PortalFormDefinition.Field declared in fire.Definition.GetFields(sectionName))
        {
            fireBaseFields.Add(declared.Name);
        }

        // 【这里刻意不去断言"没标签的必须是系统列"】实测没标签的还包括 APPLYING_UNIT_ID
        // 这类伴生 id（显示值在同名的非 _ID 字段上）、以及 ACCEPTANCE_PERSON / 气体分析那种
        // 属于后续环节的列 —— 它们本来就不在当前环节的表单上，不显示是对的。
        // 真正要守住的是另一个方向：**表单声明的字段必须在数据里真的存在**。
        // 解析器认错了段名或字段名，就会表现为"表单说有、数据里没有"，而那是静默错误：
        // 票面只会少几行，不会报错。
        List<string> phantom = new List<string>();
        foreach (string declared in fireBaseFields)
        {
            if (data.Property(declared) == null) phantom.Add(declared);
        }

        bool sane = labelled.Count >= 15 && phantom.Count == 0;
        Add("票面字段中文标签", sane ? "通过" : "失败",
            sane
                ? "基础信息段 " + data.Count + " 个键里解析出 " + labelled.Count + " 个中文标签（" +
                  string.Join("、", labelled.Take(8)) + " …）；表单声明的字段在数据里全部存在。" +
                  "没标签的 " + unlabelled.Count + " 个是系统列和后续环节的列（" +
                  string.Join("、", unlabelled.Take(6)) + "…），不显示是对的"
                : (labelled.Count < 15 ? "只解析出 " + labelled.Count + " 个标签，预期至少 15 个。" : "") +
                  (phantom.Count > 0 ? "表单声明了但数据里没有的字段：" + string.Join("、", phantom) : ""));
    }

    /// <summary>一次票面三步的中间产物，供 <see cref="CheckFormDefinition"/> 复用。</summary>
    private sealed class FormProbe
    {
        public string FormKey;
        public string FormInsId;
        public JArray Defs;
        public PortalFormDefinition Definition;
    }

    /// <summary>挑一张指定类型 + 指定环节的票，跑完票面前两步。取不到样本返回 null。</summary>
    private static FormProbe ProbeForm(string typeCode, string status)
    {
        JObject res = QueryTickets("find", null, null, status, 1, 1, typeCode);
        JArray rows = res == null ? null : res["body"] as JArray;
        JObject ticket = rows == null ? null : rows.OfType<JObject>().FirstOrDefault();
        if (ticket == null) return null;

        try
        {
            // body 可能不是对象：台账里有票但流程实例没了时，服务端回
            // success=false + body=null + message="流程记录未找到"。直接 view["tasks"]
            // 会抛 "Cannot access child value on JValue"，所以先转型再取。
            JObject view = JObject.Parse(Get(Api.FlowView((string)ticket["processDefinitionKey"],
                (string)ticket["sid"])))["body"] as JObject;
            JArray tasks = view == null ? null : view["tasks"] as JArray;
            if (tasks == null || tasks.Count == 0) return null;

            JObject current = tasks.OfType<JObject>().FirstOrDefault(
                t => t["endTime"] == null || t["endTime"].Type == JTokenType.Null)
                ?? tasks.OfType<JObject>().LastOrDefault();
            string formKey = current == null ? null : (string)current["formKey"];
            if (string.IsNullOrEmpty(formKey)) return null;

            JObject form = JObject.Parse(Post(Api.FormBaseInfoDetail(),
                new JValue(formKey).ToString(Newtonsoft.Json.Formatting.None),
                "application/json"))["body"] as JObject;
            if (form == null) return null;

            return new FormProbe
            {
                FormKey = formKey,
                FormInsId = (string)view["formInsId"],
                Defs = form["datadefList"] as JArray,
                Definition = PortalFormDefinition.Parse(form),
            };
        }
        catch (Exception e)
        {
            Debug.LogWarning("[门户自检] 取表单失败（类型 " + typeCode + "，环节 " + status + "）：" + e.Message);
            return null;
        }
    }

    /// <summary>读一段的数据，已处理双重序列化。</summary>
    private static JToken ReadSection(JObject def, FormProbe probe)
    {
        JObject inner = new JObject();
        inner["form.id"] = probe.FormKey;
        inner["form.insId"] = probe.FormInsId;

        JObject payload = new JObject();
        payload["dataDefId"] = (string)def["sid"];
        payload["params"] = inner.ToString(Newtonsoft.Json.Formatting.None);

        try
        {
            JObject res = JObject.Parse(Post(Api.FormDataGet(),
                payload.ToString(Newtonsoft.Json.Formatting.None), "application/json"));
            string raw = (string)res["body"];
            return string.IsNullOrEmpty(raw) ? null : JToken.Parse(raw);
        }
        catch
        {
            return null;
        }
    }

    private static void CheckVocabulary(List<JObject> workTypes)
    {
        // 抓够样本：一页 100，抓三页，覆盖全部 246 条
        List<JObject> rows = new List<JObject>();
        for (int page = 1; page <= 3; page++)
        {
            JObject res = QueryTickets("find", null, null, null, page, 100);
            JArray body = res == null ? null : res["body"] as JArray;
            if (body == null || body.Count == 0) break;
            rows.AddRange(body.OfType<JObject>());
        }

        if (rows.Count == 0)
        {
            Add("词汇一致性", "跳过", "台账是空的，没有样本可验");
            return;
        }

        // --- 作业类型：客户端必须认得出台账里出现过的每一种 ---
        Dictionary<string, int> typeCounts = Tally(rows, "jobTicketType");
        List<string> unknownTypes = typeCounts.Keys
            .Where(t => !PortalJobTicketCatalog.TryResolveFlowKey(t, out string _)).ToList();

        Add("作业类型可解析", unknownTypes.Count == 0 ? "通过" : "失败",
            "台账 " + rows.Count + " 条里出现 " + typeCounts.Count + " 种作业类型" +
            (unknownTypes.Count == 0
                ? "，全部能解析出 flowKey"
                : "，这些解析不出 flowKey（打不开票面）：" + string.Join("、", unknownTypes)));

        // --- 字典写法 vs 列表写法：字典能不能当筛选值用 ---
        HashSet<string> dictValues = new HashSet<string>(
            workTypes.Select(t => (string)t["dictValue"]).Where(v => v != null), StringComparer.Ordinal);
        List<string> mismatched = typeCounts.Keys.Where(t => !dictValues.Contains(t)).ToList();
        if (mismatched.Count > 0)
        {
            Add("字典写法≠列表写法", "待后端",
                "台账返回的这些写法字典里没有：" + string.Join("、", mismatched) +
                "。所以字典值不能直接当筛选值用，客户端一律以列表返回的写法为准。");
        }

        // --- 按类型单独筛：实测这是坏的，8 类里只有动火作业筛得出来 ---
        //     不是写法问题（两种写法都试过），也不是偶发（重复 8 次结果一致）。
        //     服务端不报错，只是返回 0，和「这类作业确实没有票」长得一样。
        List<string> brokenType = new List<string>();
        foreach (KeyValuePair<string, int> pair in typeCounts.OrderByDescending(x => x.Value))
        {
            int filtered = Total(QueryTickets("find", null, pair.Key, null));
            if (filtered != pair.Value) brokenType.Add(pair.Key + "（台账 " + pair.Value + "，筛出 " + filtered + "）");
        }

        // 同样的类型配上状态一起筛就正常了 —— 这条对比是给后端定位用的
        string probeType = typeCounts.Keys.FirstOrDefault(
            t => !PortalJobTicketCatalog.IsFireWork(t) && typeCounts[t] > 0);
        string paired = "(没有可对比的样本)";
        if (probeType != null)
        {
            int withStatus = Total(QueryTickets("find", null, probeType, "作业负责人审批"));
            int typeOnly = Total(QueryTickets("find", null, probeType, null));
            paired = "「" + probeType + "」单独筛=" + typeOnly +
                     "，配上状态「作业负责人审批」一起筛=" + withStatus;
        }

        Add("按作业类型单独筛选", brokenType.Count == 0 ? "通过" : "待后端",
            brokenType.Count == 0
                ? typeCounts.Count + " 种类型的筛选结果都与台账条数一致"
                : "⚠ 这些类型单独筛返回 0：" + string.Join("；", brokenType) +
                  "。对比：" + paired +
                  "。→ 客户端不要单独按作业类型筛；本期只做动火作业（动火单独筛正常），暂不阻塞。");

        // --- 绕法：jobTicketTypeCode（数字）实测 8 类全对，客户端要筛类型只能用它 ---
        //     这一步是 PortalJobTicketService.BuildBody 里「只开放 code 入口」那条注释的依据。
        //     哪天后端把中文那条修好了，这步照样通过；要是连 code 也坏了，这里会立刻红。
        Dictionary<string, string> codeOfType = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JObject row in rows)
        {
            string t = (string)row["jobTicketType"];
            string c = row["jobTicketTypeCode"] == null ? null : row["jobTicketTypeCode"].ToString();
            if (t != null && !string.IsNullOrEmpty(c) && !codeOfType.ContainsKey(t)) codeOfType[t] = c;
        }

        List<string> brokenCode = new List<string>();
        foreach (KeyValuePair<string, int> pair in typeCounts.OrderByDescending(x => x.Value))
        {
            if (!codeOfType.TryGetValue(pair.Key, out string code)) { brokenCode.Add(pair.Key + "（没有 code）"); continue; }
            int filtered = Total(QueryTickets("find", null, null, null, 1, 1, code));
            if (filtered != pair.Value)
                brokenCode.Add(pair.Key + "/code=" + code + "（台账 " + pair.Value + "，筛出 " + filtered + "）");
        }

        Add("按 jobTicketTypeCode 筛选", brokenCode.Count == 0 ? "通过" : "失败",
            brokenCode.Count == 0
                ? codeOfType.Count + " 种类型用数字 code 筛都与台账精确一致（" +
                  string.Join("、", codeOfType.OrderBy(kv => kv.Value).Select(kv => kv.Value + "=" + kv.Key)) +
                  "）→ 这是客户端按类型筛的唯一可用入口"
                : "数字 code 这条路也不对了：" + string.Join("；", brokenCode) +
                  "。中文那条本来就是坏的，两条都坏就没法按类型筛了。");

        // --- processDefinitionKey：打开票面的 flowKey 直接来自服务端，不靠客户端查表 ---
        List<JObject> noKey = rows.Where(r => string.IsNullOrWhiteSpace((string)r["processDefinitionKey"])).ToList();
        Dictionary<string, HashSet<string>> keysOfType = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (JObject row in rows)
        {
            string t = (string)row["jobTicketType"];
            string k = (string)row["processDefinitionKey"];
            if (t == null || k == null) continue;
            if (!keysOfType.TryGetValue(t, out HashSet<string> set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                keysOfType[t] = set;
            }
            set.Add(k);
        }
        List<string> ambiguous = keysOfType.Where(kv => kv.Value.Count > 1)
            .Select(kv => kv.Key + "→" + string.Join("/", kv.Value)).ToList();

        Add("行内 processDefinitionKey", noKey.Count == 0 && ambiguous.Count == 0 ? "通过" : "失败",
            noKey.Count == 0 && ambiguous.Count == 0
                ? "台账 " + rows.Count + " 条全部带 processDefinitionKey，且与作业类型严格一对一" +
                  " → PortalJobTicketService.ResolveFlowKey 用服务端的值，不必查表猜"
                : (noKey.Count > 0 ? noKey.Count + " 条缺 processDefinitionKey；" : "") +
                  (ambiguous.Count > 0 ? "同一类型对应多个 key：" + string.Join("、", ambiguous) : ""));

        // --- 客户端查表要和服务端下发的对得上，对不上说明表过期了 ---
        List<string> tableDrift = keysOfType
            .Where(kv => PortalJobTicketCatalog.TryResolveFlowKey(kv.Key, out string mapped) &&
                         !kv.Value.Contains(mapped))
            .Select(kv => kv.Key + "：服务端 " + string.Join("/", kv.Value) + "，表里写的是别的")
            .ToList();
        Add("查表结果与服务端一致", tableDrift.Count == 0 ? "通过" : "失败",
            tableDrift.Count == 0
                ? "PortalJobTicketCatalog 的 flowKey 与服务端下发的 processDefinitionKey 全部吻合"
                : string.Join("；", tableDrift));

        // --- 按企业筛：审批 tab 真正依赖的就是这一条，必须准 ---
        Dictionary<string, int> unitCounts = Tally(rows, "applyingUnitId");
        List<string> brokenUnit = new List<string>();
        foreach (KeyValuePair<string, int> pair in unitCounts.OrderByDescending(x => x.Value).Take(8))
        {
            int filtered = Total(QueryTickets("find", pair.Key, null, null));
            if (filtered != pair.Value) brokenUnit.Add(pair.Key + "（台账 " + pair.Value + "，筛出 " + filtered + "）");
        }

        Add("按企业筛选", brokenUnit.Count == 0 ? "通过" : "失败",
            brokenUnit.Count == 0
                ? "抽查条数最多的 " + Math.Min(8, unitCounts.Count) + " 家单位，applyingUnitId 筛选结果都与台账精确一致" +
                  "　（注：台账里多数票挂在园区节点而不是具体企业，所以选中企业后看到的条数会远少于总数）"
                : "⚠ 这些单位筛不准：" + string.Join("；", brokenUnit) + " —— 按企业筛是审批 tab 的基础，必须先修");

        // --- 动火作业的审批环节必须都能映射到 vars 键，否则提交时静默失效 ---
        List<string> fireStatuses = rows
            .Where(r => PortalJobTicketCatalog.IsFireWork((string)r["jobTicketType"]))
            .Select(r => (string)r["jobTicketStatus"])
            .Where(v => !string.IsNullOrEmpty(v))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // 只有审批环节才需要 vars 键；草稿/结束/验收/作业记录这些不是审批动作
        string[] notApproval = { "草稿", "结束", "验收", "作业记录", "再次补充材料" };
        List<string> missing = fireStatuses
            .Where(st => !notApproval.Contains(st))
            .Where(st => !PortalJobTicketCatalog.TryResolveFireWorkVarsKey("动火作业", st, out string _))
            .ToList();

        Add("动火审批环节→vars 键", missing.Count == 0 ? "通过" : "失败",
            "台账里动火作业出现过 " + fireStatuses.Count + " 种状态" +
            (missing.Count == 0
                ? "，审批环节全部能映射到 vars 键"
                : "，这些审批环节没有对应的 vars 键（提交会静默失效）：" + string.Join("、", missing)));
    }

    private static Dictionary<string, int> Tally(List<JObject> rows, string field)
    {
        Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (JObject row in rows)
        {
            string value = (string)row[field];
            if (string.IsNullOrEmpty(value)) continue;
            counts.TryGetValue(value, out int n);
            counts[value] = n + 1;
        }
        return counts;
    }

    private static int Total(JObject res)
    {
        return res == null ? -1 : ((int?)res["total"] ?? -1);
    }

    /// <param name="typeCode">
    /// 按作业类型筛的**可用**入口（数字 1–8）。中文 <paramref name="type"/> 那条路是坏的，
    /// 见「按作业类型单独筛选」那一步。
    /// </param>
    private static JObject QueryTickets(string endpoint, string companySid, string type, string status,
        int page = 1, int pageSize = 5, string typeCode = null)
    {
        JObject job = new JObject();
        job["applyingUnitId"] = companySid;
        job["jobTicketUnitId"] = null;
        job["jobTicketType"] = type;
        job["jobTicketStatus"] = status;
        job["applyTimeStart"] = null;
        job["applyTimeEnd"] = null;
        if (typeCode != null) job["jobTicketTypeCode"] = typeCode;

        JObject body = new JObject();
        body["job"] = job;
        if (endpoint == "todo") body["operateType"] = "audit";

        JObject payload = new JObject();
        payload["body"] = body;
        payload["page"] = page;
        payload["pageSize"] = pageSize;

        try
        {
            return JObject.Parse(Post(
                Api.ICC_URL + "iflow-task/task/jobTicket/" + endpoint,
                payload.ToString(Newtonsoft.Json.Formatting.None),
                "application/json"));
        }
        catch (Exception e)
        {
            Debug.LogWarning("[门户自检] jobTicket/" + endpoint + " 请求失败：" + e.Message);
            return null;
        }
    }

    /// <summary>
    /// 从台账里挑一张**正在审批中**的动火票，供票面验证用。
    ///
    /// 不能随便挑：草稿票没有流程实例，<c>flow/view</c> 会返回空 body，看着像接口坏了。
    /// </summary>
    private static JObject SampleFromLedger(string status)
    {
        JObject res = QueryTickets("find", null, "动火作业", status);
        JArray rows = res == null ? null : res["body"] as JArray;
        JObject first = rows == null ? null : rows.OfType<JObject>().FirstOrDefault();

        Add("票面样本（" + status + "）", first != null ? "通过" : "失败",
            first != null
                ? (string)first["jobTicket"] + "，sid " + (string)first["sid"]
                : "台账里没有「" + status + "」的动火票");
        return first;
    }

    /// <summary>
    /// 打开一张票，三步走完。只走只读的 <c>flow/view</c>：<c>flow/handle</c> 的 GET 会占用工作流任务，
    /// 自检不该产生这种副作用。
    ///
    /// 三处实测与文档不符，这里按实际来：
    ///   1. 响应里是 <c>body.tasks</c>【数组】，不是文档写的 <c>body.task</c>；
    ///      当前环节靠 <c>endTime == null</c> 认，<c>active</c> 在 view 模式下恒为 false；
    ///   2. <c>formBaseInfo/detail</c> 的请求体是【裸字符串 formKey】，
    ///      传 <c>{"sid":...}</c> 这类对象一律回「JSON转换异常」；
    ///   3. <c>formData/get</c> 的 <c>params.key</c> 【可以不传】，
    ///      文档里那个写成占位符的 base64 常量并不是必需的。
    /// </summary>
    private static void OpenTicket(JObject ticket, string tag)
    {
        if (ticket == null)
        {
            Add("打开票面（" + tag + "）", "跳过", "没有票据样本");
            return;
        }

        string typeName = ((string)ticket["jobTicketType"] ?? string.Empty).Trim();
        string flowKey;
        if (!PortalJobTicketCatalog.TryResolveFlowKey(typeName, out flowKey))
        {
            Add("打开票面（" + tag + "）", "失败",
                "作业类型「" + typeName + "」不在 flowKey 对照表里，对不上 URL 的 {type} 段");
            return;
        }

        string sid = (string)ticket["sid"];
        JToken info;
        try
        {
            info = JObject.Parse(
                Get(Api.ICC_URL + "iflow-task/flow/view/" + flowKey + "/" + sid))["body"];
        }
        catch (Exception e)
        {
            Add("打开票面 1/3 flow/view（" + tag + "）", "失败", e.Message);
            return;
        }

        JArray tasks = info == null ? null : info["tasks"] as JArray;
        if (tasks == null || tasks.Count == 0)
        {
            Add("打开票面 1/3 flow/view（" + tag + "）", "失败",
                "body.tasks 是空的。多半是挑到草稿票了：草稿没起流程实例。");
            return;
        }

        // 当前环节 = 还没结束的那个 task
        JToken current = tasks.FirstOrDefault(t => t["endTime"] == null || t["endTime"].Type == JTokenType.Null)
                         ?? tasks[0];
        string formKey = (string)current["formKey"];
        string formInsId = (string)info["formInsId"];

        JArray btns = current["btns"] as JArray;
        Add("打开票面 1/3 flow/view（" + tag + "）", formKey != null ? "通过" : "失败",
            "tasks " + tasks.Count + " 个，当前环节「" + (string)current["taskName"] + "」" +
            "：formKey=" + formKey + "，taskInsId=" + (string)current["taskInsId"] +
            "，formInsId=" + formInsId + "，flowInsId=" + (string)info["flowInsId"] +
            "，可用按钮=" + (btns == null
                ? "(无)"
                : string.Join("/", btns.Select(b => (string)b["key"]))) +
            "　按钮是服务端下发的，客户端应照它渲染而不是自己硬编码");

        if (formKey == null) return;

        // 请求体是裸字符串 formKey，不是对象
        JToken detail;
        try
        {
            detail = JObject.Parse(Post(
                Api.ICC_URL + "idaf-server/formBaseInfo/detail", formKey, "application/json"))["body"];
        }
        catch (Exception e)
        {
            Add("打开票面 2/3 formBaseInfo/detail（" + tag + "）", "失败", e.Message);
            return;
        }

        JArray defs = detail == null ? null : detail["datadefList"] as JArray;
        if (defs == null || defs.Count == 0)
        {
            Add("打开票面 2/3 formBaseInfo/detail（" + tag + "）", "失败", "datadefList 是空的，取不到段定义");
            return;
        }

        // 附件段只能按 title 前缀认。段的 name 会随表单版本变（同一环节实测出现过
        // dhzy_zyfzrsp_wj 和 dhzy_zyfzrsp_file 两种），硬编码段名必然在换版本时失效。
        List<string> attachTitles = defs
            .Select(d => (string)d["title"] ?? string.Empty)
            .Where(t => t.StartsWith("文件_") || t.StartsWith("图片_"))
            .ToList();

        Add("打开票面 2/3 formBaseInfo/detail（" + tag + "）", "通过",
            "请求体用裸字符串 formKey 通过，datadefList " + defs.Count + " 段；" +
            "附件段 " + attachTitles.Count + " 个：" + string.Join("、", attachTitles));

        ReadSections(defs, formKey, formInsId, tag);
    }

    /// <summary>
    /// 逐段取数据，并盯住审批段的字段名 —— 提交审批时要往这些字段写。
    ///
    /// 响应的 <c>body</c> 本身还是一个 JSON 字符串，要再 parse 一次（文档 §3.2 的「两个坑」之一）。
    /// </summary>
    private static void ReadSections(JArray defs, string formKey, string formInsId, string tag)
    {
        string[] wanted = { "作业负责人", "所在单位", "安全部门", "企业领导" };
        int read = 0;
        bool sawApprovalFields = false;

        foreach (JToken def in defs)
        {
            string title = (string)def["title"] ?? string.Empty;
            if (!wanted.Any(w => title.Contains(w)) || title.StartsWith("下拉_")) continue;

            JObject payload = new JObject();
            payload["dataDefId"] = (string)def["sid"];
            JObject inner = new JObject();
            inner["form.id"] = formKey;
            inner["form.insId"] = formInsId;
            payload["params"] = inner.ToString(Newtonsoft.Json.Formatting.None);

            try
            {
                JObject res = JObject.Parse(Post(Api.ICC_URL + "idaf-server/formData/get",
                    payload.ToString(Newtonsoft.Json.Formatting.None), "application/json"));

                string raw = (string)res["body"];
                read++;
                if (string.IsNullOrEmpty(raw)) continue;

                JToken parsed = JToken.Parse(raw);   // 双重序列化：body 是 JSON 字符串
                JObject section = parsed as JObject;
                if (section == null) continue;

                if (section.Property("APPROVAL_RESULT") != null)
                {
                    sawApprovalFields = true;
                    Add("审批段字段（" + title + "）", "通过",
                        string.Join("、", section.Properties().Select(p => p.Name)));
                }
            }
            catch (Exception e)
            {
                Add("打开票面 3/3 formData/get（" + tag + "）", "失败", title + "：" + e.Message);
                return;
            }
        }

        Add("打开票面 3/3 formData/get（" + tag + "）", read > 0 ? "通过" : "失败",
            "params 里不传 key 也能取到数据（文档那个 base64 常量非必需）；" +
            "读了 " + read + " 段，body 是 JSON 字符串、需二次反序列化" +
            (sawApprovalFields
                ? "。A2 已定：审批结果 APPROVAL_RESULT（1 通过 / 0 不通过）、" +
                  "意见 APPROVAL_OPINION、签名 APPROVAL_SIGNATURE"
                : "。⚠ 这张票的审批段还是空的，看不到字段名；换一张已审批过的票再跑"));
    }

    // ------------------------------------------------------------------ HTTP

    private static string Get(string url)
    {
        return Send(HttpMethod.Get, url, null, null);
    }

    private static string Post(string url, string body, string contentType)
    {
        return Send(HttpMethod.Post, url, body ?? string.Empty, contentType);
    }

    private static string PostMultipart(string url, Dictionary<string, string> fields)
    {
        Guard(HttpMethod.Post, url);

        using (MultipartFormDataContent content = new MultipartFormDataContent())
        {
            foreach (KeyValuePair<string, string> field in fields)
            {
                content.Add(new StringContent(field.Value, Encoding.UTF8), field.Key);
            }
            return SendCore(new HttpRequestMessage(HttpMethod.Post, url) { Content = content });
        }
    }

    private static string Send(HttpMethod method, string url, string body, string contentType)
    {
        Guard(method, url);

        HttpRequestMessage request = new HttpRequestMessage(method, url);
        if (body != null)
        {
            StringContent content = new StringContent(body, Encoding.UTF8);
            // 文档 §1.3 逐接口规定 Content-Type，连 "application/json;" 这种带尾分号的写法
            // 都照抄；StringContent 会自己塞 text/plain，所以先清掉再原样写回去。
            content.Headers.Remove("Content-Type");
            content.Headers.TryAddWithoutValidation("Content-Type", contentType);
            request.Content = content;
        }
        return SendCore(request);
    }

    private static string SendCore(HttpRequestMessage request)
    {
        // 字段名就是小写 token，没有 Bearer 前缀（文档 §1.2）。
        // HTTP 头本身大小写不敏感，工程里 ICC 那条链路发的是大写 Token 且能通，
        // 所以这里不为大小写额外做探测。
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.TryAddWithoutValidation("token", token);
        }

        HttpResponseMessage response = http.SendAsync(request).GetAwaiter().GetResult();
        string text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception("HTTP " + (int)response.StatusCode + " " +
                request.RequestUri.AbsolutePath + "：" + Trim(text, 300));
        }
        return text;
    }

    /// <summary>写接口一律不发。见类注释：流程一旦推进，客户端撤不回来。</summary>
    private static void Guard(HttpMethod method, string url)
    {
        if (method != HttpMethod.Post) return;

        foreach (string fragment in ForbiddenFragments)
        {
            if (url.Contains(fragment))
            {
                throw new Exception("自检不允许发写请求：" + fragment);
            }
        }

        if (url.Contains("iflow-task/flow/handle"))
        {
            throw new Exception("自检不允许 POST flow/handle：那会真的推进作业票流程");
        }
    }

    // ------------------------------------------------------------------ 报告

    private static void Add(string name, string state, string detail)
    {
        steps.Add(new Step { name = name, state = state, detail = detail });
        Debug.Log("[门户自检] " + state + "　" + name + "　" + detail);
    }

    private static void Report()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("========== 门户接口自检报告 ==========");
        foreach (Step step in steps)
        {
            sb.AppendLine("[" + step.state + "] " + step.name);
            sb.AppendLine("        " + step.detail);
        }

        sb.AppendLine("合计 " + steps.Count + " 项：" +
            "失败 " + steps.Count(s => s.state == "失败") + "，" +
            "待后端 " + steps.Count(s => s.state == "待后端") + "，" +
            "通过 " + steps.Count(s => s.state == "通过") + "，" +
            // 「注意」= 不是错，但影响客户端该怎么写；不计入失败，也不能被吞掉
            "注意 " + steps.Count(s => s.state == "注意") + "，" +
            "跳过 " + steps.Count(s => s.state == "跳过"));
        sb.Append("=====================================");

        Debug.Log(sb.ToString());
    }

    private static void Finish(bool ok)
    {
        if (!Application.isBatchMode) return;
        EditorApplication.Exit(ok ? 0 : 1);
    }

    private static string Trim(string text, int max)
    {
        if (string.IsNullOrEmpty(text)) return "(空响应)";
        text = text.Replace("\n", " ").Replace("\r", " ");
        return text.Length <= max ? text : text.Substring(0, max) + " …";
    }
}
#endif
