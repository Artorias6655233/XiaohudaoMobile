using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Run with Unity -batchmode -executeMethod DoublePreventionRuntimeChecks.Run -quit.
public static class DoublePreventionRuntimeChecks
{
    public static void Run()
    {
        CheckUnfinishedHazards();
        CheckContentRules();
        CheckMediaKinds();
        CheckPatrolContracts();
        CheckPatrolUsesRealBackendByDefault();
        CheckDoublePreventionServicesInitializeBeforePanelDeactivation();
        CheckPatrolTransitionPresentation();
        CheckPatrolDetailOverlayAndBackButton();
        CheckRemediationDetailOverlayActions();
        CheckSupervisionPhraseSelection();
        CheckSupervisionResultFlowsBackToDetail();
        CheckSupervisionPanelUsesSeparatedColumns();
        CheckDoublePreventionVisualHierarchy();
        CheckInspectionAnnotationPhraseGroups();
        CheckInspectionAnnotationPhraseSelection();
        CheckMockContracts();
        CheckLocalAnnotationCrud();
        CheckLocalAnnotationPermissions();
        CheckLocalAnnotationRecovery();
        CheckSceneWiring();
        Debug.Log("DoublePreventionRuntimeChecks: all checks passed");
    }

    private static void CheckPatrolUsesRealBackendByDefault()
    {
        var host = new GameObject("PatrolBackendDefaultCheck");
        try
        {
            Double_CheckList panel = host.AddComponent<Double_CheckList>();
            Check(!panel.useMockBackend,
                "Runtime inspection must use the real patrol backend by default");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(host);
        }
    }

    private static void CheckDoublePreventionServicesInitializeBeforePanelDeactivation()
    {
        var patrolHost = new GameObject("PatrolEarlyInitializationCheck");
        var supervisionHost = new GameObject("SupervisionEarlyInitializationCheck");
        try
        {
            Double_CheckList patrolPanel = patrolHost.AddComponent<Double_CheckList>();
            Double_ReportList supervisionPanel = supervisionHost.AddComponent<Double_ReportList>();

            System.Collections.IEnumerator patrolStart = patrolPanel.Start();
            System.Collections.IEnumerator supervisionStart = supervisionPanel.Start();
            Check(patrolStart.MoveNext(), "Patrol panel Start must enter its initialization flow");
            Check(supervisionStart.MoveNext(), "Supervision panel Start must enter its initialization flow");

            const System.Reflection.BindingFlags privateInstance =
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            Check(typeof(Double_CheckList).GetField("patrolService", privateInstance)
                      ?.GetValue(patrolPanel) != null,
                "Patrol service must exist before the base panel can deactivate the GameObject");
            Check(typeof(Double_ReportList).GetField("supervisionService", privateInstance)
                      ?.GetValue(supervisionPanel) != null,
                "Supervision service must exist before the base panel can deactivate the GameObject");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(patrolHost);
            UnityEngine.Object.DestroyImmediate(supervisionHost);
        }
    }

    private static void CheckUnfinishedHazards()
    {
        var open = Danger("open", "4");
        var reviewing = Danger("reviewing", "5", "3");
        var closed = Danger("closed", "5", "3", "2");
        var duplicate = Danger("open", "5");

        List<DoubleDanger> result = DoublePreventionRules.SelectUnfinished(
            new[] { open, closed }, new[] { reviewing, duplicate });

        Check(result.Count == 2, "Status 4/5 hazards must be deduplicated and closed hazards excluded");
        Check(result[0].sid == "open" && result[1].sid == "reviewing",
            "Unfinished hazards must keep stable first-seen order");
    }

    private static void CheckContentRules()
    {
        ExpectFailure(() => DoublePreventionRules.NormalizeContent("   "),
            "Blank content must be rejected");
        ExpectFailure(() => DoublePreventionRules.NormalizeContent(new string('字', 501)),
            "Content over 500 characters must be rejected");
        Check(DoublePreventionRules.NormalizeContent("  立即整改。  ") == "立即整改。",
            "Content must be trimmed");
    }

