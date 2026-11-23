using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public class JobTicketApprovalEntry
{
    public string sid;          // 作业票 sid
    public bool passed;         // true 通过 / false 不通过
    public string opinion;
    public string signatureId;  // PortalSignatureConfig.Option.Id，也就是签名图的文件名
    public string taskName;     // 做这次处置时票在哪个环节，事后对账用
    public string time;         // yyyy-MM-dd HH:mm
}

/// <summary>
/// 作业票审批结论的【本机】存档。
///
/// 本类不发起任何网络请求：审批结论、意见、签名全部只写到本机
/// <c>persistentDataPath/job_ticket_approval.json</c>，**不回传门户**。
/// 这是 2026-09-23 的产品决定 —— 现场戴眼镜做的是演示/试用性质的处置，
/// 不能污染真实审批流；而且签名是从预置图里选的象征性签字，不是真实笔迹。
///
/// 【因此不再看账号权限】门户只对少数几张票下发 <c>audit</c> 按钮，按它卡就只有 2 张票能操作，
/// 而既然结论不回传，卡着也没有意义。现在**所有票都能操作**。
/// 以后若要真回传，应该新加一个 service 走 Api（<c>PortalApprovalSubmitService</c> 还留着），
/// 而不是在这里偷偷加请求 —— 那时权限判断要一并回来。
///
/// 写法照 <see cref="EntryExitReviewStore"/>，那边是出入园审核的同类存档。
/// </summary>
public static class JobTicketApprovalStore
{
    public const string FileName = "job_ticket_approval.json";

    /// <summary>某张票的处置结果变了，参数是票 sid。列表行据此刷新自己。</summary>
    public static event Action<string> OnChanged;

    [Serializable]
    private class Wrapper
    {
        // JsonUtility 不支持顶层数组，必须套一层
        public List<JobTicketApprovalEntry> items = new List<JobTicketApprovalEntry>();
    }

    private static Dictionary<string, JobTicketApprovalEntry> cache;

    public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

    // ------------------------------------------------------------------ 读

    public static JobTicketApprovalEntry Get(string sid)
    {
        if (string.IsNullOrEmpty(sid)) return null;

        Load();
        return cache.TryGetValue(sid, out JobTicketApprovalEntry entry) ? entry : null;
    }

    public static bool Has(string sid)
    {
        return Get(sid) != null;
    }

    /// <summary>
    /// 列表状态列显示的审核结果；没处置过返回 null。
    ///
    /// 【为什么就两个字】那一列宽 200、字号 40，两行封顶约 10 个字。
    /// 加前缀（"本机已通过"之类）会把这一列占满，而列表里要一眼看出来的只有一件事：
    /// 这张票审没审、结果是什么。是本机处置还是门户审批，票面里写得清清楚楚。
    /// 明细在票面正文的「本机处置」那一段，逐条列着。
    /// </summary>
    public static string Badge(string sid)
    {
        JobTicketApprovalEntry entry = Get(sid);
        if (entry == null) return null;

        return entry.passed ? "通过" : "不通过";
    }

    // ------------------------------------------------------------------ 写

    /// <summary>记一次处置。同一张票再处置一次就覆盖，不留历史。</summary>
    public static void Save(string sid, bool passed, string opinion, string signatureId, string taskName)
    {
        if (string.IsNullOrEmpty(sid))
        {
            Debug.LogError("[作业票审批] 票没有 sid，处置结果无处可存");
            return;
        }

        Load();

        cache[sid] = new JobTicketApprovalEntry
        {
            sid = sid,
            passed = passed,
            opinion = opinion ?? string.Empty,
            signatureId = signatureId ?? string.Empty,
            taskName = taskName ?? string.Empty,
            time = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
        };

        Write();
        OnChanged?.Invoke(sid);

        Debug.Log("[作业票审批] 已存本机：" + sid + " → " + (passed ? "通过" : "不通过") +
                  "，意见「" + opinion + "」，签名 " + signatureId);
    }

    // ------------------------------------------------------------------ 落盘

    private static void Load()
    {
        if (cache != null) return;

        cache = new Dictionary<string, JobTicketApprovalEntry>(StringComparer.Ordinal);

        string path = FilePath;
        if (!File.Exists(path))
        {
            Debug.Log("[作业票审批] 本机存档不存在，按空档起步：" + path);
            return;
        }

        try
        {
            Wrapper wrapper = JsonUtility.FromJson<Wrapper>(File.ReadAllText(path));
            if (wrapper?.items != null)
            {
                foreach (JobTicketApprovalEntry entry in wrapper.items)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.sid)) continue;
                    cache[entry.sid] = entry;
                }
            }

            Debug.Log("[作业票审批] 读到本机存档 " + cache.Count + " 条：" + path);
        }
        catch (Exception e)
        {
            // 存档坏掉不该让面板打不开，清空重来即可（本来就是本机演示数据）
            Debug.LogError("[作业票审批] 本机存档读取失败，按空档继续：" + e.Message);
            cache.Clear();
        }
    }

    private static void Write()
    {
        if (cache == null) return;

        Wrapper wrapper = new Wrapper { items = new List<JobTicketApprovalEntry>(cache.Values) };

        try
        {
            File.WriteAllText(FilePath, JsonUtility.ToJson(wrapper));
        }
        catch (Exception e)
        {
            Debug.LogError("[作业票审批] 本机存档写入失败：" + e.Message);
        }
    }
}
