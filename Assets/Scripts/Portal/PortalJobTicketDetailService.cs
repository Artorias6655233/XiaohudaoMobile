using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// 打开一张作业票的三步调用，产出 <see cref="PortalTicketDetail"/>。
///
///   1. <c>GET  iflow-task/flow/view/{flowKey}/{sid}</c>   → 流程头、当前环节、可用按钮、formKey
///   2. <c>POST idaf-server/formBaseInfo/detail</c>        → 段定义 + 表单定义（中文标签、审批变量）
///   3. <c>POST idaf-server/formData/get</c>（逐段）        → 每段的数据
///
/// 三处与接口文档不符的地方（都已实测确认，照文档写会拿不到数据）：
///
///   - 第 1 步返回的是 <c>body.tasks</c>【数组】，文档写的是 <c>body.task</c> 单数。
///     当前环节靠 <c>endTime == null</c> 认，<c>active</c> 在 view 模式下恒为 false。
///   - 第 2 步的请求体是**裸字符串 formKey**，传 <c>{"sid":…}</c> 之类一律回
///     <c>{"success":false,"code":"700","message":"JSON转换异常"}</c>。
///   - 第 3 步 <c>params</c> 是一个**字符串化的 JSON**，且响应的 <c>body</c> 还要再 parse 一次。
///     文档里那个写成 base64 常量的 <c>key</c> 可以不传。
///
/// 【为什么用 flow/view 而不是 flow/handle】两者都能取到票面，但 handle 是"进入办理"的语义。
/// 只读查看一律走 view，避免在服务端留下办理痕迹。
/// </summary>
public static class PortalJobTicketDetailService
{
    /// <summary>下拉数据源段的标题前缀。这些段装的是下拉框选项，不是票面内容。</summary>
    private const string DropdownPrefix = "下拉_";

