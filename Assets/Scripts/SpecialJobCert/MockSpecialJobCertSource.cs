using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// 随包演示数据源。读 <see cref="ResourcePath"/>，不发任何网络请求，也不碰
/// <see cref="DataProvider"/>（那边有离线镜像兜底，演示数据混进去就分不清来源了）。
///
/// 【状态为什么在客户端算】演示包里只存发证日期和有效期，「有效 / 临期 / 已过期」由
/// <see cref="ResolveStatus"/> 按当天日期现算。这样演示数据放几个月也是自洽的。
/// 正式接口反过来——状态由服务端判定并返回，客户端不许自己算（见接口需求 C3），
/// 所以 <see cref="CertRow.statusText"/> 在两种实现里都是「已经定好的结果」，页面不关心它怎么来的。
/// </summary>
public sealed class MockSpecialJobCertSource : ISpecialJobCertSource
{
    public const string ResourcePath = "SpecialJobCertMock/cert_mock_data";

    private readonly Dictionary<string, CompanyCertSummary> byCompany =
        new Dictionary<string, CompanyCertSummary>(StringComparer.Ordinal);

    private bool loaded;

    public bool IsDemo => true;

    public bool Ready => loaded;

    /// <summary>演示包里出现过的企业编码，Editor 校验用来和企业目录对账。</summary>
    public IEnumerable<string> CompanyCodes => byCompany.Keys;

    public void Load()
    {
        Load(DateTime.Today);
    }

    /// <param name="today">状态判定的基准日。测试和 Editor 校验传固定值，运行时传 <see cref="DateTime.Today"/>。</param>
    public void Load(DateTime today)
    {
        TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
        if (asset == null)
            throw new InvalidOperationException($"证件演示数据包不可用：Resources/{ResourcePath}.json 没找到");

        MockDataSet data;
        try
        {
            data = JsonConvert.DeserializeObject<MockDataSet>(asset.text);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("证件演示数据包解析失败", exception);
        }

        if (data?.companies == null) throw new InvalidOperationException("证件演示数据包里没有 companies");

        byCompany.Clear();
        foreach (MockCompany company in data.companies)
        {
            if (company == null || string.IsNullOrWhiteSpace(company.companyCode))
                throw new InvalidOperationException("证件演示数据包里有企业缺 companyCode");

            string code = company.companyCode.Trim();
            if (byCompany.ContainsKey(code))
                throw new InvalidOperationException($"证件演示数据包里企业编码重复：{code}");

            byCompany[code] = Build(company, today);
        }

        loaded = true;
        Debug.Log($"[证件演示] 企业 {byCompany.Count} 家，" +
            $"持证人 {byCompany.Values.Sum(item => item.holderCount)} 人，" +
            $"证件 {byCompany.Values.Sum(item => item.certCount)} 本");
    }

    /// <summary>
    /// 演示包本来就是同步读的，这里只是满足统一入口。异常就地转成 Ready=false——
    /// 协程里抛出去调用方接不住。<see cref="Load()"/> 照抛，Editor 校验靠的就是它。
    /// </summary>
    public IEnumerator LoadAsync()
    {
        try
        {
            Load();
        }
        catch (Exception exception)
        {
            Debug.LogError("[证件演示] 数据包不可用：" + exception.Message);
        }
        yield break;
    }

    public CompanyCertSummary GetByCompany(string companyCode)
    {
        if (!loaded || string.IsNullOrWhiteSpace(companyCode)) return CompanyCertSummary.Empty;

        return byCompany.TryGetValue(companyCode.Trim(), out CompanyCertSummary summary)
            ? summary
            : CompanyCertSummary.Empty;
    }

