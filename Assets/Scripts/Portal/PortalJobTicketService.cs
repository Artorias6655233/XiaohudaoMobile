using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// 作业票列表的门户数据源：台账（<c>jobTicket/find</c>）+ 我的待办（<c>jobTicket/todo</c>）。
///
/// 【为什么是「台账为主、待办作标记」而不是「待办为主」】
/// 计划原本写的是列表主体用待办。实测 2026-09-22 否掉了这个方案：
///
///   - 待办是**按登录账号的岗位分派**的，和沙盘选中哪家企业没有关系。
///     xhdAdmin 的 2 条待办都挂在政府单位（广州市南沙区化工应急管理中心）名下，
///     园区里 51 家企业一家都没有。拿选中企业的编码去筛待办，恒定 0 条。
///   - 台账按 <c>applyingUnitId</c> 筛企业则精确可靠（抽查 8 家单位，条数与全量一致）。
///
/// 沙盘的主交互是「在地图上点一家企业」，所以列表主体必须是跟着企业走的台账；
/// 待办只用来回答「这张票轮到我审批了吗」，即 <see cref="Snapshot.IsActionable"/>。
///
/// 【flowKey 不要自己猜】每行都带 <c>processDefinitionKey</c>，实测 247/247 全有值、
/// 与作业类型严格一对一。打开票面用它，不要拿中文类型去查表 ——
/// 查表那条路要经过「列表写法 / 字典写法」的词汇漂移，而服务端已经把答案直接给了。
/// <see cref="PortalJobTicketCatalog"/> 退化成交叉校验用（见 <see cref="ResolveFlowKey"/>）。
/// </summary>
public static class PortalJobTicketService
{
    /// <summary>每页条数。门户实测 pageSize 给多少返回多少，没有静默上限。</summary>
    public const int PageSize = 100;

    /// <summary>最多翻几页。全量台账实测 247 条，5 页 500 条留了一倍余量。</summary>
    public const int MaxPages = 5;

    /// <summary>待办的三种类型。本期只用 audit（待审批）。</summary>
    public const string OperateAudit = "audit";

    /// <summary>一次拉取的结果。</summary>
    public sealed class Snapshot
    {
        /// <summary>拉取是否成功。false 时 <see cref="Error"/> 是可以直接显示给用户的一句话。</summary>
        public bool Ok;

        /// <summary>失败原因（面向用户）。成功时为 null。</summary>
        public string Error;

        /// <summary>台账行。成功时非 null，可能是空列表（该企业确实没有作业票）。</summary>
        public List<JobTicket> Tickets = new List<JobTicket>();

        /// <summary>在我的待审批队列里的票 sid。</summary>
        public HashSet<string> ActionableSids = new HashSet<string>();

        /// <summary>服务端报的台账总数。和 <c>Tickets.Count</c> 不一致就说明被 <see cref="MaxPages"/> 截断了。</summary>
        public int LedgerTotal = -1;

        /// <summary>我的待审批总数（全部单位，不按企业筛）。</summary>
        public int TodoTotal = -1;

        /// <summary>待办那一路是不是没取到。台账有数据但待办失败时，列表照常显示，只是标不出哪张能审批。</summary>
        public bool TodoUnavailable;

        /// <summary>这张票现在轮到我审批了吗。</summary>
        public bool IsActionable(JobTicket ticket)
        {
            return ticket != null && !string.IsNullOrEmpty(ticket.sid) && ActionableSids.Contains(ticket.sid);
        }
    }

