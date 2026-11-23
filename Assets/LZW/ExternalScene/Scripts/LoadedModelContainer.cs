using Mirror;
using UnityEngine;

[RequireComponent(typeof(NetworkIdentity)), DisallowMultipleComponent]
public class LoadedModelContainer : MonoBehaviour
{
    public NetworkIdentity LocalNetworkIdentity => GetComponent<NetworkIdentity>();

    [SerializeField]
    protected GameObject loadedModel;

    public virtual void LoadModel(GameObject @object)
    {
        loadedModel = @object;
        loadedModel.transform.SetParent(transform);
        loadedModel.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
    }

    protected virtual void Update()
    {
        if (loadedModel == null)
            return;
        loadedModel.transform.SetPositionAndRotation(transform.position, transform.rotation);
        loadedModel.transform.localScale = transform.localScale;
    }

    public GameObject GetLoadedModel()
    {
        return loadedModel;
    }
}
