using System.Collections;
using UnityEngine;
using Mirror;

public class AvatarLoaderMain : MonoBehaviour
{
    public MirrorConfig mirrorConfig => DynamicalHttpConfigLoader.Ins.mirrorConfig;

    // Start is called before the first frame update
    protected virtual void Start()
    {
#if !UNITY_STANDALONE_LINUX
        StartCoroutine(ConnectServerLoop());
#endif
    }

    public void StartHost()
    {
        NetworkManager.singleton.StartHost();
    }

    public virtual IEnumerator ConnectServerLoop(float interval = 1)
    {
        // TODO: It is recommended to set it up through the remote server API.
        NetworkManager.singleton.networkAddress = mirrorConfig.networkAddress;
        yield break;
    }

    [ContextMenu("ConnectServerApi")]
    public void ConnectServer()
    {
        NetworkManager.singleton.StartClient();
    }
}
