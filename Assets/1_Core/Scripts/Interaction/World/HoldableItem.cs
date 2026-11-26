using UnityEngine;
using UnityEngine.Events;

namespace Codetective.Interaction
{
    public enum HeldItemUse { Custom, Newspaper, Profile }

    [DisallowMultipleComponent]
    public sealed class HoldableItem : InspectionActionTarget
    {
        public static readonly Vector3 NewspaperRollSize = new Vector3(0.28f, 0.052f, 0.052f);
        public const float InspectionLaneWidth = 0.64f;
        private const float LiftSeconds = 0.28f;

        [SerializeField] private string itemId;
        [SerializeField] private string displayName = "물건";
        [SerializeField, TextArea] private string description;
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
        private ItemPlacementSurface pickupSurface;
        private Transform pickupParent;
        private Vector3 pickupPosition, pickupScale;
        private Quaternion pickupRotation;
        private int pickupDrawerSlot;
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
        private Vector3 liftPosition, liftScale;
        private Quaternion liftRotation, inspectionFront;
        private Vector2 inspectionAngles;
        private float liftElapsed;
        private Bounds inspectionBounds;
        public string Id => itemId;
        public string DisplayName => displayName;
        public string Description => description;
        public bool HasUseAction => onUse != null && onUse.GetPersistentEventCount() > 0;
        public HeldItemUse Use => use;
        public string ProfileId => profileId;
        public bool IsHeld => owner != null && owner.HeldItem == this;
        public ItemPlacementSurface Storage { get; private set; }
        public int DrawerSlot { get; private set; }
        internal ItemPlacementSurface PickupSurface => pickupSurface;
        public bool IsStored => Storage != null;
        public bool IsRolled => Use == HeldItemUse.Newspaper && (!IsHeld || Storage != null);
        public Transform PlacementVisual { get; private set; }
        public Vector3 RestingWorldScale => owner != null ? restingWorldScale : transform.lossyScale;
        public Quaternion PlacementRotation => use == HeldItemUse.Custom ? restingWorldRotation : Quaternion.identity;
        public Vector3 PlacementSize
        {
            get
            {
                if (use == HeldItemUse.Custom && measuredSize != Vector3.zero) return measuredSize;
                Vector3 scale = RestingWorldScale;
                return Vector3.Scale(use == HeldItemUse.Newspaper ? NewspaperRollSize : placementSize,
                    new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            }
        }
        public Vector3 PlacementOffset => use == HeldItemUse.Custom ? placementOffset : Vector3.zero;
        public event System.Action PoseChanged;

        public void Configure(string id, string label, HeldItemUse action, string personId = "", string description = null)
        { itemId = id; displayName = label; use = action; profileId = personId; if (description != null) this.description = description; }
        public void ConfigurePaper(Vector3 size)
        { placementSize = size; heldPosition = new Vector3(0, -0.02f, 0.55f); heldEuler = new Vector3(-90, 0, 0); }
        public void SetPlacementVisual(Transform visual) => PlacementVisual = visual;
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
        public override bool CanInteract(string angleId) => isActiveAndEnabled && owner != null && owner.CanManipulate && !IsHeld && owner.HeldItem == null && (Storage == null || Storage.CanRetrieve);
        public override void Interact() { if (CanInteract(null)) owner.TryPickup(this); }
        internal void PickUp(Transform hand)
        {
            pickupSurface = Storage;
            pickupParent = transform.parent;
            pickupPosition = transform.localPosition;
            pickupRotation = transform.localRotation;
            pickupScale = transform.localScale;
            pickupDrawerSlot = DrawerSlot;
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
            liftPosition = transform.localPosition; liftRotation = transform.localRotation; liftScale = transform.localScale;
            inspectionFront = Quaternion.Euler(heldEuler) * PlacementRotation;
            inspectionAngles = Vector2.zero; liftElapsed = 0;
            PoseChanged?.Invoke();
            inspectionBounds = MeasureInspectionBounds();
            Camera camera = hand.GetComponent<Camera>();
            if (camera != null) FrameForInspection(camera, Application.isPlaying ? 0 : LiftSeconds);
            else { transform.localPosition = heldPosition; transform.localRotation = inspectionFront; }
        }

        internal void RotateInspection(Vector2 degrees)
        {
            inspectionAngles.x = Mathf.Repeat(inspectionAngles.x + degrees.x, 360f);
            inspectionAngles.y = Mathf.Repeat(inspectionAngles.y + degrees.y, 360f);
        }

        internal void ResetInspectionRotation() => inspectionAngles = Vector2.zero;

        internal void FrameForInspection(Camera camera, float deltaTime)
        {
            if (camera == null) return;
            liftElapsed = Mathf.Min(LiftSeconds, liftElapsed + deltaTime);
            Vector3 parentScale = transform.parent.lossyScale;
            Vector3 scale = new Vector3(restingWorldScale.x / parentScale.x, restingWorldScale.y / parentScale.y, restingWorldScale.z / parentScale.z);
            float distance = Mathf.Max(camera.nearClipPlane + 0.25f, heldPosition.z);
            Vector3 extents = Vector3.Scale(inspectionBounds.extents, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            Vector3 frontExtents = Vector3.zero;
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
            {
                Vector3 point = inspectionFront * Vector3.Scale(extents, new Vector3(x, y, z));
                frontExtents = Vector3.Max(frontExtents, new Vector3(Mathf.Abs(point.x), Mathf.Abs(point.y), Mathf.Abs(point.z)));
            }
            if (extents.sqrMagnitude > 0.0000001f)
            {
                float vertical = Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
                float horizontal = vertical * camera.aspect;
                float halfHeight = camera.orthographic ? camera.orthographicSize : distance * vertical;
                float width = InspectionLaneWidth - 0.09f;
                float widthLimit = frontExtents.x + (camera.orthographic ? 0 : 0.91f * horizontal * frontExtents.z);
                float heightLimit = frontExtents.y + (camera.orthographic ? 0 : 0.8f * vertical * frontExtents.z);
                float factor = Mathf.Min(halfHeight * camera.aspect * width / Mathf.Max(0.001f, widthLimit),
                    halfHeight * 0.76f / Mathf.Max(0.001f, heightLimit));
                // Keep the entire item clear of the near plane at any rotation.
                scale *= Mathf.Min(factor, (distance - camera.nearClipPlane - 0.02f) / extents.magnitude);
            }
            Quaternion rotation = Quaternion.Euler(inspectionAngles.y, inspectionAngles.x, 0) * inspectionFront;
            Vector3 center = camera.transform.InverseTransformPoint(camera.ViewportToWorldPoint(new Vector3(InspectionLaneWidth * 0.5f, 0.52f, distance)));
            Vector3 target = center - rotation * Vector3.Scale(inspectionBounds.center, scale);
            float blend = Mathf.SmoothStep(0, 1, liftElapsed / LiftSeconds);
            transform.localScale = Vector3.Lerp(liftScale, scale, blend);
            transform.localPosition = Vector3.Lerp(liftPosition, target, blend);
            transform.localRotation = Quaternion.Slerp(liftRotation, rotation, blend);
        }

        private Bounds MeasureInspectionBounds()
        {
            bool found = false;
            Bounds bounds = default;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                Bounds local = renderer.localBounds;
                Matrix4x4 toItem = transform.worldToLocalMatrix * renderer.localToWorldMatrix;
                for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 point = toItem.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents, new Vector3(x, y, z)));
                    if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; } else bounds.Encapsulate(point);
                }
            }
            return bounds;
        }
        internal void PutDown()
        {
            if (pickupSurface != null)
            {
                if (!pickupSurface.RestorePlacement(this, pickupPosition, pickupDrawerSlot)) ReturnHome();
            }
            else RestorePose(pickupParent, pickupPosition, pickupRotation, pickupScale);
        }
        internal void ReturnHome() => RestorePose(restingParent, restingPosition, restingRotation, restingScale);

        private void RestorePose(Transform parent, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            pickupSurface = null;
            pickupParent = null;
            ReleaseStorage();
            transform.SetParent(parent, false); transform.localPosition = position;
            transform.localRotation = rotation; transform.localScale = scale;
            if (colliders != null) for (int i = 0; i < colliders.Length; i++) if (colliders[i] != null) colliders[i].enabled = colliderEnabled[i];
            if (bodies != null) for (int i = 0; i < bodies.Length; i++) if (bodies[i] != null) bodies[i].isKinematic = kinematic[i];
            SetPresentationVisible(true);
            PoseChanged?.Invoke();
        }
        internal void Store(ItemPlacementSurface surface, Vector3 position, int drawerSlot)
        {
            pickupSurface = null;
            pickupParent = null;
            if (Storage != surface) ReleaseStorage();
            transform.SetParent(surface.transform, false); transform.localPosition = position;
            transform.localRotation = PlacementRotation;
            Vector3 parentScale = surface.transform.lossyScale;
            transform.localScale = new Vector3(restingWorldScale.x / parentScale.x, restingWorldScale.y / parentScale.y, restingWorldScale.z / parentScale.z);
            if (colliders != null) for (int i = 0; i < colliders.Length; i++) if (colliders[i] != null) colliders[i].enabled = colliderEnabled[i];
            Storage = surface; DrawerSlot = drawerSlot; SetPresentationVisible(true); PoseChanged?.Invoke();
        }
        private void ReleaseStorage()
        {
            if (Storage != null) Storage.Remove(this);
            Storage = null; DrawerSlot = 0;
        }
        private void OnDestroy()
        {
            ReleaseStorage();
            if (owner != null) owner.Unregister(this);
        }
        internal void SetPresentationVisible(bool visible)
        {
            if (renderers != null) for (int i = 0; i < renderers.Length; i++) if (renderers[i] != null) renderers[i].enabled = visible && rendererEnabled[i];
            if (canvasGroups != null) for (int i = 0; i < canvasGroups.Length; i++) if (canvasGroups[i] != null)
            { canvasGroups[i].alpha = visible ? canvasAlpha[i] : 0; canvasGroups[i].interactable = visible && canvasInteractable[i]; canvasGroups[i].blocksRaycasts = visible && canvasRaycasts[i]; }
        }
        internal void InvokeUse() => onUse?.Invoke();
    }
}
