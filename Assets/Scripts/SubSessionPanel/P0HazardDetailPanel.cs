using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;

public sealed class P0HazardDetailPanel : MonoBehaviour
{
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI sourceBadgeText;
    public TextMeshProUGUI summaryText;
    public TextMeshProUGUI responsiblePeopleText;
    public TextMeshProUGUI statisticsText;
    public TextMeshProUGUI hintText;
    public UnityEngine.UI.Button annotationButton;

    /// <summary>取数期间的占位。真实接口要发三个请求，不给提示的话面板会空着好几秒。</summary>
    public void ShowLoading()
    {
        ShowHint("重大危险源管控", "加载中…");
    }

    public void ShowHint(string title, string hint)
    {
        gameObject.SetActive(true);
        Set(titleText, title);
        Set(sourceBadgeText, SourceBadge);
        Set(summaryText, string.Empty);
        Set(responsiblePeopleText, string.Empty);
        Set(statisticsText, string.Empty);
        Set(hintText, hint);
    }

    /// <summary>
    /// 数据来源角标。由 <see cref="isDemo"/> 决定——接真实接口之后还挂着"演示数据"，
    /// 现场会以为数据是假的。
    /// </summary>
    public bool isDemo = true;

    private string SourceBadge => isDemo ? "演示数据" : "生产数据";

    public void ShowStatistics(string companyName, P0HazardProjectionResult projection)
    {
        gameObject.SetActive(true);
        Set(titleText, companyName + " · 重大危险源");
        Set(sourceBadgeText, SourceBadge);
        Set(summaryText, string.Empty);
        Set(responsiblePeopleText, string.Empty);
        string levels = string.Join("  ", projection.byLevel
            .OrderBy(pair => pair.Key)
            .Select(pair => pair.Key + " " + pair.Value));
        Set(statisticsText, $"总数 {projection.total}  {levels}");
        Set(hintText, projection.unlocated > 0
            ? $"{projection.unlocated} 个危险源缺少有效坐标，未创建空间图标"
            : "请选择危险源图标查看详情");
    }

    public void ShowDetails(
        string companyName,
        P0HazardSource hazard,
        P0TankArea area,
        IReadOnlyList<P0ResponsiblePerson> people,
        P0HazardProjectionResult projection)
    {
        ShowStatistics(companyName, projection);
        // 逐项拼而不是一把梭：真实接口只给得出名称和等级，位置/危险物质/罐区全是空，
        // 硬拼会留下一串「位置：」「危险物质：」的空行。
        var summary = new StringBuilder();
        AppendField(summary, "名称", hazard.name);
        AppendField(summary, "等级", hazard.level);
        AppendField(summary, "位置", hazard.location);
        AppendField(summary, "危险物质", hazard.materials);
        AppendField(summary, "关联罐区", area != null ? area.name : null);
        Set(summaryText, summary.ToString());
        if (people == null || people.Count == 0)
        {
            Set(responsiblePeopleText, "责任人资料缺失");
        }
        else
        {
            var builder = new StringBuilder();
            foreach (P0ResponsiblePerson person in people)
            {
                if (builder.Length > 0) builder.AppendLine();
                builder.Append(person.name).Append(" · ").Append(person.duty)
                    .Append(" · ").Append(person.department).Append(" · ").Append(person.phone);
            }
            Set(responsiblePeopleText, builder.ToString());
        }
        Set(hintText, isDemo ? "演示数据，仅用于交互验证" : string.Empty);
    }

    private static void AppendField(StringBuilder builder, string label, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (builder.Length > 0) builder.AppendLine();
        builder.Append(label).Append('：').Append(value);
    }

    public void Hide() => gameObject.SetActive(false);

    private static void Set(TextMeshProUGUI target, string value)
    {
        if (target != null) target.text = value ?? string.Empty;
    }
}
