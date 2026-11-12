using System;
using UnityEngine;

namespace Codetective.Interaction
{
    /// <summary>One inspected object at a time; put down restores its authored desk location.</summary>
    [DisallowMultipleComponent]
    public sealed class ItemCarryController : MonoBehaviour
    {
        [SerializeField] private Transform hand;
        public HoldableItem HeldItem { get; private set; }
        public event Action<HoldableItem> Used;
        public void Initialize(Transform camera)
        {
            hand = camera;
            foreach (HoldableItem item in FindObjectsOfType<HoldableItem>(true)) item.Register(this);
        }
        public void Register(HoldableItem item) => item.Register(this);
        public void SetPresentationVisible(bool visible) { if (HeldItem != null) HeldItem.SetPresentationVisible(visible); }
        public bool TryPickup(HoldableItem item)
        {
            if (hand == null || HeldItem != null || item == null || !item.isActiveAndEnabled) return false;
            item.Register(this); HeldItem = item; item.PickUp(hand); return true;
        }
        public bool TryUse(bool textInputFocused)
        {
            if (textInputFocused || HeldItem == null) return false;
            HoldableItem item = HeldItem;
            item.InvokeUse(); Used?.Invoke(item); return true;
        }
        public void PutDown()
        {
            if (HeldItem == null) return;
            HoldableItem item = HeldItem; HeldItem = null; item.PutDown();
        }
        private void OnDisable() => PutDown();
    }
}
