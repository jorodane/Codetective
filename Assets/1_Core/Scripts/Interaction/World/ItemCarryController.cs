using System;
using System.Collections.Generic;
using UnityEngine;

namespace Codetective.Interaction
{
    /// <summary>One evidence object at a time. Placing it is a separate, cursor-driven interaction.</summary>
    [DisallowMultipleComponent]
    public sealed class ItemCarryController : MonoBehaviour
    {
        [SerializeField] private Transform hand;
        [Tooltip("Preplaced holdable items. Spawned items register explicitly with Register.")]
        [SerializeField] private HoldableItem[] sceneItems = Array.Empty<HoldableItem>();
        private readonly Dictionary<string, HoldableItem> registeredItems = new Dictionary<string, HoldableItem>(StringComparer.Ordinal);
        public HoldableItem HeldItem { get; private set; }
        public event Action<HoldableItem> Used;
        public event Action<HoldableItem> PickedUp;
        public event Action<HoldableItem> Placed;
        public bool IsPlacing { get; private set; }
        public bool IsExamining => isActiveAndEnabled && !sessionEnded && HeldItem != null && !IsPlacing;
        public Camera ViewCamera => hand != null ? hand.GetComponent<Camera>() : null;
        public bool PlacementValid { get; private set; }
        public Func<bool> ManipulationAllowed { get; set; }
        public bool CanManipulate => isActiveAndEnabled && !sessionEnded && (ManipulationAllowed == null || ManipulationAllowed());
        public string PlacementHint { get; private set; }
        private GameObject preview;
        private Material previewMaterial;
        private ItemPlacementSurface destination;
        private ItemPlacementSurface.Placement destinationPlacement;
        private bool sessionEnded;
        public void Initialize(Transform camera)
        {
            hand = camera;
            foreach (HoldableItem item in sceneItems) Register(item);
        }
        public void Register(HoldableItem item)
        {
            if (item == null) return;
            item.Register(this);
            if (!string.IsNullOrEmpty(item.Id)) registeredItems[item.Id] = item;
        }
        public HoldableItem RegisteredItem(string id) =>
            id != null && registeredItems.TryGetValue(id, out HoldableItem item) && item != null ? item : null;

