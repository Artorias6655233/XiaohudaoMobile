using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// 审批提交：<c>formData/saveall</c> → <c>flow/handle</c>，顺序不可颠倒。
///
/// 【封存中，没有任何地方调用它】2026-09-23 产品决定：通过 / 不通过 / 签名全部只留在
/// 眼镜本地（<see cref="JobTicketApprovalStore"/>），不回传门户。这个类连同它查清楚的
/// swagger 路径和 <c>{key}</c> 语义一起留着，等哪天要真回传时直接接回去 ——
/// 那时**权限判断要一并回来**：本地处置可以不看账号权限，往生产写不行。
/// 在那之前不要调用它，它每跑一次都会改动门户的生产数据。
///
/// 【这是整个工程里唯一会改动门户生产数据的链路】所以这里的每个决定都偏保守：
/// 不回退离线镜像、不在结果未知时重试、半途失败时如实说清楚已经写进去了什么。
///
/// 【saveall 的路径与接口文档不一致，以服务端 swagger 为准】
/// 文档写 <c>saveall/{formKey}/{key}</c>，服务端声明是 <c>saveall/{formId}/{key}</c>，
/// 其中 <c>formId</c> 是 <b>integer</b>，取 <c>flow/view</c> 里当前 task 的 <c>formKey</c>。
///
/// 【签名不提交】按产品决定，签名只是眼镜端选一张预置图做象征性动作，结果留在本地
/// （<see cref="PortalApprovalDraft"/>），不写 <c>APPROVAL_SIGNATURE</c>。
/// </summary>
public static class PortalApprovalSubmitService
{
    /// <summary>
    /// <c>saveall</c> 路径上的第二段：表单授权 key。
    ///
    /// 接口文档把它描述成「写死的 base64 常量，原样照抄即可（其语义未确认）」，但没给值。
    /// 实测查清楚了它是什么：表单自己的 <c>formDefine.formParam</c> 第三项就叫 <c>key</c>、
    /// 标题「表单授权key」；同一个 <c>{formId}/{key}</c> 组合出现在
    /// <c>formAuthorize/loadPromis</c> 和 <c>deleteByPromisKey</c> 上，对应实体字段是
    /// <c>FormPromis.promisKey</c>。
    ///
    /// 而这套授权数据在本环境**整个是空的**：<c>FormScene</c> 全表 0 行，
    /// <c>loadPromis</c> 对任意 key 都返回 <c>{}</c>，<c>getPromis</c> 同样查不到记录。
    /// 也就是说它当前只是个查不到东西的索引键，取什么值都一样。取一个能在服务端日志里
    /// 认得出是谁发的值，比照抄一个来历不明的 base64 常量更有用。
    ///
    /// 万一以后后端真的配了表单授权，这里会从「取什么都行」变成「必须取对」，
    /// 表现是 saveall 被拒。那时改这一个常量即可。
    /// </summary>
    public const string AuthorizeKey = "mr-glass";

    /// <summary>提交结果。</summary>
    public sealed class Outcome
    {
        public bool Ok;

        /// <summary>失败原因，优先用服务端原话。</summary>
        public string Error;

        /// <summary>
        /// 表单已写入但流程没推动，或者结果未知。
        ///
        /// 这两种都**不能提示用户重试** —— 重试会产生第二次审批。UI 要引导用户去门户端回查。
        /// </summary>
        public bool NeedsManualCheck;
    }

    /// <summary>
    /// 提交一次审批。
    /// </summary>
    /// <param name="detail">刚拉下来的票面，提供三个流程 id、审批段和流程变量。</param>
    /// <param name="passed">true = 通过，写 <c>APPROVAL_RESULT=1</c>；false = 不通过，写 0。</param>
    /// <param name="opinion">审批意见，写 <c>APPROVAL_OPINION</c>。服务端下发的
    /// <c>audit</c> 按钮带 <c>opinionRequired=true</c>，所以这里不接受空值。</param>
    public static IEnumerator Submit(PortalTicketDetail detail, bool passed, string opinion,
        Outcome outcome)
    {
        if (detail == null || !detail.CanAudit)
        {
            outcome.Error = "当前环节不可审批";
            yield break;
        }

        if (string.IsNullOrWhiteSpace(opinion))
        {
            outcome.Error = "审批意见不能为空";
            yield break;
        }

        string result = passed ? "1" : "0";

        // --- 1/2 写表单 ---
        string saveBody = BuildSaveAllBody(detail, result, opinion.Trim());
        if (saveBody == null)
        {
            outcome.Error = "审批段数据缺失，无法提交";
            yield break;
        }

        Reply save = new Reply();
        yield return Post(Api.FormDataSaveAll(detail.FormKey, AuthorizeKey), saveBody, "存表单", save);

        if (!save.Ok)
        {
            outcome.Error = save.Message;
            // 写请求超时：服务端可能已经存了，不能当作「什么都没发生」
            outcome.NeedsManualCheck = save.ResultUnknown;
            yield break;
        }

        // --- 2/2 推流程 ---
        Reply handle = new Reply();
        yield return Post(Api.FlowHandle(), BuildHandleBody(detail, result), "推流程", handle);

        if (!handle.Ok)
        {
            outcome.Error = handle.Message;
            // 到这里表单已经写进去了，但流程没动。这是个半截状态，必须让用户知道。
            outcome.NeedsManualCheck = true;
            yield break;
        }

        outcome.Ok = true;
        Debug.Log($"[审批] 已提交 {detail.Sid}：{(passed ? "通过" : "不通过")}，" +
                  $"环节「{detail.TaskName}」，变量 {detail.Approval.VarsKey}={result}");
    }

