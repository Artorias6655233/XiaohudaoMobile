using System;
using UnityEngine;
using UnityEngine.Events;

public class SandTableSceneManager : MonoBehaviour
{
    private MirrorSyncManager mirrorSyncManager;

    public UnityAction<int, int, bool, bool> onChangeSceneStateLocal;

    // Start is called before the first frame update
    void Start()
    {
        mirrorSyncManager = AvatarLoaderNetworkManager.Ins;
        mirrorSyncManager.onClientMessageReceived += OnClientMessageReceived;
    }

    public void ChangeSceneStateNet(int index, int index2, bool visible, bool visible2)
    {
        var data = new SandTableSceneData
        {
            actionName = "ChangeSceneState",
            index = index,
            index2 = index2,
            visible = visible,
            visible2 = visible2
        };
        var json = JsonUtility.ToJson(data);
        mirrorSyncManager.ClientSendToServer<MirrorSyncMessageData>(MirrorSyncMessageData.BuildData(json));
    }

    private void ChangeSceneStateLocal(int index, int index2, bool visible, bool visible2)
    {
        onChangeSceneStateLocal?.Invoke(index, index2, visible, visible2);
    }

    public void OnClientMessageReceived(MirrorSyncMessageData mirrorSyncMessageData)
    {
        try
        {
            var data = JsonUtility.FromJson<SandTableSceneData>(mirrorSyncMessageData.data);
            if (data.actionName == "ChangeSceneState")
            {
                ChangeSceneStateLocal(data.index, data.index2, data.visible, data.visible2);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"ex: {ex.Message}");
        }
    }

    [Serializable]
    public class SandTableSceneData
    {
        public string actionName;
        public int index; // Current and feature
        public int index2; // Daytime and evening
        public bool visible; // Scene choice panel
        public bool visible2; // Instruction panel
    }
}
