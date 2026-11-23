using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>审核结论。初始都是「待审核」，不落盘也按待审核算。</summary>
public enum EntryExitReviewState
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
}

[Serializable]
public class EntryExitReviewEntry
{
    public string key;      // "car:<ID>" / "person:<ID>"
    public int state;       // EntryExitReviewState
    public string remark;
    public string time;     // 最后一次改动时间 yyyy-MM-dd HH:mm

    public EntryExitReviewState State => (EntryExitReviewState)state;
    public bool hasRemark => !string.IsNullOrEmpty(remark);
}

/// <summary>
/// 出入园审核 / 备注的【本机】存档。
///
/// 本类不发起任何网络请求，也不经过 <see cref="OfflineMock"/>：出入园的审核结论和备注
/// 只写到本机 Application.persistentDataPath 下的一个 json，【不回传生产环境】。
/// 这是产品上的硬要求——现场戴眼镜的人做的是演示/试用性质的处置，不能污染真实审批流。
/// 以后若要真回传，应该新加一个 service 走 Api，而不是在这里偷偷加请求。
///
/// 用法：
///   string key = EntryExitReviewStore.KeyFor(record);
///   EntryExitReviewStore.SetState(key, EntryExitReviewState.Approved);
///   EntryExitReviewStore.SetRemark(key, "证件已核验");
///
/// 变更通过 <see cref="OnChanged"/> 广播（参数是发生变化的 key），行内操作列据此自刷新——
/// 虚拟滚动会复用行，靠"谁改谁刷新自己"比让面板去遍历行实例简单得多。
/// </summary>
public static class EntryExitReviewStore
{
    public const string FileName = "entry_exit_review.json";

    /// <summary>某个 key 的状态/备注/选中态发生变化。参数可能为 null（表示取消选中）。</summary>
    public static event Action<string> OnChanged;

    [Serializable]
    private class Wrapper
    {
        // JsonUtility 不支持顶层数组，必须套一层
        public List<EntryExitReviewEntry> items = new List<EntryExitReviewEntry>();
    }

    private static Dictionary<string, EntryExitReviewEntry> cache;

    /// <summary>
    /// 当前审核卡片打开的是哪一条。严格说是 UI 状态而不是存档内容，放这里是因为
    /// 行内操作列本来就订阅了本类的变更，选中高亮跟着同一条广播走最省事；它【不落盘】。
    /// </summary>
    public static string SelectedKey { get; private set; }

    /// <summary>
    /// 射线当前指着哪一行。和 <see cref="SelectedKey"/> 一样是 UI 态、【不落盘】，
    /// 放这里同样是因为行内操作列已经订阅了本类的变更，高亮跟着同一条广播走最省事。
    ///
    /// 由 <see cref="EntryExitHoverShortcut"/> 每帧算，但【只在换行时】才调 <see cref="Hover"/>，
    /// 所以广播频率是"每次射线扫过一行一次"，不是每帧一次。
    /// </summary>
    public static string HoveredKey { get; private set; }

    public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

    // ------------------------------------------------------------------ key

    /// <summary>车辆记录的存档 key。取不到稳定标识时返回 null（调用方应把操作列置灰）。</summary>
    public static string KeyFor(CarAccessRecord record)
    {
        if (record == null) return null;

        string id = FirstNonEmpty(record.ID, record.CARID, record.CARNUM);
        if (string.IsNullOrEmpty(id))
        {
            Debug.LogWarning("[出入园审核] 车辆记录没有 ID/CARID/CARNUM，无法存档，该行操作列不可用");
            return null;
        }

        return "car:" + id;
    }

    /// <summary>人员记录的存档 key，规则同车辆。</summary>
    public static string KeyFor(PersonAccessRecord record)
    {
        if (record == null) return null;

        string id = FirstNonEmpty(record.ID, record.IDCARDNUM, record.PERSONNAME);
        if (string.IsNullOrEmpty(id))
        {
            Debug.LogWarning("[出入园审核] 人员记录没有 ID/IDCARDNUM/PERSONNAME，无法存档，该行操作列不可用");
            return null;
        }

        return "person:" + id;
    }

