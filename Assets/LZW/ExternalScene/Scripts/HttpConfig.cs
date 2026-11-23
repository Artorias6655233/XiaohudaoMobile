using UnityEngine;

[CreateAssetMenu(menuName = "Config/HttpConfig", fileName = "HttpConfig")]
public class HttpConfig : ScriptableObject
{
    public string listURL;

    public string downloadURL;

    public string loginURL;

    public string pdfListURL;

    public string pdfDownloadURL;
}
