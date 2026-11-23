[System.Serializable]
public class DoubleDanger
{
    public string sid;
    public string hiddenDangerName;
    public string hiddenDangerLevel;
    public string hiddenDangerType;
    public string hiddenDangerDescription;
    public string hiddenDangerLocation;
    public string hiddenDangerStatus;
    public string hiddenDangerSource;
    public string relevanceRiskPoint;
    public string majorHazardName;
    public string created;
    public string createdBy;
    public string reportedPerson;
    public string hiddenDangerImgUrls;
    public string companyName;
    public string topOrgCode;
    public string deptOrgCode;
    
    public VerifyInfo verifyInfo;
    public System.Collections.Generic.List<ChangeInfo> changeInfos;
    public System.Collections.Generic.List<ReviewInfo> reviewInfos;

    // Old fields for compatibility if needed, or remove them
    public string id;
    public string sysOrgCode;
    public string companyId;
}

[System.Serializable]
public class VerifyInfo
{
    public string sid;
    public string hiddenDangerReportId;
    public string hiddenDangerVerifyResult;
    public string hiddenDangerVerifyInfo;
    public string hiddenDangerVerifyImgUrls;
    public string rectificationDeadline;
    public string hiddenDangerVerifyChangePerson;
    public string hiddenDangerVerifyChangePersonId;
    public string rectificationDept;
    public string rectificationDeptId;
    public string submitStatus;
    public string created;
    public string createdBy;
}

[System.Serializable]
public class ChangeInfo
{
    public string sid;
    public string hiddenDangerReportId;
    public string hiddenDangerChangeInfo;
    public string hiddenDangerChangeImgUrls;
    public string hiddenDangerChangeTime;
    public int submitStatus;
    public string created;
    public string createdBy;
}

[System.Serializable]
public class ReviewInfo
{
    public string sid;
    public string hiddenDangerReportId;
    public string reviewResult;
    public string hiddenDangerReviewInfo;
    public string hiddenDangerReviewImgUrls;
    public string reviewPerson;
    public string reviewPersonId;
    public string created;
    public string createdBy;
}
