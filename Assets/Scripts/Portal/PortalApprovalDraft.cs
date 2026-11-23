using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 一张票的审批草稿：审批结论、意见、选中的签名。
///
/// 【范围】草稿是填写过程中的临时态。填完之后整份（结论 + 意见 + 签名）写进
/// <see cref="JobTicketApprovalStore"/>，**只留在眼镜本地，一个字节都不回传门户**。
///
/// 【为什么只放内存、不落盘】草稿的生命周期就是"打开这张票到审批完"这一小段。
/// 落盘会带来两个更麻烦的问题：一是票在门户侧被别人推进了，本地草稿就成了过期数据，
/// 但界面看起来仍然有效；二是审批意见属于业务内容，散落在设备文件里不好管。
/// 需要跨会话保留时再说，那时应该连同"草稿对应的是哪个流程环节"一起存，否则会张冠李戴。
/// </summary>
public static class PortalApprovalDraft
{
    public sealed class Draft
    {
        /// <summary>审批结论。null = 还没选；对应表单字段 APPROVAL_RESULT（1 通过 / 0 不通过）。</summary>
        public bool? Passed;

        /// <summary>审批意见，必填。</summary>
        public string Opinion = string.Empty;

        /// <summary>选中的签名 id，对应 <see cref="PortalSignatureConfig.Option.Id"/>。</summary>
        public string SignatureId;

        /// <summary>草稿是针对哪个环节的。环节变了，草稿就该作废 —— 见 <see cref="Get"/>。</summary>
        public string TaskInsId;

        public bool HasSignature => !string.IsNullOrEmpty(SignatureId);

        /// <summary>
        /// 能不能确认：结论选了、意见填了、签名选了。
        ///
        /// 【签名库空的时候不要签名】签名是从预置图里选的，一张图都没有时无法"选"，
        /// 卡着只会让确认按钮永远是灰的、而且看不出是因为没放图。
        /// 图放进去之后这一项自动变成必填，不用改代码。
        /// </summary>
        public bool IsComplete =>
            Passed.HasValue && !string.IsNullOrWhiteSpace(Opinion)
            && (HasSignature || PortalSignatureConfig.IsEmpty);

        /// <summary>还差什么，直接拿去显示给用户，不要让人猜按钮为什么是灰的。</summary>
        public string MissingHint
        {
            get
            {
                List<string> missing = new List<string>();
                if (!Passed.HasValue) missing.Add("审批结论");
                if (string.IsNullOrWhiteSpace(Opinion)) missing.Add("审批意见");
                if (!HasSignature && !PortalSignatureConfig.IsEmpty) missing.Add("签名");

                if (missing.Count > 0) return "还需填写：" + string.Join("、", missing);
                return PortalSignatureConfig.IsEmpty ? PortalSignatureConfig.EmptyHint : string.Empty;
            }
        }
    }

    private static readonly Dictionary<string, Draft> drafts =
        new Dictionary<string, Draft>(StringComparer.Ordinal);

    /// <summary>
    /// 取这张票的草稿，没有就新建。
    ///
    /// <paramref name="taskInsId"/> 和草稿里记的对不上时**直接丢掉重来**：
    /// 说明这张票的流程已经走到下一环节了（可能是别人在门户上推的），
    /// 上一环节填的结论和意见不该继续套在新环节上。
    /// </summary>
    public static Draft Get(string ticketSid, string taskInsId)
    {
        if (string.IsNullOrEmpty(ticketSid)) return new Draft { TaskInsId = taskInsId };

        if (drafts.TryGetValue(ticketSid, out Draft existing))
        {
            if (string.Equals(existing.TaskInsId, taskInsId, StringComparison.Ordinal))
            {
                return existing;
            }

            Debug.Log("[审批草稿] 票 " + ticketSid + " 的环节已变（" + existing.TaskInsId +
                      " → " + taskInsId + "），旧草稿作废");
        }

        Draft fresh = new Draft { TaskInsId = taskInsId };
        drafts[ticketSid] = fresh;
        return fresh;
    }

    public static void Clear(string ticketSid)
    {
        if (!string.IsNullOrEmpty(ticketSid)) drafts.Remove(ticketSid);
    }

    /// <summary>进程内已有多少份草稿，供自检和日志用。</summary>
    public static int Count => drafts.Count;
}
