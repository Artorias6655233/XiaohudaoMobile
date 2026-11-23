// {
//     "id": "1946026416745971714",
//     "operationalStatus": "1",
//     "deviceName": "测试1",
//     "isMajorHazard": "0",
//     "deviceResponsiblePerson": "1",
//     "contactPhone": null,
//     "plannedStartTime": "2025-07-18 00:00:00",
//     "plannedEndTime": "2025-07-18 00:00:00",
//     "startupImplementationPlanId": "/jeecg/sys/common/download/minio/jeecg/basic/BullseyeCoverageError_1752803859797.txt",
//     "startupOtherAttachmentId": null,
//     "isRelatedToOverhaul": null,
//     "overhaulType": null,
//     "overhaulPlanSummary": null,
//     "majorOverhaulImplementationPlanId": null,
//     "createBy": "lqrh",
//     "createTime": "2025-07-18 09:57:07",
//     "updateBy": "lqrh",
//     "updateTime": "2025-07-18 10:02:36",
//     "sysOrgCode": "1801092293733789696",
//     "shutdownImplementationPlanId": "/jeecg/sys/common/download/minio/jeecg/basic/BullseyeCoverageError_1752803826705.txt",
//     "deptOrgCode": null,
//     "operationalStatusName": "开车",
//     "isMajorHazardName": "否"
//   }
[System.Serializable]
public class State
{
    public string id;
    public string operationalStatus;
    public string deviceName;
    public bool isMajorHazard;
    public string deviceResponsiblePerson;
    public string contactPhone;
    public string plannedStartTime;
    public string plannedEndTime;
    public string startupImplementationPlanId;
    public string startupOtherAttachmentId;
    public string isRelatedToOverhaul;
    public string overhaulType;
    public string overhaulPlanSummary;
    public string majorOverhaulImplementationPlanId;
    public string createBy;
    public string createTime;
    public string updateBy;
    public string updateTime;
    public string sysOrgCode;
    public string shutdownImplementationPlanId;
    public string deptOrgCode;
    public string operationalStatusName;
    public string isMajorHazardName;
}