using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LZW.Module.MajorHazard.Mobile.UI
{
    /// <summary>
    /// 单选标签组（横向可滑动），代替 AR 端「筛选：xxx ▸」循环切换按钮：选项一目了然，点一下即选中。
    /// </summary>
    public class ChipGroup
    {
        public int Selected { get; private set; }

        public event Action<int> Changed;

        private readonly List<Button> _chips = new List<Button>();
        private readonly Color _selectedColor;

        /// <param name="colors">各选项选中时的颜色，为空用强调色</param>
        public ChipGroup(Transform parent, IReadOnlyList<string> labels, int selected = 0, IReadOnlyList<Color> colors = null, float height = 38f)
        {
            _selectedColor = MUI.ButtonColor;
            var row = MUI.HorizontalScroll(parent, height, 6);
            for (var i = 0; i < labels.Count; i++)
            {
                var index = i;
                var chip = MUI.Button(row, labels[i], () => Select(index, true), -1, height, MUI.ChipColor, MUI.FontSmall + 1);
                // 宽度按文字
                var le = chip.GetComponent<LayoutElement>();
                le.flexibleWidth = 0;
                le.preferredWidth = Mathf.Max(56f, labels[i].Length * (MUI.FontSmall + 1) + 28f);
                _chips.Add(chip);
                if (colors != null && i < colors.Count)
                    _colors[i] = colors[i];
            }
            Select(selected, false);
        }

        private readonly Dictionary<int, Color> _colors = new Dictionary<int, Color>();

        public void Select(int index, bool notify)
        {
            Selected = Mathf.Clamp(index, 0, _chips.Count - 1);
            for (var i = 0; i < _chips.Count; i++)
            {
                var on = i == Selected;
                var color = on ? _colors.TryGetValue(i, out var c) ? c * 0.8f + new Color(0, 0, 0, 0.2f) : _selectedColor : MUI.ChipColor;
                MUI.SetColor(_chips[i], color);
                var text = _chips[i].GetComponentInChildren<Text>();
                text.fontStyle = on ? FontStyle.Bold : FontStyle.Normal;
                text.color = on ? Color.white : MUI.DimTextColor;
            }
            if (notify)
                Changed?.Invoke(Selected);
        }
    }
}
