using System;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    /// <summary>A single click selects; double click or keyboard submit loads that same record.</summary>
    public sealed class SystemSaveRow : Button
    {
        public Action Activated, Focused;
        public override void OnSelect(BaseEventData eventData)
        { base.OnSelect(eventData); if (IsActive() && IsInteractable()) Focused?.Invoke(); }
        public override void OnPointerClick(PointerEventData eventData)
        {
            base.OnPointerClick(eventData);
            if (eventData.button == PointerEventData.InputButton.Left && eventData.clickCount == 2 && IsActive() && IsInteractable()) Activated?.Invoke();
        }
        public override void OnSubmit(BaseEventData eventData)
        { if (IsActive() && IsInteractable()) { onClick.Invoke(); Activated?.Invoke(); } }
    }
}
