using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Run with Unity -batchmode -executeMethod TankWarningChecks.Run -quit.
public static class TankWarningChecks
{
    public static void Run()
    {
        Check(!TankWarningData.SameCompany(null, null), "Empty company codes must not match");
        Check(TankWarningData.SameCompany(" A ", "A"), "Company codes are trimmed");
        Check(!TankWarningData.SameCompany("A", "AB"), "Company match must be exact");
        var tank = new Tank { tankId = "tank", equipCode = "device", tankName = "Tank", tankVol = 1, liquidTop = 0 };
        var snapshot = TankWarningData.ParseLatest(
            "{\"success\":true,\"body\":{\"device\":{\"liquidlevel\":\"0\",\"reserves\":\"1.000000\",\"temperature\":\"\",\"stress\":\"NaN\",\"ts\":\"2026-09-20T10:00:00+08:00\"}}}", tank);
        Check(snapshot.readings.Count == 4, "Four readings");
        Check(snapshot.readings[0].hasValue && snapshot.readings[0].numeric == 0, "Zero is valid");
        Check(snapshot.readings[1].value == "1", "Readings are formatted, not echoed as 1.000000");
        // tankVol / liquidTop 是非空 double，接口没填时落成 0，不能当成真值显示
        Check(snapshot.detail.tankVol == "1", "Tank volume comes from tank/list");
        Check(snapshot.detail.liquidTop == null, "Zero means not configured, not a real limit");
        Check(snapshot.readings[0].level == TankAlarmLevel.NoThreshold, "No thresholds never means normal");
        Check(!snapshot.readings[2].hasValue, "Empty value is not zero");
        Check(!snapshot.readings[3].hasValue, "NaN is invalid");
        ExpectFailure(() => TankWarningData.ParseLatest("{\"success\":true,\"body\":{\"other\":{\"liquidlevel\":\"9\"}}}", tank));
        ExpectFailure(() => TankWarningData.ParseLatest("{\"success\":false,\"body\":{}}", tank));
        ExpectFailure(() => TankWarningData.ParseLatest("not json", tank));
        Check(snapshot.readings[0].unit == "未提供", "Do not infer measurement units");
        var markerRoot = new GameObject("test tank");
        try
        {
            var marker = markerRoot.AddComponent<TankObject>();
            var dot = new GameObject("dot", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            dot.transform.SetParent(markerRoot.transform);
            marker.hitpointImage = dot.GetComponent<UnityEngine.UI.Image>();
            marker.SetLabelState(TankLabelState.Unavailable);
            Check(marker.hitpointImage.color == Color.gray, "Unknown is neutral, not green");
            marker.SetLabelState(TankLabelState.SelectedUnavailable);
            Check(marker.hitpointImage.color == Color.cyan, "Selection is not an alarm");
            marker.SetLabelState(TankLabelState.AboveNormal);
            Check(marker.hitpointImage.color == Color.red, "Existing alarm rendering is preserved");
        }
        finally { UnityEngine.Object.DestroyImmediate(markerRoot); }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ArtWorks/TankMonitorPanel.prefab");
        Check(prefab != null && prefab.GetComponent<TankMonitorPanel>() != null, "Monitor prefab exists");
        var panel = prefab.GetComponent<TankMonitorPanel>();
        Check(panel.liquidLevelRow != null && panel.temperatureRow != null && panel.statusHintText != null,
            "Required display bindings exist");
        CheckMarkerKeyIsDeviceCode();
        CheckGrouping();
        CheckTankListCard();
        CheckHostCanvas();
        CheckShippedConfigHasNoDebugCompany();
        Debug.Log("TankWarningChecks: all checks passed");
    }

    /// <summary>
    /// tankId 是企业内序号，不是主键：实测 tank/list 264 条里 "1" 被 5 家企业同时占用。
    /// 按它建 Session.tankObjects 的键，后建的会覆盖先建的，而被覆盖掉的恰好是全库仅有的
    /// 几家有逐罐独立坐标的企业——三个面板都会少图标，症状看起来却像"接口没给数据"。
    /// </summary>
    private static void CheckMarkerKeyIsDeviceCode()
    {
        var hualong = new Tank { tankId = "1", equipCode = "440100110001G0001" };
        var jiantao = new Tank { tankId = "1", equipCode = "440100120001G0001" };
        Check(hualong.MarkerKey == hualong.equipCode, "Marker key must be the device code");
        Check(hualong.MarkerKey != jiantao.MarkerKey,
            "Two companies sharing a tankId must not collide into one marker");
        Check(new Tank { tankId = "1", equipCode = " x " }.MarkerKey == "x", "Marker key is trimmed");
        Check(new Tank { tankId = "1", equipCode = " " }.MarkerKey == null,
            "A tank with no device code cannot be placed");
    }

    /// <summary>
    /// 聚合规则：同企业只出一个图标，落点取该企业罐最密集的坐标。
    /// 落点必须稳定——若并列时按字典序或遍历序挑，同一份目录两次进页面图标会跳到不同的点。
    /// </summary>
    private static void CheckGrouping()
    {
        Tank At(string code, string org, string lon, string lat)
        {
            return new Tank { tankId = "1", equipCode = code, sysOrgCode = org, tankName = code,
                longitude = lon, latitude = lat };
        }

        // 少数派坐标排在最前面，确保挑的是众数而不是第一条
        var groups = TankGrouping.Build(new List<Tank>
        {
            At("a1", " org-a ", "113.5", "22.8"),
            At("a2", "org-a", "113.6", "22.9"),
            At("a3", "org-a", "113.6", "22.9"),
            At("a4", "org-a", "113.6", "22.9"),
            At("b1", "org-b", "113.7", "22.7"),
            At("bad", "org-a", "longitude", "latitude"),
            At("zero", "org-a", "0", "0"),
            At("orphan", "  ", "113.5", "22.8"),
        });

        Check(groups.Count == 2, "Tanks are grouped by company");
        var a = groups[0];
        Check(a.sysOrgCode == "org-a", "Company code is trimmed into the group key");
        Check(a.tanks.Count == 4, "Unplaceable tanks are dropped, the rest all stay in the list");

        // markers 仍按坐标分桶保留真实罐位（将来要摊开时用得上），
        // 但沙盘上只放 Primary 那一个，入口统一成"点图标 → 看列表 → 选罐"。
        Check(a.markers.Count == 2, "Real per-tank coordinates are still kept in the group");
        Check(a.Primary.longitude == 113.6 && a.Primary.latitude == 22.9,
            "The primary marker sits on the busiest coordinate, not on the first row");
        Check(a.Primary.tankCount == 3, "The primary marker knows how many tanks it stands for");
        Check(a.Primary.representative.MarkerKey == "a2",
            "Representative is the first tank on that coordinate");
        Check(a.markers[1].tankCount == 1 && a.markers[1].representative.MarkerKey == "a1",
            "The lone tank keeps its own marker");
        Check(groups[1].markers.Count == 1, "A single-tank company still gets one marker");

        // 同一份输入必须给出同一个落点顺序
        var again = TankGrouping.Build(new List<Tank>
        {
            At("a1", "org-a", "113.5", "22.8"),
            At("a2", "org-a", "113.6", "22.9"),
            At("a3", "org-a", "113.6", "22.9"),
            At("a4", "org-a", "113.6", "22.9"),
        });
        Check(again[0].Primary.longitude == a.Primary.longitude
            && again[0].Primary.latitude == a.Primary.latitude,
            "Marker placement must be deterministic across rebuilds");

        // 沙盘上一家企业只亮一个图标，哪怕它的罐坐标各不相同
        var keys = TankGrouping.MarkerKeys(groups);
        Check(keys.Count == groups.Count, "Exactly one marker per company reaches the sandtable");
        Check(keys.Contains("a2") && !keys.Contains("a1"),
            "The one marker is the primary, not some other coordinate of the same company");

        var spread = TankGrouping.Build(new List<Tank>
        {
            At("s1", "org-s", "113.1", "22.1"),
            At("s2", "org-s", "113.2", "22.2"),
            At("s3", "org-s", "113.3", "22.3"),
        });
        Check(TankGrouping.MarkerKeys(spread).Count == 1,
            "A company whose tanks all sit apart still gets a single marker");
    }

    /// <summary>
    /// 储罐列表是同企业多罐时唯一的选择入口，接线漏一处就等于那些罐又选不到了。
    ///
    /// 它是【独立的一张卡片】，不在监测卡片里：要和管理员信息卡片并排，
    /// 而详情卡片是另一张。所以这里顺带钉住"监测卡片上不该再有列表残留"。
    /// </summary>
    private static void CheckTankListCard()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ArtWorks/TankListPanel.prefab");
        Check(prefab != null, "Tank list card exists as its own prefab");

        var panel = prefab.GetComponent<TankListPanel>();
        Check(panel != null, "The list card carries TankListPanel");
        Check(panel.panelRoot != null, "The list card needs a body to show and hide");
        Check(panel.rows != null && panel.rows.Count > 0, "The list needs rows");
        foreach (var row in panel.rows)
            Check(row != null && row.button != null && row.nameText != null,
                "Every list row needs a button and a name, otherwise that tank cannot be picked");
        Check(panel.prevButton != null && panel.nextButton != null && panel.pageText != null,
            "Paging controls are required: one page cannot hold 30 tanks");

        // 列表在左、管理员信息在右，间隔做在本卡片【右侧】留白里，不动宿主布局组的 m_Spacing
        var rt = (RectTransform)prefab.transform;
        var bodyRt = (RectTransform)panel.panelRoot.transform;
        Check(bodyRt.anchoredPosition.x == 0f,
            "The body sits flush left; the gap belongs on the right, towards the person card");
        Check(rt.rect.width > bodyRt.rect.width,
            "The card rect must include that gap, otherwise the layout group closes it up");

        var monitor = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ArtWorks/TankMonitorPanel.prefab");
        foreach (Transform child in monitor.transform)
            Check(child.name != "ListRoot" && child.name != "DetailRoot" && child.name != "BackToList",
                "The detail card must not carry a list any more: " + child.name);

        // 详情态会把列表和管理员信息一起收起来，没有返回键就回不去
        Check(monitor.GetComponent<TankMonitorPanel>().backButton != null,
            "The detail card needs a back button, it hides the other two cards");
    }

