using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LZW.Module.MajorHazard.Mobile.UI
{
    /// <summary>
    /// 可嵌套的 ScrollRect：拖动方向与自己的滚动方向不一致时（如在横向标签行上竖着滑），把拖动交给外层的滚动区域，
    /// 否则内层会吞掉拖动，外层滑不动。
    /// </summary>
    public class NestedScrollRect : ScrollRect
    {
        private ScrollRect _parent;
        private bool _routeToParent;

        protected override void Awake()
        {
            base.Awake();
            if (transform.parent != null)
                _parent = transform.parent.GetComponentInParent<ScrollRect>();
        }

        public override void OnInitializePotentialDrag(PointerEventData eventData)
        {
            if (_parent != null)
                _parent.OnInitializePotentialDrag(eventData);
            base.OnInitializePotentialDrag(eventData);
        }

        public override void OnBeginDrag(PointerEventData eventData)
        {
            var delta = eventData.delta;
            var mostlyHorizontal = Mathf.Abs(delta.x) > Mathf.Abs(delta.y);
            _routeToParent = _parent != null && (horizontal && !vertical && !mostlyHorizontal || vertical && !horizontal && mostlyHorizontal);
            if (_routeToParent)
                _parent.OnBeginDrag(eventData);
            else
                base.OnBeginDrag(eventData);
        }

        public override void OnDrag(PointerEventData eventData)
        {
            if (_routeToParent)
                _parent.OnDrag(eventData);
            else
                base.OnDrag(eventData);
        }

        public override void OnEndDrag(PointerEventData eventData)
        {
            if (_routeToParent)
                _parent.OnEndDrag(eventData);
            else
                base.OnEndDrag(eventData);
            _routeToParent = false;
        }

        public override void OnScroll(PointerEventData data)
        {
            // 鼠标滚轮：横向行不处理竖向滚轮
            if (_parent != null && horizontal && !vertical)
                _parent.OnScroll(data);
            else
                base.OnScroll(data);
        }
    }
}
