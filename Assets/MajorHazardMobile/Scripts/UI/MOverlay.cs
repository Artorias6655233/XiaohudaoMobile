using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace LZW.Module.MajorHazard.Mobile.UI
{
    /// <summary>
    /// 页面之上的浮层：底部弹出的详情面板、确认对话框、选择列表、Toast。
    /// 同一时间只有一个对话框；安卓返回键先关闭对话框（见 <see cref="CloseTop"/>）。
    /// </summary>
    public class MOverlay : MonoBehaviour
    {
        public static MOverlay Instance { get; private set; }

        private RectTransform _root;
        private RectTransform _dialog;
        private RectTransform _toast;
        private Text _toastText;
        private Coroutine _toastRoutine;

        public bool HasDialog => _dialog != null;

        public static MOverlay Create(RectTransform parent)
        {
            var rect = MUI.Rect(parent, "Overlay");
            MUI.Stretch(rect);
            var overlay = rect.gameObject.AddComponent<MOverlay>();
            overlay._root = rect;
            Instance = overlay;
            return overlay;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// 关闭最上层的对话框，返回是否关闭了。
        /// </summary>
        public bool CloseTop()
        {
            if (_dialog == null)
                return false;
            Close();
            return true;
        }

        public void Close()
        {
            if (_dialog != null)
                Destroy(_dialog.gameObject);
            _dialog = null;
        }

        #region Toast

        public static void Toast(string message, float seconds = 2.5f)
        {
            if (Instance != null && !string.IsNullOrEmpty(message))
                Instance.ShowToast(message, seconds);
        }

        private void ShowToast(string message, float seconds)
        {
            if (_toast == null)
            {
                _toast = MUI.Rect(_root, "Toast");
                _toast.anchorMin = new Vector2(0.5f, 0f);
                _toast.anchorMax = new Vector2(0.5f, 0f);
                _toast.pivot = new Vector2(0.5f, 0f);
                _toast.anchoredPosition = new Vector2(0, 72);
                MUI.Image(_toast, new Color(0.05f, 0.1f, 0.16f, 0.94f), false, 20f);
                var layout = _toast.gameObject.AddComponent<HorizontalLayoutGroup>();
                layout.padding = new RectOffset(20, 20, 12, 12);
                layout.childControlWidth = layout.childControlHeight = true;
                var fitter = _toast.gameObject.AddComponent<ContentSizeFitter>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                _toastText = MUI.Text(_toast, "", MUI.FontBody, MUI.TextColor, TextAnchor.MiddleCenter);
                _toastText.horizontalOverflow = HorizontalWrapMode.Overflow;
                _toastText.verticalOverflow = VerticalWrapMode.Overflow;
            }

            _toast.SetAsLastSibling();
            _toast.gameObject.SetActive(true);
            _toastText.text = message;
            if (_toastRoutine != null)
                StopCoroutine(_toastRoutine);
            _toastRoutine = StartCoroutine(HideToast(seconds));
        }

        private IEnumerator HideToast(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            _toast.gameObject.SetActive(false);
            _toastRoutine = null;
        }

        #endregion

        #region 对话框

        /// <summary>
        /// 详情面板：手机竖屏从底部弹出，横屏 / 平板居中显示；点遮罩或「关闭」收起。
        /// </summary>
        public void ShowDetail(string title, string body, params (string label, Action action, Color color)[] actions)
        {
            var content = OpenDialog(title, true);
            var scroll = MUI.Scroll(content, out var sr, 6);
            MUI.Size(sr, -1, 0).flexibleHeight = 1;
            MUI.Paragraph(scroll, body, MUI.FontBody, MUI.TextColor);
            var buttons = MUI.Row(content);
            foreach (var (label, action, color) in actions)
                MUI.Button(buttons, label, () =>
                {
                    Close();
                    action?.Invoke();
                }, -1, MUI.ButtonHeight, color);
            MUI.Button(buttons, "关闭", Close, -1, MUI.ButtonHeight, MUI.ChipColor);
            FitHeight(sr, 0.7f);
        }

        /// <summary>
        /// 确认对话框（写平台数据前使用，代替 AR 端的「连续确认两次」）。
        /// </summary>
        public void Confirm(string title, string message, string confirmLabel, Action onConfirm, Color? confirmColor = null)
        {
            var content = OpenDialog(title, false);
            MUI.Paragraph(content, message, MUI.FontBody, MUI.TextColor);
            var buttons = MUI.Row(content);
            MUI.Button(buttons, "取消", Close, -1, MUI.ButtonHeight, MUI.ChipColor);
            MUI.Button(buttons, confirmLabel, () =>
            {
                Close();
                onConfirm?.Invoke();
            }, -1, MUI.ButtonHeight, confirmColor ?? MUI.ConfirmColor);
        }

        /// <summary>
        /// 带搜索框的选择列表（选危险源等）。
        /// </summary>
        public void Pick<T>(string title, IReadOnlyList<T> items, Func<T, string> label, Action<T> onPick, Func<T, string> subLabel = null)
        {
            var content = OpenDialog(title, true);
            RectTransform list = null;
            ScrollRect sr = null;

            void Fill(string keyword)
            {
                MUI.Clear(list);
                keyword = (keyword ?? "").Trim();
                var shown = items.Where(i => keyword.Length == 0 || (label(i) ?? "").Contains(keyword)
                                                                   || subLabel != null && (subLabel(i) ?? "").Contains(keyword)).Take(200).ToList();
                if (shown.Count == 0)
                    MUI.Text(list, "没有匹配的项", MUI.FontBody, MUI.DimTextColor, TextAnchor.MiddleCenter, 60);
                foreach (var item in shown)
                {
                    var card = MUI.Card(list, () =>
                    {
                        Close();
                        onPick(item);
                    }, null, 10);
                    MUI.Paragraph(card.transform, label(item), MUI.FontBody, MUI.TextColor);
                    var sub = subLabel?.Invoke(item);
                    if (!string.IsNullOrEmpty(sub))
                        MUI.Paragraph(card.transform, sub, MUI.FontSmall, MUI.DimTextColor);
                }
                sr.verticalNormalizedPosition = 1f;
            }

            MUI.Search(content, "输入关键词搜索", Fill);
            list = MUI.Scroll(content, out sr, 6);
            MUI.Size(sr, -1, 0).flexibleHeight = 1;
            MUI.Button(content, "取消", Close, -1, MUI.ButtonHeight, MUI.ChipColor);
            Fill("");
            FitHeight(sr, 0.8f, true);
        }

        /// <summary>
        /// 建立遮罩 + 对话框外壳，返回内容容器（竖排）。
        /// </summary>
        private RectTransform OpenDialog(string title, bool sheet)
        {
            Close();
            var mask = MUI.Rect(_root, "Dialog");
            MUI.Stretch(mask);
            var maskImage = MUI.Image(mask, new Color(0, 0, 0, 0.55f), true);
            var maskButton = mask.gameObject.AddComponent<Button>();
            maskButton.targetGraphic = maskImage;
            maskButton.transition = Selectable.Transition.None;
            maskButton.onClick.AddListener(Close);
            _dialog = mask;

            var portrait = _root.rect.height > _root.rect.width;
            var box = MUI.Rect(mask, "Box");
            if (sheet && portrait)
            {
                // 竖屏：底部弹出，宽度铺满
                box.anchorMin = new Vector2(0f, 0f);
                box.anchorMax = new Vector2(1f, 0f);
                box.pivot = new Vector2(0.5f, 0f);
                box.offsetMin = new Vector2(0, 0);
                box.offsetMax = new Vector2(0, 0);
            }
            else
            {
                box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
                box.pivot = new Vector2(0.5f, 0.5f);
                box.sizeDelta = new Vector2(Mathf.Min(560f, _root.rect.width - 32f), 0);
            }
            MUI.Image(box, MUI.PanelColor, true, 14f); // 接收射线：点对话框内部不关闭
            var layout = MUI.Vertical(box, 10, 18);
            layout.padding.bottom = 20;
            box.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            if (sheet && portrait)
            {
                var handle = MUI.Rect(box, "Handle");
                MUI.Size(handle, -1, 6);
                var bar = MUI.Rect(handle, "Bar");
                bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0.5f);
                bar.sizeDelta = new Vector2(48, 5);
                MUI.Image(bar, new Color(1, 1, 1, 0.3f), false, 2.5f);
            }

            var titleText = MUI.Paragraph(box, title, MUI.FontTitle, MUI.TextColor);
            titleText.fontStyle = FontStyle.Bold;
            return box;
        }

        /// <summary>
        /// 对话框里的滚动区高度：按内容自适应，最多占屏幕高度的 maxShare。
        /// </summary>
        private void FitHeight(ScrollRect scroll, float maxShare, bool fixedMax = false)
        {
            StartCoroutine(FitNextFrame(scroll, maxShare, fixedMax));
        }

        private IEnumerator FitNextFrame(ScrollRect scroll, float maxShare, bool fixedMax)
        {
            var le = scroll.GetComponent<LayoutElement>();
            var max = _root.rect.height * maxShare - 140f;
            le.preferredHeight = le.minHeight = fixedMax ? max : 40f;
            yield return null;
            if (scroll == null || fixedMax)
                yield break;
            var content = scroll.content;
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            var height = Mathf.Min(content.rect.height, max);
            le.preferredHeight = le.minHeight = Mathf.Max(40f, height);
        }

        #endregion
    }
}
