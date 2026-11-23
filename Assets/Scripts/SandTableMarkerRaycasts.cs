using UnityEngine;

/// <summary>
/// 卡片打开期间，把沙盘标记物（企业 / 储罐图标）从手柄射线里摘出去。
///
/// 【为什么得手动摘，靠遮挡和层级都不行】手柄走的是 tracked 指针通路，命中判定在
/// InputSystemUIInputModule.PerformRaycast（com.unity.inputsystem@1.7.0 第 253-268 行）：
///
///     for (var i = 0; i &lt; TrackedDeviceRaycaster.s_Instances.length; ++i) {
///         trackedDeviceRaycaster.PerformRaycast(eventData, m_RaycastResultCache);
///         if (m_RaycastResultCache.Count &gt; 0) return m_RaycastResultCache[0];   // 就地返回
///     }
///
/// 它遍历全部 TrackedDeviceRaycaster、拿【第一个有命中的】就返回，跨 Canvas 之间
/// 既不比距离也不比 sortingOrder。沙盘上每个企业图标自己就是一个 Canvas
/// （Company.prefab 根节点上挂着 Canvas），各自一个 raycaster；卡片则共用宿主子面板
/// 那一个 Canvas。谁排在 s_Instances 前面，谁就赢——和"谁离眼睛近"完全无关。
///
/// 而次序是会变的：s_Instances 按 OnEnable 追加，OnDisable 用
/// RemoveAtByMovingTailWithCapacity（拿尾巴填坑）。子面板在场景启动时是
/// SetActive(false)，TrackedUiBootstrap.Rescan 用 FindObjectsOfType&lt;Canvas&gt;() 扫不到，
/// 它们的 raycaster 往往比早已建好的企业图标更晚注册，于是企业图标排在前面。
/// 表现就是：在批注面板上点输入框 / 按钮，射线穿过卡片先命中后面沙盘上的企业，
/// MajorHazard_Management.OnCompanyChanged 把批注面板连草稿一起关掉。
///
/// 唯一能可靠拦住的办法是让那些图标压根不产生命中。CanvasGroup.blocksRaycasts=false
/// 正好落在 Graphic.Raycast 的父链遍历里（com.unity.ugui@1.0.0 Graphic.cs:854 起，
/// 遍历【不在 Canvas 处停】，只在 overrideSorting 处停；企业图标和储罐图标的 Canvas
/// 都是 m_OverrideSorting=0，已核对 prefab 与场景），所以在标记物根节点上放一个
/// CanvasGroup 就能盖住它下面现在和以后实例化出来的所有图标，不用逐个遍历。
///
/// 只动 blocksRaycasts：alpha 保持 1、interactable 保持 true，图标照常显示，
/// 也不会把 Button 置灰。
/// </summary>
public static class SandTableMarkerRaycasts
{
    /// <summary>
    /// 当前要求屏蔽的卡片数。多张卡片各自 Suppress / Release，最后一张关掉才放开——
    /// 单个 bool 的话，两张卡片一先一后关闭时，先关的那张会提前把射线放回去。
    /// </summary>
    private static int suppressions;

    public static void Suppress()
    {
        suppressions++;
        Apply();
    }

    public static void Release()
    {
        // 场景切换时 OnDisable 的调用次数不一定和 OnEnable 对得上，钳到 0 免得计数变负数后
        // 再也放不开（表现为沙盘图标永久点不动）
        suppressions = Mathf.Max(0, suppressions - 1);
        Apply();
    }

    private static void Apply()
    {
        bool blocks = suppressions <= 0;
        Session session = Session.Instance;
        if (session == null) return;

        SetBlocks(RootOf(session.companyPrefab), blocks);
        SetBlocks(RootOf(session.tankPrefab), blocks);
    }

    /// <summary>
    /// Session.companyRoot / tankRoot 是 prefab.transform.parent，prefab 没接上时会 NRE。
    /// 这里自己取一遍并判空：射线屏蔽失败不该把整个批注流程带崩。
    /// </summary>
    private static GameObject RootOf(CesiumForUnity.CesiumGlobeAnchor prefab)
    {
        Transform parent = prefab != null ? prefab.transform.parent : null;
        return parent != null ? parent.gameObject : null;
    }

    private static void SetBlocks(GameObject root, bool blocks)
    {
        if (root == null) return;
        CanvasGroup group = root.GetComponent<CanvasGroup>();
        if (group == null)
        {
            // 放开就是默认值，没有组件时不必为了"放开"而新建一个
            if (blocks) return;
            group = root.AddComponent<CanvasGroup>();
        }
        group.blocksRaycasts = blocks;
    }
}
