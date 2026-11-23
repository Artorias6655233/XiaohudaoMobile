using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// 解析 <c>formBaseInfo/detail</c> 里的表单定义，得到两样东西：
///
///   1. **字段的中文标签** —— <c>formDefine.formWidget</c> 里每个控件带
///      <c>name = "段名.字段名"</c> 和 <c>label = "中文"</c>。
///   2. **当前环节要写的 vars 键** —— <c>configs.expVars</c> 里那条 <c>*_AuditResult</c>。
///
/// 【为什么必须从这里取标签】<c>datadefList[].fields</c> 里的 <c>dfTitle</c> 和 <c>dfName</c>
/// 完全相同，都是 SQL 列名（<c>PIPELINE_MEDIUM</c>、<c>HOT_WORK_LOCATION</c>）。
/// 照那个渲染，票面上会写着「PIPELINE_MEDIUM：蒸汽」。中文标签只在 formWidget 里。
///
/// 【顺带解决了 A1】接口文档只给了动火作业 6 个环节的 <c>vars</c> 键名，其余 7 类没给。
/// 实测 <c>configs.expVars</c> 会给出**当前环节**对应的那个键，8 类都验过：
///
///   动火@作业负责人审批 → zyfzr_AuditResult   动火@所在部门审批 → bmfzr_AuditResult
///   动火@安全部审批     → aqbmshr_AuditResult 动火@副总审批     → fzld_AuditResult
///
/// 4 个环节与文档给的对照表逐字一致。其余 7 类在「作业负责人审批」环节也各自给对，
/// 其中**吊装作业用的是 <c>zyzh_AuditResult</c> 而不是 <c>zyfzr_AuditResult</c>** ——
/// 按命名规律猜必然猜错，而猜错了服务端不报错，审批只会静默失效。
/// 所以这个键一律从服务端读，<b>永远不要写死</b>。
///
/// 【只认识 formWidget 的树形结构】顶层那层全是 <c>stepWidget</c> / <c>gridLayout</c> 容器，
/// 真正的字段嵌在 <c>children</c> 里（实测三层深）。只看顶层会一个字段都找不到。
/// </summary>
public sealed class PortalFormDefinition
{
    /// <summary>一个字段的显示信息。</summary>
    public sealed class Field
    {
        /// <summary>SQL 列名，如 <c>HOT_WORK_LOCATION</c>。</summary>
        public string Name;

        /// <summary>中文标签，如「动火地点及动火部位」。</summary>
        public string Label;

        /// <summary>控件类型，如 <c>inputWidget</c> / <c>DatePicker</c> / <c>imgShowWidget</c>。</summary>
        public string Widget;

        /// <summary>表单里的先后次序（深度优先展开的序号），用来决定票面上的排列顺序。</summary>
        public int Order;

        /// <summary>表单把这个控件藏起来了，票面上也不该显示。</summary>
        public bool Hidden;

        /// <summary>
        /// 这个字段装的是图片而不是文字。
        ///
        /// <c>imgShowWidget</c> 绑的是签名图（动火票的「编制人」「监护人」都是），
        /// <c>APPROVAL_SIGNATURE</c> 装的是审批签名。两者的值都是 base64 data URL，
        /// 直接塞进 TMP 会是几十上百 KB 的乱码。
        /// </summary>
        public bool IsImage =>
            string.Equals(Widget, "imgShowWidget", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Name, "APPROVAL_SIGNATURE", StringComparison.Ordinal);
    }

    /// <summary>当前环节的审批变量。</summary>
    public sealed class ApprovalVar
    {
        /// <summary>提交时 <c>vars</c> 里要写的键，如 <c>zyfzr_AuditResult</c>。</summary>
        public string VarsKey;

        /// <summary>这个键取自哪个段，如 <c>dhzy_zyfzr</c>。</summary>
        public string SectionName;

        /// <summary>取自段里的哪个字段，实测一律是 <c>APPROVAL_RESULT</c>。</summary>
        public string FieldName;
    }

    /// <summary>段名 → （字段名 → 字段信息）。</summary>
    private readonly Dictionary<string, Dictionary<string, Field>> fields =
        new Dictionary<string, Dictionary<string, Field>>(StringComparer.Ordinal);

    /// <summary>整段绑定的控件：段名 → 控件类型（<c>uploadWidget</c> / <c>imgWidget</c>）。</summary>
    private readonly Dictionary<string, string> sectionWidgets =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>整段绑定控件的标签，如「盲板位置图及其编号」。</summary>
    private readonly Dictionary<string, string> sectionLabels =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>当前环节的审批变量；表单没声明时为 null。</summary>
    public ApprovalVar Approval { get; private set; }

