using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 企业证件管理演示数据的只读校验。
///
///   Unity.exe -batchmode -nographics -projectPath &lt;repo&gt;/sandtable \
///     -executeMethod SpecialJobCertChecks.Run -quit -logFile &lt;log&gt;
///
/// 盯的是三件最容易悄悄错掉的事：
///   1. 演示企业编码和真实企业目录对不上 —— 页面会永远显示「无数据」，还找不出为什么；
///   2. 人数被证件数顶替 —— 一人多证时人数会虚高，这是这一页最核心的口径；
///   3. 演示文案里有字不在 TMP 图集用字表里 —— 真机上就是一排豆腐块，而 Editor 里看不出来。
/// </summary>
public static class SpecialJobCertChecks
{
    private const string CompanyMirror = "Assets/StreamingAssets/mock/security_company_list.json";
    private const string CharsetPath = "Assets/Fonts/charset_used.txt";

    /// <summary>场景实际引用的三个字重，和 <c>CliAddGlyphs.FontAssetPaths</c> 保持一致。</summary>
    private static readonly string[] FontAssetPaths =
    {
        "Assets/Fonts/SourceHanSansCN/SourceHanSansCN.asset",
        "Assets/Fonts/SourceHanSansCN/SourceHanSansCN-Bold.asset",
        "Assets/Fonts/SourceHanSansCN/SourceHanSansCN-Heavy.asset",
    };

    /// <summary>和 <c>SpecialJob_CertificateList.rowsPerPage</c> 的默认值保持一致。</summary>
    private const int RowsPerPage = 7;

    public static void Run()
    {
        Check(P0DataModeParser.Parse(null) == P0DataMode.Mock,
            "旧配置缺 certDataMode 时必须进 Mock");

        // 工厂不能把演示数据当正式数据发出去——那是最危险的退化：
        // 现场看到的是 6 家企业的演示证件，却以为是真实台账
        ISpecialJobCertSource network = SpecialJobCertSourceFactory.Create(P0DataMode.Network);
        Check(!network.IsDemo, "network 模式不能自称演示数据");
        Check(!(network is MockSpecialJobCertSource), "network 模式不能退回演示数据包");
        ExpectFailure(() => network.Load(), "正式接口还没有，network 模式必须明确失败");

        var mock = new MockSpecialJobCertSource();
        // 固定基准日，免得校验结果随运行日期漂
        mock.Load(new DateTime(2026, 9, 22));
        Check(mock.IsDemo, "演示数据源必须自称演示数据");
        Check(mock.Ready, "演示数据必须加载成功");

        CheckCompanyCodes(mock);
        CheckSummaries(mock);
        CheckStatusRule();
        CheckPagingShape(mock);
        CheckGlyphs(mock);
        CheckSceneWiring();

        Debug.Log("SpecialJobCertChecks: 全部通过");
    }

    /// <summary>演示企业编码必须真的能在企业目录里找到，否则沙盘上点不出这家企业。</summary>
    private static void CheckCompanyCodes(MockSpecialJobCertSource mock)
    {
        string path = Path.Combine(Directory.GetCurrentDirectory(), CompanyMirror);
        Check(File.Exists(path), $"找不到企业目录镜像：{CompanyMirror}");

        string json = File.ReadAllText(path);
        HashSet<string> known = new HashSet<string>(
            Regex.Matches(json, "\"sysOrgCode\"\\s*:\\s*\"([^\"]+)\"")
                .Cast<Match>()
                .Select(m => m.Groups[1].Value),
            StringComparer.Ordinal);

        Check(known.Count > 0, "企业目录镜像里一个 sysOrgCode 都没读到");

        foreach (string code in mock.CompanyCodes)
        {
            Check(known.Contains(code),
                $"演示数据里的企业编码 {code} 不在企业目录里，页面会永远显示无数据");
        }
    }

