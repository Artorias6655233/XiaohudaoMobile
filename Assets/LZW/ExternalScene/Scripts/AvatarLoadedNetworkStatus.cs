using Mirror;
using TMPro;
using UnityEngine;

public class AvatarLoadedNetworkStatus : MonoBehaviour
{
    [SerializeField]
    private TMP_Text statsText;

    // Update is called once per frame
    void Update()
    {
        DetectStatsText();
    }

    private void DetectStatsText()
    {
        string content = NetworkClient.active == false
                ? GetStatusContent()
                : GetStatusContent(NetworkClient.active, NetworkClient.isConnected,
                NetworkClient.ready.ToString(), $"{Mathf.Round((float)NetworkTime.rtt * 1000)}ms");

        statsText.text = content;
    }

    private string GetStatusContent(bool active = false, bool isConnected = false, string ready = "Unknown", string rtt = "Unknown")
    {
        string content = $"Client status >>> " +
                $"Active: {active}\n" +
                $"Connected: {isConnected}\n" +
                $"Ready: {ready}\n" +
                $"RTT(Lantency): {rtt}";

        return content;
    }
}