    private readonly List<ApprovalVar> allVars = new List<ApprovalVar>();

    /// <summary>
    /// 当前环节声明的**全部**流程变量，提交时 <c>vars</c> 要按这份逐条填。
    /// 顺序与服务端下发一致。
    /// </summary>
    public IReadOnlyList<ApprovalVar> AllVars => allVars;

    /// <summary>解析出多少个带标签的字段，供自检与日志用。</summary>
    public int LabelledFieldCount { get; private set; }

    /// <summary>
    /// 解析表单定义。<paramref name="detail"/> 是 <c>formBaseInfo/detail</c> 的 <c>body</c>。
    /// 解析失败不抛异常，返回一个空定义 —— 票面退化成「只显示有值的段」而不是整个打不开。
    /// </summary>
    public static PortalFormDefinition Parse(JObject detail)
    {
        PortalFormDefinition def = new PortalFormDefinition();
        if (detail == null) return def;

        try
        {
            def.ParseWidgets(detail["formDefine"]);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[票面] formDefine 解析失败，字段将没有中文标签：{e.Message}");
        }

        try
        {
            def.ParseExpVars(detail["configs"]);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[票面] configs.expVars 解析失败，取不到审批变量名：{e.Message}");
        }

        return def;
    }

    /// <summary>取某个字段的显示信息；表单上没有这个字段时返回 null（说明它是系统列，不该显示）。</summary>
    public Field GetField(string sectionName, string fieldName)
    {
        if (sectionName == null || fieldName == null) return null;
        return fields.TryGetValue(sectionName, out Dictionary<string, Field> inSection) &&
               inSection.TryGetValue(fieldName, out Field field)
            ? field
            : null;
    }

