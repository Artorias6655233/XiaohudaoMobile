#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 生成【独立的】储罐列表卡片 TankListPanel.prefab，并把之前塞进 TankMonitorPanel 的
/// 列表区拆掉。
///
/// 为什么独立成卡：列表要和管理员信息卡片并排，而罐体详情是另一张卡、位置也不同。
/// 挤在一张卡里只能切态，做不到"列表和详情同时看得见"。
///
/// 样式（底图、字体、字号、栅格）沿用 TankMonitorPanelBuilder 那套，不另起一套。
/// 与那个脚本一样：【不保存场景】。接线走 YAML 补丁，Editor 里拖引用会让 Unity
/// 重新序列化整个场景、把所有 fileID 重编号。
/// </summary>
internal static class TankListPanelBuilder
{
    private const string MonitorPrefabPath = "Assets/ArtWorks/TankMonitorPanel.prefab";
    private const string PrefabPath = "Assets/ArtWorks/TankListPanel.prefab";

    private const string BackgroundSpritePath =
        "Assets/ArtWorks/UI/双重预防机制-整改督办_slices/Group 1321316901@2x.png";

    private const float CardWidth = 1881.5f;
    private const float CardHeight = 1688f;

    /// <summary>
    /// 卡片右侧留白，就是列表和它右边那张管理员信息卡片之间的间隔。
    ///
    /// 【不改宿主 HorizontalLayoutGroup 的 m_Spacing】：那个值（7.5）是所有卡片共用的，
    /// 调大会连主面板那边也一起撑开，而且要动场景。
    /// 把留白做进本卡片的 RectTransform 里，布局组按 rect.width 排，间隔自然就出来了，
    /// 场景一个字节都不用动。1881.5 的卡宽下 160 约合真机 14 cm。
    /// </summary>
    private const float RightGap = 160f;

    private const float PadLeft = 90f;
    private const float TitleSize = 76f;
    private const float HintSize = 52f;
    private const float RowNameSize = 60f;
    private const float RowCodeSize = 44f;
    private const float ButtonSize = 52f;

    private const int RowsPerPage = 8;
    private const float FirstRowY = -300f;
    private const float RowStep = 140f;
    private const float RowHeight = 120f;

    /// <summary>每行「批注」列的宽度。行宽 1701.5，1400+280=1680 正好落在行内。</summary>
    private const float AnnotateWidth = 280f;
    private const float ControlY = -1450f;

    private static readonly Color DimColor = new Color(1f, 1f, 1f, 0.7f);
    private static readonly Color IdleRowColor = new Color(1f, 1f, 1f, 0.07f);
    private static readonly Color ButtonColor = new Color(1f, 1f, 1f, 0.16f);