    // ------------------------------------------------------------------ 请求体

    /// <summary>
    /// <c>saveall</c> 的请求体：<c>Params&lt;List&lt;FormDataDto&gt;&gt;</c>。
    ///
    /// <c>formData</c> 和 <c>params</c> 都是**字符串化的 JSON**，写成嵌套对象服务端会回
    /// 「JSON转换异常」—— 和 <c>formData/get</c> 的 <c>params</c> 同一个坑。
    ///
    /// 走读改写：把拉下来的那条审批记录整份带回去，只改两个字段。只发两个字段的话
    /// 系统列（<c>SID</c> 等）全丢，服务端会当新增插一行而不是更新当前这条。
    /// </summary>
    private static string BuildSaveAllBody(PortalTicketDetail detail, string result, string opinion)
    {
        JObject row;
        if (string.IsNullOrEmpty(detail.ApprovalRawData))
        {
            return null;
        }

        try
        {
            row = JObject.Parse(detail.ApprovalRawData);
        }
        catch (Exception e)
        {
            Debug.LogError($"[审批] 审批段原始数据解析失败：{e.Message}");
            return null;
        }

        row["APPROVAL_RESULT"] = result;
        row["APPROVAL_OPINION"] = opinion;

        JObject inner = new JObject
        {
            ["form.id"] = detail.FormKey,
            ["form.insId"] = detail.FormInsId,
        };

        JObject dto = new JObject
        {
            ["dataDefId"] = detail.ApprovalDataDefId,
            ["formData"] = PortalEnvelope.EmbedJson(row),
            ["params"] = PortalEnvelope.EmbedJson(inner),
        };

        JObject payload = new JObject
        {
            ["body"] = new JArray { dto },
        };

        return payload.ToString(Newtonsoft.Json.Formatting.None);
    }

    /// <summary>
    /// <c>flow/handle</c> 的请求体。
    ///
    /// <c>result</c> 恒 true、<c>opinions</c> 恒 "1" —— 同意/驳回**不是**用它们表达的，
    /// 真正的结论在 <c>vars</c> 里当前环节自己的那个 <c>*_AuditResult</c>。
    ///
    /// 其余流程变量（<c>business_sid</c> 等）必须照原样带回去：服务端对 vars 缺键不报错，
    /// 缺了只会静默不生效，从客户端看不出区别。
    /// </summary>
    private static string BuildHandleBody(PortalTicketDetail detail, string result)
    {
        JObject vars = new JObject();
        foreach (KeyValuePair<string, string> kv in detail.FlowVars)
        {
            vars[kv.Key] = kv.Value;
        }

        vars[detail.Approval.VarsKey] = result;

        JObject payload = new JObject
        {
            ["flowInsId"] = detail.FlowInsId,
            ["formInsId"] = detail.FormInsId,
            ["taskInsId"] = detail.TaskInsId,
            ["opinions"] = "1",
            ["result"] = true,
            ["vars"] = vars,
        };

        return payload.ToString(Newtonsoft.Json.Formatting.None);
    }

    // ------------------------------------------------------------------ 发请求

    private sealed class Reply
    {
        public bool Ok;
        public string Message;
        public bool ResultUnknown;
    }

    /// <summary>
    /// 发一次写请求。
    ///
    /// 【只在 401 时重试】401 是服务端明确拒绝，重登后重发是安全的。其余失败一律不重试：
    /// 超时/连接中断时服务端可能已经处理完了，重发就是第二次审批。
    /// </summary>
    private static IEnumerator Post(string url, string body, string who, Reply reply)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            OfflineMock.Result fetch = new OfflineMock.Result();
            Dictionary<string, string> headers = new Dictionary<string, string>
            {
                { "token", IccAuth.Token },
            };

            yield return OfflineMock.PostJsonStrict(url, body, headers, fetch);

            if (fetch.httpCode == 401 && attempt == 0)
            {
                Debug.LogWarning($"[审批] {who} 返回 401，重新登录后重试一次");
                yield return IccAuth.EnsureToken(true);
                if (!IccAuth.HasToken) break;
                continue;
            }

            if (!fetch.ok)
            {
                reply.ResultUnknown = fetch.resultUnknown;
                reply.Message = fetch.resultUnknown
                    ? who + "结果未知，请到门户端确认后再决定要不要重提"
                    : who + "失败：" + (fetch.error ?? "网络请求失败");
                yield break;
            }

            if (!PortalEnvelope.TryParse(fetch.json, out PortalEnvelope envelope, out string error))
            {
                Debug.LogError($"[审批] {who} 响应解析失败：{error}");
                reply.Message = who + "的响应格式不对";
                yield break;
            }

            if (!envelope.Success)
            {
                Debug.LogError($"[审批] {who} 服务端返回失败：code={envelope.Code}，" +
                               $"message={envelope.Message}");
                reply.Message = string.IsNullOrWhiteSpace(envelope.Message)
                    ? who + "被服务端拒绝" : envelope.Message;
                yield break;
            }

            reply.Ok = true;
            yield break;
        }

        reply.Message = who + "失败：登录已失效";
    }
}
