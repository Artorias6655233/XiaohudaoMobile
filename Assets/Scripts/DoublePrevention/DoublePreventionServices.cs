using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

public sealed class ServiceResult<T>
{
    public T value;
    public string error;
    public long httpCode;
    public bool isMock;
    public bool isLocal;
    public bool conflict;
    public bool ok => error == null;
}

public static class PortalApiClient
{
    public static IEnumerator PostJson<T>(string url, object body, ServiceResult<T> outcome)
    {
        yield return SendJson(url, JsonConvert.SerializeObject(body), outcome, false);
    }

    private static IEnumerator SendJson<T>(
        string url, string json, ServiceResult<T> outcome, bool retried)
    {
        yield return IccAuth.EnsureToken(retried);
        if (!IccAuth.HasToken)
        {
            outcome.error = "平台登录失败";
            yield break;
        }

        var fetch = new OfflineMock.Result();
        var headers = new Dictionary<string, string> { { "token", IccAuth.Token } };
        yield return OfflineMock.PostJson(url, json, headers, fetch);
        if (fetch.httpCode == 401 && !retried)
        {
            yield return SendJson(url, json, outcome, true);
            yield break;
        }
        Parse(fetch, url, outcome);
    }

    public static IEnumerator PostForm<T>(string url, WWWForm form, ServiceResult<T> outcome)
    {
        yield return SendForm(url, form, outcome, false);
    }

    private static IEnumerator SendForm<T>(
        string url, WWWForm form, ServiceResult<T> outcome, bool retried)
    {
        yield return IccAuth.EnsureToken(retried);
        if (!IccAuth.HasToken)
        {
            outcome.error = "平台登录失败";
            yield break;
        }

        var fetch = new OfflineMock.Result();
        using (UnityWebRequest request = UnityWebRequest.Post(url, form))
        {
            request.SetRequestHeader("token", IccAuth.Token);
            request.timeout = OfflineMock.NetworkTimeoutSeconds;
            yield return request.SendWebRequest();
            fetch.httpCode = request.responseCode;
            if (request.result == UnityWebRequest.Result.Success)
            {
                fetch.json = request.downloadHandler.text;
                fetch.source = "网络";
            }
            else
            {
                fetch.error = $"网络错误: {request.error} (HTTP {request.responseCode})";
            }
        }
        if (fetch.httpCode == 401 && !retried)
        {
            yield return SendForm(url, form, outcome, true);
            yield break;
        }
        Parse(fetch, url, outcome);
    }

    public static IEnumerator Download(
        string url,
        string destination,
        Func<bool> cancelled,
        Action<float> progress,
        ServiceResult<string> outcome,
        bool retried = false)
    {
        yield return IccAuth.EnsureToken(retried);
        if (!IccAuth.HasToken)
        {
            outcome.error = "平台登录失败";
            yield break;
        }

        string partial = destination + ".part";
        TryDelete(partial);
        using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET))
        {
            request.downloadHandler = new DownloadHandlerFile(partial);
            request.SetRequestHeader("token", IccAuth.Token);
            request.timeout = OfflineMock.NetworkTimeoutSeconds;
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                progress?.Invoke(request.downloadProgress);
                if (cancelled != null && cancelled())
                {
                    request.Abort();
                    TryDelete(partial);
                    outcome.error = "下载已取消";
                    yield break;
                }
                yield return null;
            }

            if (request.responseCode == 401 && !retried)
            {
                TryDelete(partial);
                yield return Download(url, destination, cancelled, progress, outcome, true);
                yield break;
            }
            if (request.result != UnityWebRequest.Result.Success)
            {
                TryDelete(partial);
                outcome.error = $"附件下载失败（HTTP {request.responseCode}）";
                yield break;
            }

            string contentType = request.GetResponseHeader("Content-Type") ?? string.Empty;
            if (contentType.IndexOf("text/html", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                TryDelete(partial);
                outcome.error = "附件接口返回了登录页面";
                yield break;
            }
        }

        TryDelete(destination);
        File.Move(partial, destination);
        progress?.Invoke(1f);
        outcome.value = destination;
    }

    private static void Parse<T>(OfflineMock.Result fetch, string context, ServiceResult<T> outcome)
    {
        outcome.httpCode = fetch.httpCode;
        if (!fetch.ok)
        {
            outcome.error = fetch.error ?? "请求失败";
            return;
        }
        string trimmed = (fetch.json ?? string.Empty).TrimStart();
        if (trimmed.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            outcome.error = "接口返回了登录页面";
            return;
        }
        try
        {
            outcome.value = JsonConvert.DeserializeObject<T>(fetch.json);
            outcome.isMock = fetch.source == "离线镜像";
            if (outcome.value == null) outcome.error = "响应内容为空";
        }
        catch (Exception exception)
        {
            outcome.error = $"{context} 响应解析失败：{exception.Message}";
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception exception) { Debug.LogWarning("[附件缓存] 清理失败：" + exception.Message); }
    }
}