    private static void CheckMediaKinds()
    {
        Check(InspectionMediaKindResolver.Resolve("image/jpeg") == InspectionMediaKind.Image,
            "JPEG must resolve as image");
        Check(InspectionMediaKindResolver.Resolve("audio/mpeg") == InspectionMediaKind.Audio,
            "MP3 must resolve as audio");
        Check(InspectionMediaKindResolver.Resolve("video/mp4") == InspectionMediaKind.Video,
            "MP4 must resolve as video");
        Check(InspectionMediaKindResolver.Resolve(null) == InspectionMediaKind.Unsupported,
            "Missing MIME must remain unsupported");
        Check(InspectionMediaKindResolver.Resolve("application/octet-stream") ==
              InspectionMediaKind.Unsupported,
            "Filename-independent MIME classification must reject octet-stream");
    }

    private static void CheckPatrolContracts()
    {
        const string json = "{\"rows\":[{\"sid\":\"patrol-1\",\"corpName\":\"测试企业\",\"companySafetyInspections\":[{\"patrolRecordId\":\"item-1\",\"inspectionStatus\":\"正常\"}]}],\"total\":1}";
        PatrolPage response = SandTableJson.Deserialize<PatrolPage>(json, "patrol/check");
        Check(response.total == 1 && response.rows.Count == 1,
            "Patrol page wrapper must parse rows and total");
        Check(response.rows[0].companySafetyInspections[0].patrolRecordId == "item-1",
            "Inspection item target ID must survive parsing");

        const string annotationJson = "{\"sid\":\"annotation-1\",\"patrolRecordId\":\"item-1\",\"version\":2,\"editable\":true}";
        InspectionAnnotation annotation = SandTableJson.Deserialize<InspectionAnnotation>(
            annotationJson, "patrol/annotation");
        Check(annotation.sid == "annotation-1" && annotation.version == 2 && annotation.editable,
            "Create/update annotation responses must parse as complete annotation objects");
    }

    private static void CheckPatrolTransitionPresentation()
    {
        var patrol = new PatrolRecord
        {
            corpName = "测试企业",
            docCode = "XC-001",
            patrolStartTime = "2026-09-22 09:00:00",
            patrolAddress = "一号罐区"
        };
        var item = new PatrolInspectionItem
        {
            inspectionType = "aqyh",
            hazardCategory = "消防安全",
            isMajorAccident = "0",
            inspectionStatus = "通道需要复核"
        };

        string text = PatrolTransitionPresentation.Build(patrol, item);
        Check(text.Contains("企业：测试企业") && text.Contains("巡查单号：XC-001"),
            "Transition detail must show the selected patrol identity");
        Check(text.Contains("检查情况：通道需要复核"),
            "Transition detail must show the inspection result");
        Check(text.Contains("图片：无") && text.Contains("语音：无") && text.Contains("视频：无"),
            "Transition detail must explicitly show all three media categories as unavailable");
    }

    private static void CheckPatrolDetailOverlayAndBackButton()
    {
        var host = new GameObject("PatrolDetailOverlayCheck", typeof(RectTransform));
        try
        {
            PatrolDetailPanel panel = PatrolDetailPanel.Create(
                host.transform,
                new PatrolInspectionService(true, MockDoublePreventionStore.CreateDefault()),
                new InspectionAnnotationService());

            UnityEngine.UI.LayoutElement layout = panel.GetComponent<UnityEngine.UI.LayoutElement>();
            Check(layout != null && layout.ignoreLayout,
                "Patrol detail must be excluded from the list parent's layout");

            Transform backTransform = panel.transform.Find("Back");
            Check(backTransform != null,
                "Patrol detail must provide a dedicated back button");
            UnityEngine.UI.Button back = backTransform.GetComponent<UnityEngine.UI.Button>();
            TMPro.TextMeshProUGUI firstItemLabel = panel.itemButtons[0]
                .GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
            Check(panel.headerText.fontSize >= 40 && panel.itemText.fontSize >= 34 &&
                  firstItemLabel != null && firstItemLabel.fontSize >= 30,
                "Patrol detail text and controls must remain readable in the headset");
            panel.gameObject.SetActive(true);
            back.onClick.Invoke();
            Check(!panel.gameObject.activeSelf,
                "The patrol detail back button must close the detail view");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(host);
        }
    }

