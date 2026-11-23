using UnityEngine;

public class SandTableLoadedModelContainer : LoadedModelContainer
{
    // Start is called before the first frame update
    private void Start()
    {
        LoadModel(loadedModel);
    }

    public GameObject GetLoadedModel()
    {
        return loadedModel;
    }
}