    private static CompanyCertSummary Build(MockCompany company, DateTime today)
    {
        Dictionary<string, MockPerson> people = new Dictionary<string, MockPerson>(StringComparer.Ordinal);
        foreach (MockPerson person in company.people ?? new List<MockPerson>())
        {
            if (person == null || string.IsNullOrWhiteSpace(person.personId))
                throw new InvalidOperationException($"{company.companyCode} 下有人员缺 personId");
            if (people.ContainsKey(person.personId))
                throw new InvalidOperationException($"{company.companyCode} 下人员 ID 重复：{person.personId}");
            people[person.personId] = person;
        }

        List<CertRow> rows = new List<CertRow>();
        foreach (MockCert cert in company.certs ?? new List<MockCert>())
        {
            if (cert == null || string.IsNullOrWhiteSpace(cert.certId))
                throw new InvalidOperationException($"{company.companyCode} 下有证件缺 certId");
            if (!people.TryGetValue(cert.personId ?? string.Empty, out MockPerson owner))
                throw new InvalidOperationException(
                    $"{company.companyCode} 下证件 {cert.certId} 的 personId「{cert.personId}」在人员表里不存在");

            rows.Add(new CertRow
            {
                certId = cert.certId,
                personId = owner.personId,
                personName = owner.personName,
                phone = owner.phone,
                workYears = owner.workYears,
                certTypeName = cert.certTypeName,
                certName = cert.certName,
                certNumber = cert.certNumber,
                issueDate = cert.issueDate,
                expireDate = cert.expireDate,
                statusText = ResolveStatus(cert.expireDate, today),
                thirdParty = owner.thirdParty,
                managementUnit = owner.managementUnit,
            });
        }

        // 快到期的排前面：这一页最该被人看见的就是临期和已过期的。
        // 同一天到期的按姓名再排一次，保证每次打开顺序一致。
        rows = rows
            .OrderBy(row => ParseDate(row.expireDate) ?? DateTime.MaxValue)
            .ThenBy(row => row.personName, StringComparer.Ordinal)
            .ThenBy(row => row.certId, StringComparer.Ordinal)
            .ToList();

        int holders = rows.Select(row => row.personId).Distinct(StringComparer.Ordinal).Count();
        return new CompanyCertSummary(holders, rows.Count, rows);
    }

    /// <summary>有效期缺失或解析不了时返回空串，页面按「无数据」显示，不当成有效。</summary>
    public static string ResolveStatus(string expireDate, DateTime today)
    {
        DateTime? expire = ParseDate(expireDate);
        if (expire == null) return string.Empty;

        double days = (expire.Value.Date - today.Date).TotalDays;
        if (days < 0) return CertStatus.Expired;
        if (days <= CertStatus.ExpiringWithinDays) return CertStatus.Expiring;
        return CertStatus.Valid;
    }

    public static DateTime? ParseDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        // 演示包统一写 yyyy-MM-dd；真实接口那边常见 "yyyy-MM-dd HH:mm:ss"，顺手也认一下
        string trimmed = value.Trim();
        int space = trimmed.IndexOf(' ');
        if (space > 0) trimmed = trimmed.Substring(0, space);

        return DateTime.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out DateTime parsed)
            ? parsed
            : (DateTime?)null;
    }

    // ---------------------------------------------------------------- 演示包结构

    [Serializable]
    private sealed class MockDataSet
    {
        public string note;
        public List<MockCompany> companies;
    }

    [Serializable]
    private sealed class MockCompany
    {
        /// <summary>企业 sysOrgCode，必须能在 security/company/list 里找到。</summary>
        public string companyCode;

        /// <summary>只为了看数据时认得出是哪家，运行时不用。</summary>
        public string companyName;

        public List<MockPerson> people;
        public List<MockCert> certs;
    }

    [Serializable]
    private sealed class MockPerson
    {
        public string personId;
        public string personName;
        public string phone;
        public int workYears;
        public string thirdParty;
        public string managementUnit;
    }

    [Serializable]
    private sealed class MockCert
    {
        public string certId;
        public string personId;
        public string certTypeName;
        public string certName;
        public string certNumber;
        public string issueDate;
        public string expireDate;
    }
}