    private static void CheckRemediationDetailOverlayActions()
    {
        var host = new GameObject("RemediationDetailOverlayCheck", typeof(RectTransform));
        var list = new GameObject("ListPanel", typeof(RectTransform));
        list.transform.SetParent(host.transform, false);
        RectTransform listRect = list.GetComponent<RectTransform>();
        listRect.anchorMin = new Vector2(0.12f, 0.16f);
        listRect.anchorMax = new Vector2(0.88f, 0.84f);
        listRect.anchoredPosition = new Vector2(18f, -12f);
        listRect.sizeDelta = new Vector2(-40f, -24f);
        bool returned = false;
        DoubleDanger supervised = null;
        try
        {
            RemediationDetailPanel panel = RemediationDetailPanel.Create(host.transform);
            panel.ConfigureListView(listRect, () => returned = true,
                danger => supervised = danger);
            var danger = new DoubleDanger
            {
                sid = "danger-ui-1",
                hiddenDangerName = "罐区消防通道占用",
                hiddenDangerDescription = "消防通道存在堆物",
                verifyInfo = new VerifyInfo
                {
                    hiddenDangerVerifyChangePerson = "张三",
                    rectificationDeadline = "2026-09-30"
                }
            };

            panel.Open(danger);
            RectTransform detailRect = panel.GetComponent<RectTransform>();
            Check(detailRect.anchorMin == listRect.anchorMin &&
                  detailRect.anchorMax == listRect.anchorMax &&
                  detailRect.anchoredPosition == listRect.anchoredPosition &&
                  detailRect.sizeDelta == listRect.sizeDelta,
                "Remediation detail must replace the list at the same position and size");
            Check(panel.transform.Find("HeaderBar") != null &&
                  panel.transform.Find("HazardCard") != null &&
                  panel.transform.Find("RectificationCard") != null,
                "Remediation detail must use the sand-table card hierarchy");
            Check(panel.titleText.fontSize >= 40 && panel.bodyText.fontSize >= 30,
                "Remediation detail text must remain readable in the headset");

            panel.transform.Find("Supervise").GetComponent<UnityEngine.UI.Button>()
                .onClick.Invoke();
            Check(supervised == danger,
                "The visible supervision button must act on the displayed hazard");
            panel.transform.Find("Back").GetComponent<UnityEngine.UI.Button>()
                .onClick.Invoke();
            Check(returned && !panel.gameObject.activeSelf,
                "The visible back button must close detail and restore the list");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(host);
        }
    }

    private static void CheckSupervisionPhraseSelection()
    {
        var host = new GameObject("SupervisionPhraseSelectionCheck", typeof(RectTransform));
        try
        {
            SupervisionPanel panel = SupervisionPanel.Create(host.transform, null);
            Check(panel.phraseButtons.Count == 4,
                "Supervision must expose four controller-friendly quick phrases");

            TMPro.TextMeshProUGUI firstLabel = panel.phraseButtons[0]
                .GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
            Check(firstLabel != null && firstLabel.text ==
                  "请立即落实整改措施，并在整改期限内完成闭环。",
                "The first supervision phrase must provide the approved immediate action text");

            panel.contentInput.text = "原督办内容";
            panel.phraseButtons[1].onClick.Invoke();
            Check(panel.contentInput.text ==
                  "整改进度滞后，请加快整改并及时反馈进展。",
                "Selecting a supervision phrase must replace the draft content");
            Check(panel.statusText.text.Contains("可按 Y 发送"),
                "Selecting a supervision phrase must explain the controller send action");
            Color selectedColor = panel.phraseButtons[1]
                .GetComponent<UnityEngine.UI.Image>().color;
            Check(selectedColor == RuntimeWorldUi.AccentColor,
                "The selected supervision phrase must be visually highlighted");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(host);
        }
    }

