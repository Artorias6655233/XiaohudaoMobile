using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

/// <summary>
/// 出入园列表的射线悬停 + 手柄快捷键。
///
/// 解决的问题：行最右侧那两个按钮只有 280px 宽（<see cref="EntryExitActionCell.ColumnWidth"/>），
/// 戴着眼镜用射线去点两米外的一个小方块很费劲。所以改成【射线指着哪一行，X 键就作用于哪一行】，
/// 只要射线落在那一行的任意位置（序号、车牌、电话……）都算数。
///
/// 键位：
///   X —— 有悬停行时：对那一行打开审核/备注卡片；
///        无悬停行时：卡片开着则切换当前焦点短语的选中状态，没有焦点短语就关掉卡片；
///   Y —— 卡片开着时把焦点移到下一条短语。
/// 避开了 A / B：这两个键是全局占用的（B 是沙盘菜单瞬移，A 是锚点定位），抢了会伤到别的模块。
///
/// 命中检测【不能】用 BNG 的 VRUISystem：本工程里它根本没在跑（EventSystem 每帧只 Process
/// 第一个 ShouldActivateModule 的模块，场景预置的是 InputSystemUIInputModule，VRUISystem 排它
/// 后面）。所以这里和 <see cref="AimPointerBeam"/> 一样，直接向真正在跑的那个模块要命中结果。
///
/// 由 <see cref="ClosedPark_EntryExitList"/> 在 Start 里 AddComponent 挂上，不进场景
/// ——SandTable.unity 不能重新保存，保存一次全场景 fileID 都会重编号。
/// </summary>
public class EntryExitHoverShortcut : MonoBehaviour
{
    /// <summary>打开卡片。参数是原始记录和「是否高亮备注区」，由宿主面板塞进来。</summary>
    [NonSerialized] public Action<object, bool> onOpenCard;

    /// <summary>取当前卡片实例。卡片是懒实例化的，可能还没建出来，所以取的是委托不是引用。</summary>
    [NonSerialized] public Func<EntryExitReviewCard> cardProvider;

    private InputSystemUIInputModule module;
    private InputDevice device;

    // 上一帧指着的那条记录。留着是为了不每帧重算 key——KeyFor 在记录缺 ID 时会打 Warning，
    // 每帧算一次就是每秒几十条重复日志。
    private object lastPayload;
    private string lastKey;

    private void OnDisable()
    {
        // 离开这个 tab（或面板被关掉）时必须清掉悬停态，否则行高亮会一直挂在最后指过的那行上
        lastPayload = null;
        lastKey = null;
        EntryExitReviewStore.Hover(null);
    }

    private void Update()
    {
        // 门禁顺序照 Double_CheckList：先排空引用，再看当前子会话是不是自己这一页。
        // 用 Session 的全局状态而不是 gameObject.activeSelf —— 面板对象在别的 tab 下也可能是开着的。
        if (Session.Instance == null || BNG.InputBridge.Instance == null ||
            Session.Instance.subSessionType != SubSessionType.ClosedPark_EntryExitList)
        {
            return;
        }

        object payload = HitRecord();

        if (!ReferenceEquals(payload, lastPayload))
        {
            lastPayload = payload;
            lastKey = KeyOf(payload);
        }

        // Hover 内部自己比对，只有真换行时才广播，所以每帧调是安全的
        EntryExitReviewStore.Hover(lastKey);

        if (BNG.InputBridge.Instance.XButtonDown) OnX(payload);
        if (BNG.InputBridge.Instance.YButtonDown) OnY();
    }

    private void OnX(object payload)
    {
        if (payload != null)
        {
            // 卡片开着时也照做：射线换一行再按 X 就直接看下一条，不用先关卡片
            if (onOpenCard != null) onOpenCard(payload, true);
            return;
        }

        EntryExitReviewCard card = cardProvider != null ? cardProvider() : null;
        if (card == null || !card.IsOpen) return;

        // 射线不在列表上，说明人正在看卡片：X 先当"选中焦点短语"用，
        // 没有焦点短语（还没按过 Y）时才退化成关闭卡片。
        if (!card.ToggleFocusedChip()) card.Hide();
    }

    private void OnY()
    {
        EntryExitReviewCard card = cardProvider != null ? cardProvider() : null;
        if (card == null || !card.IsOpen) return;

        card.FocusNextChip();
    }

    // ------------------------------------------------------------------ 命中检测

    /// <summary>射线当前指着的那一行的原始记录，没指着行就返回 null。</summary>
    private object HitRecord()
    {
        if (module == null)
        {
            module = FindObjectOfType<InputSystemUIInputModule>();
            if (module == null) return null;
        }

        // 手柄可能中途掉线重连，deviceId 会变，所以每次都校验 added
        if (device == null || !device.added)
        {
            device = XrAimPose.FindRightHandDevice();

            // 编辑器里没手柄，退到鼠标上，这样悬停和快捷键在 Play 模式下也能验
            if (device == null && Application.isEditor) device = Mouse.current;
            if (device == null) return null;
        }

        RaycastResult hit = module.GetLastRaycastResult(device.deviceId);
        if (!hit.isValid || hit.gameObject == null) return null;

        // 命中的通常是行里的某个 TMP 子物体（它们的 raycastTarget 都是 1），
        // 所以要一路往上找到行脚本，而不是指望正好打在行根上。
        CarAccessRecordObject car = hit.gameObject.GetComponentInParent<CarAccessRecordObject>();
        if (car != null) return car.recordDetails;

        PersonAccessRecordObject person = hit.gameObject.GetComponentInParent<PersonAccessRecordObject>();
        if (person != null) return person.recordDetails;

        return null;
    }

    private static string KeyOf(object payload)
    {
        CarAccessRecord car = payload as CarAccessRecord;
        if (car != null) return EntryExitReviewStore.KeyFor(car);

        PersonAccessRecord person = payload as PersonAccessRecord;
        if (person != null) return EntryExitReviewStore.KeyFor(person);

        return null;
    }
}
