using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// 门户后端（<c>www.nsxhcip.cn</c> 的 <c>portal</c> / <c>iflow-task</c> / <c>idaf-server</c>）
/// 的统一响应壳：<c>{ success, code, message, body, total }</c>。
///
/// 【为什么要单独一层】这套后端有三件事会让"看起来成功"和"真的成功"错开：
///
///   1. HTTP 200 不代表业务成功。取不到数据、入参形状不对，都是 200 +
///      <c>{"success":false,"code":"700","message":"JSON转换异常"}</c>。
///      只看 HTTP 状态码会把这些当成功。
///   2. <c>body</c> 有时是对象、有时是数组、有时**是一个 JSON 字符串**
///      （<c>idaf-server</c> 的 <c>formData/get</c> 就是），要再 parse 一次。
///      直接当对象用会静默拿到 null。
///   3. 分页的总数在**壳上**的 <c>total</c>，不在 <c>body</c> 里。
///      （注意和双重预防那套 <c>{status, message, body:{data,total}}</c> 区分开，
///      那是另一套壳，不归本类管。）
///
/// 所以所有门户响应都先过这里，不要在业务代码里各写一遍 <c>JObject.Parse</c>。
///
/// 用 Newtonsoft 而不是 <c>JsonUtility</c>：<c>body</c> 的形状不固定，
/// <c>JsonUtility</c> 需要预先定义强类型且不支持顶层数组，这里用不了。
/// </summary>
public sealed class PortalEnvelope
{
    /// <summary>业务是否成功。注意这和 HTTP 200 是两回事。</summary>
    public bool Success { get; private set; }

    /// <summary>失败码，如 <c>"700"</c>（JSON转换异常）。成功时通常为 null。</summary>
    public string Code { get; private set; }

    /// <summary>服务端给的提示文字。失败时应原样展示给用户，不要自己编。</summary>
    public string Message { get; private set; }

    /// <summary>分页总数。壳上没有这个字段时为 -1，用来和"确实是 0 条"区分开。</summary>
    public int Total { get; private set; }

    /// <summary>原始的 <c>body</c>。可能是对象、数组、字符串或 null。</summary>
    public JToken Body { get; private set; }

    private PortalEnvelope() { }

    /// <summary>
    /// 解析响应壳。
    ///
    /// 只要能认出壳就返回 true，<b>业务成功与否要另看 <see cref="Success"/></b> ——
    /// 这两件事分开，调用方才能把服务端的 <see cref="Message"/> 原样报给用户。
    /// </summary>
    /// <param name="json">HTTP 响应正文。</param>
    /// <param name="envelope">解析结果；失败时为 null。</param>
    /// <param name="error">解析失败的原因，可直接写进日志。</param>
    public static bool TryParse(string json, out PortalEnvelope envelope, out string error)
    {
        envelope = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "响应是空的";
            return false;
        }

        JObject root;
        try
        {
            root = JObject.Parse(json);
        }
        catch (Exception e)
        {
            // 只报长度和前几十个字符：响应里可能带账号、人名一类的东西
            error = $"不是合法 JSON（{json.Length} 字符）：{e.Message}";
            return false;
        }

        // 没有 success 字段就不是这套壳。不默认当成功 —— 那正是最危险的假设：
        // 换了个接口、换了套后端，响应形状变了，业务代码却一路当成功往下走。
        JToken success = root["success"];
        if (success == null)
        {
            error = $"响应里没有 success 字段，不是门户响应壳（顶层字段：{string.Join("/", GetNames(root))}）";
            return false;
        }

        envelope = new PortalEnvelope
        {
            Success = success.Type == JTokenType.Boolean && (bool)success,
            Code = root["code"]?.Type == JTokenType.Null ? null : (string)root["code"],
            Message = root["message"]?.Type == JTokenType.Null ? null : (string)root["message"],
            Body = root["body"],
            Total = ReadTotal(root["total"]),
        };

        error = null;
        return true;
    }

    /// <summary>
    /// 解析并要求业务成功。失败时打一行日志并返回 false，调用方直接 return 即可。
    ///
    /// 这是最常用的入口；只有需要把失败信息展示给用户时才用 <see cref="TryParse"/>。
    /// </summary>
    /// <param name="who">日志前缀，如 "作业票列表"，用来定位是哪个调用失败的。</param>
    public static bool TryParseSuccess(string json, string who, out PortalEnvelope envelope)
    {
        if (!TryParse(json, out envelope, out string error))
        {
            Debug.LogError($"[门户] {who} 响应解析失败：{error}");
            envelope = null;
            return false;
        }

        if (!envelope.Success)
        {
            Debug.LogError($"[门户] {who} 服务端返回失败：code={envelope.Code}，message={envelope.Message}");
            envelope = null;
            return false;
        }

        return true;
    }

    /// <summary>
    /// 取 <c>body</c> 并按需二次反序列化。
    ///
    /// <c>idaf-server</c> 的 <c>formData/get</c> 把整个段数据塞成一个 JSON **字符串**
    /// 放在 <c>body</c> 里，要再 parse 一次才拿得到对象。别的接口的 <c>body</c>
    /// 本身就是对象或数组。这里统一处理：是字符串就再 parse，不是就原样返回。
    ///
    /// 返回 null 表示 <c>body</c> 为空（不是错误 —— 比如还没人审批的段就是空的）。
    /// </summary>
    public JToken UnwrapBody()
    {
        if (Body == null || Body.Type == JTokenType.Null) return null;

        if (Body.Type != JTokenType.String) return Body;

        string inner = (string)Body;
        if (string.IsNullOrWhiteSpace(inner)) return null;

        try
        {
            return JToken.Parse(inner);
        }
        catch (Exception e)
        {
            // 走到这里说明 body 是个普通字符串而不是嵌套 JSON。
            // 不静默吞掉：调用方期待的是结构化数据，拿到 null 会一路显示空白。
            Debug.LogWarning($"[门户] body 是字符串但不是嵌套 JSON（{inner.Length} 字符）：{e.Message}");
            return null;
        }
    }

    /// <summary>取 <c>body</c> 里的数组；不是数组时返回空数组而不是 null，免得调用方到处判空。</summary>
    public JArray BodyArray()
    {
        return UnwrapBody() as JArray ?? new JArray();
    }

    /// <summary>取 <c>body</c> 里的对象；不是对象时返回 null（空对象和"没有"要能区分）。</summary>
    public JObject BodyObject()
    {
        return UnwrapBody() as JObject;
    }

    /// <summary>
    /// 把一个对象包成 <c>idaf-server</c> 要的"JSON 字符串"形态。
    ///
    /// <c>formData/get</c> 的 <c>params</c> 是入参侧的同一个坑：它要的是一个
    /// 字符串化之后的 JSON，不是嵌套对象。写成对象服务端会回「JSON转换异常」。
    /// </summary>
    public static string EmbedJson(JObject value)
    {
        return (value ?? new JObject()).ToString(Newtonsoft.Json.Formatting.None);
    }

    private static int ReadTotal(JToken total)
    {
        if (total == null || total.Type == JTokenType.Null) return -1;

        // 实测返回的是数字，但这套后端别处有把数字写成字符串的先例，两种都收
        if (total.Type == JTokenType.Integer) return (int)total;
        return int.TryParse((string)total, out int parsed) ? parsed : -1;
    }

    private static System.Collections.Generic.IEnumerable<string> GetNames(JObject root)
    {
        foreach (JProperty property in root.Properties()) yield return property.Name;
    }
}
