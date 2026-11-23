using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System;

public class JobTicketObject : MonoBehaviour
{
    private JobTicket _data;
    public Action<JobTicket> OnDetailClick;
    public Button detailButton;

    private void Start()
    {
        if (detailButton == null) detailButton = GetComponent<Button>();
        if (detailButton != null)
        {
            detailButton.onClick.AddListener(OnButtonClick);
        }
        else
        {
            Debug.LogWarning($"JobTicketObject: No Button component found on {gameObject.name}");
        }
    }

    private void OnButtonClick()
    {
        Debug.Log($"JobTicketObject clicked: {_data?.jobTicket}");
        OnDetailClick?.Invoke(_data);
    }

    /// <summary>
    /// 绑定一行。
    ///
    /// 【为什么合成一个方法而不是三个 setter】<paramref name="actionable"/> 要参与
    /// 状态列的文案，分开设就依赖赋值顺序 —— 虚拟滚动复用行实例，顺序一旦写反，
    /// 第 3 行会带着第 40 行的标记，而且不报错。一次绑完就没有这个问题。
    /// </summary>
    /// <param name="index">数据下标（从 0 起），显示时 +1。</param>
    /// <param name="actionable">这张票是否在当前账号的待审批队列里。</param>
    public void Bind(JobTicket ticket, int index, bool actionable)
    {
        _data = ticket;
        indexText.text = (index + 1).ToString();

        if (ticket == null)
        {
            enterpriseNameText.text = jobTypeText.text = applyTimeText.text = "";
            applyCodeText.text = responsibleText.text = locationText.text = statusText.text = "";
            LayoutRebuilder.ForceRebuildLayoutImmediate(GetComponent<RectTransform>());
            return;
        }

        // 序号, 企业名称, 作业类型, 申请时间, 申请编码, 作业负责人, 作业地点, 作业状态
        enterpriseNameText.text = ticket.applyingUnit ?? "";
        jobTypeText.text = ticket.jobTicketType ?? "";
        applyTimeText.text = !string.IsNullOrEmpty(ticket.applyTime) ? ticket.applyTime.Split(' ')[0] : "";
        applyCodeText.text = ticket.jobTicketCode ?? "";
        responsibleText.text = ticket.jobTicketResponsible ?? "";
        locationText.text = ticket.jobTicketUnit ?? ""; // Assuming Unit is Location

        // 状态列一列担三件事：审核结果 + 门户环节 + 待办标记。
        // 行结构是固定 8 列，加列要动场景和表头，代价远大于收益，所以并进同一格；
        // 这一列只有 200 宽、字号 40，两行封顶约 10 个字，三件事同时摆出来是放不下的。
        //
        // 【审过的票只显示结果，不再带门户环节】审核结果是这一列最该回答的问题，
        // 而且结果不回传门户，门户那个环节名在这张票上永远停在处置之前的样子 ——
        // 两个并排显示（"通过 / 审批中"）只会让人以为哪里出错了。环节名在票面里看得到。
        string status = ticket.jobTicketStatus ?? "";
        string reviewed = JobTicketApprovalStore.Badge(ticket.sid);

        if (reviewed != null) status = reviewed;
        else if (actionable) status = ActionablePrefix + status;

        statusText.text = status;

        LayoutRebuilder.ForceRebuildLayoutImmediate(GetComponent<RectTransform>());
    }

    /// <summary>
    /// 可审批标记。
    ///
    /// 【为什么不用「·」当分隔符】三个在用的字体资产是 Static 图集，表外字直接显示成豆腐块。
    /// 中点 U+00B7 不在 Assets/Fonts/charset_used.txt 里，用它就要跑一次补字形，
    /// 那会改写三个 35MB 的二进制资产 —— 为一个分隔符不值得。
    /// 「【】」和这四个汉字都已在表内，改这里的文案前先照上面的方法确认一遍。
    /// </summary>
    private const string ActionablePrefix = "【待我审批】";

    private TextMeshProUGUI indexText => transform.GetChild(0).GetComponent<TextMeshProUGUI>();
    private TextMeshProUGUI enterpriseNameText => transform.GetChild(1).GetComponent<TextMeshProUGUI>();
    private TextMeshProUGUI jobTypeText => transform.GetChild(2).GetComponent<TextMeshProUGUI>();
    private TextMeshProUGUI applyTimeText => transform.GetChild(3).GetComponent<TextMeshProUGUI>();
    private TextMeshProUGUI applyCodeText => transform.GetChild(4).GetComponent<TextMeshProUGUI>();
    private TextMeshProUGUI responsibleText => transform.GetChild(5).GetComponent<TextMeshProUGUI>();
    private TextMeshProUGUI locationText => transform.GetChild(6).GetComponent<TextMeshProUGUI>();
    private TextMeshProUGUI statusText => transform.GetChild(7).GetComponent<TextMeshProUGUI>();
}
