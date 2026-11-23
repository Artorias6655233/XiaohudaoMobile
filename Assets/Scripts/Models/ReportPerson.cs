using System.Collections.Generic;

[System.Serializable]
public class ReportPerson
{
    public string id;
    public string name;
    public string phone;
    public string sysOrgCode;
    public string type;
    public string responsePosition;
    public string responsePositionName;
    public string isSafetyOrgMember;
}

[System.Serializable]
public class ReportPersonResponse
{
    public List<ReportPerson> result;
    public int code;
    public bool success;
    public string message;
}
