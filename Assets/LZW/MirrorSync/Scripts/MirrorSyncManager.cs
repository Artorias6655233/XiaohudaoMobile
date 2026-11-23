using UnityEngine;
using Mirror;
using UnityEngine.Serialization;
using System;
using Mirror.SimpleWeb;
using System.Collections.Generic;
using UnityEngine.Events;

public class MirrorSyncManager : NetworkManager
{
    public static MirrorSyncManager Ins;

    [SerializeField, Header("A pivot for loaded model."), FormerlySerializedAs("loadedModelContainer")]
    protected LoadedModelContainer loadedModelContainer;

    // Player manager in dedicated server.
    protected List<NetworkConnectionToClient> players = new List<NetworkConnectionToClient>();

    public UnityAction<MirrorSyncMessageData> onClientMessageReceived;

    public UnityAction<MirrorSyncMessageData> onServerMessageReceived;

    public override void Awake()
    {
        base.Awake();
        Ins = this;
        if (Application.platform != RuntimePlatform.WebGLPlayer || Application.isEditor)
        {
            SimpleWebTransport simpleWebTransport = GetComponent<SimpleWebTransport>();
            if (simpleWebTransport != null)
            {
                simpleWebTransport.clientUseWss = false;
                simpleWebTransport.port += 1;
            }
        }
    }

    // Start is called before the first frame update
    public override void Start()
    {
        base.Start();
    }

    // Update is called once per frame
    public override void Update()
    {
        base.Update();
        EditorAction();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        NetworkServer.RegisterHandler<MirrorSyncMessageData>(OnServerMessage);
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        NetworkServer.UnregisterHandler<MirrorSyncMessageData>();
    }

    protected virtual void OnServerMessage(NetworkConnectionToClient networkConnectionToClient, MirrorSyncMessageData messageData)
    {
        Debug.Log($"OnServerMessage >>> id: {networkConnectionToClient}, dt: {messageData.data}");

        onServerMessageReceived?.Invoke(messageData);

        NetworkIdentity lmcNI = loadedModelContainer.LocalNetworkIdentity;

        MirrorSyncMessageData data = MirrorSyncMessageData.BuildData("Authorized");

        if (messageData.data.Equals("ClientRQF"))
        {
            if (lmcNI.connectionToClient == null)
            {
                try
                {
                    lmcNI.AssignClientAuthority(networkConnectionToClient);
                    data.data = "Authorized";
                }
                catch (Exception e)
                {
                    Debug.Log($"Authorized: {e.Message}");
                }
            }
            else if (lmcNI.connectionToClient.connectionId.Equals(networkConnectionToClient.connectionId))
            {
                data.data = "You owned";
            }
            else
            {
                try
                {
                    lmcNI.RemoveClientAuthority();
                    lmcNI.AssignClientAuthority(networkConnectionToClient);
                }
                catch (Exception e)
                {
                    Debug.Log($"RemoveClientAuthority: {e.Message}");
                }
                data.data = "Force to authorized";
            }
        }
        else if (messageData.data.Equals("ClientRQ"))
        {
            if (lmcNI.connectionToClient == null)
            {
                try
                {
                    lmcNI.AssignClientAuthority(networkConnectionToClient);
                    data.data = "Authorized";
                }
                catch (Exception e)
                {
                    Debug.Log($"ClientRQ: {e.Message}");
                }
            }
            else if (lmcNI.connectionToClient.connectionId.Equals(networkConnectionToClient.connectionId))
            {
                data.data = "You owned";
            }
            else
            {
                data.data = "Busy authority";
            }
        }
        else if (messageData.data.Equals("ClientRM"))
        {
            if (lmcNI.connectionToClient == null)
            {
                data.data = "No authority to remove";
            }
            else
            {
                if (lmcNI.connectionToClient.connectionId.Equals(networkConnectionToClient.connectionId))
                {
                    try
                    {
                        lmcNI.RemoveClientAuthority();
                        data.data = "Removed authority";
                    }
                    catch (Exception e)
                    {
                        Debug.Log($"ClientRM: {e.Message}");
                    }
                }
                else
                {
                    data.data = "You have no authority to remove";
                }
            }
        }
        else
        {
            ServerSendToAll<MirrorSyncMessageData>(messageData);
            return;
        }
        data.data += $" [{networkConnectionToClient.connectionId}]";
        if (messageData.forwardToClient)
            ServerSendToAll<MirrorSyncMessageData>(messageData);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        NetworkClient.RegisterHandler<MirrorSyncMessageData>(OnClientMessage);
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        NetworkClient.UnregisterHandler<MirrorSyncMessageData>();
    }

    protected virtual void OnClientMessage(MirrorSyncMessageData messageData)
    {
        Debug.Log($"OnClientMessage >>> dt: {messageData.data}");
        onClientMessageReceived?.Invoke(messageData);
    }

    public void ServerSendToAll<T>(T data) where T : struct, NetworkMessage
    {
        if (!NetworkServer.active)
        {
            Debug.Log("Please start server first.");
            return;
        }
        NetworkServer.SendToAll<T>(data);
    }

    public void ClientSendToServer<T>(T data) where T : struct, NetworkMessage
    {
        if (!NetworkClient.active)
        {
            Debug.Log("Please connect to server first.");
            return;
        }
        NetworkClient.Send<T>(data);
    }

    private void EditorAction()
    {
        if (Application.isEditor == false)
            return;

        if (Input.GetKeyDown(KeyCode.S))
        {
            RequestAuthorityForce();
        }

        if (Input.GetKeyDown(KeyCode.D))
        {
            RequestAuthority();
        }

        if (Input.GetKeyDown(KeyCode.F))
        {
            RemoveAuthority();
        }
    }

    public void RequestAuthorityForce()
    {
        MirrorSyncMessageData data = MirrorSyncMessageData.BuildData("ClientRQF");
        ClientSendToServer<MirrorSyncMessageData>(data);
    }

    public void RequestAuthority()
    {
        MirrorSyncMessageData data = MirrorSyncMessageData.BuildData("ClientRQ");
        ClientSendToServer<MirrorSyncMessageData>(data);
    }

    public void RemoveAuthority()
    {
        MirrorSyncMessageData data = MirrorSyncMessageData.BuildData("ClientRM");
        ClientSendToServer<MirrorSyncMessageData>(data);
    }

    public virtual List<NetworkConnectionToClient> GetPlayers()
    {
        return players;
    }

    public override void OnServerConnect(NetworkConnectionToClient conn)
    {
        base.OnServerConnect(conn);
        players.Add(conn);
    }

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        base.OnServerDisconnect(conn);
        players.Remove(conn);
    }

    public virtual LoadedModelContainer GetLoadedModelContainer()
    {
        return loadedModelContainer;
    }
}
