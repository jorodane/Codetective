using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    public sealed class CrtScrollRect : ScrollRect
    {
        private CrtScreen screen;
        private CrtScreen Screen => screen != null ? screen : screen = GetComponentInParent<CrtScreen>();

        public override void OnBeginDrag(PointerEventData eventData)
        {
            using (new CrtPointerProjection(eventData, Screen))
                base.OnBeginDrag(eventData);
        }

        public override void OnDrag(PointerEventData eventData)
        {
            using (new CrtPointerProjection(eventData, Screen))
                base.OnDrag(eventData);
        }
    }
}
