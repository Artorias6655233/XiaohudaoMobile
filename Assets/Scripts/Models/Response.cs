
using System.Collections.Generic;

[System.Serializable]
public class Response<T>
{
    public bool success;
    public int code;
    public string message;
    public T result;
    
    // For new API format
    public string status;
    public T body;
}

[System.Serializable]
public class ResponseList<T> : Response<ListResult<T>> { }

[System.Serializable]
public class ListResult<T>
{
    [UnityEngine.SerializeField]
    public List<T> data;
    [UnityEngine.SerializeField]
    public List<T> records;
    public int total;
}

[System.Serializable]
public class ExternalResponseList<T>
{
    public string message;
    public string status;
    public ExternalBody<T> body;
}

[System.Serializable]
public class ExternalBody<T>
{
    public string total;
    public List<T> data;
}

[System.Serializable]
public class ClosedParkResponse<T>
{
    public int total;
    public int code;
    public List<T> records;
    public bool success;
    public int pageNo;
    public int pageSize;
    public string message;
}

// Concrete classes to help JsonUtility
[System.Serializable]
public class DoubleDangerExternalResponse : ExternalResponseList<DoubleDanger> {}

[System.Serializable]
public class CarAccessRecordResponse : ClosedParkResponse<CarAccessRecord> {}

[System.Serializable]
public class PersonAccessRecordResponse : ClosedParkResponse<PersonAccessRecord> {}