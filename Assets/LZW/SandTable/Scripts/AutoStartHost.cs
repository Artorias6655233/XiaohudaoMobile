using System.Collections;
using Mirror;
using UnityEngine;

namespace LZW.SandTable.Scripts
{
    public class AutoStartHost : MonoBehaviour
    {
        // Start is called before the first frame update
        private IEnumerator Start()
        {
            yield return new WaitForEndOfFrame();
            // TODO: It is recommended to set it up through the remote server API.
            while (true)
            {
                yield return new WaitForEndOfFrame();
                if (NetworkClient.active) continue;
                NetworkManager.singleton.StartHost();
                yield return new WaitForSeconds(1);
            }
            // ReSharper disable once IteratorNeverReturns
        }
    }
}