    /// <summary>人数按人员去重，证件数按行数；一人多证时两者必须不同。</summary>
    private static void CheckSummaries(MockSpecialJobCertSource mock)
    {
        bool sawMultiCert = false;

        foreach (string code in mock.CompanyCodes)
        {
            CompanyCertSummary summary = mock.GetByCompany(code);
            Check(!summary.IsEmpty, $"{code} 的演示数据是空的");
            Check(summary.certCount == summary.rows.Count, $"{code} 的证件数必须等于行数");

            int distinct = summary.rows.Select(row => row.personId).Distinct(StringComparer.Ordinal).Count();
            Check(summary.holderCount == distinct, $"{code} 的持证人数必须按人员 ID 去重");
            Check(summary.holderCount <= summary.certCount, $"{code} 的人数不可能多过证件数");

            if (summary.holderCount < summary.certCount) sawMultiCert = true;

            foreach (CertRow row in summary.rows)
            {
                Check(!string.IsNullOrWhiteSpace(row.personName), $"{row.certId} 缺人员姓名");
                Check(!string.IsNullOrWhiteSpace(row.certTypeName), $"{row.certId} 缺证件类型");
                Check(!string.IsNullOrWhiteSpace(row.certNumber), $"{row.certId} 缺证件编号");
                Check(CertStatus.IsKnown(row.statusText),
                    $"{row.certId} 的状态「{row.statusText}」不在 有效/临期/已过期 里");

                DateTime? issue = MockSpecialJobCertSource.ParseDate(row.issueDate);
                DateTime? expire = MockSpecialJobCertSource.ParseDate(row.expireDate);
                Check(issue != null, $"{row.certId} 的发证日期解析不了：{row.issueDate}");
                Check(expire != null, $"{row.certId} 的有效期解析不了：{row.expireDate}");
                Check(issue.Value <= expire.Value, $"{row.certId} 的发证日期晚于有效期");
            }

            // 快到期的排前面，页面第一屏就该看见问题件
            for (int i = 1; i < summary.rows.Count; i++)
            {
                DateTime prev = MockSpecialJobCertSource.ParseDate(summary.rows[i - 1].expireDate).Value;
                DateTime curr = MockSpecialJobCertSource.ParseDate(summary.rows[i].expireDate).Value;
                Check(prev <= curr, $"{code} 的行没有按有效期升序排");
            }
        }

        Check(sawMultiCert, "演示数据必须至少有一家企业存在一人多证，否则人数口径根本没被验证到");
    }

    /// <summary>状态判定的边界：到期当天算有效，过一天算过期，30 天整算临期。</summary>
    private static void CheckStatusRule()
    {
        DateTime today = new DateTime(2026, 9, 22);

        Check(MockSpecialJobCertSource.ResolveStatus("2026-09-22", today) == CertStatus.Expiring,
            "到期当天应算临期，不是已过期");
        Check(MockSpecialJobCertSource.ResolveStatus("2026-09-21", today) == CertStatus.Expired,
            "过期一天就是已过期");
        Check(MockSpecialJobCertSource.ResolveStatus("2026-10-22", today) == CertStatus.Expiring,
            "整 30 天应算临期");
        Check(MockSpecialJobCertSource.ResolveStatus("2026-10-23", today) == CertStatus.Valid,
            "31 天应算有效");
        Check(MockSpecialJobCertSource.ResolveStatus(null, today) == string.Empty,
            "没有有效期时不能推断成有效");
        Check(MockSpecialJobCertSource.ResolveStatus("不是日期", today) == string.Empty,
            "日期解析不了时不能推断成有效");
        Check(MockSpecialJobCertSource.ResolveStatus("2027-01-14 00:00:00", today) == CertStatus.Valid,
            "带时分秒的日期也要认");
    }

    /// <summary>翻页的三种形态都要有样本，否则改分页逻辑时没有东西能验。</summary>
    private static void CheckPagingShape(MockSpecialJobCertSource mock)
    {
        List<int> counts = mock.CompanyCodes.Select(code => mock.GetByCompany(code).certCount).ToList();

        Check(counts.Any(n => n < RowsPerPage), "缺少不满一页的企业样本");
        Check(counts.Any(n => n == RowsPerPage), $"缺少正好 {RowsPerPage} 条（单页边界）的企业样本");
        Check(counts.Any(n => n > RowsPerPage * 2), "缺少三页以上的企业样本");

        Check(mock.GetByCompany("不存在的企业编码").IsEmpty, "未知企业必须返回空汇总");
        Check(mock.GetByCompany(null).IsEmpty, "企业编码为空时必须返回空汇总");
    }