    /// <summary>
    /// 卡片是 Instantiate 到面板自己的 transform 下的。宿主没有 Canvas 时 Unity
    /// 既不报错也不绘制，只能靠这条断言拦住——上一版就是这么漏到真机的。
    /// </summary>
    private static void CheckHostCanvas()
    {
        EditorSceneManager.OpenScene("Assets/LZW/SandTable/SandTable.unity", OpenSceneMode.Single);
        var host = UnityEngine.Object.FindObjectOfType<MajorHazard_TankList>(true);
        Check(host != null, "Scene contains the monitoring panel");
        Check(host.tankMonitorPrefab != null, "Monitor prefab is wired in the scene");
        Check(host.tankListPrefab != null, "List prefab is wired in the scene");

        var management = UnityEngine.Object.FindObjectOfType<MajorHazard_Management>(true);
        Check(management != null, "Scene contains the risk-control panel");
        Check(management.tankListPrefab != null,
            "The risk-control panel needs the list too, its tanks overlap the same way");

        var canvas = host.GetComponentInParent<Canvas>();
        Check(canvas != null, "Monitor card host has a Canvas, otherwise the card never draws");
        Check(canvas.renderMode == RenderMode.WorldSpace, "Card canvas must be world space on the headset");
        Check(host.GetComponentInParent<GraphicRaycaster>() != null,
            "Card host needs a GraphicRaycaster for handle input");
        CheckCompanyCacheIsPublishedInOneStep(host);
        CheckDebugDefaultCompanySelection(host);
    }

