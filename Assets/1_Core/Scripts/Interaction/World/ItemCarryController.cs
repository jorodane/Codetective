using System;
using UnityEngine;

namespace Codetective.Interaction
{
    /// <summary>One inspected object at a time; put down restores its authored desk location.</summary>
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
        public bool PlacementValid { get; private set; }
        public Func<bool> ManipulationAllowed { get; set; }
        public bool CanManipulate => ManipulationAllowed == null || ManipulationAllowed();
        public string PlacementHint { get; private set; }
        private GameObject preview;
        private Material previewMaterial;
        private ItemPlacementSurface destination;
        public void Initialize(Transform camera)
        {
            hand = camera;
            foreach (HoldableItem item in sceneItems) Register(item);
        }
        public void Register(HoldableItem item) { if (item != null) item.Register(this); }
        public void SetPresentationVisible(bool visible) { if (HeldItem != null) HeldItem.SetPresentationVisible(visible); }
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
            PlacementHint = "열린 서랍 위로 마우스를 이동하세요. 우클릭 취소";
            SetPresentationVisible(false);
            preview = new GameObject("ItemPlacementPreview");
            Shader shader = Resources.Load<Shader>("PlacementPreview");
            if (shader == null) { CancelPlacement(); return false; }
            previewMaterial = new Material(shader);
            if (HeldItem.Use == HeldItemUse.Newspaper)
            {
                GameObject roll = GameObject.CreatePrimitive(PrimitiveType.Cylinder); roll.transform.SetParent(preview.transform, false);
                roll.transform.localRotation = Quaternion.Euler(0, 0, 90); roll.transform.localScale = new Vector3(0.052f, 0.14f, 0.052f);
                Discard(roll.GetComponent<Collider>()); roll.GetComponent<Renderer>().sharedMaterial = previewMaterial;
            }
            else
            {
                foreach (MeshFilter source in HeldItem.GetComponentsInChildren<MeshFilter>())
                {
                    if (source.sharedMesh == null) continue;
                    var part = new GameObject("PreviewMesh", typeof(MeshFilter), typeof(MeshRenderer)); part.transform.SetParent(preview.transform, false);
                    part.GetComponent<MeshFilter>().sharedMesh = source.sharedMesh;
                    part.transform.localPosition = Quaternion.Inverse(HeldItem.transform.rotation) * (source.transform.position - HeldItem.transform.position);
                    part.transform.localRotation = Quaternion.Inverse(HeldItem.transform.rotation) * source.transform.rotation;
                    part.transform.localScale = source.transform.lossyScale;
                    part.GetComponent<Renderer>().sharedMaterial = previewMaterial;
                }
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
            Camera camera = hand != null ? hand.GetComponent<Camera>() : null;
            if (camera == null) { HidePlacementPreview(); return; }
            Ray ray = camera.ScreenPointToRay(point);
            RaycastHit[] hits = Physics.RaycastAll(ray, 10, ~0, QueryTriggerInteraction.Collide);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            Vector3 location = ray.GetPoint(1); Quaternion rotation = Quaternion.identity;
            foreach (RaycastHit hit in hits)
            {
                ItemPlacementSurface surface = hit.collider.GetComponentInParent<ItemPlacementSurface>();
                SlidingDrawer drawer = hit.collider.GetComponentInParent<SlidingDrawer>();
                if (surface == null && drawer != null) surface = drawer.GetComponentInChildren<ItemPlacementSurface>();
                if (surface == null && hit.collider.GetComponentInParent<HoldableItem>() == null && hit.collider.isTrigger) continue;
                location = hit.point;
                if (surface != null)
                {
                    destination = surface;
                    PlacementValid = surface.TryGetPosition(HeldItem, out Vector3 local);
                    location = PlacementValid ? surface.transform.TransformPoint(local) : hit.point + hit.normal * (HeldItem.PlacementSize.y * 0.5f + 0.01f);
                    rotation = surface.transform.rotation;
                }
                break;
            }
            preview.transform.SetPositionAndRotation(location, rotation); preview.SetActive(true);
            previewMaterial.SetColor("_Tint", PlacementValid ? new Color(0.15f, 1f, 0.32f, 0.7f) : new Color(1f, 0.36f, 0.04f, 0.7f));
            PlacementHint = PlacementValid ? "클릭: 서랍에 넣기  ·  우클릭 취소" : "배치 불가 · 서랍 열림 / 크기 / 빈 공간을 확인하세요.";
        }
        public bool TryPlaceAtScreenPoint(Vector2 point)
        {
            UpdatePlacement(point);
            return PlacementValid && TryPlaceOn(destination);
        }
        public bool TryPlaceOn(ItemPlacementSurface surface)
        {
            if (!CanManipulate || !IsPlacing || surface == null || HeldItem == null || !surface.Place(HeldItem)) return false;
            HoldableItem item = HeldItem; HeldItem = null; CancelPlacement(); Placed?.Invoke(item); return true;
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
            if (HeldItem == item) { CancelPlacement(); HeldItem = null; }
            item.PutDown();
        }
        public bool RestoreStorage(HoldableItem item, ItemPlacementSurface surface)
        { return item != null && surface != null && !item.IsHeld && surface.Place(item, true); }
        private static void Discard(UnityEngine.Object value)
        { if (value == null) return; if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
        private void OnDisable() => PutDown();
    }
}