    /// <summary>
    /// 演示文案里的字必须真的在 TMP 图集里。
    ///
    /// 【为什么查图集而不是查 <c>charset_used.txt</c>】那份用字表只有 1738 字，图集里实际有
    /// 三千多个，两者早就不同步（见 <c>CliAddGlyphs</c> 的说明）。查字表会把图集里有、字表里
    /// 没有的字误报成缺字。图集才是真机上的事实。
    ///
    /// 三个在用的字体资产都是 Static 图集，运行时没有 Dynamic 兜底，表外字在真机上就是
    /// 豆腐块——而 Editor 里用的是系统字体，看不出来。真报缺字时的处理是：把字加进
    /// <c>charset_used.txt</c>，再跑 Tools/字体/补齐缺失字形。那一步会改写三个 35MB 的
    /// 字体资产，所以演示文案能用图集里已有的字就别引入新字。
    /// </summary>
    private static void CheckGlyphs(MockSpecialJobCertSource mock)
    {
        // 取三个字重的【交集】而不是并集：卡片上不同文本用的字重不一样，
        // 只有一个字重有的字，换个字重就是豆腐块。
        HashSet<char> charset = null;
        foreach (string fontPath in FontAssetPaths)
        {
            TMPro.TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(fontPath);
            Check(font != null, $"找不到字体资产：{fontPath}");
            Check(font.characterTable != null && font.characterTable.Count > 0,
                $"{fontPath} 的字形表是空的");

            // 只收 BMP 范围：演示文案里不会出现代理对
            HashSet<char> here = new HashSet<char>(font.characterTable
                .Where(character => character.unicode <= char.MaxValue)
                .Select(character => (char)character.unicode));

            if (charset == null) charset = here;
            else charset.IntersectWith(here);
        }

        Check(charset != null && charset.Count > 0, "三个字重的字形交集是空的");

        // 页面上会出现的所有文案：演示数据 + 代码里写死的提示语
        List<string> texts = new List<string>
        {
            "自有员工", "无数据", "未提供", "年",
            "请在沙盘上选择企业", "该企业", "证件数据加载失败，退出本页重进可重试",
            "无特殊作业持证人员数据", "持证人员", "人，证件", "本", "【演示数据】",
            CertStatus.Valid, CertStatus.Expiring, CertStatus.Expired,
        };

        foreach (string code in mock.CompanyCodes)
        {
            foreach (CertRow row in mock.GetByCompany(code).rows)
            {
                texts.Add(row.personName);
                texts.Add(row.certTypeName);
                texts.Add(row.certName);
                texts.Add(row.thirdParty);
                texts.Add(row.managementUnit);
            }
        }

        SortedSet<char> missing = new SortedSet<char>();
        foreach (string text in texts)
        {
            if (string.IsNullOrEmpty(text)) continue;
            foreach (char c in text)
            {
                if (!char.IsControl(c) && !charset.Contains(c)) missing.Add(c);
            }
        }

        Check(missing.Count == 0,
            $"这些字不在 TMP 图集里，真机会显示豆腐块：{new string(missing.ToArray())}" +
            $"（要么把演示文案换成图集里已有的字，要么把它们加进 {CharsetPath} 再跑 " +
            "Tools/字体/补齐缺失字形——后者会改写三个 35MB 的字体资产）");
    }

    /// <summary>
    /// 场景接线只读校验。
    ///
    /// 接线是手写 YAML 补丁打进去的（在编辑器里拖引用会重新序列化整个场景），
    /// 写错一个 fileID 不会报错，只会在真机上表现成「翻页按钮没反应」这类查不出来的现象，
    /// 所以这一步必须自动验。只读打开场景，绝不保存。
    /// </summary>
    private static void CheckSceneWiring()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
            "Assets/LZW/SandTable/SandTable.unity",
            UnityEditor.SceneManagement.OpenSceneMode.Single);

        SpecialJob_CertificateList host = null;
        foreach (SpecialJob_CertificateList candidate in
            Resources.FindObjectsOfTypeAll<SpecialJob_CertificateList>())
        {
            // 面板默认是关掉的，FindObjectOfType 找不到
            if (candidate.gameObject.scene.IsValid()) { host = candidate; break; }
        }

        Check(host != null, "场景里找不到 SpecialJob_CertificateList");
        Check(host.certificateObjectPrefab != null, "没接行模板 certificateObjectPrefab");
        Check(host.listContainer != null, "没接 listContainer");
        Check(host.detailPanel != null, "没接 detailPanel");
        Check(host.toolbarPrefab != null, "没接 toolbarPrefab（先跑 Tools/企业证件/生成工具栏）");
        Check(host.toolbarParent != null, "没接 toolbarParent");
        Check(host.rowsPerPage == RowsPerPage,
            $"场景里的 rowsPerPage 是 {host.rowsPerPage}，和校验用的 {RowsPerPage} 不一致");

        // 行模板必须留在关闭状态：它只是模板，自己不该出现在列表里
        Check(!host.certificateObjectPrefab.gameObject.activeSelf, "行模板应保持 inactive");
        Check(host.certificateObjectPrefab.detailButton != null, "行模板没接 detailButton");
        Check(host.certificateObjectPrefab.transform.childCount >= 8,
            "行模板的列数少于 8，和表头对不上");

        CertListToolbar toolbar = host.toolbarPrefab;
        Check(toolbar.summaryText != null, "工具栏没接 summaryText");
        Check(toolbar.pageText != null, "工具栏没接 pageText");
        Check(toolbar.prevButton != null, "工具栏没接 prevButton");
        Check(toolbar.nextButton != null, "工具栏没接 nextButton");

        CertificateDetailPanel detail = host.detailPanel;
        Check(detail.personNameText != null, "详情卡没接 personNameText");
        Check(detail.certNumberText != null, "详情卡没接 certNumberText");
        Check(detail.phoneText != null, "详情卡没接 phoneText");
        Check(detail.certTypeNameText != null, "详情卡没接 certTypeNameText");
        Check(detail.statusText != null, "详情卡没接 statusText");
        Check(detail.workYearsText != null, "详情卡没接 workYearsText");
        Check(detail.certNameText != null, "详情卡没接 certNameText");

        Debug.Log("SpecialJobCertChecks: 场景接线校验通过（未保存场景）");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void ExpectFailure(Action action, string message)
    {
        try
        {
            action();
        }
        catch (Exception)
        {
            return;
        }

        throw new Exception(message);
    }
}
