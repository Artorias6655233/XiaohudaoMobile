using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LZW.Module.MajorHazard.Mobile.UI
{
    /// <summary>
    /// 移动端数据表格：固定表头 + 手指上下滑动的表体（惯性滚动），点一行触发回调并高亮。
    /// 代替 AR 端挂 RkGesUITransformer 的分页表格（捏合拖动 / 握拳缩放）：手机上直接滑动浏览，不再需要翻页和缩放。
    /// 设置 <see cref="onReachEnd"/> 后滑到底部自动加载下一页（服务端分页）。
    /// </summary>
    public class MTable
    {
        public struct Column
        {
            public string title;
            public float width; // 0 = 按权重分配剩余宽度
            public float flex;
            public TextAnchor align;

            public Column(string title, float width, float flex, TextAnchor align)
            {
                this.title = title;
                this.width = width;
                this.flex = flex;
                this.align = align;
            }
        }

        public struct Cell
        {
            public string text;
            public Color color;

            public Cell(string text, Color? color = null)
            {
                this.text = text;
                this.color = color ?? MUI.TextColor;
            }

            public static implicit operator Cell(string text) => new Cell(text);
        }

        private static readonly Color HeaderColor = new Color(0.06f, 0.24f, 0.42f, 1f);
        private static readonly Color RowColor = new Color(0.04f, 0.12f, 0.22f, 1f);
        private static readonly Color RowAltColor = new Color(0.06f, 0.16f, 0.28f, 1f);
        private static readonly Color SelectedColor = new Color(0.1f, 0.42f, 0.6f, 1f);

        public RectTransform Root { get; }

        /// <summary>滑到底部时回调（加载下一页）。</summary>
        public Action onReachEnd;

        private readonly Column[] _columns;
        private readonly float _rowHeight;
        private readonly RectTransform _body;
        private readonly ScrollRect _scroll;
        private readonly Text _footer;
        private readonly List<(object key, Image background, int index)> _rows = new List<(object, Image, int)>();
        private object _selected;
        private bool _endNotified;

        public MTable(Transform parent, Column[] columns, float rowHeight = 52f)
        {
            _columns = columns;
            _rowHeight = rowHeight;

            Root = MUI.Rect(parent, "Table");
            MUI.Image(Root, MUI.PanelColor, false, 10f);
            MUI.Vertical(Root, 0, 0).padding = new RectOffset(0, 0, 0, 6);

            var header = CreateRow(Root, HeaderColor);
            for (var i = 0; i < columns.Length; i++)
                AddCell(header, i, new Cell(columns[i].title, MUI.AccentColor)).fontStyle = FontStyle.Bold;

            _body = MUI.Scroll(Root, out _scroll, 2, 0);
            MUI.Size(_scroll, -1, 0).flexibleHeight = 1;
            _scroll.onValueChanged.AddListener(OnScrolled);

            _footer = MUI.Text(Root, "", MUI.FontSmall, MUI.DimTextColor, TextAnchor.MiddleCenter, 26);
        }

        /// <summary>是否停在顶部（自动刷新只在顶部进行，避免正在看的行跳走）。</summary>
        public bool AtTop => _scroll.content.rect.height <= _scroll.viewport.rect.height + 1f || _scroll.verticalNormalizedPosition > 0.98f;

        public string Footer
        {
            set => _footer.text = value ?? "";
        }

        public object Selected => _selected;

        /// <summary>
        /// 清空并显示一行提示（加载中 / 暂无数据）。
        /// </summary>
        public void ShowMessage(string message)
        {
            Clear();
            MUI.Text(_body, message, MUI.FontBody, MUI.DimTextColor, TextAnchor.MiddleCenter, _rowHeight * 2);
        }

        public void Clear()
        {
            MUI.Clear(_body);
            _rows.Clear();
            _endNotified = false;
        }

        public void ScrollToTop()
        {
            _scroll.StopMovement();
            _scroll.verticalNormalizedPosition = 1f;
        }

        /// <summary>
        /// 追加一行。key 用于选中高亮（同一个 key 视为同一行）。
        /// </summary>
        public void AddRow(object key, IReadOnlyList<Cell> cells, Action onClick)
        {
            var index = _rows.Count;
            var row = CreateRow(_body, Background(key, index));
            for (var i = 0; i < _columns.Length && i < cells.Count; i++)
                AddCell(row, i, cells[i]);

            var image = row.GetComponent<Image>();
            image.raycastTarget = true;
            var button = row.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = button.colors;
            colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
            colors.fadeDuration = 0.05f;
            button.colors = colors;
            button.onClick.AddListener(() =>
            {
                Select(key);
                onClick?.Invoke();
            });
            _rows.Add((key, image, index));
            _endNotified = false;
        }

        public void Select(object key)
        {
            _selected = key;
            foreach (var (rowKey, background, index) in _rows)
                if (background != null)
                    background.color = Background(rowKey, index);
        }

        /// <summary>
        /// 滚动使指定行可见。
        /// </summary>
        public void ScrollTo(object key)
        {
            var i = _rows.FindIndex(r => Equals(r.key, key));
            if (i < 0)
                return;
            Canvas.ForceUpdateCanvases();
            var contentHeight = _scroll.content.rect.height;
            var viewHeight = _scroll.viewport.rect.height;
            if (contentHeight <= viewHeight)
                return;
            var y = i * (_rowHeight + 2f);
            _scroll.StopMovement();
            _scroll.verticalNormalizedPosition = 1f - Mathf.Clamp01((y - viewHeight / 3f) / (contentHeight - viewHeight));
        }

        private void OnScrolled(Vector2 position)
        {
            if (onReachEnd == null || _rows.Count == 0)
                return;
            var contentHeight = _scroll.content.rect.height;
            var viewHeight = _scroll.viewport.rect.height;
            var atEnd = contentHeight <= viewHeight || position.y < 0.02f;
            if (atEnd && !_endNotified)
            {
                _endNotified = true;
                onReachEnd();
            }
            else if (!atEnd)
            {
                _endNotified = false;
            }
        }

        private Color Background(object key, int index)
        {
            return key != null && Equals(key, _selected) ? SelectedColor : index % 2 == 0 ? RowColor : RowAltColor;
        }

        private RectTransform CreateRow(Transform parent, Color color)
        {
            var row = MUI.Rect(parent, "Row");
            MUI.Image(row, color);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6;
            layout.padding = new RectOffset(8, 8, 2, 2);
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            MUI.Size(row, -1, parent == Root ? 40f : _rowHeight);
            return row;
        }

        private Text AddCell(RectTransform row, int column, Cell cell)
        {
            var c = _columns[column];
            var text = MUI.Text(row, cell.text ?? "—", MUI.FontSmall + 1, cell.color, c.align);
            // 固定列用首选宽度；弹性列按权重分剩余宽度（最多两行，超出截断）
            var le = MUI.Size(text, c.width > 0 ? c.width : 0, -1, c.width > 0 ? 0 : c.flex);
            le.minWidth = c.width > 0 ? c.width : 0;
            return text;
        }
    }
}
