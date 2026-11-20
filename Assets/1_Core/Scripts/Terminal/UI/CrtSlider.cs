using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    /// <summary>A single horizontal setting with pointer coordinates projected through the CRT.</summary>
    public sealed class CrtSlider : Slider
    {
        public RectTransform Track { get; set; }
        private CrtScreen screen;
        private bool draggingTrack;
        private CrtScreen Screen => screen != null ? screen : screen = GetComponentInParent<CrtScreen>();
        public override void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !IsActive() || !IsInteractable()) return;
            using (new CrtPointerProjection(eventData, Screen))
            {
                draggingTrack = Track != null && RectTransformUtility.RectangleContainsScreenPoint(Track, eventData.position, eventData.pressEventCamera);
                if (draggingTrack) base.OnPointerDown(eventData); else Select();
            }
        }
        public override void OnDrag(PointerEventData eventData)
        { if (draggingTrack) using (new CrtPointerProjection(eventData, Screen)) base.OnDrag(eventData); }
        public override void OnPointerUp(PointerEventData eventData)
        { draggingTrack = false; base.OnPointerUp(eventData); }
    }
}
