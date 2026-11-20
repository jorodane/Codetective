using System;
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
        private Vector3 destinationPosition;
        private Transform flatPreview, rolledPreview;
        private bool sessionEnded;
        public void Initialize(Transform camera)
        {
            hand = camera;
            foreach (HoldableItem item in sceneItems) Register(item);
        }
        public void Register(HoldableItem item) { if (item != null) item.Register(this); }
        public void SetPresentationVisible(bool visible) { if (HeldItem != null) HeldItem.SetPresentationVisible(visible && isActiveAndEnabled && !sessionEnded); }
        private void LateUpdate()
        { if (IsExamining) HeldItem.FrameForInspection(ViewCamera); }
        public bool TryPickup(HoldableItem item)
        {
            if (!CanManipulate || hand == null || HeldItem != null || item == null || !item.isActiveAndEnabled || (item.IsStored && !item.Storage.CanRetrieve)) return false;
            item.Register(this); HeldItem = item; item.PickUp(hand); PickedUp?.Invoke(item); return true;
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
            HoldableItem item = HeldItem; HeldItem = null; item.PutDown();
        }
        public bool BeginPlacement()
        {
            if (!CanManipulate || HeldItem == null || IsPlacing) return false;
            IsPlacing = true; destination = null; PlacementValid = false;
            PlacementHint = "놓을 곳을 클릭하세요 · 4 서랍 앵글 · 우클릭 확인으로 돌아가기";
            SetPresentationVisible(false);
            preview = new GameObject("ItemPlacementPreview");
            Shader shader = Resources.Load<Shader>("PlacementPreview");
            if (shader == null) { CancelPlacement(); return false; }
            previewMaterial = new Material(shader);
            flatPreview = new GameObject("UnfoldedPreview").transform; flatPreview.SetParent(preview.transform, false);
            flatPreview.localRotation = HeldItem.PlacementRotation; flatPreview.localScale = HeldItem.RestingWorldScale;
            foreach (MeshFilter source in HeldItem.GetComponentsInChildren<MeshFilter>())
            {
                if (source.sharedMesh == null) continue;
                var part = new GameObject("PreviewMesh", typeof(MeshFilter), typeof(MeshRenderer)); part.transform.SetParent(flatPreview, false);
                part.GetComponent<MeshFilter>().sharedMesh = source.sharedMesh;
                part.transform.localPosition = HeldItem.transform.InverseTransformPoint(source.transform.position);
                part.transform.localRotation = Quaternion.Inverse(HeldItem.transform.rotation) * source.transform.rotation;
                Vector3 scale = HeldItem.transform.lossyScale, partScale = source.transform.lossyScale;
                part.transform.localScale = new Vector3(partScale.x / scale.x, partScale.y / scale.y, partScale.z / scale.z);
                part.GetComponent<Renderer>().sharedMaterial = previewMaterial;
            }
            if (HeldItem.Use == HeldItemUse.Newspaper)
            {
                rolledPreview = new GameObject("RolledPreview").transform; rolledPreview.SetParent(preview.transform, false);
                rolledPreview.localScale = HeldItem.RestingWorldScale;
                GameObject roll = GameObject.CreatePrimitive(PrimitiveType.Cylinder); roll.transform.SetParent(rolledPreview, false);
                roll.transform.localRotation = Quaternion.Euler(0, 0, 90); roll.transform.localScale = new Vector3(0.052f, 0.14f, 0.052f);
                Discard(roll.GetComponent<Collider>()); roll.GetComponent<Renderer>().sharedMaterial = previewMaterial;
                rolledPreview.gameObject.SetActive(false);
            }
            foreach (Renderer renderer in preview.GetComponentsInChildren<Renderer>())
            { renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false; }
            preview.SetActive(false); return true;
        }
        public void UpdatePlacement(Vector2 point, bool pointerBlocked = false)
        {
            if (!IsPlacing || HeldItem == null || preview == null) return;
            destination = null; PlacementValid = false;
            if (pointerBlocked) { HidePlacementPreview(); return; }
            Camera camera = ViewCamera;
            if (camera == null) { HidePlacementPreview(); return; }
            Ray ray = camera.ScreenPointToRay(point);
            RaycastHit[] hits = Physics.RaycastAll(ray, 10, ~0, QueryTriggerInteraction.Collide);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            Vector3 location = ray.GetPoint(1); Quaternion rotation = Quaternion.identity;
            foreach (RaycastHit hit in hits)
            {
                ItemPlacementSurface surface = hit.collider.GetComponentInParent<ItemPlacementSurface>();
                HoldableItem other = hit.collider.GetComponentInParent<HoldableItem>();
                if (surface == null && other != null) surface = other.Storage;
                if (surface == null && hit.collider.GetComponentInParent<SlidingDrawer>() != null && hit.collider.GetComponent<DrawerFrontTarget>() == null) continue;
                if (surface == null && other == null && hit.collider.isTrigger && hit.collider.GetComponent<DrawerFrontTarget>() == null) continue;
                location = hit.point;
                if (surface != null)
                {
                    destination = surface;
                    PlacementValid = surface.TryGetPosition(HeldItem, ray, out destinationPosition);
                    location = surface.transform.TransformPoint(destinationPosition);
                    rotation = surface.transform.rotation;
                }
                break;
            }
            bool rolled = HeldItem.Use == HeldItemUse.Newspaper && destination != null && destination.IsContainer;
            flatPreview.gameObject.SetActive(!rolled); if (rolledPreview != null) rolledPreview.gameObject.SetActive(rolled);
            preview.transform.SetPositionAndRotation(location, rotation); preview.SetActive(true);
            previewMaterial.SetColor("_Tint", PlacementValid ? new Color(0.15f, 1f, 0.32f, 0.7f) : new Color(1f, 0.36f, 0.04f, 0.7f));
            PlacementHint = PlacementValid ? "클릭: 내려놓기 · 우클릭: 확인으로 돌아가기" : "이곳에는 놓을 수 없습니다 · 서랍 앞면 클릭: 열기 / 닫기 · 우클릭: 확인으로 돌아가기";
        }
        public bool TryPlaceAtScreenPoint(Vector2 point)
        {
            UpdatePlacement(point);
            return PlacementValid && CommitPlacement(destination, destinationPosition);
        }
        public bool TryPlaceOn(ItemPlacementSurface surface)
        {
            if (surface == null || HeldItem == null || !surface.TryGetPosition(HeldItem, out Vector3 position)) return false;
            return CommitPlacement(surface, position);
        }
        public bool TryPlaceOn(ItemPlacementSurface surface, Vector3 worldPoint)
        {
            if (surface == null || HeldItem == null || !surface.TryGetPosition(HeldItem, worldPoint, out Vector3 position)) return false;
            return CommitPlacement(surface, position);
        }
        private bool CommitPlacement(ItemPlacementSurface surface, Vector3 localPosition)
        {
            if (!CanManipulate || !IsPlacing || surface == null || HeldItem == null || !surface.PlaceAt(HeldItem, localPosition)) return false;
            HoldableItem item = HeldItem; HeldItem = null; CancelPlacement(); Placed?.Invoke(item); return true;
        }
        public void HidePlacementPreview() { if (preview != null) preview.SetActive(false); PlacementValid = false; destination = null; }
        public void CancelPlacement()
        {
            IsPlacing = false; PlacementValid = false; destination = null;
            Discard(preview); Discard(previewMaterial);
            preview = null; previewMaterial = null; SetPresentationVisible(true);
            flatPreview = rolledPreview = null;
        }
        public void ReturnToDesk(HoldableItem item)
        {
            if (item == null) return;
            if (HeldItem == item) { CancelPlacement(); HeldItem = null; }
            item.PutDown();
        }
        public bool RestoreStorage(HoldableItem item, ItemPlacementSurface surface)
        { return item != null && surface != null && !item.IsHeld && surface.Place(item, true); }
        public bool RestoreStorage(HoldableItem item, ItemPlacementSurface surface, Vector3 localPosition)
        { return item != null && surface != null && !item.IsHeld && surface.PlaceAt(item, localPosition, true); }
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
