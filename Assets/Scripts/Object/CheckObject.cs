using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System;

public class CheckObject : MonoBehaviour
{
    private DoubleDanger _data;
    private PatrolRecord _patrolData;
    public Action<DoubleDanger> OnDetailClick;
    public Action<PatrolRecord> OnPatrolDetailClick;
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
            Debug.LogWarning($"CheckObject: No Button component found on {gameObject.name}");
        }
    }

    private void OnButtonClick()
    {
        Debug.Log($"CheckObject clicked: {_data?.hiddenDangerName}");
        if (_patrolData != null) OnPatrolDetailClick?.Invoke(_patrolData);
        else OnDetailClick?.Invoke(_data);
    }

    public int index
    {
        set
        {
            indexText.text = value.ToString();
        }
    }

    public DoubleDanger data
    {
        set
        {
            _patrolData = null;
            _data = value;
            nameText.text = value.hiddenDangerName ?? "";
            deptText.text = value.deptOrgCode ?? "";
            categoryText.text = value.hiddenDangerType ?? "";
            locationText.text = value.hiddenDangerLocation ?? "";
            levelText.text = value.hiddenDangerLevel ?? "";
            statusText.text = value.hiddenDangerStatus ?? "";
            sourceText.text = value.hiddenDangerSource ?? "";
            inspectorText.text = value.reportedPerson ?? "";

            LayoutRebuilder.ForceRebuildLayoutImmediate(GetComponent<RectTransform>());
        }
    }

    public PatrolRecord patrolData
    {
        set
        {
            _data = null;
            _patrolData = value;
            nameText.text = value?.corpName ?? "";
            deptText.text = value?.docCode ?? "";
            categoryText.text = PatrolLabels.RiskLevel(value?.riskLevel);
            locationText.text = value?.patrolAddress ?? "";
            levelText.text = value?.patrolStartTime ?? "";
            statusText.text = PatrolLabels.Status(value?.patrolStatus);
            sourceText.text = value?.patrolPerson ?? "";
            inspectorText.text = value?.sid ?? "";
            LayoutRebuilder.ForceRebuildLayoutImmediate(GetComponent<RectTransform>());
        }
    }

    private TextMeshProUGUI indexText => transform.GetChild(0).GetComponent<TextMeshProUGUI>();
    private TextMeshProUGUI nameText => transform.GetChild(1).GetComponent<TextMeshProUGUI>();
    private TextMeshProUGUI deptText => transform.GetChild(2).GetComponent<TextMeshProUGUI>();
    private TextMeshProUGUI categoryText => transform.GetChild(3).GetComponent<TextMeshProUGUI>();
    private TextMeshProUGUI locationText => transform.GetChild(4).GetComponent<TextMeshProUGUI>();
    private TextMeshProUGUI levelText => transform.GetChild(5).GetComponent<TextMeshProUGUI>();
    private TextMeshProUGUI statusText => transform.GetChild(6).GetComponent<TextMeshProUGUI>();
    private TextMeshProUGUI sourceText => transform.GetChild(7).GetComponent<TextMeshProUGUI>();
    private TextMeshProUGUI inspectorText => transform.GetChild(8).GetComponent<TextMeshProUGUI>();
}
