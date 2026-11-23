using System;
using System.Collections.Generic;

[Serializable]
public sealed class SupervisionRecord
{
    public string sid;
    public string hiddenDangerReportId;
    public string remindContent;
    public string senderId;
    public string senderName;
    public string recipientId;
    public string recipientName;
    public string deliveryStatus;
    public string created;
}

[Serializable]
public sealed class PatrolRecord
{
    public string sid;
    public string corpName;
    public string corpAddress;
    public string legalPerson;
    public string phoneNumber;
    public string patrolStartTime;
    public string patrolAddress;
    public string docCode;
    public string riskLevel;
    public string patrolPerson;
    public string patrolStatus;
    public string patrolId;
    public string rectSid;
    public string startTime;
    public string endTime;
    public List<PatrolPerson> persons = new List<PatrolPerson>();
    public List<PatrolInspectionItem> companySafetyInspections =
        new List<PatrolInspectionItem>();
}

[Serializable]
public sealed class PatrolPerson
{
    public string sid;
    public string name;
}

[Serializable]
public sealed class PatrolInspectionItem
{
    public string inspectionStatus;
    public string inspectionType;
    public string hazardCategory;
    public string isMajorAccident;
    public string patrolRecordId;
}

[Serializable]
public sealed class InspectionAttachment
{
    public string sid;
    public string attachName;
    public string mimeType;
    public long fileSize;
    public double durationSeconds;
}

[Serializable]
public sealed class InspectionAnnotation
{
    public string sid;
    public string patrolRecordId;
    public string content;
    public string authorId;
    public string authorName;
    public string created;
    public string updated;
    public int version;
    public bool editable;
}

[Serializable]
public sealed class PatrolPage
{
    public List<PatrolRecord> rows = new List<PatrolRecord>();
    public int total;
}

[Serializable]
public sealed class AttachmentPage
{
    public List<InspectionAttachment> rows = new List<InspectionAttachment>();
    public int total;
}

[Serializable]
public sealed class SupervisionPageBody
{
    public List<SupervisionRecord> data = new List<SupervisionRecord>();
    public int total;
}

[Serializable]
public sealed class SupervisionPage
{
    public string status;
    public string message;
    public SupervisionPageBody body;
}

[Serializable]
public sealed class SupervisionResponse
{
    public string status;
    public string message;
    public SupervisionRecord body;
}

public enum InspectionMediaKind
{
    Unsupported,
    Image,
    Audio,
    Video
}

[Serializable]
public sealed class DoublePreventionMockDocument
{
    public List<DoubleDanger> hazards = new List<DoubleDanger>();
    public List<PatrolRecord> patrols = new List<PatrolRecord>();
    public List<InspectionAttachment> attachments = new List<InspectionAttachment>();
    public List<SupervisionRecord> supervisions = new List<SupervisionRecord>();
}
