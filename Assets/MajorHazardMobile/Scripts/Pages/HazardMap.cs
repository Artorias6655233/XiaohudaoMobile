using LZW.Module.MajorHazard.Mobile.UI;
using LZW.Module.Map3DViewer.Scripts;
using UnityEngine;
using UnityEngine.UI;

namespace LZW.Module.MajorHazard.Mobile.Pages
{
    /// <summary>
    /// 在页面主区域放一个三维地图（<see cref="Map3DViewer"/>，小虎岛模型），右下角浮动「放大 / 缩小 / 复位」按钮，
    /// 左下角一行操作提示。智能监测预警和三维地图查看共用。
    /// </summary>
    public static class HazardMap
    {
        public static Map3DViewer.Scripts.Map3DViewer Create(RectTransform parent, GameObject model, string hint)
        {
            var frame = MUI.Rect(parent, "MapFrame");
            MUI.Stretch(frame);
            MUI.Image(frame, MUI.PanelColor, false, 12f);

            if (model == null)
            {
                var warn = MUI.Paragraph(frame, "未配置三维模型（菜单 Tools/MajorHazard/Setup Mobile Scene）", MUI.FontBody, MUI.WarnColor);
                MUI.Stretch(warn.rectTransform, 16, 16, 16, 16);
                return null;
            }

            // 先建未激活的物体：Awake / OnEnable 要在设置模型之后执行
            var go = new GameObject("Map3D-Viewer", typeof(RectTransform)) { layer = parent.gameObject.layer };
            go.SetActive(false);
            var rect = (RectTransform)go.transform;
            rect.SetParent(frame, false);
            MUI.Stretch(rect, 2, 2, 2, 2);
            // 透明的接收射线底图：触摸从地图上开始时才转动地图（见 Map3DTouchControl.StartsOnMap）
            MUI.Image(rect, new Color(0, 0, 0, 0), true);
            go.AddComponent<Map3DTouchControl>();
            var map = go.AddComponent<Map3DViewer.Scripts.Map3DViewer>();
            map.Model = model;
            map.PoiFont = MUI.Font;
            go.SetActive(true);

            // 浮动按钮（在地图物体之外，不会被当成地图触摸）
            var tools = MUI.Rect(frame, "MapTools");
            tools.anchorMin = tools.anchorMax = tools.pivot = new Vector2(1f, 0f);
            tools.anchoredPosition = new Vector2(-10f, 10f);
            var layout = MUI.Vertical(tools, 8);
            layout.childForceExpandWidth = false;
            tools.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            tools.sizeDelta = new Vector2(48f, 0f);
            var toolColor = new Color(0.05f, 0.16f, 0.28f, 0.92f);
            MUI.Button(tools, "＋", () => map.Orbit.Nudge(0, 0, 1.35f), 48, 48, toolColor, MUI.FontLarge);
            MUI.Button(tools, "－", () => map.Orbit.Nudge(0, 0, 1f / 1.35f), 48, 48, toolColor, MUI.FontLarge);
            MUI.Button(tools, "复位", map.ResetView, 48, 48, toolColor, MUI.FontSmall);

            if (!string.IsNullOrEmpty(hint))
            {
                var tip = MUI.Text(frame, hint, MUI.FontSmall - 1, new Color(0.75f, 0.85f, 0.95f, 0.8f), TextAnchor.LowerLeft);
                tip.rectTransform.anchorMin = new Vector2(0f, 0f);
                tip.rectTransform.anchorMax = new Vector2(1f, 0f);
                tip.rectTransform.pivot = new Vector2(0f, 0f);
                tip.rectTransform.offsetMin = new Vector2(12f, 8f);
                tip.rectTransform.offsetMax = new Vector2(-70f, 40f);
                tip.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.7f);
            }
            return map;
        }
    }
}
