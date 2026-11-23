using UnityEngine.Networking;

public static class DoublePreventionApi
{
    public static string DangerReviewFind =>
        Api.ICC_URL + "double-prevention/hiddenDangerReportTaskReview/find";
    public static string DangerRemind =>
        Api.ICC_URL + "double-prevention/hiddenDangerReport/remind";
    public static string DangerRemindFind =>
        Api.ICC_URL + "double-prevention/hiddenDangerReport/remind/find";
    public static string PatrolFindByPage => Api.ICC_URL + "enforce-client/patrol/findByPage";
    public static string PatrolFindBySid => Api.ICC_URL + "enforce-client/patrol/findBySid";
    public static string AttachmentFind => Api.ICC_URL + "enforce-client/attach/find";
    public static string AttachmentDownload(string sid)
    {
        return Api.ICC_URL + "enforce-client/attach/downloadPreview?sid=" +
               UnityWebRequest.EscapeURL(sid ?? string.Empty);
    }
}
