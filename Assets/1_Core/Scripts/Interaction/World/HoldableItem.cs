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
        [SerializeField] private Vector3 heldPosition = new Vector3(0, 0, 0.55f);
        [SerializeField] private Vector3 heldEuler = new Vector3(-15, -20, 0);
        [SerializeField] private Vector3 placementSize = new Vector3(0.2f, 0.06f, 0.2f);
        [SerializeField] private UnityEvent onUse = new UnityEvent();
        private ItemCarryController owner;
        private Transform restingParent;
        private Vector3 restingPosition, restingScale, restingWorldScale;
        private Quaternion restingRotation;
        private Quaternion restingWorldRotation;
        private Vector3 placementOffset;
        private Vector3 measuredSize;
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
        public bool IsRolled => Use == HeldItemUse.Newspaper && Storage != null && Storage.IsContainer;
        public Vector3 RestingWorldScale => owner != null ? restingWorldScale : transform.lossyScale;
        public Quaternion PlacementRotation => use == HeldItemUse.Custom ? restingWorldRotation : Quaternion.identity;
        public Vector3 PlacementSize => PlacementSizeOn(Storage);
        public Vector3 PlacementSizeOn(ItemPlacementSurface surface)
        {
            if (use == HeldItemUse.Custom && measuredSize != Vector3.zero) return measuredSize;
            Vector3 scale = RestingWorldScale;
            return Vector3.Scale(use == HeldItemUse.Newspaper && surface != null && surface.IsContainer ?
                new Vector3(0.28f, 0.052f, 0.052f) : placementSize, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        }
        public Vector3 PlacementOffset => use == HeldItemUse.Custom ? placementOffset : Vector3.zero;
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
            restingWorldRotation = transform.rotation;
            bool found = false; Bounds bounds = default;
            if (use == HeldItemUse.Custom)
                foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
                {
                    if (!renderer.enabled) continue;
                    if (!found) { bounds = renderer.bounds; found = true; } else bounds.Encapsulate(renderer.bounds);
                }
            if (found) { measuredSize = bounds.size; placementOffset = bounds.center - transform.position; }
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
            transform.localPosition = heldPosition; transform.localRotation = Quaternion.Euler(heldEuler) * PlacementRotation;
            PoseChanged?.Invoke();
            FrameForInspection(hand.GetComponent<Camera>());
        }
        internal void FrameForInspection(Camera camera)
        {
            if (camera == null) return;
            Vector3 parentScale = transform.parent.lossyScale;
            transform.localScale = new Vector3(restingWorldScale.x / parentScale.x, restingWorldScale.y / parentScale.y, restingWorldScale.z / parentScale.z);
            float distance = Mathf.Max(camera.nearClipPlane + 0.25f, heldPosition.z);
            transform.localPosition = new Vector3(0, 0, distance);
            bool found = false; Bounds bounds = default;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                Bounds local = renderer.localBounds;
                for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 point = camera.transform.InverseTransformPoint(renderer.transform.TransformPoint(local.center + Vector3.Scale(local.extents, new Vector3(x, y, z))));
                    if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; } else bounds.Encapsulate(point);
                }
            }
            if (!found) return;
            float height = camera.orthographic ? camera.orthographicSize * 2 : 2 * distance * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
            float factor = Mathf.Min(height * 0.64f / Mathf.Max(0.001f, bounds.size.y), height * camera.aspect * 0.64f / Mathf.Max(0.001f, bounds.size.x));
            if (!camera.orthographic)
            {
                float vertical = Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f) * 0.64f, horizontal = vertical * camera.aspect;
                factor = Mathf.Min(distance * vertical / Mathf.Max(0.001f, bounds.extents.y + vertical * bounds.extents.z),
                    distance * horizontal / Mathf.Max(0.001f, bounds.extents.x + horizontal * bounds.extents.z));
            }
            // Keep the complete mesh in front of the near plane even for a deep object.
            factor = Mathf.Min(factor, (distance - camera.nearClipPlane - 0.02f) / Mathf.Max(0.001f, bounds.extents.z));
            transform.localScale *= factor;
            transform.localPosition = new Vector3(0, 0, distance) - (bounds.center - new Vector3(0, 0, distance)) * factor;
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
            transform.localRotation = PlacementRotation;
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
