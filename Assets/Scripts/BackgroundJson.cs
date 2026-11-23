using System;
using System.Collections;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// 大响应的后台线程 JSON 解析。
///
/// 起因：点某些 tab 会卡一两秒。设备日志（2026-08-25 18:00:46，作业票 tab）：
///
///   18:00:46.029  [DataProvider] JobTicket 列表 3353023 字节   ← 这行打在 FromJson 之前
///        ↓ 1.38 秒，期间只渲染了 13 帧（约 9 FPS）
///   18:00:47.409  TMPro … Rebuild(CanvasUpdate)                ← UI 才开始重建
///
/// 系统 FPS 探针同一时刻从 20 掉到 7.5 再到 8.7。3.3MB 的
/// specialJob_jobTicket_list.json 是主要来源；隐患报告 1.1MB、罐区 388KB、
/// 车辆出入 264KB×2 也在同一量级。
///
/// JsonUtility 只能在主线程调用，所以大响应改用 Newtonsoft（工程已依赖
/// com.unity.nuget.newtonsoft-json 3.2.1）丢进线程池，主线程逐帧轮询。
///
/// 小响应仍走 JsonUtility：两个解析器在边界情况上的行为并不完全一致，
/// 只让真正会卡的那几个端点换实现，出问题时排查范围也小。
/// 把 <see cref="OffThreadThreshold"/> 调成 int.MaxValue 就整体回退了。
///
/// 注意：解析期间调用方的协程可能被切 tab 打断（面板 SetActive(false) 会停协程）。
/// 线程池里的任务会自己跑完然后被丢弃，不泄漏；结果没写进缓存而已，再进面板重来一遍。
/// 各面板取数处已经在判 GetLastResult() 为 null，行为与改动前一致。
/// </summary>
public static class BackgroundJson
{
    /// <summary>
    /// 超过这个字符数才值得丢后台。按当前离线镜像，256KB 以上只有 4 个端点：
    /// 作业票 3306KB、隐患报告 1173KB、罐区 388KB、车辆出入 264KB（进/出各一份）。
    /// </summary>
    public const int OffThreadThreshold = 256 * 1024;

    /// <summary>
    /// 必须显式给设置，不能用 JsonConvert 的默认值。
    ///
    /// DateParseHandling 默认是 DateTime：Newtonsoft 会把形如 "2024-01-02 10:00:00"
    /// 的字符串先解析成 DateTime 再转回 string，日期显示格式就变了。而模型里
    /// createTime / applyTime / jobTicketTime 这类全是 string，必须关掉。
    /// </summary>
    private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
    {
        DateParseHandling = DateParseHandling.None,
        MissingMemberHandling = MissingMemberHandling.Ignore,
    };

    /// <summary>协程没有返回值，用它把结果带出去。</summary>
    public class Result
    {
        public object value;
        public string error;
    }

    public static bool ShouldOffload(string json)
    {
        return json != null && json.Length >= OffThreadThreshold;
    }

    /// <summary>
    /// 按响应大小自动选主线程 / 后台线程。调用方 <c>yield return</c> 它，
    /// 完成后从 <paramref name="result"/> 取值。
    /// </summary>
    public static IEnumerator Parse(Type type, string json, Result result)
    {
        if (ShouldOffload(json))
        {
            yield return Deserialize(type, json, result);
        }
        else
        {
            DeserializeInline(type, json, result);
        }
    }

    /// <summary>主线程直接解析，给小响应用。</summary>
    public static void DeserializeInline(Type type, string json, Result result)
    {
        try
        {
            result.value = JsonUtility.FromJson(json, type);
        }
        catch (Exception ex)
        {
            result.error = ex.Message;
        }
    }

    /// <summary>
    /// 丢到线程池解析，主线程逐帧轮询直到完成。
    ///
    /// Newtonsoft 失败（或返回 null）时回落到主线程的 JsonUtility：最坏情况退化成
    /// 改动前的行为（卡一下），而不是整个面板空白。
    /// </summary>
    public static IEnumerator Deserialize(Type type, string json, Result result)
    {
        Task<object> task = Task.Run(() => JsonConvert.DeserializeObject(json, type, Settings));

        while (!task.IsCompleted)
        {
            yield return null;
        }

        // 先看 Status 再碰 Result：faulted 的 task 取 Result 会把异常重新抛出来
        if (task.Status == TaskStatus.RanToCompletion && task.Result != null)
        {
            result.value = task.Result;
            yield break;
        }

        string reason = task.Exception != null
            ? task.Exception.GetBaseException().Message
            : "返回 null";
        Debug.LogWarning($"[BackgroundJson] 后台解析 {type.Name} 失败（{reason}），回落到主线程 JsonUtility");

        DeserializeInline(type, json, result);
    }
}
