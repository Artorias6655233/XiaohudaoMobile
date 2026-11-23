using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Backend-contract fixture used while the new MR endpoints are unavailable.
/// It is process-local and is always labelled as demo data by the service layer.
/// </summary>
public sealed class MockDoublePreventionStore
{
    private readonly List<DoubleDanger> hazards = new List<DoubleDanger>();
    private readonly List<PatrolRecord> patrols = new List<PatrolRecord>();
    private readonly List<InspectionAttachment> attachments = new List<InspectionAttachment>();
    private readonly List<SupervisionRecord> supervisions = new List<SupervisionRecord>();
    private readonly Dictionary<string, string> supervisionRequests =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public static MockDoublePreventionStore CreateDefault()
    {
        var store = new MockDoublePreventionStore();
        store.hazards.Add(new DoubleDanger
        {
            sid = "danger-001",
            hiddenDangerName = "罐区消防通道堆放杂物",
            hiddenDangerDescription = "消防通道被临时物料占用",
            hiddenDangerLocation = "一号罐区东侧",
            hiddenDangerLevel = "X2001",
            hiddenDangerStatus = "4",
            companyName = "演示企业",
            topOrgCode = "mock-org-a",
            verifyInfo = new VerifyInfo
            {
                rectificationDeadline = "2026-09-30",
                hiddenDangerVerifyChangePerson = "张整改",
                hiddenDangerVerifyChangePersonId = "mock-person-1"
            },
            reviewInfos = new List<ReviewInfo>()
        });
        store.hazards.Add(new DoubleDanger
        {
            sid = "danger-002",
            hiddenDangerName = "巡检记录未及时补录",
            hiddenDangerDescription = "班组巡检记录缺失",
            hiddenDangerLocation = "装卸区",
            hiddenDangerLevel = "X2001",
            hiddenDangerStatus = "5",
            companyName = "演示企业",
            topOrgCode = "mock-org-a",
            verifyInfo = new VerifyInfo
            {
                rectificationDeadline = "2026-09-25",
                hiddenDangerVerifyChangePerson = "李整改",
                hiddenDangerVerifyChangePersonId = "mock-person-2"
            },
            reviewInfos = new List<ReviewInfo> { new ReviewInfo { reviewResult = "3" } }
        });
        store.supervisions.Add(new SupervisionRecord
        {
            sid = "supervision-seed-001",
            hiddenDangerReportId = "danger-001",
            remindContent = "请在整改期限前清理通道并反馈。",
            senderId = "mock-supervisor",
            senderName = "演示监管员",
            recipientId = "mock-person-1",
            recipientName = "张整改",
            deliveryStatus = "sent",
            created = "2026-09-21 09:30:00"
        });

        var item = new PatrolInspectionItem
        {
            patrolRecordId = "patrol-item-001",
            inspectionStatus = "消防设施完好，通道存在临时堆物。",
            inspectionType = "aqyh",
            hazardCategory = "消防安全",
            isMajorAccident = "0"
        };
        store.patrols.Add(new PatrolRecord
        {
            sid = "patrol-001",
            corpName = "演示企业",
            corpAddress = "南沙区演示路 1 号",
            patrolStartTime = "2026-09-20 09:00:00",
            patrolAddress = "一号罐区",
            docCode = "MR-XC-20260920-001",
            riskLevel = "2",
            patrolPerson = "演示监管员",
            patrolStatus = "2",
            companySafetyInspections = new List<PatrolInspectionItem> { item }
        });
        store.attachments.AddRange(new[]
        {
            new InspectionAttachment { sid = "attachment-image", attachName = "现场照片.jpg", mimeType = "image/jpeg", fileSize = 128000 },
            new InspectionAttachment { sid = "attachment-audio", attachName = "现场说明.mp3", mimeType = "audio/mpeg", fileSize = 256000, durationSeconds = 18 },
            new InspectionAttachment { sid = "attachment-video", attachName = "现场视频.mp4", mimeType = "video/mp4", fileSize = 1048576, durationSeconds = 32 }
        });
        return store;
    }

    public static MockDoublePreventionStore Create(DoublePreventionMockDocument document)
    {
        if (document == null) return CreateDefault();
        var store = new MockDoublePreventionStore();
        store.hazards.AddRange(document.hazards ?? new List<DoubleDanger>());
        store.patrols.AddRange(document.patrols ?? new List<PatrolRecord>());
        store.attachments.AddRange(document.attachments ?? new List<InspectionAttachment>());
        store.supervisions.AddRange(document.supervisions ?? new List<SupervisionRecord>());
        return store;
    }

    public List<DoubleDanger> GetUnfinishedHazards(string topOrgCode)
    {
        return hazards.Where(item =>
                DoublePreventionRules.IsUnfinished(item) &&
                (string.IsNullOrEmpty(topOrgCode) || item.topOrgCode == topOrgCode))
            .ToList();
    }

    public List<PatrolRecord> GetPatrols(string companyName)
    {
        return patrols.Where(item => string.IsNullOrEmpty(companyName) || item.corpName == companyName)
            .ToList();
    }

    public PatrolRecord GetPatrol(string sid)
    {
        return patrols.FirstOrDefault(item => item.sid == sid);
    }

    public List<InspectionAttachment> GetAttachments(string patrolRecordId)
    {
        return patrolRecordId == "patrol-item-001"
            ? new List<InspectionAttachment>(attachments)
            : new List<InspectionAttachment>();
    }

    public List<SupervisionRecord> GetSupervisions(string hiddenDangerReportId)
    {
        return supervisions.Where(item => item.hiddenDangerReportId == hiddenDangerReportId)
            .OrderByDescending(item => item.created).ToList();
    }

    public SupervisionRecord SendSupervision(
        string hiddenDangerReportId,
        string content,
        string clientRequestId)
    {
        string normalized = DoublePreventionRules.NormalizeContent(content);
        DoubleDanger danger = hazards.FirstOrDefault(item => item.sid == hiddenDangerReportId);
        if (!DoublePreventionRules.IsUnfinished(danger))
            throw new InvalidOperationException("仅未完成隐患可以下达督办");
        if (string.IsNullOrWhiteSpace(clientRequestId))
            throw new ArgumentException("请求标识不能为空", nameof(clientRequestId));
        if (supervisionRequests.TryGetValue(clientRequestId, out string existingId))
            return supervisions.First(item => item.sid == existingId);

        var record = new SupervisionRecord
        {
            sid = Guid.NewGuid().ToString("N"),
            hiddenDangerReportId = hiddenDangerReportId,
            remindContent = normalized,
            senderId = "mock-supervisor",
            senderName = "当前演示用户",
            recipientId = danger.verifyInfo?.hiddenDangerVerifyChangePersonId,
            recipientName = danger.verifyInfo?.hiddenDangerVerifyChangePerson,
            deliveryStatus = "mock",
            created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };
        supervisions.Add(record);
        supervisionRequests.Add(clientRequestId, record.sid);
        return record;
    }

}