    /// <summary>
    /// 附件段的标题前缀（兜底识别）。
    ///
    /// 首选是看表单有没有整段绑 <c>uploadWidget</c> / <c>imgWidget</c>
    /// （<see cref="PortalFormDefinition.IsAttachmentSection"/>），那是表单自己声明的，最可靠。
    /// 但已结束的票有 9 个附件段而当前环节的表单只绑了其中一两个，所以标题前缀作为兜底保留。
    /// </summary>
    private static readonly string[] AttachmentPrefixes = { "文件_", "图片_" };

    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };

    /// <summary>拉取结果。</summary>
    public sealed class Outcome
    {
        public bool Ok;

        /// <summary>失败原因，可直接显示给用户。</summary>
        public string Error;

        public PortalTicketDetail Detail;
    }

    /// <summary>
    /// 打开一张票。<paramref name="ticket"/> 用列表里那一行 —— flowKey 取它的
    /// <c>processDefinitionKey</c>（服务端下发，不猜）。
    /// </summary>
    public static IEnumerator Fetch(JobTicket ticket, Outcome outcome)
    {
        if (outcome == null) yield break;

        if (ticket == null || string.IsNullOrEmpty(ticket.sid))
        {
            outcome.Error = "没有选中作业票";
            yield break;
        }

        if (!PortalJobTicketService.ResolveFlowKey(ticket, out string flowKey))
        {
            outcome.Error = "这张票的作业类型无法识别，打不开票面";
            yield break;
        }

        yield return IccAuth.EnsureToken();
        if (!IccAuth.HasToken)
        {
            outcome.Error = "门户登录失败，请检查网络";
            yield break;
        }

        // --- 1/3 流程头 ---
        Reply viewReply = new Reply();
        yield return Request("GET", Api.FlowView(flowKey, ticket.sid), null, "票面流程", viewReply);

        if (viewReply.Envelope == null)
        {
            // 实测这里最常见的不是网络问题，而是服务端回 success=false、
            // message="流程记录未找到"（台账里有这张票，但流程实例没了）。
            // 原样转述服务端那句话：它能直接告诉运维是哪种情况，"获取失败"不能。
            outcome.Error = viewReply.Message ?? "作业票流程信息获取失败";
            yield break;
        }

        PortalEnvelope viewEnvelope = viewReply.Envelope;

        JObject view = viewEnvelope.BodyObject();
        JArray tasks = view?["tasks"] as JArray;
        if (tasks == null || tasks.Count == 0)
        {
            // 草稿票没有流程实例，tasks 是空的。这不是接口坏了，要说清楚。
            outcome.Error = "这张票还没有进入流程（草稿），没有票面可看";
            yield break;
        }

        // 当前环节 = 还没结束的那个；全都结束了（已归档的票）就取最后一个
        JObject current = tasks.OfType<JObject>()
            .FirstOrDefault(t => t["endTime"] == null || t["endTime"].Type == JTokenType.Null)
            ?? tasks.OfType<JObject>().LastOrDefault();

        if (current == null)
        {
            outcome.Error = "取不到当前流程环节";
            yield break;
        }

        PortalTicketDetail detail = new PortalTicketDetail
        {
            Sid = ticket.sid,
            FlowKey = flowKey,
            TaskName = (string)current["taskName"],
            FormKey = (string)current["formKey"],
            FormInsId = (string)view["formInsId"],
            FlowInsId = (string)view["flowInsId"],
            TaskInsId = (string)current["taskInsId"],
        };

        // 按钮由服务端下发，客户端照它渲染，不要自己硬编码"有没有审批权限"
        foreach (JObject button in (current["btns"] as JArray ?? new JArray()).OfType<JObject>())
        {
            string key = (string)button["key"];
            if (!string.IsNullOrEmpty(key)) detail.Buttons[key] = (string)button["label"] ?? key;
        }

        if (string.IsNullOrEmpty(detail.FormKey))
        {
            outcome.Error = "这个环节没有关联表单，打不开票面";
            yield break;
        }

        // --- 2/3 段定义 + 表单定义 ---
        Reply detailReply = new Reply();
        // 请求体是裸字符串 formKey。用 JValue 序列化保证带引号且转义正确。
        yield return Request("POST", Api.FormBaseInfoDetail(),
            new JValue(detail.FormKey).ToString(Newtonsoft.Json.Formatting.None),
            "票面表单", detailReply);

        if (detailReply.Envelope == null)
        {
            outcome.Error = detailReply.Message ?? "作业票表单定义获取失败";
            yield break;
        }

        PortalEnvelope detailEnvelope = detailReply.Envelope;

        JObject form = detailEnvelope.BodyObject();
        JArray defs = form?["datadefList"] as JArray;
        if (defs == null || defs.Count == 0)
        {
            outcome.Error = "作业票表单没有段定义";
            yield break;
        }

        PortalFormDefinition definition = PortalFormDefinition.Parse(form);
        detail.Approval = definition.Approval;

        // --- 3/3 逐段取数据 ---
        foreach (JObject def in defs.OfType<JObject>())
        {
            string title = (string)def["title"] ?? string.Empty;
            string name = (string)def["name"];

            // 下拉数据源不是票面内容，跳过。不跳的话票面上会多出几十行企业名单。
            if (title.StartsWith(DropdownPrefix, StringComparison.Ordinal)) continue;

            detail.SectionsFetched++;

            Reply sectionReply = new Reply();
            yield return Request("POST", Api.FormDataGet(),
                BuildSectionBody((string)def["sid"], detail.FormKey, detail.FormInsId),
                "票面段「" + title + "」", sectionReply);

            // 单段失败不整票失败：少一段总比整张票打不开好，日志里已经记了是哪一段
            if (sectionReply.Envelope == null) continue;

            JToken sectionBody = sectionReply.Envelope.UnwrapBody();

            // 这一段里有没有 expVars 要的值？有就顺手取走 —— 段数据只在这里出现一次，
            // 错过就得为了一个 business_sid 再拉一遍全部段。
            CollectFlowVars(definition, name, sectionBody, detail);

            // 审批段要单独留一份原始数据：它很可能还没填过，HasContent=false 会被下面剔掉，
            // 而提交时正需要它（读改写，见 PortalTicketDetail.ApprovalRawData）。
            if (detail.Approval != null &&
                string.Equals(name, detail.Approval.SectionName, StringComparison.Ordinal))
            {
                detail.ApprovalDataDefId = (string)def["sid"];
                JToken approvalRow = sectionBody is JArray approvalRows
                    ? (approvalRows.Count > 0 ? approvalRows[0] : null)
                    : sectionBody;
                if (approvalRow is JObject)
                {
                    detail.ApprovalRawData = approvalRow.ToString(Newtonsoft.Json.Formatting.None);
                }
            }

            PortalTicketDetail.Section section = BuildSection(def, name, title, definition,
                sectionBody);

            if (section.HasContent) detail.Sections.Add(section);
            else detail.SectionsEmpty++;
        }

        outcome.Detail = detail;
        outcome.Ok = true;

        Debug.Log($"[票面] {ticket.jobTicketType} {ticket.sid}：当前环节「{detail.TaskName}」，" +
                  $"{detail.Sections.Count}/{detail.SectionsFetched} 段有内容，" +
                  $"按钮 [{string.Join("/", detail.Buttons.Keys)}]，" +
                  $"审批变量 {(detail.Approval != null ? detail.Approval.VarsKey : "(无)")}，" +
                  $"流程变量 [{string.Join(", ", detail.FlowVars.Select(kv => kv.Key + "=" + Abbrev(kv.Value)))}]");
    }

    // ------------------------------------------------------------------ 组装

    private static PortalTicketDetail.Section BuildSection(JObject def, string name, string title,
        PortalFormDefinition definition, JToken data)
    {
        PortalTicketDetail.Section section = new PortalTicketDetail.Section
        {
            Name = name,
            Title = definition.GetSectionLabel(name) ?? title,
            IsAttachment = definition.IsAttachmentSection(name) ||
                           AttachmentPrefixes.Any(p => title.StartsWith(p, StringComparison.Ordinal)),
        };

        if (data == null) return section;

        if (section.IsAttachment)
        {
            foreach (JObject row in AsRows(data))
            {
                PortalTicketDetail.Attachment attachment = ToAttachment(row);
                if (attachment != null) section.Attachments.Add(attachment);
            }
            return section;
        }

        if (data is JArray array)
        {
            foreach (JObject row in array.OfType<JObject>())
            {
                List<PortalTicketDetail.Entry> entries = ToEntries(row, name, definition);
                if (entries.Count > 0) section.Rows.Add(entries);
            }
            return section;
        }

        if (data is JObject obj) section.Entries = ToEntries(obj, name, definition);
        return section;
    }

    /// <summary>
    /// 把一行数据转成票面上的若干行。
    ///
    /// **只保留表单上有的字段**：返回的 40 个键里有一半是 <c>SID</c> / <c>CREATE_BY</c> /
    /// <c>SYS_ORG_CODE</c> 这类系统列，它们在表单定义里没有控件、也没有中文名。
    /// 全显示出来票面会被一堆 UUID 淹掉，而且列名是英文的，用户读不懂。
    /// </summary>
    private static List<PortalTicketDetail.Entry> ToEntries(JObject row, string sectionName,
        PortalFormDefinition definition)
    {
        List<KeyValuePair<int, PortalTicketDetail.Entry>> ordered =
            new List<KeyValuePair<int, PortalTicketDetail.Entry>>();

        foreach (JProperty property in row.Properties())
        {
            PortalFormDefinition.Field field = definition.GetField(sectionName, property.Name);
            if (field == null || field.Hidden) continue;

            string value = Stringify(property.Value);
            bool isImage = field.IsImage || LooksLikeDataUrl(value);

            ordered.Add(new KeyValuePair<int, PortalTicketDetail.Entry>(field.Order,
                new PortalTicketDetail.Entry
                {
                    Label = string.IsNullOrEmpty(field.Label) ? property.Name : field.Label,
                    Value = isImage ? DescribeImage(value) : FormatValue(property.Name, value),
                    IsImage = isImage,
                }));
        }

        ordered.Sort((a, b) => a.Key.CompareTo(b.Key));
        return ordered.Select(pair => pair.Value).ToList();
    }

    private static PortalTicketDetail.Attachment ToAttachment(JObject row)
    {
        string path = (string)row["FILEPATH"];
        if (string.IsNullOrEmpty(path)) return null;

        string fileName = (string)row["FILENAME"] ?? (string)row["NAME"] ?? path;
        string fileType = (string)row["FILETYPE"] ?? string.Empty;

        return new PortalTicketDetail.Attachment
        {
            FileName = fileName,
            FilePath = path,
            FileType = fileType,
            IsImage = ImageExtensions.Any(ext =>
                          fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ||
                          path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) ||
                      fileType.IndexOf("image", StringComparison.OrdinalIgnoreCase) >= 0,
        };
    }

    /// <summary>
    /// 审批结果是 1/0，直接显示数字没人看得懂。
    ///
    /// 字段名在 8 种作业类型里都是 <c>APPROVAL_RESULT</c>（实测），语义见接口文档 §3.3：
    /// 1 通过 / 0 不通过。
    /// </summary>
    private static string FormatValue(string fieldName, string value)
    {
        if (!string.Equals(fieldName, "APPROVAL_RESULT", StringComparison.Ordinal)) return value;
        if (value == "1") return "通过";
        if (value == "0") return "不通过";
        return value;
    }

    /// <summary>签名和图片字段装的是 base64 data URL，只说有没有，不把内容塞进 UI。</summary>
    private static string DescribeImage(string value)
    {
        return string.IsNullOrEmpty(value) ? string.Empty : "已签名";
    }

    private static bool LooksLikeDataUrl(string value)
    {
        return value != null && value.StartsWith("data:", StringComparison.OrdinalIgnoreCase);
    }

    private static string Stringify(JToken value)
    {
        if (value == null || value.Type == JTokenType.Null) return string.Empty;
        return value.Type == JTokenType.String ? (string)value : value.ToString();
    }

    private static IEnumerable<JObject> AsRows(JToken data)
    {
        if (data is JArray array) return array.OfType<JObject>();
        if (data is JObject obj) return new[] { obj };
        return Enumerable.Empty<JObject>();
    }

    /// <summary>日志里截断长值 —— business_sid 这种雪花 id 占一行，看不清别的。</summary>
    private static string Abbrev(string v)
    {
        if (string.IsNullOrEmpty(v)) return "(空)";
        return v.Length <= 10 ? v : v.Substring(0, 4) + "…" + v.Substring(v.Length - 4);
    }

    /// <summary>
    /// 把这一段里 <c>expVars</c> 声明要用的字段值收进 <see cref="PortalTicketDetail.FlowVars"/>。
    ///
    /// 多行段取第一行：实测 expVars 指向的都是单条段（基础段的 SID、审批段的 APPROVAL_RESULT），
    /// 真遇到多行也只能取第一行，并在日志里留痕，不静默。
    /// </summary>
    private static void CollectFlowVars(PortalFormDefinition definition, string sectionName,
        JToken sectionBody, PortalTicketDetail detail)
    {
        if (definition == null || string.IsNullOrEmpty(sectionName)) return;

        foreach (PortalFormDefinition.ApprovalVar v in definition.AllVars)
        {
            if (!string.Equals(v.SectionName, sectionName, StringComparison.Ordinal)) continue;

            List<JObject> rows = AsRows(sectionBody).ToList();
            if (rows.Count == 0) continue;
            if (rows.Count > 1)
            {
                Debug.LogWarning($"[票面] 流程变量 {v.VarsKey} 指向的段「{sectionName}」有 " +
                                 rows.Count + " 行，取第一行");
            }

            JToken cell = rows[0][v.FieldName];
            if (cell == null || cell.Type == JTokenType.Null) continue;

            detail.FlowVars[v.VarsKey] = cell.ToString();
        }
    }

    /// <summary>
    /// 第 3 步的请求体。<c>params</c> 是**字符串化的 JSON**，写成嵌套对象服务端会回「JSON转换异常」。
    /// </summary>
    private static string BuildSectionBody(string dataDefId, string formKey, string formInsId)
    {
        JObject inner = new JObject
        {
            ["form.id"] = formKey,
            ["form.insId"] = formInsId,
        };

        JObject payload = new JObject
        {
            ["dataDefId"] = dataDefId,
            ["params"] = PortalEnvelope.EmbedJson(inner),
        };

        return payload.ToString(Newtonsoft.Json.Formatting.None);
    }

    /// <summary>
    /// 一次请求的结果。要单独一个类是因为**业务失败的那句话必须带回来**：
    /// 服务端的「流程记录未找到」比客户端编的「获取失败」有用得多。
    /// </summary>
    private sealed class Reply
    {
        /// <summary>业务成功时的响应壳；失败为 null。</summary>
        public PortalEnvelope Envelope;

        /// <summary>失败原因，优先是服务端给的话。成功时为 null。</summary>
        public string Message;
    }

    /// <summary>
    /// 发一次请求，撞 401 重登再试一次。读请求也不回退镜像，理由同
    /// <see cref="PortalJobTicketService"/>：镜像不区分请求体，会把别的票的内容显示成这一张的。
    /// </summary>
    private static IEnumerator Request(string method, string url, string body, string who, Reply reply)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            OfflineMock.Result fetch = new OfflineMock.Result();
            Dictionary<string, string> headers = new Dictionary<string, string>
            {
                { "token", IccAuth.Token },
            };

            if (method == "GET") yield return OfflineMock.FetchStrict(url, fetch, headers);
            else
            {
                yield return OfflineMock.PostJsonStrict(url, body, headers, fetch,
                    "application/json", OfflineMock.NetworkTimeoutSeconds);
            }

            if (fetch.httpCode == 401 && attempt == 0)
            {
                Debug.LogWarning($"[票面] {who} 返回 401，重新登录后重试一次");
                yield return IccAuth.EnsureToken(true);
                if (!IccAuth.HasToken) break;
                continue;
            }

            if (!fetch.ok)
            {
                reply.Message = "网络请求失败";
                yield break;
            }

            if (!PortalEnvelope.TryParse(fetch.json, out PortalEnvelope envelope, out string error))
            {
                Debug.LogError($"[票面] {who} 响应解析失败：{error}");
                reply.Message = "响应格式不对";
                yield break;
            }

            if (!envelope.Success)
            {
                Debug.LogError($"[票面] {who} 服务端返回失败：code={envelope.Code}，message={envelope.Message}");
                reply.Message = string.IsNullOrWhiteSpace(envelope.Message) ? "服务端返回失败" : envelope.Message;
                yield break;
            }

            reply.Envelope = envelope;
            yield break;
        }

        reply.Message = reply.Message ?? "登录失效，请重试";
    }
}
