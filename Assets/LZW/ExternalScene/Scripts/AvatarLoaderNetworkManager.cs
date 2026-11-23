using Mirror;

public class AvatarLoaderNetworkManager : MirrorSyncManager
{
    // Start is called before the first frame update
    public sealed override void Start()
    {
        base.Start();
    }

    // Update is called once per frame
    public sealed override void Update()
    {
        base.Update();
    }

    protected sealed override void OnServerMessage(NetworkConnectionToClient networkConnectionToClient, MirrorSyncMessageData messageData)
    {
        base.OnServerMessage(networkConnectionToClient, messageData);
    }

    protected sealed override void OnClientMessage(MirrorSyncMessageData messageData)
    {
        base.OnClientMessage(messageData);
    }

    public sealed override void OnServerConnect(NetworkConnectionToClient conn)
    {
        base.OnServerConnect(conn);
        NetworkServer.Spawn(loadedModelContainer.gameObject);
    }
}
