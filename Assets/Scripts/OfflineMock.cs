using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 随包的离线数据镜像 + 统一取数入口。
///
/// 原先 Api.API_URL 指向 http://127.0.0.1:8082/，依赖 adb reverse 把端口转发到开发机，
/// USB 一断就全部拿不到数据。这里把各端点的响应烘进 StreamingAssets，网络不可达时回退，
/// 使设备脱离开发机也能有数据。
///
/// 取数一律走本类的 <see cref="Fetch"/> / <see cref="PostJson"/> / <see cref="PostForm"/>，
/// 不要再直接 new UnityWebRequest —— 那样会绕过回退逻辑（ClosedPark_EntryExitList 曾经
/// 就是这么写的，导致门禁列表在无 adb 时永远为空）。
///
/// **写操作是例外**：一律走 <see cref="PostJsonStrict"/>，它不回退镜像。
/// 回退对读是兜底，对写是假成功 —— 提交失败却读到镜像，UI 会显示"提交成功"。
///
/// 文件名规则：取 URL 的 path（丢掉 query），把 '/' 换成 '_'，加 .json。
///   http://127.0.0.1:8082/security/company/list?pageNum=1&amp;pageSize=10
///   -> security_company_list.json
/// POST 端点同样按 path 归档，回退时用 GET 读本地文件：
///   https://www.nsxhcip.cn/icc/dataEventDetail/find -> icc_dataEventDetail_find.json
///
/// 已知取舍：镜像按 path 归档，不区分 query 与请求体。分页参数不同会拿到同一份数据；
/// 按 id 取详情的端点（security/patrol?sid=、security/company/chemical/count?companyId= 等）
/// 也只有一份样本。作为脱机演示用的兜底可以接受，不能当数据库用。
/// </summary>
public static class OfflineMock
{
    /// <summary>StreamingAssets 下存放镜像的子目录。</summary>
    public const string Folder = "mock";

    /// <summary>网络请求超时秒数。设短一点，让回退尽快发生。</summary>
    public const int NetworkTimeoutSeconds = 5;

    /// <summary>
    /// 写操作的超时秒数。
    ///
    /// 比读操作长得多，因为两者要的东西正好相反：读超时了回退镜像就行，所以越快越好；
    /// 写超时了是**最坏的情况** —— 服务端可能已经处理完了，客户端却不知道。
    /// 审批提交要带签名的 base64，体积不小，现场网络下 5 秒远远不够，
    /// 卡在这里等一会儿，比拿到一个「结果未知」划算得多。
    /// </summary>
    public const int WriteTimeoutSeconds = 30;

    /// <summary>
    /// 置 true 则完全跳过网络，直接读镜像。
    /// 在没有任何服务可达的环境里演示时打开，可省去每个请求的连接超时等待。
    /// </summary>
    public static bool PreferOffline = false;

    /// <summary>取数结果载体（协程不能有返回值）。</summary>
    public class Result
    {
        /// <summary>取到的原始 JSON；失败为 null。</summary>
        public string json;

        /// <summary>"网络" 或 "离线镜像"，用于日志溯源。</summary>
        public string source;

        public string error;

        /// <summary>
        /// 网络那一跳的 HTTP 状态码；没走网络（PreferOffline）时为 0。
        /// ICC 那套接口的 token 过期是靠 401 判的，所以要把它带出来。
        /// </summary>
        public long httpCode;

        /// <summary>
        /// 只对写操作有意义：请求发出去了，但没能拿到完整的 HTTP 响应（超时 / 连接中断）。
        ///
        /// 这时候**不能说失败**，因为服务端可能已经处理完了。区分「确定失败」和「结果未知」
        /// 是为了让 UI 能说出两句不同的话：前者「提交失败，请重试」，后者「提交结果未知，
        /// 请到门户端确认后再决定要不要重提」—— 后者要是也提示重试，用户一重试就可能
        /// 产生第二次审批。
        ///
        /// 判据只有 <c>responseCode == 0</c>（一个 HTTP 响应都没收到），
        /// 分不出「根本没连上」和「发出去了没等到回复」，所以宁可保守地都算未知。
        /// </summary>
        public bool resultUnknown;

        public bool ok => json != null;
    }