public sealed class SupervisionService
{
    private readonly bool useMock;
    private readonly MockDoublePreventionStore mock;

    public SupervisionService(bool useMock, MockDoublePreventionStore mock = null)
    {
        this.useMock = useMock;
        this.mock = mock ?? MockDoublePreventionStore.CreateDefault();
    }

    public IEnumerator LoadUnfinished(string topOrgCode, ServiceResult<List<DoubleDanger>> outcome)
    {
        if (useMock)
        {
            outcome.value = mock.GetUnfinishedHazards(topOrgCode);
            outcome.isMock = true;
            yield break;
        }

        var statusFour = new ServiceResult<DoubleDangerExternalResponse>();
        var statusFive = new ServiceResult<DoubleDangerExternalResponse>();
        yield return LoadStatus(topOrgCode, "4", statusFour);
        if (!statusFour.ok) { outcome.error = statusFour.error; yield break; }
        yield return LoadStatus(topOrgCode, "5", statusFive);
        if (!statusFive.ok) { outcome.error = statusFive.error; yield break; }
        outcome.value = DoublePreventionRules.SelectUnfinished(
            statusFour.value?.body?.data, statusFive.value?.body?.data);
    }

    private static IEnumerator LoadStatus(
        string topOrgCode, string status, ServiceResult<DoubleDangerExternalResponse> outcome)
    {
        var request = new
        {
            body = new { topOrgCode = topOrgCode ?? string.Empty, hiddenDangerStatus = status },
            page = 1,
            pageSize = 1000
        };
        yield return PortalApiClient.PostJson(DoublePreventionApi.DangerReviewFind, request, outcome);
    }

    public IEnumerator LoadHistory(string dangerId, ServiceResult<List<SupervisionRecord>> outcome)
    {
        if (useMock)
        {
            outcome.value = mock.GetSupervisions(dangerId);
            outcome.isMock = true;
            yield break;
        }
        var raw = new ServiceResult<SupervisionPage>();
        yield return PortalApiClient.PostJson(DoublePreventionApi.DangerRemindFind, new
        {
            body = new { hiddenDangerReportId = dangerId }, page = 1, pageSize = 20
        }, raw);
        CopyError(raw, outcome);
        if (raw.ok) outcome.value = raw.value?.body?.data ?? new List<SupervisionRecord>();
    }

    public IEnumerator Send(
        DoubleDanger danger, string content, string clientRequestId,
        ServiceResult<SupervisionRecord> outcome)
    {
        if (!DoublePreventionRules.IsUnfinished(danger))
        {
            outcome.error = "仅未完成隐患可以下达督办";
            yield break;
        }
        string dangerId = danger.sid;
        string normalized;
        try { normalized = DoublePreventionRules.NormalizeContent(content); }
        catch (Exception exception) { outcome.error = exception.Message; yield break; }
        if (useMock)
        {
            try { outcome.value = mock.SendSupervision(dangerId, normalized, clientRequestId); outcome.isMock = true; }
            catch (Exception exception) { outcome.error = exception.Message; }
            yield break;
        }
        var raw = new ServiceResult<SupervisionResponse>();
        yield return PortalApiClient.PostJson(DoublePreventionApi.DangerRemind, new
        {
            hiddenDangerReportId = dangerId,
            remindContent = normalized,
            clientRequestId
        }, raw);
        CopyError(raw, outcome);
        if (raw.ok && raw.value?.status != "success") outcome.error = raw.value?.message ?? "督办发送失败";
        else if (raw.ok) outcome.value = raw.value.body;
    }

    private static void CopyError<TIn, TOut>(ServiceResult<TIn> source, ServiceResult<TOut> target)
    {
        target.error = source.error;
        target.isMock = source.isMock;
    }
}

public sealed class PatrolInspectionService
{
    private readonly bool useMock;
    private readonly MockDoublePreventionStore mock;

    public PatrolInspectionService(bool useMock, MockDoublePreventionStore mock = null)
    {
        this.useMock = useMock;
        this.mock = mock ?? MockDoublePreventionStore.CreateDefault();
    }

    public IEnumerator LoadPage(string companyName, ServiceResult<PatrolPage> outcome)
    {
        if (useMock)
        {
            List<PatrolRecord> rows = mock.GetPatrols(companyName);
            outcome.value = new PatrolPage { rows = rows, total = rows.Count };
            outcome.isMock = true;
            yield break;
        }
        WWWForm form = new WWWForm();
        form.AddField("corpName", companyName ?? string.Empty);
        form.AddField("page", "1");
        yield return PortalApiClient.PostForm(DoublePreventionApi.PatrolFindByPage, form, outcome);
    }