    /// <summary>
    /// 拉当前企业的台账 + 我的待办。
    /// </summary>
    /// <param name="applyingUnitId">
    /// 企业编码（<c>sysOrgCode</c>，由 <see cref="PortalCompany"/> 换算）。
    /// 传 null / 空表示不按企业筛，拉全园区。
    /// </param>
    /// <param name="snapshot">结果写在这里；调用方先 new 一个传进来。</param>
    public static IEnumerator Fetch(string applyingUnitId, Snapshot snapshot)
    {
        if (snapshot == null) yield break;

        yield return IccAuth.EnsureToken();
        if (!IccAuth.HasToken)
        {
            snapshot.Ok = false;
            snapshot.Error = "门户登录失败，请检查网络";
            yield break;
        }

        // --- 1. 台账：列表主体 ---
        List<JobTicket> tickets = new List<JobTicket>();
        int ledgerTotal = -1;

        for (int page = 1; page <= MaxPages; page++)
        {
            PortalEnvelope envelope = null;
            yield return Post(Api.JobTicketFind(), BuildBody(applyingUnitId, null, page), "作业票台账",
                result => envelope = result);

            if (envelope == null)
            {
                snapshot.Ok = false;
                snapshot.Error = "作业票台账获取失败";
                yield break;
            }

            if (ledgerTotal < 0) ledgerTotal = envelope.Total;

            JArray rows = envelope.BodyArray();
            if (rows.Count == 0) break;

            foreach (JToken row in rows)
            {
                JobTicket ticket = ToTicket(row);
                if (ticket != null) tickets.Add(ticket);
            }

            if (rows.Count < PageSize) break;
        }

        snapshot.Tickets = tickets;
        snapshot.LedgerTotal = ledgerTotal;

        if (ledgerTotal > tickets.Count)
        {
            // 不静默截断：用户看到的条数比服务端报的少，必须有一行能解释为什么
            Debug.LogWarning($"[作业票] 台账共 {ledgerTotal} 条，本次只取了前 {tickets.Count} 条" +
                             $"（上限 {MaxPages} 页 × {PageSize} 条）");
        }

        // --- 2. 待办：只用来标记哪几张能审批 ---
        // 刻意【不传 applyingUnitId】：待办按账号岗位分派，不按企业分。传了就恒定 0 条。
        PortalEnvelope todo = null;
        yield return Post(Api.JobTicketTodo(), BuildBody(null, OperateAudit, 1), "待审批作业票",
            result => todo = result);

        if (todo == null)
        {
            // 台账已经有数据了，待办拿不到不算整体失败 —— 列表照常显示，只是标不出可审批的票
            snapshot.TodoUnavailable = true;
            Debug.LogWarning("[作业票] 待办列表获取失败，本次无法标记哪些票可审批");
        }
        else
        {
            snapshot.TodoTotal = todo.Total;
            foreach (JToken row in todo.BodyArray())
            {
                string sid = (string)row["sid"];
                if (!string.IsNullOrEmpty(sid)) snapshot.ActionableSids.Add(sid);
            }
        }

        snapshot.Ok = true;
        Debug.Log($"[作业票] 台账 {tickets.Count}/{ledgerTotal} 条" +
                  $"（{(string.IsNullOrEmpty(applyingUnitId) ? "全园区" : "企业 " + applyingUnitId)}）；" +
                  $"我的待审批 {snapshot.TodoTotal} 条，其中 {CountActionable(tickets, snapshot)} 条在本次列表里");
    }

    /// <summary>
    /// 取这张票的 flowKey（打开票面要用）。
    ///
    /// 以服务端下发的 <c>processDefinitionKey</c> 为准。只有它缺失时才回退查表，
    /// 并且会留一行警告 —— 走到回退分支说明服务端行为变了，值得被人看见。
    /// </summary>
    public static bool ResolveFlowKey(JobTicket ticket, out string flowKey)
    {
        flowKey = null;
        if (ticket == null) return false;

        if (!string.IsNullOrWhiteSpace(ticket.processDefinitionKey))
        {
            flowKey = ticket.processDefinitionKey.Trim();
            return true;
        }

        if (PortalJobTicketCatalog.TryResolveFlowKey(ticket.jobTicketType, out flowKey))
        {
            Debug.LogWarning($"[作业票] {ticket.sid} 没有 processDefinitionKey，按作业类型" +
                             $"「{ticket.jobTicketType}」查表得到 {flowKey}。实测 247/247 都有这个字段，" +
                             "出现这条说明服务端返回变了。");
            return true;
        }

        Debug.LogError($"[作业票] {ticket.sid}（{ticket.jobTicketType}）既没有 processDefinitionKey " +
                       "也查不到 flowKey，打不开票面");
        return false;
    }