    private static void CheckSupervisionResultFlowsBackToDetail()
    {
        var host = new GameObject("SupervisionResultFlowCheck", typeof(RectTransform));
        try
        {
            SupervisionPanel supervision = SupervisionPanel.Create(host.transform, null);
            RemediationDetailPanel detail = RemediationDetailPanel.Create(host.transform);
            SupervisionRecord delivered = null;
            supervision.Sent += record => delivered = record;
            var record = new SupervisionRecord
            {
                sid = "remind-1",
                remindContent = "整改进度滞后，请加快整改并及时反馈进展。",
                senderName = "监管人员",
                created = "2026-09-23 15:30:00"
            };

            System.Reflection.MethodInfo complete = typeof(SupervisionPanel).GetMethod(
                "CompleteSuccessfulSend",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            Check(complete != null,
                "A successful supervision send must have one completion path");
            complete.Invoke(supervision, new object[] { record, false });
            Check(delivered == record,
                "A successful supervision send must publish the created record");

            detail.ShowLatestSupervision(delivered);
            Check(detail.latestSupervisionText.text.Contains(record.remindContent) &&
                  detail.latestSupervisionText.text.Contains("监管人员"),
                "Remediation detail must display the latest supervision requirement");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(host);
        }
    }

    private static void CheckSupervisionPanelUsesSeparatedColumns()
    {
        var host = new GameObject("SupervisionLayoutCheck", typeof(RectTransform));
        try
        {
            SupervisionPanel panel = SupervisionPanel.Create(host.transform, null);
            RectTransform root = panel.GetComponent<RectTransform>();
            RectTransform history = panel.transform.Find("HistoryCard") as RectTransform;
            RectTransform editor = panel.transform.Find("EditorCard") as RectTransform;
            Check(root.sizeDelta.x >= 1300f && root.sizeDelta.y >= 900f,
                "Supervision panel must be large enough for long controller phrases");
            Check(history != null && editor != null &&
                  history.anchorMax.x < editor.anchorMin.x,
                "Supervision history and editor must occupy separate columns");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(host);
        }
    }

    private static void CheckInspectionAnnotationPhraseGroups()
    {
        Type phraseType = typeof(DoublePreventionRules).Assembly.GetType(
            "InspectionAnnotationPhrases");
        Check(phraseType != null,
            "Inspection annotation phrase groups must be available at runtime");
        System.Reflection.MethodInfo resolve = phraseType.GetMethod("ForInspectionType");
        Check(resolve != null,
            "Inspection annotation phrase groups must resolve by inspection type");

        string[] hazards = (string[])resolve.Invoke(null, new object[] { "aqyh" });
        string[] patrols = (string[])resolve.Invoke(null, new object[] { "xcjl" });
        Check(hazards.Length == 3 && hazards[0] == "现场存在安全隐患，请立即整改" &&
              hazards[2] == "整改结果符合要求",
            "Safety hazard phrases must match the approved defaults");
        Check(patrols.Length == 3 && patrols[0] == "现场运行正常" &&
              patrols[2] == "发现异常，请及时处置",
            "Patrol record phrases must match the approved defaults");
    }

    private static void CheckDoublePreventionVisualHierarchy()
    {
        var host = new GameObject("DoublePreventionVisualHierarchy", typeof(RectTransform));
        try
        {
            PatrolDetailPanel detail = PatrolDetailPanel.Create(
                host.transform,
                new PatrolInspectionService(true, MockDoublePreventionStore.CreateDefault()),
                new InspectionAnnotationService());
            Check(detail.transform.Find("HeaderBar") != null &&
                  detail.transform.Find("InspectionNavigation") != null &&
                  detail.transform.Find("DetailCard") != null &&
                  detail.transform.Find("MediaCard") != null,
                "Patrol detail must use the sand-table card hierarchy");

            InspectionAnnotationPanel annotation = detail.annotationPanel;
            Check(annotation.transform.Find("HeaderBar") != null &&
                  annotation.transform.Find("HistoryCard") != null &&
                  annotation.transform.Find("PhraseTitle") != null,
                "Annotation panel must use the sand-table card hierarchy");
            Color deleteColor = annotation.deleteOwnButton.GetComponent<UnityEngine.UI.Image>().color;
            Check(deleteColor.r > deleteColor.g && deleteColor.r > deleteColor.b,
                "Annotation delete action must use the sand-table destructive color");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(host);
        }
    }

    private static void CheckInspectionAnnotationPhraseSelection()
    {
        var host = new GameObject("InspectionAnnotationPhraseSelection", typeof(RectTransform));
        try
        {
            InspectionAnnotationPanel panel = InspectionAnnotationPanel.Create(
                host.transform, new InspectionAnnotationService());
            System.Reflection.MethodInfo configure = typeof(InspectionAnnotationPanel)
                .GetMethod("ConfigureForInspectionType");
            Check(configure != null,
                "Annotation panel must support configuring phrases by inspection type");

            configure.Invoke(panel, new object[] { "aqyh" });
            Transform firstPhrase = panel.transform.Find("Phrase0");
            Check(firstPhrase != null,
                "Annotation panel must render selectable phrase buttons");
            TMPro.TextMeshProUGUI label = firstPhrase
                .GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
            Check(label != null && label.text == "现场存在安全隐患，请立即整改",
                "Hazard annotation panel must show the approved hazard phrases");

            panel.contentInput.text = "原输入内容";
            firstPhrase.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            Check(panel.contentInput.text == "现场存在安全隐患，请立即整改",
                "Selecting a phrase must replace the annotation input content");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(host);
        }
    }

    private static void CheckMockContracts()
    {
        MockDoublePreventionStore store = DoublePreventionMockLoader.Load();

        int before = store.GetSupervisions("danger-001").Count;
        SupervisionRecord sent = store.SendSupervision(
            "danger-001", "请立即整改", "same-supervision-request");
        SupervisionRecord retried = store.SendSupervision(
            "danger-001", "请立即整改", "same-supervision-request");
        Check(sent.sid == retried.sid &&
              store.GetSupervisions("danger-001").Count == before + 1,
            "A retried supervision request must be idempotent");

        List<InspectionAttachment> attachments = store.GetAttachments("patrol-item-001");
        Check(attachments.Count == 3 &&
              attachments.Exists(item => InspectionMediaKindResolver.Resolve(item.mimeType) == InspectionMediaKind.Image) &&
              attachments.Exists(item => InspectionMediaKindResolver.Resolve(item.mimeType) == InspectionMediaKind.Audio) &&
              attachments.Exists(item => InspectionMediaKindResolver.Resolve(item.mimeType) == InspectionMediaKind.Video),
            "Mock contract must include image, audio, and video metadata");

    }

    private static void CheckLocalAnnotationCrud()
    {
        string directory = Path.Combine(Path.GetTempPath(),
            "xiaohudao-inspection-annotations-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "inspection_annotations.json");
        try
        {
            var store = new InspectionAnnotationStore(path, "local-user", "本机用户");
            InspectionAnnotation added = store.Add("patrol-item-001", "  首次批注  ");
            Check(added.content == "首次批注" && added.version == 1 && added.editable,
                "Local annotation add must normalize content and mark the author editable");

            var reloaded = new InspectionAnnotationStore(path, "local-user", "本机用户");
            List<InspectionAnnotation> persisted = reloaded.Load("patrol-item-001");
            Check(persisted.Count == 1 && persisted[0].sid == added.sid,
                "Local annotations must survive store recreation");

            InspectionAnnotation updated = reloaded.Update(added.sid, 1, "更新后的批注");
            Check(updated.version == 2 && updated.content == "更新后的批注",
                "Local annotation update must persist content and increment version");
            ExpectFailure(() => reloaded.Update(added.sid, 1, "旧版本覆盖"),
                "Stale local annotation versions must be rejected");

            reloaded.Delete(added.sid, 2);
            Check(new InspectionAnnotationStore(path, "local-user", "本机用户")
                    .Load("patrol-item-001").Count == 0,
                "Deleted local annotations must stay deleted after reload");
            Check(!File.Exists(path + ".tmp"),
                "Atomic local annotation saves must not leave a temporary file");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static void CheckLocalAnnotationRecovery()
    {
        string directory = Path.Combine(Path.GetTempPath(),
            "xiaohudao-inspection-annotations-broken-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "inspection_annotations.json");
        try
        {
            File.WriteAllText(path, "broken-json");
            var store = new InspectionAnnotationStore(path, "local-user", "本机用户");
            Check(store.Load("patrol-item-001").Count == 0 && store.RecoveredCorruptFile,
                "A corrupt local annotation file must recover as an empty collection");
            Check(Directory.GetFiles(directory, "*.broken-*").Length == 1,
                "A corrupt local annotation file must be quarantined");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static void CheckLocalAnnotationPermissions()
    {
        string directory = Path.Combine(Path.GetTempPath(),
            "xiaohudao-inspection-annotations-permissions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "inspection_annotations.json");
        try
        {
            File.WriteAllText(path,
                "{\"items\":[{\"sid\":\"other-annotation\",\"patrolRecordId\":\"patrol-item-001\",\"content\":\"其他本机用户的批注\",\"authorId\":\"other-user\",\"authorName\":\"其他用户\",\"created\":\"2026-09-22 10:00:00\",\"updated\":\"2026-09-22 10:00:00\",\"version\":1}]}" );
            var store = new InspectionAnnotationStore(path, "local-user", "本机用户");
            Check(!store.Load("patrol-item-001")[0].editable,
                "Annotations from another local author must be read-only");
            ExpectFailure(() => store.Update("other-annotation", 1, "越权修改"),
                "Annotations from another local author must not be editable");
            ExpectFailure(() => store.Delete("other-annotation", 1),
                "Annotations from another local author must not be deletable");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static void CheckSceneWiring()
    {
        EditorSceneManager.OpenScene("Assets/LZW/SandTable/SandTable.unity", OpenSceneMode.Single);
        Double_ReportList supervision = UnityEngine.Object.FindObjectOfType<Double_ReportList>(true);
        Double_CheckList inspection = UnityEngine.Object.FindObjectOfType<Double_CheckList>(true);
        Check(supervision != null && supervision.detailPanel != null,
            "Remediation supervision host and legacy detail fields must remain wired");
        Check(inspection != null && inspection.checkObjectPrefab != null,
            "Patrol host must keep the reusable list row wired");
        Check(typeof(Double_ReportList).GetField("supervisionPanelPrefab") != null,
            "Supervision host must expose its runtime-created panel");
        Check(typeof(Double_ReportList).GetField("remediationDetailPanelPrefab") != null,
            "Supervision host must expose its runtime-created detail panel");
        Check(typeof(Double_CheckList).GetField("patrolDetailPanelPrefab") != null,
            "Patrol host must expose its runtime-created detail panel");
    }

    private static DoubleDanger Danger(string sid, string status, params string[] reviewResults)
    {
        var danger = new DoubleDanger
        {
            sid = sid,
            hiddenDangerStatus = status,
            reviewInfos = new List<ReviewInfo>()
        };
        foreach (string result in reviewResults)
            danger.reviewInfos.Add(new ReviewInfo { reviewResult = result });
        return danger;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void ExpectFailure(Action action, string message)
    {
        try { action(); }
        catch (Exception) { return; }
        throw new Exception(message);
    }
}
