using System.Collections.Generic;
using UnityEngine;

namespace Codetective.Interaction
{
    /// <summary>A drawer's usable interior, in drawer-local metres. Contents follow its slide.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(BoxCollider))]
    public sealed class ItemPlacementSurface : MonoBehaviour
    {
        [SerializeField] private Vector3 center;
        [SerializeField] private Vector3 size = new Vector3(0.36f, 0.11f, 0.36f);
        private readonly List<HoldableItem> contents = new List<HoldableItem>();
        private SlidingDrawer drawer;
        public string Id { get; private set; }
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
                Vector3 candidate = center + new Vector3((x - 1) * margin.x, -margin.y, (z - 1) * margin.z);
                var bounds = new Bounds(candidate, itemSize + Vector3.one * 0.004f);
                bool occupied = false;
                foreach (HoldableItem other in contents)
                    if (other != item && bounds.Intersects(new Bounds(other.transform.localPosition, LocalSize(other))))
                    { occupied = true; break; }
                if (!occupied) { position = candidate; return true; }
            }
            return false;
        }
        internal bool Place(HoldableItem item, bool restoring = false)
        {
            if (!TryGetPosition(item, out Vector3 position, !restoring)) return false;
            item.Store(this, position); contents.Add(item); return true;
        }
        internal void Remove(HoldableItem item) => contents.Remove(item);
        private Vector3 LocalSize(HoldableItem item)
        {
            Vector3 scale = transform.lossyScale, world = item.PlacementSize;
            return new Vector3(world.x / Mathf.Abs(scale.x), world.y / Mathf.Abs(scale.y), world.z / Mathf.Abs(scale.z));
        }
    }
}