    public IEnumerator LoadDetail(string sid, ServiceResult<PatrolRecord> outcome)
    {
        if (useMock)
        {
            outcome.value = mock.GetPatrol(sid);
            outcome.isMock = true;
            if (outcome.value == null) outcome.error = "巡查记录不存在";
            yield break;
        }
        WWWForm form = new WWWForm();
        form.AddField("sid", sid ?? string.Empty);
        yield return PortalApiClient.PostForm(DoublePreventionApi.PatrolFindBySid, form, outcome);
    }
}

public sealed class InspectionAttachmentService
{
    private readonly bool useMock;
    private readonly MockDoublePreventionStore mock;
    private readonly HashSet<string> cachedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private int generation;

    public InspectionAttachmentService(bool useMock, MockDoublePreventionStore mock = null)
    {
        this.useMock = useMock;
        this.mock = mock ?? MockDoublePreventionStore.CreateDefault();
    }

    public IEnumerator Load(string patrolRecordId, ServiceResult<List<InspectionAttachment>> outcome)
    {
        if (useMock)
        {
            outcome.value = mock.GetAttachments(patrolRecordId);
            outcome.isMock = true;
            yield break;
        }
        WWWForm form = new WWWForm();
        form.AddField("linkId", patrolRecordId ?? string.Empty);
        var raw = new ServiceResult<AttachmentPage>();
        yield return PortalApiClient.PostForm(DoublePreventionApi.AttachmentFind, form, raw);
        outcome.error = raw.error;
        outcome.isMock = raw.isMock;
        if (raw.ok) outcome.value = raw.value?.rows ?? new List<InspectionAttachment>();
    }

    public IEnumerator Download(
        InspectionAttachment attachment,
        Action<float> progress,
        ServiceResult<string> outcome)
    {
        if (attachment == null || string.IsNullOrWhiteSpace(attachment.sid))
        {
            outcome.error = "附件标识为空";
            yield break;
        }
        if (useMock)
        {
            outcome.isMock = true;
            outcome.error = "演示附件仅验证格式，接入后端文件后可播放";
            yield break;
        }
        int requestGeneration = generation;
        string extension = Path.GetExtension(attachment.attachName ?? string.Empty);
        if (extension.Length > 10) extension = string.Empty;
        string path = Path.Combine(Application.temporaryCachePath,
            "inspection-" + SafeName(attachment.sid) + extension);
        yield return PortalApiClient.Download(
            DoublePreventionApi.AttachmentDownload(attachment.sid), path,
            () => requestGeneration != generation, progress, outcome);
        if (outcome.ok) cachedPaths.Add(path);
    }

    public void CancelAndCleanup()
    {
        generation++;
        foreach (string path in cachedPaths.ToList())
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception exception) { Debug.LogWarning("[附件缓存] 清理失败：" + exception.Message); }
        }
        cachedPaths.Clear();
    }

    private static string SafeName(string value)
    {
        return new string((value ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
    }
}

public sealed class InspectionAnnotationService
{
    private const string LocalFileName = "inspection_annotations.json";
    private readonly InspectionAnnotationStore store;

    public InspectionAnnotationService(InspectionAnnotationStore store = null)
    {
        this.store = store ?? new InspectionAnnotationStore(
            Path.Combine(Application.persistentDataPath, LocalFileName),
            "mr-local-user", "本机用户");
    }

    public IEnumerator Load(string patrolRecordId, ServiceResult<List<InspectionAnnotation>> outcome)
    {
        try
        {
            outcome.value = store.Load(patrolRecordId);
            outcome.isLocal = true;
        }
        catch (Exception exception) { outcome.error = exception.Message; }
        yield break;
    }

    public IEnumerator Add(
        string patrolRecordId, string content,
        ServiceResult<InspectionAnnotation> outcome)
    {
        try
        {
            outcome.value = store.Add(patrolRecordId, content);
            outcome.isLocal = true;
        }
        catch (Exception exception) { outcome.error = exception.Message; }
        yield break;
    }

    public IEnumerator Update(
        string sid, int version, string content,
        ServiceResult<InspectionAnnotation> outcome)
    {
        try
        {
            outcome.value = store.Update(sid, version, content);
            outcome.isLocal = true;
        }
        catch (InvalidOperationException exception)
        {
            outcome.error = exception.Message;
            outcome.conflict = true;
        }
        catch (Exception exception) { outcome.error = exception.Message; }
        yield break;
    }

    public IEnumerator Delete(string sid, int version, ServiceResult<bool> outcome)
    {
        try
        {
            store.Delete(sid, version);
            outcome.value = true;
            outcome.isLocal = true;
        }
        catch (InvalidOperationException exception)
        {
            outcome.error = exception.Message;
            outcome.conflict = true;
        }
        catch (Exception exception) { outcome.error = exception.Message; }
        yield break;
    }
}