    /// <summary>
    /// 默认选中是个联调开关，必须满足两条：目录里没有这个 id 时当没配置（否则会把选中态
    /// 指到一个不存在的企业，界面上既没图标也没储罐，看起来像功能坏了）；配了就必须经由
    /// Session.companyID 生效，而不是绕过事件自己改一套状态。
    /// </summary>
    private static void CheckDebugDefaultCompanySelection(MajorHazard_TankList host)
    {
        var select = typeof(MajorHazard_TankList).GetMethod(
            "SelectDebugDefaultCompany", BindingFlags.Instance | BindingFlags.NonPublic);
        Check(select != null, "Debug default company selection must exist");

        var session = Session.Instance;
        var catalog = (Dictionary<string, Company>)typeof(MajorHazard_TankList)
            .GetField("companies", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host);
        string before = session.companyID;
        string configured = ApiConfig.DebugDefaultCompanyId;
        try
        {
            catalog.Clear();
            Check(!(bool)select.Invoke(host, null),
                "A debug company missing from the catalog must leave the selection alone");

            if (!string.IsNullOrWhiteSpace(configured))
            {
                catalog[configured] = new Company { id = configured, companyName = "debug" };
                Check((bool)select.Invoke(host, null), "A configured debug company must be selected");
                Check(session.companyID == configured, "Selection must go through Session.companyID");
            }
        }
        finally
        {
            catalog.Clear();
            session.companyID = before;
        }
    }

