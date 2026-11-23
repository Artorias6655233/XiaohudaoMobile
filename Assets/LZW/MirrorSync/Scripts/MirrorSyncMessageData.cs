using Mirror;

public struct MirrorSyncMessageData : NetworkMessage
{
    public uint netId;

    public string data;

    public bool forwardToClient;

    public MirrorSyncMessageData(string data, bool forwardToClient = true)
    {
        this.netId = 0;
        if (NetworkClient.active && NetworkClient.isConnected)
            this.netId = NetworkClient.connection.identity.netId;
        this.data = data;
        this.forwardToClient = forwardToClient;
    }

    public static MirrorSyncMessageData BuildData(string data, bool forwardToClient = true)
    {
        return new MirrorSyncMessageData(data, forwardToClient);
    }
}
