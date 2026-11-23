using System.Collections.Generic;

[System.Serializable]
public class CompanyID
{
    public string id;
    public string companyName;
}

// {"success":true,"code":200,"message":"success","result":{"id":"1889124753553694722","companyName":"广州南业运输有限公司","establishmentDate":"2011-05-05 00:00:00","address":"广州市南沙区黄阁镇小虎南四路22号","buildingArea":63266.00,"employeeCount":2,"industryClassification":null,"longitude":"113.5522568281492","latitude":"22.852322664367513","longitude84":null,"latitude84":null,"companySize":4,"registeredCapital":1000.00,"socialCreditCode":"91440101574021134E","legalRepresentative":"陈伟","legalRepresentativePhone":null,"safetyOfficer":null,"safetyManagementOrganizationName":null,"safetyManagementSystem":"","safetyOfficerPhone":"13501534348","businessScope":"危险货物运输【3类(仅准运输:瓦斯油或柴油或轻质燃料油;车用油或汽油)禁运爆炸品、化学品、强腐蚀性危险货物】","pictureId":null,"evaluationReportId":null,"createBy":"nyys","createTime":"2025-02-11 09:30:14","updateBy":"nyys","updateTime":"2025-04-03 09:26:41","sysOrgCode":"1781217600564244480","companyControllerName":"陈伟","companyTags":null,"inZone":"1","companyNature":"2","companyFloorImgId":"/jeecg/sys/common/download/minio/jeecg/basic/2_1739237410366.jpg","haveBuyInsurance":"N","industryType":"交通运输、仓储和邮政业[G]","landArea":"63266","companyStatus":"1","insuranceBuyTime":null,"riskLevel":"4","companyShortName":"南业运输","safetyManagementPersonnelRequired":1,"plannedSafetyPersonnel":1,"implementationSystemTime":null,"actualSafetyPersonnel":0,"safetyManagementPersonnelActual":1,"deptOrgCode":"1781217600564244480","townStreet":null,"safetyResponsibilityInsuranceImage":"","safetyContactPerson":"","riskLevelName":"低风险","companyNatureName":"私营企业","companySizeName":"小微型","companyTagsName":null,"companyStatusName":"正常"},"timestamp":1754891639714}
[System.Serializable]
public class Company
{
    public string id;
    public string companyName;
    public string establishmentDate;
    public string address;
    public double buildingArea;
    public int employeeCount;
    public string industryClassification;
    public float longitude;
    public float latitude;
    public float longitude84;
    public float latitude84;
    public int companySize;
    public double registeredCapital;
    public string socialCreditCode;
    public string legalRepresentative;
    public string legalRepresentativePhone;
    public string safetyOfficer;
    public string safetyManagementOrganizationName;
    public string safetyManagementSystem;
    public string safetyOfficerPhone;
    public string businessScope;
    public string pictureId;
    public string evaluationReportId;
    public string createBy;
    public string createTime;
    public string updateBy;
    public string updateTime;
    public string sysOrgCode;
    public string companyControllerName;
    public string companyTags;
    public string inZone;
    public string companyNature;
    public string companyFloorImgId;
    public string haveBuyInsurance;
    public string industryType;
    public string landArea;
    public string companyStatus;
    public string insuranceBuyTime;
    public string riskLevel;
    public string companyShortName;
    public int safetyManagementPersonnelRequired;
    public int plannedSafetyPersonnel;
    public string implementationSystemTime;
    public int actualSafetyPersonnel;
    public int safetyManagementPersonnelActual;
    public string deptOrgCode;
    public string townStreet;
    public string safetyResponsibilityInsuranceImage;
    public string safetyContactPerson;
    public string riskLevelName;
    public string companyNatureName;
    public string companySizeName;
    public string companyTagsName;
    public string companyStatusName;
}