    /// <summary>
    /// 随包配置必须把调试默认企业留空，不然出到现场会替用户选中鸿业储运。
    /// 这里读的是 Resources 里那份文本，不是 ApiConfig 的生效值——本机覆盖文件填了是正常的。
    /// </summary>
    private static void CheckShippedConfigHasNoDebugCompany()
    {
        var asset = Resources.Load<TextAsset>(ApiConfig.ResourcePath);
        Check(asset != null, "Shipped api_config.json must exist");
        Check(!Regex.IsMatch(asset.text, @"""debugDefaultCompanyId""\s*:\s*""[^""]+"""),
            "Shipped api_config.json must not pin a debug default company");
    }

    /// <summary>
    /// Other panels treat a non-empty Session.companyObjects as a complete catalog. If this
    /// panel yields after publishing only part of the companies, switching pages leaves the
    /// shared cache permanently incomplete. The production publishing step must finish before
    /// it returns control to Unity.
    /// </summary>
    private static void CheckCompanyCacheIsPublishedInOneStep(MajorHazard_TankList host)
    {
        var publish = typeof(MajorHazard_TankList).GetMethod(
            "PublishCompanyMarkers", BindingFlags.Instance | BindingFlags.NonPublic);
        Check(publish != null, "Company marker publication must be an atomic step");

        var session = UnityEngine.Object.FindObjectOfType<Session>(true);
        Check(session != null, "Scene contains Session");
        typeof(Session).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)
            .SetValue(null, session);

        var catalogField = typeof(MajorHazard_TankList).GetField(
            "companies", BindingFlags.Instance | BindingFlags.NonPublic);
        var catalog = (Dictionary<string, Company>)catalogField.GetValue(host);
        var createdIds = new List<string>();
        for (int index = 0; index < 21; index++)
        {
            string id = "smoke-company-" + index;
            catalog[id] = new Company
            {
                id = id,
                companyName = id,
                sysOrgCode = "smoke-org-" + index,
                longitude = 113.5f + index * 0.0001f,
                latitude = 22.8f
            };
            createdIds.Add(id);
        }

        try
        {
            publish.Invoke(host, null);
            foreach (string id in createdIds)
                Check(session.companyObjects.ContainsKey(id),
                    "Company marker publication returned with a partial shared cache");
        }
        finally
        {
            foreach (string id in createdIds)
            {
                if (session.companyObjects.TryGetValue(id, out var marker) && marker != null)
                    UnityEngine.Object.DestroyImmediate(marker.gameObject);
                session.companyObjects.Remove(id);
                session.sysOrgCodeToID.Remove("smoke-org-" + id.Substring(id.LastIndexOf('-') + 1));
            }
            catalog.Clear();
        }
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    private static void ExpectFailure(Action action)
    {
        bool failed = false;
        try { action(); } catch (Exception) { failed = true; }
        Check(failed, "Invalid response must fail instead of returning cached/other-device data");
    }
}
