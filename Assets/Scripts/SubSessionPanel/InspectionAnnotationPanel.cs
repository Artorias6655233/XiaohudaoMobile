using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class InspectionAnnotationPanel : MonoBehaviour
{
    public TextMeshProUGUI historyText;
    public TextMeshProUGUI statusText;
    public TMP_InputField contentInput;
    public Button saveButton;
    public Button editOwnButton;
    public Button deleteOwnButton;
    public List<Button> phraseButtons = new List<Button>();

    private InspectionAnnotationService service;
    private string patrolRecordId;
    private InspectionAnnotation editing;
    private List<InspectionAnnotation> items = new List<InspectionAnnotation>();
    private bool saving;
    private bool closeConfirmationArmed;
    private bool deleteConfirmationArmed;
    private string[] currentPhrases = Array.Empty<string>();

    public bool IsOpen => gameObject.activeSelf;
    public bool IsEditingInput => contentInput != null && contentInput.isFocused;

    public static InspectionAnnotationPanel Create(
        Transform parent, InspectionAnnotationService service)
    {
        RectTransform root = RuntimeWorldUi.Panel(parent, "RuntimeInspectionAnnotationPanel",
            new Vector2(1000, 720), Vector2.zero);
        RuntimeWorldUi.Card(root, "HeaderBar", new Vector2(0.02f, 0.89f),
            new Vector2(0.98f, 0.99f), RuntimeWorldUi.HeaderColor);
        RuntimeWorldUi.Text(root, "Title", "检查条目批注",
            new Vector2(0.04f, 0.90f), new Vector2(0.96f, 0.98f), 40,
            TextAlignmentOptions.MidlineLeft);
        RuntimeWorldUi.Card(root, "HistoryCard", new Vector2(0.03f, 0.56f),
            new Vector2(0.97f, 0.87f), RuntimeWorldUi.CardColor);
        TextMeshProUGUI historyTitle = RuntimeWorldUi.Text(root, "HistoryTitle", "历史批注",
            new Vector2(0.05f, 0.80f), new Vector2(0.95f, 0.86f), 28);
        historyTitle.color = RuntimeWorldUi.MutedTextColor;
        TextMeshProUGUI history = RuntimeWorldUi.Text(root, "History", "暂无批注",
            new Vector2(0.05f, 0.58f), new Vector2(0.95f, 0.80f), 26);
        RuntimeWorldUi.Card(root, "InputCard", new Vector2(0.03f, 0.34f),
            new Vector2(0.97f, 0.55f), RuntimeWorldUi.CardColor);
        TextMeshProUGUI inputTitle = RuntimeWorldUi.Text(root, "InputTitle", "批注内容",
            new Vector2(0.05f, 0.49f), new Vector2(0.95f, 0.54f), 27);
        inputTitle.color = RuntimeWorldUi.MutedTextColor;
        TMP_InputField input = RuntimeWorldUi.Input(root, "Content",
            new Vector2(0.05f, 0.36f), new Vector2(0.95f, 0.49f));
        TextMeshProUGUI phraseTitle = RuntimeWorldUi.Text(root, "PhraseTitle", "快捷短语",
            new Vector2(0.03f, 0.285f), new Vector2(0.97f, 0.34f), 27);
        phraseTitle.color = RuntimeWorldUi.MutedTextColor;
        var phrases = new List<Button>();
        for (int index = 0; index < 3; index++)
        {
            float left = 0.03f + index * 0.315f;
            phrases.Add(RuntimeWorldUi.Button(root, "Phrase" + index, string.Empty,
                new Vector2(left, 0.19f), new Vector2(left + 0.305f, 0.285f), 22));
            RuntimeWorldUi.StyleButton(phrases[index], RuntimeWorldUi.SecondaryColor);
        }
        TextMeshProUGUI status = RuntimeWorldUi.Text(root, "Status", "",
            new Vector2(0.03f, 0.125f), new Vector2(0.97f, 0.185f), 23,
            TextAlignmentOptions.Center);
        status.color = RuntimeWorldUi.AccentColor;
        Button edit = RuntimeWorldUi.Button(root, "EditOwn", "编辑本人最近批注",
            new Vector2(0.03f, 0.02f), new Vector2(0.25f, 0.12f));
        RuntimeWorldUi.StyleButton(edit, RuntimeWorldUi.SecondaryColor);
        Button delete = RuntimeWorldUi.Button(root, "DeleteOwn", "删除本人最近批注",
            new Vector2(0.26f, 0.02f), new Vector2(0.48f, 0.12f));
        RuntimeWorldUi.StyleButton(delete, RuntimeWorldUi.DestructiveColor);
        Button save = RuntimeWorldUi.Button(root, "Save", "保存批注",
            new Vector2(0.49f, 0.02f), new Vector2(0.73f, 0.12f));
        RuntimeWorldUi.StyleButton(save, RuntimeWorldUi.PrimaryColor);
        Button close = RuntimeWorldUi.Button(root, "Close", "关闭",
            new Vector2(0.75f, 0.02f), new Vector2(0.97f, 0.12f));
        RuntimeWorldUi.StyleButton(close, RuntimeWorldUi.SecondaryColor);

        InspectionAnnotationPanel panel = root.gameObject.AddComponent<InspectionAnnotationPanel>();
        panel.service = service;
        panel.historyText = history;
        panel.statusText = status;
        panel.contentInput = input;
        panel.saveButton = save;
        panel.editOwnButton = edit;
        panel.deleteOwnButton = delete;
        panel.phraseButtons = phrases;
        for (int index = 0; index < phrases.Count; index++)
        {
            int captured = index;
            phrases[index].onClick.AddListener(() => panel.SelectPhrase(captured));
        }
        input.onValueChanged.AddListener(_ =>
        {
            panel.closeConfirmationArmed = false;
            panel.deleteConfirmationArmed = false;
        });
        edit.onClick.AddListener(panel.EditLatestOwn);
        delete.onClick.AddListener(panel.DeleteLatestOwn);
        save.onClick.AddListener(panel.Save);
        close.onClick.AddListener(panel.CancelOrClose);
        root.gameObject.SetActive(false);
        return panel;
    }

    public void Open(string targetPatrolRecordId)
    {
        if (string.IsNullOrWhiteSpace(targetPatrolRecordId)) return;
        patrolRecordId = targetPatrolRecordId;
        editing = null;
        closeConfirmationArmed = false;
        deleteConfirmationArmed = false;
        if (contentInput != null) contentInput.text = string.Empty;
        gameObject.SetActive(true);
        StartCoroutine(Refresh());
    }

    public void Open(string targetPatrolRecordId, string inspectionType)
    {
        ConfigureForInspectionType(inspectionType);
        Open(targetPatrolRecordId);
    }

    public void ConfigureForInspectionType(string inspectionType)
    {
        currentPhrases = InspectionAnnotationPhrases.ForInspectionType(inspectionType);
        for (int index = 0; index < phraseButtons.Count; index++)
        {
            Button button = phraseButtons[index];
            bool visible = index < currentPhrases.Length;
            button.gameObject.SetActive(visible);
            if (!visible) continue;
            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.text = currentPhrases[index];
            RuntimeWorldUi.StyleButton(button, RuntimeWorldUi.SecondaryColor);
        }
    }

    private void SelectPhrase(int index)
    {
        if (index < 0 || index >= currentPhrases.Length || contentInput == null) return;
        contentInput.text = currentPhrases[index];
        contentInput.DeactivateInputField();
        for (int buttonIndex = 0; buttonIndex < phraseButtons.Count; buttonIndex++)
            RuntimeWorldUi.StyleButton(phraseButtons[buttonIndex],
                buttonIndex == index ? RuntimeWorldUi.AccentColor : RuntimeWorldUi.SecondaryColor);
        SetStatus("已选择快捷短语，可按 Y 保存");
    }

    public void Save()
    {
        if (!saving && !string.IsNullOrWhiteSpace(patrolRecordId)) StartCoroutine(SaveCoroutine());
    }

    private IEnumerator SaveCoroutine()
    {
        saving = true;
        if (saveButton != null) saveButton.interactable = false;
        var result = new ServiceResult<InspectionAnnotation>();
        if (editing == null)
        {
            yield return service.Add(patrolRecordId, contentInput?.text, result);
        }
        else
        {
            yield return service.Update(editing.sid, editing.version, contentInput?.text, result);
        }
        saving = false;
        if (saveButton != null) saveButton.interactable = true;
        if (!result.ok)
        {
            if (result.conflict)
            {
                yield return Refresh();
                editing = null;
                if (contentInput != null) contentInput.text = string.Empty;
                SetStatus("本机批注版本已变化，已刷新；请重新选择后编辑");
            }
            else SetStatus(result.error);
            yield break;
        }
        editing = null;
        if (contentInput != null) contentInput.text = string.Empty;
        SetStatus("批注已保存到本机");
        yield return Refresh();
    }

    private IEnumerator Refresh()
    {
        var result = new ServiceResult<List<InspectionAnnotation>>();
        yield return service.Load(patrolRecordId, result);
        if (!result.ok) { SetStatus(result.error); yield break; }
        items = result.value ?? new List<InspectionAnnotation>();
        var builder = new StringBuilder();
        foreach (InspectionAnnotation item in items)
        {
            builder.Append(item.updated).Append("  ").Append(item.authorName)
                .Append(item.editable ? "（本人）" : string.Empty).AppendLine()
                .AppendLine(item.content).AppendLine();
        }
        historyText.text = builder.Length == 0 ? "暂无批注" : builder.ToString();
        if (editOwnButton != null)
            editOwnButton.gameObject.SetActive(items.Any(item => item.editable));
        if (deleteOwnButton != null)
            deleteOwnButton.gameObject.SetActive(items.Any(item => item.editable));
    }

    private void EditLatestOwn()
    {
        editing = items.FirstOrDefault(item => item.editable);
        if (editing == null) { SetStatus("暂无本人可编辑批注"); return; }
        contentInput.text = editing.content;
        contentInput.ActivateInputField();
        SetStatus("正在编辑本人批注");
    }

    public void DeleteLatestOwn()
    {
        if (saving) return;
        InspectionAnnotation target = editing ?? items.FirstOrDefault(item => item.editable);
        if (target == null) { SetStatus("暂无本人可删除批注"); return; }
        if (!deleteConfirmationArmed)
        {
            deleteConfirmationArmed = true;
            SetStatus("再次点击删除，将永久移除这条本机批注");
            return;
        }
        StartCoroutine(DeleteCoroutine(target));
    }

    private IEnumerator DeleteCoroutine(InspectionAnnotation target)
    {
        saving = true;
        if (deleteOwnButton != null) deleteOwnButton.interactable = false;
        var result = new ServiceResult<bool>();
        yield return service.Delete(target.sid, target.version, result);
        saving = false;
        if (deleteOwnButton != null) deleteOwnButton.interactable = true;
        deleteConfirmationArmed = false;
        if (!result.ok)
        {
            SetStatus(result.error);
            if (result.conflict) yield return Refresh();
            yield break;
        }
        editing = null;
        if (contentInput != null) contentInput.text = string.Empty;
        yield return Refresh();
        SetStatus("本机批注已删除");
    }

    private void CancelOrClose()
    {
        bool hasDraft = contentInput != null && !string.IsNullOrWhiteSpace(contentInput.text);
        if (hasDraft && !closeConfirmationArmed)
        {
            closeConfirmationArmed = true;
            SetStatus("有未保存内容，再次点击关闭将放弃草稿");
            return;
        }
        CloseImmediately();
    }

    public void CloseImmediately()
    {
        contentInput?.DeactivateInputField();
        editing = null;
        patrolRecordId = null;
        deleteConfirmationArmed = false;
        gameObject.SetActive(false);
    }

    private void SetStatus(string value)
    {
        if (statusText != null) statusText.text = value ?? string.Empty;
    }

    private void OnEnable() { SandTableMarkerRaycasts.Suppress(); }
    private void OnDisable() { SandTableMarkerRaycasts.Release(); }
}