        internal void Unregister(HoldableItem item)
        {
            if (!string.IsNullOrEmpty(item.Id) && registeredItems.TryGetValue(item.Id, out HoldableItem registered) &&
                ReferenceEquals(registered, item)) registeredItems.Remove(item.Id);
        }
        public void SetPresentationVisible(bool visible) { if (HeldItem != null) HeldItem.SetPresentationVisible(visible && isActiveAndEnabled && !sessionEnded); }
        private void LateUpdate()
        { if (IsExamining) HeldItem.FrameForInspection(ViewCamera); }
        public bool TryPickup(HoldableItem item)
        {
            if (!CanManipulate || hand == null || HeldItem != null || item == null || !item.isActiveAndEnabled || (item.IsStored && !item.Storage.CanRetrieve)) return false;
            Register(item); HeldItem = item; item.PickUp(hand); PickedUp?.Invoke(item); return true;
        }
        public bool TryUse(bool textInputFocused)
        {
            if (!CanManipulate || textInputFocused || HeldItem == null || IsPlacing) return false;
            HoldableItem item = HeldItem;
            item.InvokeUse(); Used?.Invoke(item); return true;
        }
        public void PutDown()
        {
            CancelPlacement();
            if (HeldItem == null) return;
            HoldableItem item = HeldItem; ItemPlacementSurface source = item.PickupSurface;
            HeldItem = null; item.PutDown();
            if (source != null && item.Storage != source)
            {
                source.SettleContents(Placed);
                Placed?.Invoke(item);
            }
        }
        public bool BeginPlacement()
        {
            if (!CanManipulate || HeldItem == null || IsPlacing) return false;
            IsPlacing = true; destination = null; PlacementValid = false;
            PlacementHint = "놓을 곳을 클릭하세요 · 서랍 클릭 / 4 서랍 앵글 · 우클릭 확인으로 돌아가기";
            SetPresentationVisible(false);
            preview = new GameObject("ItemPlacementPreview");
            Shader shader = Resources.Load<Shader>("PlacementPreview");
            if (shader == null) { CancelPlacement(); return false; }
            previewMaterial = new Material(shader);
            Transform shape = new GameObject("PlacementShape").transform; shape.SetParent(preview.transform, false);
            shape.localRotation = HeldItem.PlacementRotation; shape.localScale = HeldItem.RestingWorldScale;
            Transform visual = HeldItem.PlacementVisual;
            MeshFilter[] meshes = visual != null ? visual.GetComponentsInChildren<MeshFilter>(true) : HeldItem.GetComponentsInChildren<MeshFilter>();
            foreach (MeshFilter source in meshes)
            {
                if (source.sharedMesh == null) continue;
                var part = new GameObject("PreviewMesh", typeof(MeshFilter), typeof(MeshRenderer)); part.transform.SetParent(shape, false);
                part.GetComponent<MeshFilter>().sharedMesh = source.sharedMesh;
                part.transform.localPosition = HeldItem.transform.InverseTransformPoint(source.transform.position);
                part.transform.localRotation = Quaternion.Inverse(HeldItem.transform.rotation) * source.transform.rotation;
                Vector3 scale = HeldItem.transform.lossyScale, partScale = source.transform.lossyScale;
                part.transform.localScale = new Vector3(partScale.x / scale.x, partScale.y / scale.y, partScale.z / scale.z);
                Renderer renderer = part.GetComponent<Renderer>();
                renderer.sharedMaterial = previewMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
            }
            preview.SetActive(false); return true;
        }
        public void UpdatePlacement(Vector2 point, bool pointerBlocked = false, ItemPlacementSurface tray = null)
        {
            if (!IsPlacing || HeldItem == null || preview == null) return;
            destination = null; PlacementValid = false;
            if (pointerBlocked) { HidePlacementPreview(); return; }
            Camera camera = ViewCamera;
            if (camera == null) { HidePlacementPreview(); return; }
            Ray ray = camera.ScreenPointToRay(point);
            Vector3 location = ray.GetPoint(1); Quaternion rotation = Quaternion.identity;
            destination = tray;
            if (destination == null)
            {
                RaycastHit[] hits = Physics.RaycastAll(ray, 10, ~0, QueryTriggerInteraction.Collide);
                Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                foreach (RaycastHit hit in hits)
                {
                    ItemPlacementSurface surface = hit.collider.GetComponentInParent<ItemPlacementSurface>();
                    HoldableItem other = hit.collider.GetComponentInParent<HoldableItem>();
                    if (surface == null && other != null) surface = other.Storage;
                    if (surface != null && surface.IsContainer && !surface.Drawer.IsOpen) continue;
                    if (surface == null && hit.collider.GetComponentInParent<SlidingDrawer>() != null && hit.collider.GetComponent<DrawerFrontTarget>() == null) continue;
                    if (surface == null && other == null && hit.collider.isTrigger && hit.collider.GetComponent<DrawerFrontTarget>() == null) continue;
                    location = hit.point;
                    destination = surface;
                    break;
                }
            }
            if (destination != null)
            {
                PlacementValid = destination.TryGetPlacement(HeldItem, ray, out destinationPlacement);
                location = destination.transform.TransformPoint(destinationPlacement.Position);
                rotation = destination.transform.rotation;
            }
            preview.transform.SetPositionAndRotation(location, rotation); preview.SetActive(true);
            previewMaterial.SetColor("_Tint", PlacementValid ? new Color(0.15f, 1f, 0.32f, 0.7f) : new Color(1f, 0.36f, 0.04f, 0.7f));
            PlacementHint = PlacementValid ? "클릭: 배치 · 우클릭: 확인으로 돌아가기" : "이곳에는 놓을 수 없습니다 · 서랍 앞면 클릭: 열기 / 닫기 · 우클릭: 확인으로 돌아가기";
        }
        public bool TryPlaceAtScreenPoint(Vector2 point, ItemPlacementSurface tray = null)
        {
            UpdatePlacement(point, false, tray);
            return PlacementValid && CommitPlacement(destination, destinationPlacement);
        }
        public bool TryPlaceOn(ItemPlacementSurface surface) => TryPlaceOn(surface, null);
        public bool TryPlaceOn(ItemPlacementSurface surface, Vector3 worldPoint) => TryPlaceOn(surface, (Vector3?)worldPoint);
        private bool TryPlaceOn(ItemPlacementSurface surface, Vector3? worldPoint)
        {
            return surface != null && HeldItem != null && surface.TryGetPlacement(HeldItem, worldPoint, out var placement) && CommitPlacement(surface, placement);
        }
        private bool CommitPlacement(ItemPlacementSurface surface, ItemPlacementSurface.Placement placement)
        {
            if (!CanManipulate || !IsPlacing || surface == null || HeldItem == null) return false;
            HoldableItem item = HeldItem;
            ItemPlacementSurface source = item.PickupSurface;
            if (!surface.PlaceAt(item, placement)) return false;
            HeldItem = null; CancelPlacement();
            if (source != null) source.SettleContents(Placed);
            Placed?.Invoke(item); return true;
        }
        public void HidePlacementPreview() { if (preview != null) preview.SetActive(false); PlacementValid = false; destination = null; }
        public void CancelPlacement()
        {
            IsPlacing = false; PlacementValid = false; destination = null;
            Discard(preview); Discard(previewMaterial);
            preview = null; previewMaterial = null; SetPresentationVisible(true);
        }
        public void ReturnToDesk(HoldableItem item)
        {
            if (item == null) return;
            ItemPlacementSurface source = item.Storage != null ? item.Storage : item.PickupSurface;
            if (HeldItem == item) { CancelPlacement(); HeldItem = null; }
            item.ReturnHome();
            if (source != null) source.SettleContents(Placed);
        }
        public bool RestoreStorage(HoldableItem item, ItemPlacementSurface surface)
        { return item != null && surface != null && !item.IsHeld && surface.RestorePlacement(item); }
        public bool RestoreStorage(HoldableItem item, ItemPlacementSurface surface, Vector3 localPosition, int drawerSlot = 0)
        {
            return item != null && surface != null && !item.IsHeld && surface.RestorePlacement(item, localPosition, drawerSlot);
        }
        private static void Discard(UnityEngine.Object value)
        { if (value == null) return; if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
        // Disabling suspends the held object. Reparenting here is illegal while the
        // camera (or the item's resting parent) is itself changing activation state.
        private void OnDisable() { CancelPlacement(); SetPresentationVisible(false); }
        private void OnEnable() { if (!sessionEnded) SetPresentationVisible(true); }
        public void EndSession()
        {
            sessionEnded = true; CancelPlacement(); SetPresentationVisible(false); HeldItem = null;
        }
        private void OnDestroy() => EndSession();
    }
}
