using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// 用 Newtonsoft 解析这套后端响应时的统一入口。
///
/// 【为什么需要它】仓库里的数据模型（<see cref="Company"/>、<see cref="Tank"/> 等）把一堆
/// 可空数值列声明成了非空值类型：Company.longitude84 是 float、Tank.tankVol 是 double，
/// 而接口这些字段经常返回 null。老取数路径走 DataProvider（JsonUtility），它遇到 null
/// 直接当默认值，所以一直没暴露；换成 Newtonsoft 就会抛
/// 「Null object cannot be converted to a value type」，整份响应一条都拿不到。
///
/// 这里的做法是：遇到单个成员转换失败就跳过它（保留该字段的默认值）、继续解析剩下的，
/// 并把前几条错误打出来。不改那些模型的字段类型——它们被 JsonUtility 路径共用着，
/// 改成可空类型会波及一大片调用方，风险远大于收益。
///
/// 【什么时候不该用它】新写的、只声明自己要用的字段的精简模型（如
/// <see cref="P0CompanyRecord"/>）不需要容错，字段类型本来就对得上。这个入口是给
/// 复用老模型的地方兜底的，不是拿来掩盖真的解析错误的——所以它会打日志，别忽略。
/// </summary>
public static class SandTableJson
{
    /// <summary>每次解析最多打几条成员级错误，避免一份坏响应刷满 logcat。</summary>
    private const int MaxLoggedErrors = 3;

    /// <summary>
    /// 宽容解析。<paramref name="context"/> 只用于日志，写清是哪个接口，
    /// 出问题时能一眼定位。解析彻底失败时返回 default 并打 Error。
    /// </summary>
    public static T Deserialize<T>(string json, string context)
    {
        if (string.IsNullOrEmpty(json)) return default;

        int logged = 0;
        var settings = new JsonSerializerSettings
        {
            Error = (sender, args) =>
            {
                args.ErrorContext.Handled = true;
                if (logged++ >= MaxLoggedErrors) return;
                Debug.LogWarning($"[Json] {context} 跳过字段 " +
                                 $"{args.ErrorContext.Path}：{args.ErrorContext.Error.Message}");
            }
        };

        try
        {
            return JsonConvert.DeserializeObject<T>(json, settings);
        }
        catch (Exception exception)
        {
            Debug.LogError($"[Json] {context} 解析失败：{exception.Message}");
            return default;
        }
    }

    /// <summary>
    /// 取分页响应里的行。
    ///
    /// 【为什么不能只读 data】同一个 <see cref="ResponseList{T}"/> 壳，不同接口填的字段
    /// 不一样：企业列表填 result.data，责任人列表只填 result.records（实测
    /// 8.134.97.227:6003 上 data 为 null、records 有 29 条），罐体列表两个都填。
    /// 只读 data 的话责任人会静默变成空集——不报错、不抛异常，面板上只是显示
    /// "暂无登记"，很难查。
    /// </summary>
    public static List<T> Rows<T>(ResponseList<T> response)
    {
        ListResult<T> result = response?.result;
        if (result == null) return new List<T>();
        if (result.data != null && result.data.Count > 0) return result.data;
        return result.records ?? new List<T>();
    }
}
