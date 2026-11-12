using UnityEngine;
using UnityEngine.Events;

namespace Codetective.Interaction
{
    public enum HeldItemUse { Custom, Newspaper, Profile }

    [DisallowMultipleComponent]
    public sealed class HoldableItem : InspectionActionTarget
    {
        [SerializeField] private string itemId;
        [SerializeField] private string displayName = "물건";
        [SerializeField] private HeldItemUse use;
        [SerializeField] private string profileId;
        [SerializeField] private Vector3 heldPosition = new Vector3(0, -0.12f, 0.55f);
        [SerializeField] private Vector3 heldEuler = new Vector3(-70, 0, 0);
        [SerializeField] private UnityEvent onUse = new UnityEvent();
        private ItemCarryController owner;
        private Transform restingParent;
        private Vector3 restingPosition, restingScale;
        private Quaternion restingRotation;
        private Collider[] colliders;
        private bool[] colliderEnabled;
        private Rigidbody[] bodies;
        private bool[] kinematic;
        private Renderer[] renderers;
        private bool[] rendererEnabled;
        public string Id => itemId;
        public string DisplayName => displayName;
        public HeldItemUse Use => use;
        public string ProfileId => profileId;
        public bool IsHeld => owner != null && owner.HeldItem == this;

        public void Configure(string id, string label, HeldItemUse action, string personId = "")
        { itemId = id; displayName = label; use = action; profileId = personId; }
        public void Register(ItemCarryController controller)
        {
            if (owner != null) return;
            owner = controller;
            restingParent = transform.parent; restingPosition = transform.localPosition;
            restingRotation = transform.localRotation; restingScale = transform.localScale;
        }
        public override bool CanInteract(string angleId) => isActiveAndEnabled && owner != null && !IsHeld && owner.HeldItem == null;
        public override void Interact() { if (CanInteract(null)) owner.TryPickup(this); }
        internal void PickUp(Transform hand)
        {
            colliders = GetComponentsInChildren<Collider>(true); colliderEnabled = new bool[colliders.Length];
            for (int i = 0; i < colliders.Length; i++) { colliderEnabled[i] = colliders[i].enabled; colliders[i].enabled = false; }
            bodies = GetComponentsInChildren<Rigidbody>(true); kinematic = new bool[bodies.Length];
            for (int i = 0; i < bodies.Length; i++) { kinematic[i] = bodies[i].isKinematic; bodies[i].isKinematic = true; }
            renderers = GetComponentsInChildren<Renderer>(true); rendererEnabled = new bool[renderers.Length];
            for (int i = 0; i < renderers.Length; i++) rendererEnabled[i] = renderers[i].enabled;
            transform.SetParent(hand, true);
            transform.localPosition = heldPosition; transform.localRotation = Quaternion.Euler(heldEuler);
        }
        internal void PutDown()
        {
            transform.SetParent(restingParent, false); transform.localPosition = restingPosition;
            transform.localRotation = restingRotation; transform.localScale = restingScale;
            if (colliders != null) for (int i = 0; i < colliders.Length; i++) if (colliders[i] != null) colliders[i].enabled = colliderEnabled[i];
            if (bodies != null) for (int i = 0; i < bodies.Length; i++) if (bodies[i] != null) bodies[i].isKinematic = kinematic[i];
            SetPresentationVisible(true);
        }
        internal void SetPresentationVisible(bool visible)
        { if (renderers != null) for (int i = 0; i < renderers.Length; i++) if (renderers[i] != null) renderers[i].enabled = visible && rendererEnabled[i]; }
        internal void InvokeUse() => onUse?.Invoke();
    }
}
