using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 批注历史里的一条。
///
/// 【为什么要拆成行】原先整段历史是一个 1690×430 的 TextMeshProUGUI，overflowMode
/// 是 Ellipsis——第三条往后直接被省略号吞掉，用户看不到也不知道自己少看了什么。
///
/// 照仓库里行组件的一贯写法（见 <see cref="TankListRow"/>）：public 字段拖引用 +
/// 一个 Bind，字段全部允许为空，场景接线漏一个不该让整块面板炸掉。
/// </summary>
public class P0AnnotationRow : MonoBehaviour
{
    public Image background;
    public TextMeshProUGUI headerText;   // 历史巡查员 · 2026-09-20 14:32
    public TextMeshProUGUI bodyText;     // 已核对现场警示标识。

    /// <summary>本人写的那条描边高亮：只有它能点「编辑本人最近批注」，得让人一眼看出是哪条。</summary>
    private static readonly Color OwnColor = new Color(0f, 0.65f, 0.85f, 0.35f);
    private static readonly Color IdleColor = new Color(1f, 1f, 1f, 0.07f);

    public void Bind(P0Annotation item, bool isOwn)
    {
        gameObject.SetActive(item != null);
        if (item == null) return;

        SetText(headerText, $"{item.authorName} · {item.updatedAt.ToLocalTime():yyyy-MM-dd HH:mm}");
        SetText(bodyText, item.content);
        if (background != null) background.color = isOwn ? OwnColor : IdleColor;
    }

    public void Clear()
    {
        gameObject.SetActive(false);
    }

    private static void SetText(TextMeshProUGUI textComp, string value)
    {
        if (textComp != null) textComp.text = value ?? string.Empty;
    }
}