    /// <summary>
    /// 拼请求体。
    ///
    /// 【按作业类型筛只能用 jobTicketTypeCode】实测 2026-09-22：往 <c>jobTicketType</c> 里传中文，
    /// 8 类作业只有「动火作业」筛得出来，其余 7 类一律返回 0（台账里明明有几十条，缺口 A11）。
    /// 而 <c>jobTicketTypeCode</c>（数字 1–8）8 类全部精确正确。所以这里只开放 code 这条路，
    /// 不留中文入口，免得后面有人顺手用了那个坏掉的字段。
    /// </summary>
    private static string BuildBody(string applyingUnitId, string operateType, int page)
    {
        JObject job = new JObject
        {
            ["applyingUnitId"] = Nullable(applyingUnitId),
            ["jobTicketUnitId"] = JValue.CreateNull(),
            ["jobTicketType"] = JValue.CreateNull(),
            ["jobTicketStatus"] = JValue.CreateNull(),
            ["applyTimeStart"] = JValue.CreateNull(),
            ["applyTimeEnd"] = JValue.CreateNull(),
        };

        JObject body = new JObject { ["job"] = job };
        if (!string.IsNullOrEmpty(operateType)) body["operateType"] = operateType;

        JObject root = new JObject
        {
            ["body"] = body,
            ["page"] = page,
            ["pageSize"] = PageSize,
        };

        return root.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static JToken Nullable(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? JValue.CreateNull() : new JValue(value.Trim());
    }

    /// <summary>
    /// 发一次请求，撞 401 就重登再试一次。
    ///
    /// 【为什么读列表也用 PostJsonStrict】镜像按 URL path 归档、不区分请求体，
    /// 而这两个接口的返回**完全取决于请求体**（筛哪家企业、第几页）。退到镜像会把
    /// 甲企业的票当成乙企业的显示出来，且看起来一切正常。这比拿不到数据糟得多。
    /// 超时按读请求给 <see cref="OfflineMock.NetworkTimeoutSeconds"/>，失败得快一点。
    /// </summary>
    private static IEnumerator Post(string url, string body, string who,
        System.Action<PortalEnvelope> onDone)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            OfflineMock.Result fetch = new OfflineMock.Result();
            Dictionary<string, string> headers = new Dictionary<string, string>
            {
                { "token", IccAuth.Token },
            };

            yield return OfflineMock.PostJsonStrict(url, body, headers, fetch,
                "application/json", OfflineMock.NetworkTimeoutSeconds);

            if (fetch.httpCode == 401 && attempt == 0)
            {
                Debug.LogWarning($"[作业票] {who} 返回 401，重新登录后重试一次");
                yield return IccAuth.EnsureToken(true);
                if (!IccAuth.HasToken) break;
                continue;
            }

            if (!fetch.ok) break;

            if (PortalEnvelope.TryParseSuccess(fetch.json, who, out PortalEnvelope envelope))
            {
                onDone(envelope);
                yield break;
            }

            break;
        }

        onDone(null);
    }

    /// <summary>
    /// 一行 JSON 转 <see cref="JobTicket"/>。
    ///
    /// 用 Newtonsoft 而不是 <c>JsonUtility</c>：门户把 <c>jobTicketTypeCode</c> 返回成字符串
    /// （<c>"8"</c>），而模型里是 int，Newtonsoft 会转，JsonUtility 会直接解析失败。
    /// </summary>
    private static JobTicket ToTicket(JToken row)
    {
        try
        {
            return row.ToObject<JobTicket>();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[作业票] 有一行解析失败，已跳过：{e.Message}");
            return null;
        }
    }

    private static int CountActionable(List<JobTicket> tickets, Snapshot snapshot)
    {
        int count = 0;
        for (int i = 0; i < tickets.Count; i++)
        {
            if (snapshot.IsActionable(tickets[i])) count++;
        }
        return count;
    }
}
