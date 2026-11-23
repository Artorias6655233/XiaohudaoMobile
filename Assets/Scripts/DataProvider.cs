using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 数据提供者 - 管理 API 请求缓存
/// 用于缓存已请求过的 API 结果，避免重复请求
/// </summary>
public class DataProvider : MonoBehaviour
{
    private static DataProvider _instance;
    public static DataProvider Instance
    {
        get
        {
            if (_instance == null)
            {
                GameObject go = new GameObject("DataProvider");
                _instance = go.AddComponent<DataProvider>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    /// <summary>缓存存储结构：key 为 API URL，value 为解析后的数据</summary>
    private Dictionary<string, object> _cache = new Dictionary<string, object>();
    
    /// <summary>最后的请求结果存储（key为apiUrl，value为RequestResult对象）</summary>
    private Dictionary<string, object> _lastResults = new Dictionary<string, object>();

    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 结果包装器 - 用于从协程中返回数据
    /// </summary>
    public class RequestResult<T>
    {
        public bool success = false;
        public string error = "";
        public object data = null;
    }

    /// <summary>
    /// 获取最后的请求结果
    /// </summary>
    public RequestResult<T> GetLastResult<T>(string apiUrl)
    {
        string key = $"{apiUrl}_{typeof(T).Name}";
        if (_lastResults.TryGetValue(key, out var result))
        {
            return result as RequestResult<T>;
        }
        return null;
    }

    /// <summary>
    /// 通用 GET 请求方法（支持 List 和单个对象）
    /// </summary>
    /// <typeparam name="T">返回的数据类型</typeparam>
    /// <param name="apiUrl">API 地址</param>
    /// <param name="identifier">可选标识</param>
    /// <param name="forceRefresh">是否强制刷新</param>
    public IEnumerator Get<T>(
        string apiUrl,
        string identifier = null,
        bool forceRefresh = false
    )
    {
        string resultKey = $"{apiUrl}_{typeof(T).Name}";
        string cacheKey = string.IsNullOrEmpty(identifier) ? apiUrl : $"{apiUrl}#{identifier}";

        if (forceRefresh)
        {
            _cache.Remove(cacheKey);
        }

        // 检查缓存
        if (_cache.TryGetValue(cacheKey, out var cachedData))
        {
            var result = new RequestResult<T> { success = true, data = cachedData };
            _lastResults[resultKey] = result;
            yield return null;
            yield break;
        }

        var requestResult = new RequestResult<T>();

        // 网络不可达时回退到随包的离线镜像，见 OfflineMock
        var fetch = new OfflineMock.Result();
        yield return OfflineMock.Fetch(apiUrl, fetch);

        if (fetch.json == null)
        {
            requestResult.success = false;
            requestResult.error = fetch.error;
        }
        else if (!string.IsNullOrEmpty(identifier))
        {
            // 指定 identifier 时按单个对象解析并校验。这类响应都很小，留在主线程。
            ParseSingle(fetch.json, identifier, cacheKey, requestResult);
        }
        else
        {
            // 只打长度不打内容：company/list 这类响应接近 200KB，
            // 整段写进 logcat 会造成可感知的卡顿。
            bool offload = BackgroundJson.ShouldOffload(fetch.json);
            Debug.Log($"[DataProvider] {typeof(T).Name} 列表 {fetch.json.Length} 字节，" +
                $"来源：{fetch.source}，解析：{(offload ? "后台线程" : "主线程")}");

            // 3.3MB 的作业票在主线程解析要 1.4 秒，点 tab 会整个卡住
            var parsed = new BackgroundJson.Result();
            yield return BackgroundJson.Parse(typeof(ResponseList<T>), fetch.json, parsed);

            if (parsed.error != null)
            {
                Debug.LogError($"[DataProvider] 解析失败: {parsed.error}");
                requestResult.success = false;
                requestResult.error = parsed.error;
            }
            else
            {
                _cache[cacheKey] = parsed.value;
                requestResult.success = true;
                requestResult.data = parsed.value;
            }
        }

        // 存储结果
        _lastResults[resultKey] = requestResult;
    }

    /// <summary>
    /// 单对象解析。单独拆一个方法是因为 C# 迭代器不允许在带 catch 的 try 里 yield，
    /// 而列表分支现在要 yield 等后台线程。
    /// </summary>
    private void ParseSingle<T>(string json, string identifier, string cacheKey, RequestResult<T> requestResult)
    {
        try
        {
            var singleResult = JsonUtility.FromJson<Response<T>>(json);

            // 校验返回数据的 id/sid
            if (!ValidateIdentifier(singleResult.result, identifier))
            {
                requestResult.success = false;
                requestResult.error = $"返回数据的标识与请求不匹配 (期望: {identifier})";
                return;
            }

            _cache[cacheKey] = singleResult;
            requestResult.success = true;
            requestResult.data = singleResult;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DataProvider] 解析失败: {ex.Message}");
            requestResult.success = false;
            requestResult.error = ex.Message;
        }
    }

    /// <summary>
    /// 通用 GET 请求方法（针对 External API，total 为 string）
    /// </summary>
    public IEnumerator GetExternal<T>(
        string apiUrl,
        bool forceRefresh = false
    )
    {
        string resultKey = $"{apiUrl}_{typeof(T).Name}";
        string cacheKey = apiUrl;

        if (forceRefresh)
        {
            _cache.Remove(cacheKey);
        }

        // 检查缓存
        if (_cache.TryGetValue(cacheKey, out var cachedData))
        {
            var result = new RequestResult<T> { success = true, data = cachedData };
            _lastResults[resultKey] = result;
            yield return null;
            yield break;
        }

        var requestResult = new RequestResult<T>();

        // 网络不可达时回退到随包的离线镜像，见 OfflineMock
        var externalFetch = new OfflineMock.Result();
        yield return OfflineMock.Fetch(apiUrl, externalFetch);

        if (externalFetch.json == null)
        {
            requestResult.success = false;
            requestResult.error = externalFetch.error;
            Debug.LogError($"[DataProvider] 取数失败: {externalFetch.error} URL: {apiUrl}");
        }
        else
        {
            string json = externalFetch.json;

            // Special handling for DoubleDanger to use concrete class
            Type responseType;
            if (typeof(T) == typeof(DoubleDanger))
            {
                responseType = typeof(DoubleDangerExternalResponse);
            }
            else if (typeof(T) == typeof(ReportPerson))
            {
                responseType = typeof(ReportPersonResponse);
            }
            else
            {
                responseType = typeof(ExternalResponseList<T>);
            }

            // 隐患报告那份 api_external_hiddenDangerReportTaskReview_find.json 有 1.1MB，
            // 主线程解析同样会卡住点 tab 的那一帧
            var parsed = new BackgroundJson.Result();
            yield return BackgroundJson.Parse(responseType, json, parsed);

            object parsedData = parsed.value;

            // Check if parsing succeeded (using reflection or casting)
            bool success = false;
            if (parsedData != null)
            {
                if (typeof(T) == typeof(ReportPerson))
                {
                    var resultField = parsedData.GetType().GetField("result");
                    if (resultField != null)
                    {
                        var resultValue = resultField.GetValue(parsedData);
                        if (resultValue != null) success = true;
                    }
                }
                else
                {
                    var bodyField = parsedData.GetType().GetField("body");
                    if (bodyField != null)
                    {
                        var bodyValue = bodyField.GetValue(parsedData);
                        if (bodyValue != null) success = true;
                    }
                }
            }

            if (success)
            {
                _cache[cacheKey] = parsedData;
                requestResult.success = true;
                requestResult.data = parsedData;
            }
            else
            {
                requestResult.success = false;
                requestResult.error = parsed.error ?? "Failed to parse external response (body is null)";
                // 只打前 300 字符：整段响应可达数百 KB，全写进 logcat 会拖慢帧率
                Debug.LogError($"[DataProvider] 外部响应解析失败（{parsed.error ?? "body 为空"}），" +
                    $"来源：{externalFetch.source}，共 {json.Length} 字节，" +
                    $"前 300 字符：{json.Substring(0, Mathf.Min(300, json.Length))}");
            }
        }

        // 存储结果
        _lastResults[resultKey] = requestResult;
    }

    /// <summary>
    /// 校验返回的对象是否包含指定的 identifier
    /// 支持 id 和 sid 两种标识
    /// </summary>
    private bool ValidateIdentifier(object data, string identifier)
    {
        if (data == null || string.IsNullOrEmpty(identifier))
            return true;

        // 使用反射尝试获取 id 或 sid 字段
        var type = data.GetType();
        var idField = type.GetField("id");
        var sidField = type.GetField("sid");

        if (idField != null)
        {
            var value = idField.GetValue(data);
            if (value != null && value.ToString() == identifier)
                return true;
        }

        if (sidField != null)
        {
            var value = sidField.GetValue(data);
            if (value != null && value.ToString() == identifier)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 清空所有缓存
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
        Debug.Log("DataProvider 缓存已清空");
    }

    /// <summary>
    /// 清空指定 URL 的缓存
    /// </summary>
    public void ClearCache(string apiUrl)
    {
        _cache.Remove(apiUrl);
        Debug.Log($"已清空缓存: {apiUrl}");
    }

    /// <summary>
    /// 从缓存列表中移除满足条件的项
    /// </summary>
    public void RemoveItemFromCache<T>(string apiUrl, Predicate<T> match)
    {
        string cacheKey = apiUrl;
        
        if (_cache.TryGetValue(cacheKey, out var cachedData))
        {
             if (cachedData is ResponseList<T> responseList)
             {
                 if (responseList.result != null)
                 {
                     if (responseList.result.data != null)
                        responseList.result.data.RemoveAll(match);
                     if (responseList.result.records != null)
                        responseList.result.records.RemoveAll(match);
                 }
             }
             else if (cachedData is ExternalResponseList<T> externalList)
             {
                 if (externalList.body != null && externalList.body.data != null)
                 {
                     externalList.body.data.RemoveAll(match);
                 }
             }
        }
    }

    /// <summary>
    /// 获取缓存统计信息
    /// </summary>
    public int GetCacheCount()
    {
        return _cache.Count;
    }
}

