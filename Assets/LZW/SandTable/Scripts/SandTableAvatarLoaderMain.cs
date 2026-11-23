using Mirror;
using System.Collections;
using UnityEngine;

public class SandTableAvatarLoaderMain : AvatarLoaderMain
{
    public sealed override IEnumerator ConnectServerLoop(float interval = 1)
    {
        // TODO: It is recommended to set it up through the remote server API.
        NetworkManager.singleton.networkAddress = mirrorConfig.networkAddress;
        while (true)
        {
            yield return new WaitForEndOfFrame();
            if (NetworkClient.active == false)
            {
                NetworkManager.singleton.networkAddress = mirrorConfig.networkAddress;
                ConnectServer();
                yield return new WaitForSeconds(interval);
            }
        }
    }
}