    [MenuItem("Tools/罐区监测/生成 TankListPanel 卡片")]
    public static void Build()
    {
        StripMonitorCard();
        AddBackButtonToMonitorCard();

        Sprite bg = AssetDatabase.LoadAssetAtPath<Sprite>(BackgroundSpritePath);
        if (bg == null)
        {
            Debug.LogError("[列表卡片] 底图加载失败：" + BackgroundSpritePath);
            return;
        }

        GameObject monitor = AssetDatabase.LoadAssetAtPath<GameObject>(MonitorPrefabPath);
        TextMeshProUGUI sample = monitor == null ? null : monitor.GetComponentInChildren<TextMeshProUGUI>(true);
        if (sample == null)
        {
            Debug.LogError("[列表卡片] 抄不到字体：监测卡片上找不到 TMP");
            return;
        }
        TMP_FontAsset font = sample.font;
        Color textColor = sample.color;

        GameObject root = new GameObject("tanklistpanel", typeof(RectTransform));
        try
        {
            RectTransform rt = (RectTransform)root.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = Vector2.zero;
            // 宽度含右侧留白，布局组按它排，间隔就出来了
            rt.sizeDelta = new Vector2(CardWidth + RightGap, CardHeight);
            rt.localScale = Vector3.one;

            TankListPanel panel = root.AddComponent<TankListPanel>();

            // 底图挂在 Body 上而不是根上：根的宽度含留白，底图铺到根上那道留白就被涂掉了
            GameObject body = new GameObject("Body", typeof(RectTransform));
            RectTransform brt = (RectTransform)body.transform;
            brt.SetParent(root.transform, false);
            brt.anchorMin = new Vector2(0f, 1f);
            brt.anchorMax = new Vector2(0f, 1f);
            brt.pivot = new Vector2(0f, 1f);
            brt.anchoredPosition = Vector2.zero;
            brt.sizeDelta = new Vector2(CardWidth, CardHeight);
            brt.localScale = Vector3.one;

            Image image = body.AddComponent<Image>();
            image.sprite = bg;
            image.type = Image.Type.Simple;
            image.color = Color.white;
            image.raycastTarget = false;   // 纯展示，不吃射线

            panel.panelRoot = body;

            panel.titleText = Text(body, "Title", PadLeft, -70f, CardWidth - PadLeft * 2f, 96f,
                TitleSize, TextAlignmentOptions.Left, textColor, font, "企业名称");
            panel.hintText = Text(body, "Hint", PadLeft, -190f, CardWidth - PadLeft * 2f, 72f,
                HintSize, TextAlignmentOptions.Left, DimColor, font, "共 0 个储罐");

            panel.rows = new List<TankListRow>();
            for (int i = 0; i < RowsPerPage; i++) panel.rows.Add(Row(body, i, textColor, font));

            panel.prevButton = MakeButton(body, "PrevPage", PadLeft, ControlY, 260f, "上一页", font);
            panel.pageText = Text(body, "PageText", PadLeft + 290f, ControlY, 220f, 96f,
                ButtonSize, TextAlignmentOptions.Center, DimColor, font, "1 / 1");
            panel.nextButton = MakeButton(body, "NextPage", PadLeft + 530f, ControlY, 260f, "下一页", font);

            // 与监测卡片一致：prefab 根存成 inactive，Instantiate 出来先隐藏，
            // 等宿主 Show 再打开，避免闪一帧空卡片
            root.SetActive(false);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            if (saved == null)
            {
                Debug.LogError("[列表卡片] 存 prefab 失败：" + PrefabPath);
                return;
            }

            Debug.Log("[列表卡片] 已生成 " + PrefabPath + "，guid = "
                + AssetDatabase.AssetPathToGUID(PrefabPath) + "，" + RowsPerPage + " 行/页");
            Debug.Log("[列表卡片] 接线走 YAML 补丁：给 MajorHazard_Management / MajorHazard_TankList "
                + "两个 MonoBehaviour 各加一行 tankListPrefab，不要在编辑器里拖（会重编号整个场景）");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>
    /// 把之前塞进监测卡片的列表区拆掉，并把 DetailRoot 里的东西搬回根下。
    ///
    /// 必须 LoadPrefabContents 原地改：场景里两个面板指的是
    /// {fileID: 4381527705820265628, guid: 6df0ede71f5187f4ea182ae2706cf521}，
    /// 重新生成会换根 fileID，两处引用当场变空。
    /// </summary>
    private static void StripMonitorCard()
    {
        GameObject contents = PrefabUtility.LoadPrefabContents(MonitorPrefabPath);
        if (contents == null)
        {
            Debug.LogError("[列表卡片] 打不开 " + MonitorPrefabPath);
            return;
        }

        try
        {
            bool changed = false;

            GameObject detail = Find(contents, "DetailRoot");
            if (detail != null)
            {
                List<Transform> rescued = new List<Transform>();
                foreach (Transform child in detail.transform) rescued.Add(child);
                foreach (Transform child in rescued) child.SetParent(contents.transform, false);
                Object.DestroyImmediate(detail);
                changed = true;
            }

            GameObject list = Find(contents, "ListRoot");
            if (list != null) { Object.DestroyImmediate(list); changed = true; }

            GameObject back = Find(contents, "BackToList");
            if (back != null) { Object.DestroyImmediate(back); changed = true; }

            if (!changed)
            {
                Debug.Log("[列表卡片] 监测卡片上没有要拆的列表区，跳过");
                return;
            }

            PrefabUtility.SaveAsPrefabAsset(contents, MonitorPrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[列表卡片] 已把列表区从监测卡片上拆掉，参数表搬回根下");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    /// <summary>
    /// 给详情卡片补一个"返回"按钮。详情和 [列表 + 管理员信息] 是互斥两态，
    /// 没有这个按钮就回不去。同样走原地编辑，不能重建（会换根 fileID）。
    /// </summary>
    private static void AddBackButtonToMonitorCard()
    {
        GameObject contents = PrefabUtility.LoadPrefabContents(MonitorPrefabPath);
        if (contents == null)
        {
            Debug.LogError("[列表卡片] 打不开 " + MonitorPrefabPath);
            return;
        }

        try
        {
            TankMonitorPanel panel = contents.GetComponent<TankMonitorPanel>();
            if (panel == null)
            {
                Debug.LogError("[列表卡片] 监测卡片根上没有 TankMonitorPanel");
                return;
            }

            GameObject stale = Find(contents, "BackButton");
            if (stale != null) Object.DestroyImmediate(stale);

            TextMeshProUGUI sample = contents.GetComponentInChildren<TextMeshProUGUI>(true);
            if (sample == null)
            {
                Debug.LogError("[列表卡片] 监测卡片上找不到 TMP，抄不到字体");
                return;
            }

            panel.backButton = MakeButton(contents, "BackButton",
                CardWidth - PadLeft - 390f, ControlY, 390f, "返回储罐列表", sample.font);

            PrefabUtility.SaveAsPrefabAsset(contents, MonitorPrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[列表卡片] 已给详情卡片补上返回按钮");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private static GameObject Find(GameObject root, string name)
    {
        foreach (Transform child in root.transform)
        {
            if (child.name == name) return child.gameObject;
        }
        return null;
    }

    private static TankListRow Row(GameObject body, int index, Color textColor, TMP_FontAsset font)
    {
        GameObject go = new GameObject("TankRow" + index, typeof(RectTransform));
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(body.transform, false);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(PadLeft, FirstRowY - RowStep * index);
        rt.sizeDelta = new Vector2(CardWidth - PadLeft * 2f, RowHeight);
        rt.localScale = Vector3.one;

        TankListRow row = go.AddComponent<TankListRow>();
        row.background = go.AddComponent<Image>();
        row.background.color = IdleRowColor;
        row.background.raycastTarget = true;   // 整行可点，不要求精确点到文字
        row.button = go.AddComponent<Button>();
        row.button.targetGraphic = row.background;

        // 行宽 = CardWidth - PadLeft*2 = 1701.5。右端让出 AnnotateWidth 给批注列，
        // 名称和编码相应收窄；原来名称 1120 + 编码 500 正好顶到边，不让位就会压到按钮底下。
        const float annotateX = 1400f;
        row.nameText = Text(go, "Name", 24f, 0f, 900f, RowHeight,
            RowNameSize, TextAlignmentOptions.Left, textColor, font, "储罐");
        row.codeText = Text(go, "Code", 940f, 0f, 430f, RowHeight,
            RowCodeSize, TextAlignmentOptions.Right, DimColor, font, "");

        // 批注按钮是整行 Button 的子物体，射线先命中子物体，所以点它不会连带触发选中行。
        // 宿主不传 onAnnotate 时 TankListRow.Bind 会把它整个隐藏。
        row.annotateButton = MakeButton(go, "Annotate", annotateX, -12f, AnnotateWidth, "批注", font);
        return row;
    }

    private static Button MakeButton(GameObject parent, string name, float x, float y, float w,
        string caption, TMP_FontAsset font)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(parent.transform, false);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, 96f);
        rt.localScale = Vector3.one;

        Image image = go.AddComponent<Image>();
        image.color = ButtonColor;
        image.raycastTarget = true;

        Button button = go.AddComponent<Button>();
        button.targetGraphic = image;

        Text(go, "Caption", 0f, 0f, w, 96f, ButtonSize, TextAlignmentOptions.Center,
            Color.white, font, caption);
        return button;
    }

    private static TextMeshProUGUI Text(GameObject parent, string name, float x, float y,
        float w, float h, float size, TextAlignmentOptions align, Color color,
        TMP_FontAsset font, string content)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(parent.transform, false);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
        rt.localScale = Vector3.one;

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font = font;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = align;
        tmp.text = content;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.enableAutoSizing = false;
        tmp.raycastTarget = false;   // 文字不吃射线，点击交给行/按钮自己的 Image
        return tmp;
    }
}
#endif