    private static string FirstNonEmpty(params string[] candidates)
    {
        for (int i = 0; i < candidates.Length; i++)
        {
            if (!string.IsNullOrEmpty(candidates[i])) return candidates[i];
        }
        return null;
    }

    // ------------------------------------------------------------------ 读

    public static EntryExitReviewEntry Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        Load();
        EntryExitReviewEntry entry;
        return cache.TryGetValue(key, out entry) ? entry : null;
    }

    public static EntryExitReviewState StateOf(string key)
    {
        EntryExitReviewEntry entry = Get(key);
        return entry != null ? entry.State : EntryExitReviewState.Pending;
    }

    public static string RemarkOf(string key)
    {
        EntryExitReviewEntry entry = Get(key);
        return entry != null ? (entry.remark ?? string.Empty) : string.Empty;
    }

    public static string TimeOf(string key)
    {
        EntryExitReviewEntry entry = Get(key);
        return entry != null ? (entry.time ?? string.Empty) : string.Empty;
    }

    // ------------------------------------------------------------------ 写

    public static void SetState(string key, EntryExitReviewState state)
    {
        EntryExitReviewEntry entry = Ensure(key);
        if (entry == null) return;

        entry.state = (int)state;
        Touch(entry);
        Write();
        Raise(key);
    }

    public static void SetRemark(string key, string remark)
    {
        EntryExitReviewEntry entry = Ensure(key);
        if (entry == null) return;

        string safe = remark ?? string.Empty;
        if (entry.remark == safe) return;

        entry.remark = safe;
        Touch(entry);
        Write();
        Raise(key);
    }

    /// <summary>切换卡片打开的记录。传 null 表示关掉卡片。</summary>
    public static void Select(string key)
    {
        if (SelectedKey == key) return;

        string previous = SelectedKey;
        SelectedKey = key;

        // 旧的要取消高亮、新的要点亮，两边都得收到广播
        Raise(previous);
        Raise(key);
    }

    /// <summary>切换射线指着的记录。传 null 表示射线离开了列表。</summary>
    public static void Hover(string key)
    {
        if (HoveredKey == key) return;

        string previous = HoveredKey;
        HoveredKey = key;

        Raise(previous);
        Raise(key);
    }

    private static EntryExitReviewEntry Ensure(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        Load();

        EntryExitReviewEntry entry;
        if (!cache.TryGetValue(key, out entry))
        {
            entry = new EntryExitReviewEntry { key = key, state = 0, remark = string.Empty };
            cache[key] = entry;
        }

        return entry;
    }

    private static void Touch(EntryExitReviewEntry entry)
    {
        entry.time = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
    }

    private static void Raise(string key)
    {
        if (OnChanged != null) OnChanged(key);
    }

    // ------------------------------------------------------------------ 落盘

    private static void Load()
    {
        if (cache != null) return;

        cache = new Dictionary<string, EntryExitReviewEntry>();

        string path = FilePath;
        if (!File.Exists(path))
        {
            Debug.Log($"[出入园审核] 本机存档不存在，按空档起步：{path}");
            return;
        }

        try
        {
            string json = File.ReadAllText(path);
            Wrapper wrapper = JsonUtility.FromJson<Wrapper>(json);
            if (wrapper != null && wrapper.items != null)
            {
                foreach (EntryExitReviewEntry entry in wrapper.items)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.key)) continue;
                    cache[entry.key] = entry;
                }
            }

            Debug.Log($"[出入园审核] 读到本机存档 {cache.Count} 条：{path}");
        }
        catch (Exception e)
        {
            // 存档坏掉不该让面板打不开，清空重来即可（本来就是本机演示数据）
            Debug.LogError($"[出入园审核] 本机存档读取失败，按空档继续：{e.Message}");
            cache.Clear();
        }
    }

    private static void Write()
    {
        if (cache == null) return;

        Wrapper wrapper = new Wrapper();
        wrapper.items = new List<EntryExitReviewEntry>(cache.Values);

        try
        {
            // 条数是「人改过的记录数」，量级几十到几百，每次全量写盘足够便宜，不做防抖
            File.WriteAllText(FilePath, JsonUtility.ToJson(wrapper));
        }
        catch (Exception e)
        {
            Debug.LogError($"[出入园审核] 本机存档写入失败：{e.Message}");
        }
    }
}