    /// <summary>
    /// 取数据：先走网络，失败则回退到随包镜像。所有 GET 请求都应该走这里。
    /// </summary>
    /// <param name="headers">
    /// 额外请求头（如罐区监测那几个接口要带的 Token）。为 null 表示不加。
    /// 放在末尾且有默认值，老调用方不用改。
    /// </param>
    public static IEnumerator Fetch(string apiUrl, Result outcome,
        Dictionary<string, string> headers = null)
    {
        if (!PreferOffline)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(apiUrl))
            {
                ApplyHeaders(request, headers);
                request.timeout = NetworkTimeoutSeconds;
                yield return request.SendWebRequest();

                if (TakeNetworkResult(request, apiUrl, outcome))
                {
                    yield break;
                }
            }
        }

        yield return FallbackToMirror(apiUrl, outcome);
    }

    /// <summary>
    /// GET 一段 JSON，**失败就是失败，绝不回退镜像**。
    ///
    /// 【为什么票面要用这个】<see cref="Fetch"/> 会退到 <c>StreamingAssets/mock/</c> 下的
    /// 同名镜像。票面决定了"审批按钮开不开"、"当前是哪个环节"，读到一份旧快照会让用户
    /// 对着一个已经流转过去的环节做审批 —— 提交上去服务端也不会报错。
    /// 镜像按 URL path 归档，虽然 <c>flow/view/{flowKey}/{sid}</c> 带了票号不至于串票，
    /// 但"同一张票的旧状态"同样危险。这类请求宁可失败。
    ///
    /// 也不看 <see cref="PreferOffline"/>，理由同 <see cref="PostJsonStrict"/>。
    /// </summary>
    public static IEnumerator FetchStrict(string apiUrl, Result outcome,
        Dictionary<string, string> headers = null)
    {
        using (UnityWebRequest request = UnityWebRequest.Get(apiUrl))
        {
            ApplyHeaders(request, headers);
            request.timeout = NetworkTimeoutSeconds;
            yield return request.SendWebRequest();
            TakeNetworkResult(request, apiUrl, outcome);
        }
    }

    /// <summary>
    /// POST 一段 JSON 取数据，失败同样回退随包镜像。
    ///
    /// **只用于读**。写操作（审批提交、新建、修改）一律走 <see cref="PostJsonStrict"/>，
    /// 理由见那边的注释：回退镜像会让失败的提交看起来像成功。
    /// </summary>
    /// <param name="headers">额外请求头（如 ICC 的 Token）。为 null 表示不加。</param>
    public static IEnumerator PostJson(string apiUrl, string jsonBody,
        Dictionary<string, string> headers, Result outcome)
    {
        if (!PreferOffline)
        {
            yield return PostJsonOnce(apiUrl, jsonBody, headers, outcome,
                NetworkTimeoutSeconds, "application/json");
            if (outcome.ok) yield break;
        }

        yield return FallbackToMirror(apiUrl, outcome);
    }

    /// <summary>
    /// POST 一段 JSON，**失败就是失败，绝不回退镜像**。所有写操作都必须走这里。
    ///
    /// 【为什么必须有这个】<see cref="PostJson"/> 失败时会去读 <c>StreamingAssets/mock/</c>
    /// 下的同名镜像文件。对读操作那是兜底，对写操作那是灾难：审批提交失败了，
    /// 却从本地读到一份历史响应，<c>outcome.ok</c> 变成 true，UI 显示「提交成功」，
    /// 而服务端那边什么都没发生。用户以为审批完了，作业票还卡在原地。
    ///
    /// 镜像按 URL path 归档、不区分请求体，所以这种假成功一定会发生，不是概率问题。
    ///
    /// 【也不看 PreferOffline】那个开关的语义是「跳过网络直接读镜像」，是读侧的演示优化。
    /// 写操作照它执行就等于把提交变成一次镜像读，正是上面那个假成功。所以这里一律走网络，
    /// 连不上就诚实地失败。
    ///
    /// 【超时更长】默认用 <see cref="WriteTimeoutSeconds"/> 而不是
    /// <see cref="NetworkTimeoutSeconds"/>；读请求可以用 <paramref name="timeoutSeconds"/> 调回来。
    ///
    /// 【读请求什么时候也要走这里】镜像是按 URL path 归档的，**不区分请求体**。
    /// 所以凡是"同一个 URL、换个请求体就该返回不同结果"的接口，回退镜像一定是错的答案：
    /// 作业票台账按 applyingUnitId 筛企业，退到镜像就会把甲企业的票显示成乙企业的，
    /// 而且看起来一切正常。这类读请求宁可诚实地失败，也不能给错数据。
    ///
    /// 失败时看 <see cref="Result.resultUnknown"/> 再决定怎么提示用户。
    /// </summary>
    /// <param name="contentType">
    /// 门户那套后端没有统一的 Content-Type，得逐接口对应（接口文档 §1.3），
    /// 传错会直接报错或返回空数据，所以开放给调用方指定，默认 <c>application/json</c>。
    /// </param>
    /// <param name="timeoutSeconds">
    /// 超时秒数。默认 <see cref="WriteTimeoutSeconds"/>（写请求要留足最坏情况）；
    /// 读请求传 <see cref="NetworkTimeoutSeconds"/>，失败得快一点，用户不用干等。
    /// </param>
    public static IEnumerator PostJsonStrict(string apiUrl, string jsonBody,
        Dictionary<string, string> headers, Result outcome,
        string contentType = "application/json", int timeoutSeconds = WriteTimeoutSeconds)
    {
        yield return PostJsonOnce(apiUrl, jsonBody, headers, outcome,
            timeoutSeconds, contentType);

        if (!outcome.ok)
        {
            // 这一行是排查「用户说提交了但门户上没有」时的第一落脚点
            Debug.LogWarning($"[写请求] 失败 {apiUrl}：{outcome.error}" +
                (outcome.resultUnknown ? "（结果未知，服务端可能已处理）" : "（确定未提交）"));
        }
    }

    /// <summary>
    /// 发一次 POST JSON，不做任何回退。<see cref="PostJson"/> 和
    /// <see cref="PostJsonStrict"/> 共用，两者的区别只在超时长度和失败后做什么。
    ///
    /// 不能用 UnityWebRequest.Post(url, string) —— 那个重载会把正文按 form 编码，
    /// 服务端收到的就不是 JSON 了。必须自己塞 UploadHandlerRaw。
    /// </summary>
    private static IEnumerator PostJsonOnce(string apiUrl, string jsonBody,
        Dictionary<string, string> headers, Result outcome,
        int timeoutSeconds, string contentType)
    {
        using (UnityWebRequest request = new UnityWebRequest(apiUrl, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody ?? string.Empty));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", contentType);
            ApplyHeaders(request, headers);
            request.timeout = timeoutSeconds;

            yield return request.SendWebRequest();

            if (TakeNetworkResult(request, apiUrl, outcome)) yield break;

            // 一个 HTTP 响应都没收到 —— 超时、连接中断、根本没连上，这三种分不开，
            // 对写操作一律按「结果未知」处理，宁可让用户去门户端确认一下。
            outcome.resultUnknown = request.responseCode == 0;
        }
    }

    /// <summary>
    /// POST 一份 multipart 表单（登录那类接口），失败同样回退随包镜像。
    /// </summary>
    public static IEnumerator PostForm(string apiUrl, WWWForm form, Result outcome,
        Dictionary<string, string> headers = null)
    {
        if (!PreferOffline)
        {
            using (UnityWebRequest request = UnityWebRequest.Post(apiUrl, form))
            {
                ApplyHeaders(request, headers);
                request.timeout = NetworkTimeoutSeconds;
                yield return request.SendWebRequest();

                if (TakeNetworkResult(request, apiUrl, outcome))
                {
                    yield break;
                }
            }
        }

        yield return FallbackToMirror(apiUrl, outcome);
    }

    /// <summary>
    /// 网络那一跳的收尾：成功就把结果写进 outcome 并打日志、返回 true；
    /// 失败只记状态码和错误信息，交给调用方走回退。
    /// </summary>
    private static bool TakeNetworkResult(UnityWebRequest request, string apiUrl, Result outcome)
    {
        outcome.httpCode = request.responseCode;

        if (request.result == UnityWebRequest.Result.Success)
        {
            outcome.json = request.downloadHandler.text;
            outcome.source = "网络";
            Log(apiUrl, outcome);
            return true;
        }

        outcome.error = $"网络错误: {request.error} (HTTP {request.responseCode})";
        return false;
    }

    /// <summary>回退读随包镜像。GET / POST 共用。</summary>
    private static IEnumerator FallbackToMirror(string apiUrl, Result outcome)
    {
        string mirrorUrl = ResolveUrl(apiUrl);
        if (string.IsNullOrEmpty(mirrorUrl))
        {
            outcome.error = $"{outcome.error}；无法映射到离线镜像 ({apiUrl})";
            Log(apiUrl, outcome);
            yield break;
        }

        using (UnityWebRequest mirror = UnityWebRequest.Get(mirrorUrl))
        {
            yield return mirror.SendWebRequest();

            if (mirror.result == UnityWebRequest.Result.Success)
            {
                outcome.json = mirror.downloadHandler.text;
                outcome.source = "离线镜像";
                outcome.error = null;
            }
            else
            {
                outcome.error = $"{outcome.error}；离线镜像也失败: {mirror.error} ({FileNameFor(apiUrl)})";
            }
        }

        Log(apiUrl, outcome);
    }

    /// <summary>
    /// 加自定义请求头。
    ///
    /// 刻意不打日志：这里传的是 Token 一类的凭据，写进 logcat 就等于泄出去了。
    /// </summary>
    private static void ApplyHeaders(UnityWebRequest request, Dictionary<string, string> headers)
    {
        if (headers == null)
        {
            return;
        }

        foreach (KeyValuePair<string, string> header in headers)
        {
            if (string.IsNullOrEmpty(header.Key) || header.Value == null)
            {
                continue;
            }

            request.SetRequestHeader(header.Key, header.Value);
        }
    }

    /// <summary>
    /// 每个请求都留一行，便于定位"哪个接口没数据"。
    /// 只打路径和长度，不打内容——部分响应接近 200KB，整段写 logcat 会拖慢帧率；
    /// ICC 那套响应里还带设备信息，也不该整段进日志。
    /// </summary>
    private static void Log(string apiUrl, Result outcome)
    {
        string path = FileNameFor(apiUrl) ?? apiUrl;

        if (!outcome.ok)
        {
            Debug.LogError($"[Fetch] 失败 {path} :: {outcome.error}");
            return;
        }

        // 空集也要能一眼看出来：mock 里本来就有几个端点是空的
        bool looksEmpty = outcome.json.Contains("\"total\": 0") || outcome.json.Contains("\"total\":0");
        Debug.Log($"[Fetch] {(looksEmpty ? "空集" : "OK")} {path}  {outcome.json.Length} 字符  来源：{outcome.source}");
    }

    /// <summary>把 API 地址映射成可交给 UnityWebRequest 的镜像地址。无法映射时返回 null。</summary>
    public static string ResolveUrl(string apiUrl)
    {
        string file = FileNameFor(apiUrl);
        if (string.IsNullOrEmpty(file))
        {
            return null;
        }

        string root = Application.streamingAssetsPath;

        // Android 上 streamingAssetsPath 本身就是 jar:file://... 形式，可直接使用；
        // 其余平台是普通路径，UnityWebRequest 需要 file:// 前缀。
        return root.Contains("://")
            ? $"{root}/{Folder}/{file}"
            : $"file://{root}/{Folder}/{file}";
    }

    /// <summary>由 API 地址推导镜像文件名。</summary>
    public static string FileNameFor(string apiUrl)
    {
        if (string.IsNullOrEmpty(apiUrl))
        {
            return null;
        }

        // 去掉 query
        int q = apiUrl.IndexOf('?');
        string path = q >= 0 ? apiUrl.Substring(0, q) : apiUrl;

        // 去掉 scheme://host:port
        int schemeEnd = path.IndexOf("://");
        if (schemeEnd >= 0)
        {
            int firstSlash = path.IndexOf('/', schemeEnd + 3);
            if (firstSlash < 0)
            {
                return null;
            }

            path = path.Substring(firstSlash + 1);
        }

        path = path.Trim('/');
        if (path.Length == 0)
        {
            return null;
        }

        return path.Replace('/', '_') + ".json";
    }
}
