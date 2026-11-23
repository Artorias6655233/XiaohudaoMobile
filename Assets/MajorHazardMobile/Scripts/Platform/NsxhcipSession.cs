using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace LZW.Module.Nsxhcip
{
    /// <summary>
    /// 南沙小虎化工园区平台 (www.nsxhcip.cn) 登录态。
    /// 登录后返回 token，后续请求带 "token" 请求头即可；token 失效时接口会 303 跳转到 /portal/，此时自动重新登录一次。
    /// </summary>
    public static class NsxhcipSession
    {
        public const string Host = "https://www.nsxhcip.cn";

        private const string LoginUrl = Host + "/portal/login/tel/sms";

        // 平台固定账号
        private const string Username = "xhdAdmin";
        private const string Password = "xhd123@Admin";
        private const string Captcha = "123";

        public static string Token { get; private set; }

        /// <summary>
        /// 登录用户 id（principal.sid），单点登录子系统时作为 portalUserId。
        /// </summary>
        public static string UserId { get; private set; }

        /// <summary>
        /// 登录用户姓名（principal.realName）。
        /// </summary>
        public static string RealName { get; private set; }

        public static IEnumerator Login(int timeout, Action<bool> done)
        {
            var form = new List<IMultipartFormSection>
            {
                new MultipartFormDataSection("username", Username),
                // 与网页端一致：base64(md5(password))
                new MultipartFormDataSection("pwd", EncodePassword(Password)),
                new MultipartFormDataSection("captcha", Captcha)
            };

            using var request = UnityWebRequest.Post(LoginUrl, form);
            request.timeout = timeout;
            yield return request.SendWebRequest();

            Token = null;
            UserId = null;
            RealName = null;
            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    var json = JObject.Parse(request.downloadHandler.text);
                    if (json.Value<bool>("success"))
                    {
                        Token = json.Value<string>("token");
                        UserId = json.SelectToken("principal.sid")?.ToString();
                        RealName = json.SelectToken("principal.realName")?.ToString();
                    }
                    else
                        Debug.LogWarning($"[NsxhcipSession] 登录失败: {json.Value<string>("message")}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[NsxhcipSession] 登录响应解析失败: {e.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"[NsxhcipSession] 登录请求失败: {request.error}");
            }

            done(!string.IsNullOrEmpty(Token));
        }

        public static IEnumerator Get(string url, int timeout, Action<string> onSuccess)
        {
            return Send(() => UnityWebRequest.Get(url), timeout, onSuccess);
        }

        /// <param name="tokenInQuery">部分接口（如 double-prevention/riskTagRecord/find）还要求 token 出现在查询串里</param>
        public static IEnumerator PostJson(string url, string json, int timeout, Action<string> onSuccess, bool tokenInQuery = false)
        {
            return Send(() =>
            {
                // 在登录之后才创建请求，这里取到的是最新的 token
                var requestUrl = tokenInQuery
                    ? $"{url}{(url.Contains("?") ? "&" : "?")}token={UnityWebRequest.EscapeURL(Token)}"
                    : url;
                var request = new UnityWebRequest(requestUrl, UnityWebRequest.kHttpVerbPOST)
                {
                    uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                    downloadHandler = new DownloadHandlerBuffer()
                };
                request.SetRequestHeader("Content-Type", "application/json");
                return request;
            }, timeout, onSuccess);
        }

        /// <summary>
        /// 未登录时先登录；登录失效（3xx/401）时重新登录并重试一次。失败时不调用 onSuccess。
        /// </summary>
        private static IEnumerator Send(Func<UnityWebRequest> create, int timeout, Action<string> onSuccess)
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

                using var request = create();
                request.timeout = timeout;
                request.redirectLimit = 0;
                request.SetRequestHeader("token", Token);
                // 流程子系统 iflow-task（特殊作业票证审批）及其代理的 jeecg 接口只认 cookie 里的 token（网页端 axios 拦截器写 cookie）
                request.SetRequestHeader("Cookie", $"token={Token}");
                request.SetRequestHeader("X-Axios-With", "true");
                yield return request.SendWebRequest();

                var code = request.responseCode;
                if (code == 401 || (code >= 300 && code < 400))
                {
                    Token = null;
                    continue;
                }

                if (request.result == UnityWebRequest.Result.Success)
                    onSuccess(request.downloadHandler.text);
                else
                    Debug.LogWarning($"[NsxhcipSession] 请求失败: {request.url} {request.error}");
                yield break;
            }

            Debug.LogWarning("[NsxhcipSession] 重新登录后仍未授权");
        }

        private static string EncodePassword(string password)
        {
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(password));
            var hex = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
                hex.Append(b.ToString("x2"));
            return Convert.ToBase64String(Encoding.ASCII.GetBytes(hex.ToString()));
        }
    }
}
