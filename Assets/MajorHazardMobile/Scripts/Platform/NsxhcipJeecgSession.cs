using System;
using System.Collections;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace LZW.Module.Nsxhcip
{
    /// <summary>
    /// 小虎平台 jeecg 子系统（https://www.nsxhcip.cn/jeecg/ext/...，危险源设备、评估报告等）登录态。
    /// 与门户 <see cref="NsxhcipSession"/> 的 token 不通用：网页端用门户 token 换取 jeecg 的 JWT（抓包所得）：
    ///   POST /jeecg/sys/access/login，请求体为 JSON 字符串 "门户 token" → result.token
    /// 之后请求带 X-Access-Token 请求头。JWT 失效时 jeecg 返回 401（或 200 + code 401），自动重新换取一次。
    /// </summary>
    public static class NsxhcipJeecgSession
    {
        public const string Host = NsxhcipSession.Host + "/jeecg";

        private const string LoginUrl = Host + "/sys/access/login";

        public static string Token { get; private set; }

        /// <summary>
        /// 用门户 token 换取 jeecg token。门户 token 失效时重新登录门户再试一次。
        /// </summary>
        public static IEnumerator Login(int timeout, Action<bool> done)
        {
            Token = null;
            for (var attempt = 0; attempt < 2 && Token == null; attempt++)
            {
                if (attempt > 0 || string.IsNullOrEmpty(NsxhcipSession.Token))
                {
                    var portalOk = false;
                    yield return NsxhcipSession.Login(timeout, ok => portalOk = ok);
                    if (!portalOk)
                        break;
                }

                using var request = CreatePost(LoginUrl, JsonConvert.ToString(NsxhcipSession.Token));
                request.timeout = timeout;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var json = JObject.Parse(request.downloadHandler.text);
                        if (json.Value<bool>("success"))
                            Token = json.SelectToken("result.token")?.ToString();
                        else
                            Debug.LogWarning($"[{nameof(NsxhcipJeecgSession)}] 登录失败: {json.Value<string>("message")}");
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[{nameof(NsxhcipJeecgSession)}] 登录响应解析失败: {e.Message}");
                    }
                }
                else
                {
                    Debug.LogWarning($"[{nameof(NsxhcipJeecgSession)}] 登录请求失败: {request.error}");
                }
            }

            done(!string.IsNullOrEmpty(Token));
        }

        public static IEnumerator Get(string url, int timeout, Action<string> onSuccess)
        {
            return Send(url, null, timeout, onSuccess);
        }

        public static IEnumerator PostJson(string url, string json, int timeout, Action<string> onSuccess)
        {
            return Send(url, json ?? "{}", timeout, onSuccess);
        }

        /// <summary>
        /// 未登录时先登录；token 失效时重新登录并重试一次。失败时不调用 onSuccess。
        /// </summary>
        private static IEnumerator Send(string url, string json, int timeout, Action<string> onSuccess)
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (string.IsNullOrEmpty(Token))
                {
                    var loggedIn = false;
                    yield return Login(timeout, ok => loggedIn = ok);
                    if (!loggedIn)
                        yield break;
                }

                using var request = json == null ? UnityWebRequest.Get(url) : CreatePost(url, json);
                request.timeout = timeout;
                request.SetRequestHeader("X-Access-Token", Token);
                request.SetRequestHeader("X-Tenant-Id", "0");
                yield return request.SendWebRequest();

                var text = request.downloadHandler?.text ?? "";
                if (request.responseCode == 401 || IsTokenExpired(text))
                {
                    Token = null;
                    continue;
                }

                if (request.result == UnityWebRequest.Result.Success)
                    onSuccess(text);
                else
                    Debug.LogWarning($"[{nameof(NsxhcipJeecgSession)}] 请求失败: {url} code={request.responseCode} {request.error}");
                yield break;
            }

            Debug.LogWarning($"[{nameof(NsxhcipJeecgSession)}] 重新登录后仍未授权: {url}");
        }

        private static bool IsTokenExpired(string text)
        {
            // jeecg 的 token 失效有时是 HTTP 200 + {"success":false,"code":401,"message":"Token失效，请重新登录!"}
            return text.Contains("\"code\":401") || text.Contains("Token失效");
        }

        private static UnityWebRequest CreatePost(string url, string json)
        {
            var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer()
            };
            request.SetRequestHeader("Content-Type", "application/json;charset=UTF-8");
            return request;
        }
    }
}
