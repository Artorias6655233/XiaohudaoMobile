using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// ICC（周界入侵告警）接口的登录与 Token 缓存。
///
/// 账号是单独开的测试账号，凭据按需求写死在这里。两点刻意的处理：
///   1. pwd 存的是【已经算好的】 btoa(md5(明文))，明文密码不进仓库，也就不用引入 MD5 实现；
///   2. 日志只打成败与长度，绝不打 Token 本体或 pwd —— 写进 logcat 就等于泄出去了。
///
/// Token 只放在进程内静态字段，不落盘。有效期未知，所以 401 时由调用方带 force
/// 重登一次（见 <see cref="ClosedPark_CameraMonitor"/>），不做定时刷新。
/// </summary>
public static class IccAuth
{
    private const string BuiltInUsername = "dmtest2";

    /// <summary>btoa(md5("&lt;明文&gt;"))，预先算好写死。改密码时要同步换这一串。</summary>
    private const string BuiltInPwd = "MDNjMjMwZWUyOWYyMjFmMzEwMGE2MDhiODQ4OTBmOWE=";

    /// <summary>
    /// 实际使用的账号：<see cref="ApiConfig"/> 配了就用配的，没配就用内置的。
    ///
    /// 【为什么要留这个口子】门户的待办是按账号岗位分派的，换账号看到的待办完全不同
    /// （实测 2026-09-22：dmtest2 的 audit 待办 0 条，xhdAdmin 有 2 条）。
    /// 联调期换账号比出包频繁得多，配置化之后现场 adb push 一份 api_config.json 就能换。
    ///
    /// 内置的仍是那个测试账号，权限范围小；权限更大的账号一律只放设备本机覆盖文件，
    /// 不进仓库。两条链路（罐区监测、周界告警）共用这个 token，换账号后要一起回归 ——
    /// 已实测 xhdAdmin 在这两条链路上返回与 dmtest2 完全一致。
    /// </summary>
    private static string Username =>
        ApiConfig.HasPortalAccountOverride ? ApiConfig.PortalUsername : BuiltInUsername;

    private static string Pwd =>
        ApiConfig.HasPortalAccountOverride ? ApiConfig.PortalPwd : BuiltInPwd;

    private const string ApiKey = "a7d96edc-0fb9-4b23-9190-c3fdc8ad3c6f";

    /// <summary>当前 Token；未登录成功时为 null。</summary>
    public static string Token { get; private set; }

    public static bool HasToken => !string.IsNullOrEmpty(Token);

    /// <summary>
    /// 确保有 Token。
    /// </summary>
    /// <param name="force">true 表示丢弃现有 Token 强制重登（401 后重试用）。</param>
    public static IEnumerator EnsureToken(bool force = false)
    {
        if (HasToken && !force)
        {
            yield break;
        }

        Token = null;

        WWWForm form = new WWWForm();
        form.AddField("username", Username);
        form.AddField("pwd", Pwd);
        form.AddField("apiKey", ApiKey);

        var fetch = new OfflineMock.Result();
        yield return OfflineMock.PostForm(Api.IccLogin(), form, fetch);

        if (!fetch.ok)
        {
            // 具体错误 OfflineMock.Log 已经打过一行，这里只补上"登录失败"这层语义
            Debug.LogError("[ICC] 登录失败，后续告警请求都会拿不到数据");
            yield break;
        }

        LoginResponse response = null;
        try
        {
            response = JsonUtility.FromJson<LoginResponse>(fetch.json);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ICC] 登录响应解析失败：{e.Message}（响应 {fetch.json.Length} 字符）");
            yield break;
        }

        if (response == null || string.IsNullOrEmpty(response.token))
        {
            // 只打长度不打内容：响应里可能带账号信息
            Debug.LogError($"[ICC] 登录响应里没有 token 字段（响应 {fetch.json.Length} 字符，来源：{fetch.source}）");
            yield break;
        }

        Token = response.token;
        // 账号名要打出来：待办为空时第一个要排除的就是"登录的是哪个账号"
        Debug.Log($"[ICC] 登录成功，账号 {Username}（{(ApiConfig.HasPortalAccountOverride ? "配置覆盖" : "内置")}），" +
                  $"token 长度 {Token.Length}，来源：{fetch.source}");
    }

    [System.Serializable]
    private class LoginResponse
    {
        public string token;
    }
}
