using Newtonsoft.Json;
using UnityEngine;

public static class DoublePreventionMockLoader
{
    private const string ResourcePath = "DoublePreventionMock/runtime_contract";

    public static MockDoublePreventionStore Load()
    {
        TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
        if (asset == null)
        {
            Debug.LogWarning("[双重预防] 找不到演示契约，使用内置最小数据");
            return MockDoublePreventionStore.CreateDefault();
        }
        try
        {
            DoublePreventionMockDocument document =
                JsonConvert.DeserializeObject<DoublePreventionMockDocument>(asset.text);
            return MockDoublePreventionStore.Create(document);
        }
        catch (System.Exception exception)
        {
            Debug.LogError("[双重预防] 演示契约解析失败：" + exception.Message);
            return MockDoublePreventionStore.CreateDefault();
        }
    }
}
