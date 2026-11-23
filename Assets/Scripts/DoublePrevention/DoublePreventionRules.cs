using System;
using System.Collections.Generic;

public static class DoublePreventionRules
{
    public const int MaximumContentLength = 500;

    public static List<DoubleDanger> SelectUnfinished(
        IEnumerable<DoubleDanger> statusFour,
        IEnumerable<DoubleDanger> statusFive)
    {
        var result = new List<DoubleDanger>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Append(statusFour, result, seen);
        Append(statusFive, result, seen);
        return result;
    }

    public static bool IsUnfinished(DoubleDanger danger)
    {
        if (danger == null || string.IsNullOrWhiteSpace(danger.sid)) return false;
        if (danger.hiddenDangerStatus != "4" && danger.hiddenDangerStatus != "5") return false;
        if (danger.reviewInfos == null || danger.reviewInfos.Count == 0) return true;
        ReviewInfo latest = danger.reviewInfos[danger.reviewInfos.Count - 1];
        return latest == null || latest.reviewResult != "2";
    }

    public static string NormalizeContent(string content)
    {
        string normalized = (content ?? string.Empty).Trim();
        if (normalized.Length == 0) throw new ArgumentException("内容不能为空", nameof(content));
        if (normalized.Length > MaximumContentLength)
            throw new ArgumentException("内容不能超过 500 字", nameof(content));
        return normalized;
    }

    private static void Append(
        IEnumerable<DoubleDanger> source,
        List<DoubleDanger> result,
        HashSet<string> seen)
    {
        if (source == null) return;
        foreach (DoubleDanger danger in source)
        {
            if (!IsUnfinished(danger) || !seen.Add(danger.sid)) continue;
            result.Add(danger);
        }
    }
}

public static class InspectionMediaKindResolver
{
    public static InspectionMediaKind Resolve(string mimeType)
    {
        string normalized = (mimeType ?? string.Empty).Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "image/jpeg":
            case "image/png":
                return InspectionMediaKind.Image;
            case "audio/mpeg":
            case "audio/wav":
            case "audio/x-wav":
            case "audio/mp4":
                return InspectionMediaKind.Audio;
            case "video/mp4":
                return InspectionMediaKind.Video;
            default:
                return InspectionMediaKind.Unsupported;
        }
    }
}

public static class PatrolLabels
{
    public static string RiskLevel(string value)
    {
        switch (value)
        {
            case "1": return "极高";
            case "2": return "高";
            case "3": return "中";
            case "4": return "低";
            default: return value ?? string.Empty;
        }
    }

    public static string Status(string value)
    {
        switch (value)
        {
            case "1": return "草稿";
            case "2": return "跟进中";
            case "3": return "已结束";
            default: return value ?? string.Empty;
        }
    }
}

public static class PatrolTransitionPresentation
{
    public static string BuildSummary(PatrolRecord patrol, PatrolInspectionItem item)
    {
        string inspectionType = item?.inspectionType == "aqyh" ? "安全隐患" : "巡查记录";
        return $"企业：{patrol?.corpName ?? "无"}\n" +
               $"巡查单号：{patrol?.docCode ?? "无"}\n" +
               $"巡查时间：{patrol?.patrolStartTime ?? patrol?.startTime ?? "无"}\n" +
               $"巡查地点：{patrol?.patrolAddress ?? "无"}\n\n" +
               $"检查类型：{inspectionType}\n" +
               $"隐患类别：{item?.hazardCategory ?? "无"}\n" +
               $"重大事故隐患：{(item?.isMajorAccident == "1" ? "是" : "否")}\n" +
               $"检查情况：{item?.inspectionStatus ?? "暂无检查情况"}";
    }

    public static string Build(PatrolRecord patrol, PatrolInspectionItem item)
    {
        return BuildSummary(patrol, item) + "\n\n" +
               "图片：无\n" +
               "语音：无\n" +
               "视频：无";
    }
}

public static class InspectionAnnotationPhrases
{
    private static readonly string[] SafetyHazard =
    {
        "现场存在安全隐患，请立即整改",
        "整改措施不完善，请补充整改",
        "整改结果符合要求"
    };

    private static readonly string[] PatrolRecord =
    {
        "现场运行正常",
        "请持续关注并加强巡查",
        "发现异常，请及时处置"
    };

    public static string[] ForInspectionType(string inspectionType)
    {
        return inspectionType == "aqyh" ? SafetyHazard : PatrolRecord;
    }
}
