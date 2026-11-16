using System.Collections.Generic;
using UnityEngine;

namespace Codetective.Interaction
{
    /// <summary>A usable tabletop or drawer volume. Positions are local to the surface.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(BoxCollider))]
    public sealed class ItemPlacementSurface : MonoBehaviour
    {
        [SerializeField] private Vector3 center;
        [SerializeField] private Vector3 size = new Vector3(0.36f, 0.11f, 0.36f);
        private readonly List<HoldableItem> contents = new List<HoldableItem>();
        private SlidingDrawer drawer;
        public string Id { get; private set; }
        public bool IsContainer => drawer != null;
        public bool CanRetrieve => drawer == null || drawer.CanStore;
        public void Initialize(string id, SlidingDrawer owner, Bounds interior)
        {
            Id = id; drawer = owner; center = interior.center; size = interior.size;
            // Unity's missing-component wrappers compare equal to null without being CLR null.
            var collider = GetComponent<BoxCollider>();
            if (collider == null) collider = gameObject.AddComponent<BoxCollider>();
            collider.center = center; collider.size = size; collider.isTrigger = true;
        }
        public bool TryGetPosition(HoldableItem item, out Vector3 position, bool requireOpen = true)
        {
            position = center;
            if (item == null || (requireOpen && drawer != null && !drawer.CanStore)) return false;
            Vector3 itemSize = LocalSize(item);
            if (itemSize.x > size.x || itemSize.y > size.y || itemSize.z > size.z) return false;
            contents.RemoveAll(value => value == null);
            // Try a small grid; every candidate is checked against the full interior and existing contents.
            Vector3 margin = (size - itemSize) * 0.5f;
            for (int z = 0; z < 3; z++) for (int x = 0; x < 3; x++)
            {
                Vector3 candidate = center + new Vector3((x - 1) * margin.x, -margin.y, (z - 1) * margin.z) - LocalOffset(item);
                if (Fits(item, candidate, requireOpen)) { position = candidate; return true; }
            }
            return false;
        }
        public bool TryGetPosition(HoldableItem item, Vector3 worldPoint, out Vector3 position, bool requireOpen = true)
        {
            position = transform.InverseTransformPoint(worldPoint);
            if (item == null || (requireOpen && drawer != null && !drawer.CanStore)) return false;
            Vector3 offset = LocalOffset(item), itemSize = LocalSize(item);
            position.x -= offset.x; position.z -= offset.z;
            position.y = center.y - size.y * 0.5f + itemSize.y * 0.5f - offset.y;
            return Fits(item, position, requireOpen);
        }
        public bool TryGetPosition(HoldableItem item, Ray ray, out Vector3 position)
        {
            var plane = new Plane(transform.up, transform.TransformPoint(center - Vector3.up * size.y * 0.5f));
            position = default;
            return plane.Raycast(ray, out float distance) && TryGetPosition(item, ray.GetPoint(distance), out position);
        }
        private bool Fits(HoldableItem item, Vector3 position, bool checkObstacles = true)
        {
            Vector3 itemSize = LocalSize(item), target = position + LocalOffset(item);
            Vector3 margin = (size - itemSize) * 0.5f;
            if (margin.x < 0 || margin.y < 0 || margin.z < 0 || Mathf.Abs(target.x - center.x) > margin.x + 0.0001f ||
                Mathf.Abs(target.y - center.y) > margin.y + 0.0001f || Mathf.Abs(target.z - center.z) > margin.z + 0.0001f) return false;
            contents.RemoveAll(value => value == null);
            var bounds = new Bounds(target, itemSize + Vector3.one * 0.004f);
            foreach (HoldableItem other in contents)
                if (other != item && bounds.Intersects(new Bounds(other.transform.localPosition + LocalOffset(other), LocalSize(other)))) return false;
            // During load, other items may still be at their authored pose until their own record is restored.
            if (!checkObstacles) return true;
            Vector3 worldSize = item.PlacementSizeOn(this);
            Vector3 extents = new Vector3(Mathf.Max(0.0001f, worldSize.x * 0.5f - 0.001f), Mathf.Max(0.0001f, worldSize.y * 0.5f - 0.001f), Mathf.Max(0.0001f, worldSize.z * 0.5f - 0.001f));
            foreach (Collider obstacle in Physics.OverlapBox(transform.TransformPoint(target), extents, transform.rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                if (obstacle.transform.IsChildOf(item.transform) || obstacle.GetComponentInParent<ItemPlacementSurface>() == this ||
                    (drawer != null && obstacle.GetComponentInParent<SlidingDrawer>() == drawer)) continue;
                return false;
            }
            return true;
        }
        internal bool Place(HoldableItem item, bool restoring = false)
        {
            if (!TryGetPosition(item, out Vector3 position, !restoring)) return false;
            return PlaceAt(item, position, restoring);
        }
        internal bool PlaceAt(HoldableItem item, Vector3 localPosition, bool restoring = false)
        {
            if (item == null || (!restoring && drawer != null && !drawer.CanStore) || !Fits(item, localPosition, !restoring)) return false;
            item.Store(this, localPosition);
            if (!contents.Contains(item)) contents.Add(item);
            return true;
        }
        internal void Remove(HoldableItem item) => contents.Remove(item);
        private Vector3 LocalSize(HoldableItem item)
        {
            Vector3 scale = transform.lossyScale, world = item.PlacementSizeOn(this);
            return new Vector3(world.x / Mathf.Abs(scale.x), world.y / Mathf.Abs(scale.y), world.z / Mathf.Abs(scale.z));
        }
        private Vector3 LocalOffset(HoldableItem item)
        {
            Vector3 scale = transform.lossyScale, offset = item.PlacementOffset;
            return new Vector3(offset.x / scale.x, offset.y / scale.y, offset.z / scale.z);
        }
    }
}
