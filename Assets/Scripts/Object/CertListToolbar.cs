using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 证件列表卡片上那一条工具栏：左边统计、右边翻页。
///
/// 【为什么在标题下面而不是卡片底部】卡片高 1277.5，滚动区下面只剩 70 像素，
/// 塞不下 96 高的按钮。标题（y=-58）和表头（y=-375）之间有 200 多像素空着，放这里。
///
/// 单独做成 prefab 由宿主运行时 Instantiate（照 <c>EntryExitActionCell</c> 的做法），
/// 而不是往 <c>SandTable.unity</c> 里手摆一堆子物体——场景存一次会重编号全部 fileID。
/// 由 <c>Assets/Editor/CertListToolbarBuilder.cs</c> 生成，样式从场景现有文本抄。
///
/// 所有字段允许为空：接线漏一个不该让整页炸掉。
/// </summary>
public class CertListToolbar : MonoBehaviour
{
    public TextMeshProUGUI summaryText;
    public TextMeshProUGUI pageText;
    public Button prevButton;
    public Button nextButton;

    private Action<int> turn;

    private void Awake()
    {
        if (prevButton != null) prevButton.onClick.AddListener(() => turn?.Invoke(-1));
        if (nextButton != null) nextButton.onClick.AddListener(() => turn?.Invoke(1));
    }

    /// <summary>宿主把翻页回调交进来。delta 为 -1 / +1。</summary>
    public void BindTurn(Action<int> onTurn)
    {
        turn = onTurn;
    }

    /// <param name="summary">左侧统计文案，宿主已经拼好。</param>
    /// <param name="page">当前页，从 0 开始。</param>
    /// <param name="pageCount">总页数。小于等于 1 时隐藏两个按钮。</param>
    public void Render(string summary, int page, int pageCount)
    {
        if (summaryText != null) summaryText.text = summary ?? string.Empty;

        bool pageable = pageCount > 1;
        if (pageText != null)
            pageText.text = pageable ? $"{page + 1} / {pageCount}" : string.Empty;

        if (prevButton != null) prevButton.gameObject.SetActive(pageable);
        if (nextButton != null) nextButton.gameObject.SetActive(pageable);
    }
}
