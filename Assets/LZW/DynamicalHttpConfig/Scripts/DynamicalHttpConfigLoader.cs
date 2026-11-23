using System;
using UnityEngine;

public class DynamicalHttpConfigLoader : MonoBehaviour
{
    private const string httpConfigUrl = "https://changjia.deepmirror.com.cn/files/ChangJiaConfig.json";

    public HttpConfig httpConfig;

    public MirrorConfig mirrorConfig;

    public static DynamicalHttpConfigLoader Ins;

    protected virtual void Awake()
    {
        Ins = this;
    }

    // Start is called before the first frame update
    protected virtual void Start()
    {
        LoopLoadConfig();
    }

    protected virtual void LoopLoadConfig()
    {

    }

    protected void LoadConfig(ChangJiaConfig config)
    {
        httpConfig.listURL = config.httpList;
        httpConfig.downloadURL = config.httpDownload;
        mirrorConfig.networkAddress = config.mirrorServerAddress;
        httpConfig.loginURL = config.httpLogin;
        httpConfig.pdfListURL = config.pdfList;
        httpConfig.pdfDownloadURL = config.pdfDownload;
    }

    [Serializable]
    protected class ChangJiaConfig
    {
        public string mirrorServerAddress;
        public string httpList;
        public string httpDownload;
        public string httpLogin;
        public string pdfList;
        public string pdfDownload;
    }
}
