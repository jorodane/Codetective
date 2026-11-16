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
        [SerializeField] private Vector3 placementSize = new Vector3(0.2f, 0.06f, 0.2f);
        [SerializeField] private UnityEvent onUse = new UnityEvent();
        private ItemCarryController owner;
        private Transform restingParent;
        private Vector3 restingPosition, restingScale, restingWorldScale;
        private Quaternion restingRotation;
        private Collider[] colliders;
        private bool[] colliderEnabled;
        private Rigidbody[] bodies;
        private bool[] kinematic;
        private Renderer[] renderers;
        private bool[] rendererEnabled;
        private CanvasGroup[] canvasGroups;
        private float[] canvasAlpha;
        private bool[] canvasInteractable, canvasRaycasts;
        public string Id => itemId;
        public string DisplayName => displayName;
        public HeldItemUse Use => use;
        public string ProfileId => profileId;
        public bool IsHeld => owner != null && owner.HeldItem == this;
        public ItemPlacementSurface Storage { get; private set; }
        public bool IsStored => Storage != null;
        public Vector3 PlacementSize
        {
            get
            {
                Vector3 scale = owner != null ? restingWorldScale : transform.lossyScale;
                return Vector3.Scale(use == HeldItemUse.Newspaper ? new Vector3(0.28f, 0.052f, 0.052f) : placementSize,
                    new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            }
        }
        public event System.Action PoseChanged;

        public void Configure(string id, string label, HeldItemUse action, string personId = "")
        { itemId = id; displayName = label; use = action; profileId = personId; }
        public void ConfigurePaper(Vector3 size)
        { placementSize = size; heldPosition = new Vector3(0, -0.02f, 0.55f); heldEuler = new Vector3(-90, 0, 0); }
        public void Register(ItemCarryController controller)
        {
            if (owner != null) return;
            owner = controller;
            restingParent = transform.parent; restingPosition = transform.localPosition;
            restingRotation = transform.localRotation; restingScale = transform.localScale;
            restingWorldScale = transform.lossyScale;
        }
        public override bool CanInteract(string angleId) => isActiveAndEnabled && owner != null && !IsHeld && owner.HeldItem == null && (Storage == null || Storage.CanRetrieve);
        public override void Interact() { if (CanInteract(null)) owner.TryPickup(this); }
        internal void PickUp(Transform hand)
        {
            ReleaseStorage();
            colliders = GetComponentsInChildren<Collider>(true); colliderEnabled = new bool[colliders.Length];
            for (int i = 0; i < colliders.Length; i++) { colliderEnabled[i] = colliders[i].enabled; colliders[i].enabled = false; }
            bodies = GetComponentsInChildren<Rigidbody>(true); kinematic = new bool[bodies.Length];
            for (int i = 0; i < bodies.Length; i++) { kinematic[i] = bodies[i].isKinematic; bodies[i].isKinematic = true; }
            renderers = GetComponentsInChildren<Renderer>(true); rendererEnabled = new bool[renderers.Length];
            for (int i = 0; i < renderers.Length; i++) rendererEnabled[i] = renderers[i].enabled;
            canvasGroups = GetComponentsInChildren<CanvasGroup>(true);
            canvasAlpha = new float[canvasGroups.Length]; canvasInteractable = new bool[canvasGroups.Length]; canvasRaycasts = new bool[canvasGroups.Length];
            for (int i = 0; i < canvasGroups.Length; i++)
            { canvasAlpha[i] = canvasGroups[i].alpha; canvasInteractable[i] = canvasGroups[i].interactable; canvasRaycasts[i] = canvasGroups[i].blocksRaycasts; }
            transform.SetParent(hand, true);
            transform.localPosition = heldPosition; transform.localRotation = Quaternion.Euler(heldEuler);
            PoseChanged?.Invoke();
        }
        internal void PutDown()
        {
            ReleaseStorage();
            transform.SetParent(restingParent, false); transform.localPosition = restingPosition;
            transform.localRotation = restingRotation; transform.localScale = restingScale;
            if (colliders != null) for (int i = 0; i < colliders.Length; i++) if (colliders[i] != null) colliders[i].enabled = colliderEnabled[i];
            if (bodies != null) for (int i = 0; i < bodies.Length; i++) if (bodies[i] != null) bodies[i].isKinematic = kinematic[i];
            SetPresentationVisible(true);
            PoseChanged?.Invoke();
        }
        internal void Store(ItemPlacementSurface surface, Vector3 position)
        {
            transform.SetParent(surface.transform, false); transform.localPosition = position;
            transform.localRotation = Quaternion.identity;
            Vector3 parentScale = surface.transform.lossyScale;
            transform.localScale = new Vector3(restingWorldScale.x / parentScale.x, restingWorldScale.y / parentScale.y, restingWorldScale.z / parentScale.z);
            if (colliders != null) for (int i = 0; i < colliders.Length; i++) if (colliders[i] != null) colliders[i].enabled = colliderEnabled[i];
            Storage = surface; SetPresentationVisible(true); PoseChanged?.Invoke();
        }
        private void ReleaseStorage() { if (Storage != null) Storage.Remove(this); Storage = null; }
        internal void SetPresentationVisible(bool visible)
        {
            if (renderers != null) for (int i = 0; i < renderers.Length; i++) if (renderers[i] != null) renderers[i].enabled = visible && rendererEnabled[i];
            if (canvasGroups != null) for (int i = 0; i < canvasGroups.Length; i++) if (canvasGroups[i] != null)
            { canvasGroups[i].alpha = visible ? canvasAlpha[i] : 0; canvasGroups[i].interactable = visible && canvasInteractable[i]; canvasGroups[i].blocksRaycasts = visible && canvasRaycasts[i]; }
        }
        internal void InvokeUse() => onUse?.Invoke();
    }
}
