using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    public sealed class CrtScrollbar : Scrollbar
    {
        private CrtScreen screen;
        private PointerEventData projected;
        private CrtScreen Screen => screen != null ? screen : screen = GetComponentInParent<CrtScreen>();

        public override void OnPointerDown(PointerEventData eventData)
        {
            using (new CrtPointerProjection(eventData, Screen))
            {
                CopyPointer(eventData);
                base.OnPointerDown(projected);
                if (projected.used) eventData.Use();
            }
        }

        public override void OnBeginDrag(PointerEventData eventData)
        {
            using (new CrtPointerProjection(eventData, Screen))
            {
                CopyPointer(eventData);
                base.OnBeginDrag(projected);
            }
        }

        public override void OnDrag(PointerEventData eventData)
        {
            using (new CrtPointerProjection(eventData, Screen))
            {
                CopyPointer(eventData);
                base.OnDrag(projected);
                if (projected.used) eventData.Use();
            }
        }

        private void CopyPointer(PointerEventData source)
        {
            // Older uGUI versions retain this event in their track-click coroutine.
            if (projected == null)
                projected = new PointerEventData(EventSystem.current);
            projected.Reset();
            projected.position = source.position;
            projected.pressPosition = source.pressPosition;
            projected.delta = source.delta;
            projected.button = source.button;
            projected.pointerId = source.pointerId;
            projected.pointerCurrentRaycast = source.pointerCurrentRaycast;
            projected.pointerPressRaycast = source.pointerPressRaycast;
        }
    }
}
