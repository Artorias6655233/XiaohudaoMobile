using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 储罐列表里的一行。同企业的罐坐标全一样、图标叠在一起选不动，只能在卡片上列出来选，
/// 见 <see cref="TankGrouping"/>。
///
/// 照仓库里行组件的一贯写法：public 字段拖引用 + 一个 Bind，字段全部允许为空。
/// </summary>
public class TankListRow : MonoBehaviour
{
    public Button button;
    public Image background;
    public TextMeshProUGUI nameText;   // 甲醛7＃储罐甲醛储罐V-107
    public TextMeshProUGUI codeText;   // 440116240401G0007

    /// <summary>
    /// 「批注」列。宿主不传 onAnnotate 时整列隐藏——罐体列表被两个页面共用，
    /// 只有「重大风险管控」要批注，「监测预警」是纯查看。
    /// </summary>
    public Button annotateButton;

    private static readonly Color SelectedColor = new Color(0f, 0.65f, 0.85f, 0.55f);
    private static readonly Color IdleColor = new Color(1f, 1f, 1f, 0.07f);

    /// <summary>当前挂的回调，Bind 时先摘干净，避免复用行时叠加监听。</summary>
    private Action<Tank> pick;
    private Action<Tank> annotate;
    private Tank bound;

    private void Awake()
    {
        if (button != null) button.onClick.AddListener(OnClick);
        if (annotateButton != null) annotateButton.onClick.AddListener(OnAnnotateClick);
    }

    private void OnClick()
    {
        if (bound != null && pick != null) pick(bound);
    }

    private void OnAnnotateClick()
    {
        if (bound != null && annotate != null) annotate(bound);
    }

    /// <param name="onAnnotate">传 null 表示本页不提供批注，整列隐藏。</param>
    public void Bind(Tank tank, bool selected, Action<Tank> onPick, Action<Tank> onAnnotate = null)
    {
        bound = tank;
        pick = onPick;
        annotate = onAnnotate;

        gameObject.SetActive(tank != null);
        if (annotateButton != null) annotateButton.gameObject.SetActive(tank != null && onAnnotate != null);
        if (tank == null) return;

        SetText(nameText, string.IsNullOrWhiteSpace(tank.tankName) ? "(未命名储罐)" : tank.tankName);
        SetText(codeText, tank.equipCode);
        if (background != null) background.color = selected ? SelectedColor : IdleColor;
    }

    public void Clear()
    {
        bound = null;
        pick = null;
        annotate = null;
        gameObject.SetActive(false);
    }

    private static void SetText(TextMeshProUGUI textComp, string value)
    {
        if (textComp != null) textComp.text = value ?? string.Empty;
    }
}