    /// <summary>这一段是不是附件段（整段绑了 <c>uploadWidget</c> / <c>imgWidget</c>）。</summary>
    public bool IsAttachmentSection(string sectionName)
    {
        if (sectionName == null) return false;
        if (!sectionWidgets.TryGetValue(sectionName, out string widget)) return false;
        return string.Equals(widget, "uploadWidget", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(widget, "imgWidget", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>附件段在表单上的标签，如「盲板位置图及其编号」。没有就返回 null。</summary>
    public string GetSectionLabel(string sectionName)
    {
        if (sectionName == null) return null;
        return sectionLabels.TryGetValue(sectionName, out string label) ? label : null;
    }

    /// <summary>
    /// 这一段在表单上声明了哪些字段。
    ///
    /// 给自检反向核对用：表单声明了、但数据里没有这个键，说明解析器认错了段名或字段名。
    /// 那是**静默错误** —— 票面只会少几行，不会报任何错。
    /// </summary>
    public IEnumerable<Field> GetFields(string sectionName)
    {
        if (sectionName != null && fields.TryGetValue(sectionName, out Dictionary<string, Field> inSection))
        {
            return inSection.Values;
        }
        return System.Linq.Enumerable.Empty<Field>();
    }

    /// <summary>这一段有没有任何带标签的字段。一个都没有说明整段都是系统列，票面上不必显示。</summary>
    public bool HasAnyField(string sectionName)
    {
        return sectionName != null &&
               fields.TryGetValue(sectionName, out Dictionary<string, Field> inSection) &&
               inSection.Count > 0;
    }

    // ------------------------------------------------------------------ 解析

    private void ParseWidgets(JToken formDefine)
    {
        JToken widgets = Unwrap(formDefine)?["formWidget"];
        JArray roots = Unwrap(widgets) as JArray;
        if (roots == null) return;

        int order = 0;
        Walk(roots, ref order);
    }

    /// <summary>
    /// 深度优先展开控件树。
    ///
    /// 用展开的先后当显示顺序，而不是用控件自己的 <c>ordered</c> —— 后者是**同一个父节点内**
    /// 的序号，跨容器比较没有意义（两个 gridLayout 里都有 ordered=0 的控件）。
    /// </summary>
    private void Walk(JArray nodes, ref int order)
    {
        foreach (JToken node in nodes)
        {
            JObject widget = node as JObject;
            if (widget == null) continue;

            string name = (string)widget["name"];
            if (!string.IsNullOrEmpty(name))
            {
                int dot = name.IndexOf('.');
                if (dot > 0)
                {
                    Remember(name.Substring(0, dot), name.Substring(dot + 1), widget, order++);
                }
                else
                {
                    // name 就是段名：整段绑一个控件，实测是附件段（uploadWidget / imgWidget）
                    sectionWidgets[name] = (string)widget["widget"];
                    string label = (string)widget["label"];
                    if (!string.IsNullOrEmpty(label)) sectionLabels[name] = label;
                }
            }

            if (widget["children"] is JArray children) Walk(children, ref order);
        }
    }

    private void Remember(string section, string fieldName, JObject widget, int order)
    {
        if (!fields.TryGetValue(section, out Dictionary<string, Field> inSection))
        {
            inSection = new Dictionary<string, Field>(StringComparer.Ordinal);
            fields[section] = inSection;
        }

        // 同一个字段在表单上出现两次时保留第一次：先出现的那个是主展示位
        if (inSection.ContainsKey(fieldName)) return;

        JToken hidden = widget["hiddened"];
        inSection[fieldName] = new Field
        {
            Name = fieldName,
            Label = (string)widget["label"],
            Widget = (string)widget["widget"],
            Order = order,
            Hidden = hidden != null && hidden.Type == JTokenType.Boolean && (bool)hidden,
        };
        LabelledFieldCount++;
    }

    /// <summary>
    /// 从 <c>configs.expVars</c> 里挑出当前环节的审批变量。
    ///
    /// 形如 <c>{"label":"zyfzr_AuditResult","value":"data['dhzy_zyfzr.APPROVAL_RESULT']"}</c>。
    /// 同一份表单里还有 <c>business_sid</c> 之类的非审批变量，靠后缀筛掉。
    /// </summary>
    /// <summary>
    /// 解析 <c>configs.expVars</c> —— 当前环节要往流程变量里写的**全部**键。
    ///
    /// 【为什么必须全都留下，不能只留审批结果那条】实测盲板抽堵「作业负责人审批」环节的
    /// expVars 有两条：
    /// <code>
    ///   business_sid       ← data['mbcd_base.SID']
    ///   zyfzr_AuditResult  ← data['mbcd_zyfzr.APPROVAL_RESULT']
    /// </code>
    /// 接口文档 §3.3 给的 <c>flow/handle</c> 请求示例里 <c>vars</c> 也是
    /// <c>{zyfzr_AuditResult, business_sid, hotWorkLevel}</c> —— 审批结果只是其中一条。
    /// 早先这里只挑 <c>*AuditResult</c>，提交时流程变量会缺 <c>business_sid</c>，
    /// 而服务端对 vars 缺键**不报错**（<c>result</c> 恒 true），只会静默不生效。
    /// </summary>
    private void ParseExpVars(JToken configs)
    {
        JArray vars = Unwrap(configs)?["expVars"] as JArray;
        if (vars == null) return;

        foreach (JToken item in vars)
        {
            string label = (string)item["label"];
            if (string.IsNullOrEmpty(label)) continue;

            string path = ExtractPath((string)item["value"]);
            if (path == null) continue;

            int dot = path.IndexOf('.');
            if (dot <= 0) continue;

            ApprovalVar entry = new ApprovalVar
            {
                VarsKey = label,
                SectionName = path.Substring(0, dot),
                FieldName = path.Substring(dot + 1),
            };
            allVars.Add(entry);

            // 审批结果那条单独拎出来：UI 要靠它判断「本环节能不能审批」，
            // 提交时也只有它的值来自用户的选择，其余都是从票面数据原样带回去的。
            if (Approval == null &&
                (label.EndsWith("AuditResult", StringComparison.Ordinal) ||
                 label.EndsWith("AcceptanceResult", StringComparison.Ordinal)))
            {
                Approval = entry;
            }
        }
    }

    /// <summary><c>data['段.字段']</c> → <c>段.字段</c>。形状不对就返回 null，不猜。</summary>
    private static string ExtractPath(string expression)
    {
        if (string.IsNullOrEmpty(expression)) return null;
        int open = expression.IndexOf('\'');
        int close = expression.LastIndexOf('\'');
        if (open < 0 || close <= open + 1) return null;
        return expression.Substring(open + 1, close - open - 1);
    }

    /// <summary>
    /// 这套后端到处是「JSON 里套 JSON 字符串」：<c>formDefine</c> 实测是对象，
    /// 但里面的 <c>formWidget</c> 是字符串；<c>configs</c> 整个是字符串。
    /// 两种形态都收，免得换个版本就解析不出来。
    /// </summary>
    private static JToken Unwrap(JToken token)
    {
        if (token == null || token.Type == JTokenType.Null) return null;
        if (token.Type != JTokenType.String) return token;

        string text = (string)token;
        if (string.IsNullOrWhiteSpace(text)) return null;

        try
        {
            return JToken.Parse(text);
        }
        catch
        {
            return null;
        }
    }
}
