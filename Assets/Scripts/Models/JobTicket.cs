[System.Serializable]
public class JobTicket
{
    public string sid;
    public string processInstanceId;
    public string jobTicket;
    public string jobTicketType;
    public int jobTicketTypeCode;
    public string jobTicketCode;
    public string jobTicketContent;
    public string jobTicketUnit;
    public string jobTicketUnitId;
    public string jobTicketResponsibleId;
    public string jobTicketResponsible;
    public string applyingUnit;
    public string applyingUnitId;
    public string applicantId;
    public string applicant;
    public string jobTicketTime;
    public string applyTime;
    public string jobTicketStatus;
    public string createBy;
    public string createTime;
    public string updateBy;
    public string updateTime;
    public string sysOrgCode;
    public string deptOrgCode;
    public string fileTime;
    public string acceptancePerson;
    public string recordPerson;
    public string processDefinitionKey;
    public string acceptanceTime;

    // New fields added for detail panel
    public string cancelReason;
    public string monitorType;
    public string clockStatus;
    public string confirmPerson;
    public string riskResult;
    public string isArchived;
    public string highWork;
    public string workerName;
    public string jobStartTime;
    public string jobEndTime;
    public string jobAddress;
    public string longitude;
    public string latitude;
    public string jobTicketId;
    public string safetyDisclosurePerson;
    public string compilerName;
    public string otherSpecialJob;
}
