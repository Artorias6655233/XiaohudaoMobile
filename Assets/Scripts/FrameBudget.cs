using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 分帧实例化节流器。
///
/// 各面板在切 tab 时会把整份列表一次性实例化成 GameObject——摄像头 499 个、
/// 巡检 347 个、罐区 264 个、作业票 244 个。裸 foreach 会把这些全挤进同一帧，
/// 而设备端一帧预算只有 ~14ms（72Hz）。几百次 Instantiate 再叠上
/// CesiumGlobeAnchor 的经纬度→ECEF 换算必然爆帧，表现就是点完 tab 卡一下。
///
/// 用法：把批量创建方法改成协程，交给 <see cref="Spread"/> 分摊。
///
///   private void CreateTankInstances(List&lt;Tank&gt; tanks)
///   {
///       foreach (var tank in tanks) CreateTankInstance(tank);
///   }
///   ↓
///   private IEnumerator CreateTankInstances(List&lt;Tank&gt; tanks)
///   {
///       yield return FrameBudget.Spread(tanks, CreateTankInstance);
///   }
///
/// 调用方本身在协程里时用 <c>yield return CreateTankInstances(...)</c>；
/// 不在协程里（例如直接从点击回调进来）则用 <c>StartCoroutine(...)</c>。
///
/// 注意：分帧意味着列表是"逐渐长出来"的，方法返回时才算建完。任何依赖
/// "建完之后再统一处理"的逻辑（如 SetAllTanksLabelState）必须放在
/// yield return 之后，不能放在 Spread 之前。
/// </summary>
public static class FrameBudget
{
    /// <summary>
    /// 每帧处理多少个。调小更平滑但总耗时更长；调大反之。
    /// 20 是按 CesiumGlobeAnchor 这类较重的 prefab 估的保守值，
    /// 纯 UI 行可以更高，但没必要为此拆成两个常量。
    /// </summary>
    public const int ItemsPerFrame = 20;

    /// <summary>
    /// 把 items 的创建分摊到多帧，每 <see cref="ItemsPerFrame"/> 个让出一帧。
    /// </summary>
    public static IEnumerator Spread<T>(IEnumerable<T> items, System.Action<T> create)
    {
        if (items == null)
        {
            yield break;
        }

        int done = 0;
        foreach (var item in items)
        {
            create(item);

            if (++done % ItemsPerFrame == 0)
            {
                yield return null;
            }
        }
    }

    /// <summary>
    /// 带序号的版本，给 <c>CreateInstance(item, index)</c> 这种签名用。
    /// </summary>
    public static IEnumerator Spread<T>(IList<T> items, System.Action<T, int> create)
    {
        if (items == null)
        {
            yield break;
        }

        for (int i = 0; i < items.Count; i++)
        {
            create(items[i], i);

            if ((i + 1) % ItemsPerFrame == 0)
            {
                yield return null;
            }
        }
    }
}
