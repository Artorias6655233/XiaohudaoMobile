using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SandTableDynamicalHttpConfigLoader : DynamicalHttpConfigLoader
{
    [SerializeField]
    protected ChangJiaConfig changJiaConfig;

    // Start is called before the first frame update
    protected sealed override void Start()
    {
        base.Start();
    }

    protected sealed override void LoopLoadConfig()
    {
        base.LoadConfig(changJiaConfig);
    }
}
