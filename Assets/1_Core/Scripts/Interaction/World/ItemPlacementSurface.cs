using System;
using System.Collections.Generic;
using UnityEngine;

namespace Codetective.Interaction
{
    /// <summary>Free placement on a desk and cell-based storage inside a drawer.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(BoxCollider))]
    public sealed class ItemPlacementSurface : MonoBehaviour
    {
        // Local axes are width, height, depth.
        public static readonly Vector3Int DrawerSize = new Vector3Int(4, 2, 8);

        internal readonly struct Placement
        {
            public readonly Vector3 Position;
            public readonly int DrawerSlot;

            public Placement(Vector3 position, int drawerSlot = 0)
            { Position = position; DrawerSlot = drawerSlot; }
        }

        [SerializeField] private Vector3 center;
        [SerializeField] private Vector3 size = new Vector3(0.36f, 0.11f, 0.36f);
        private readonly List<HoldableItem> contents = new List<HoldableItem>();
        private SlidingDrawer drawer;
        public string Id { get; private set; }
        public SlidingDrawer Drawer => drawer;
        public bool IsContainer => drawer != null;
        public bool CanRetrieve => drawer == null || drawer.CanStore;
        public Bounds LocalBounds => new Bounds(center, size);
        public int ItemCount => Contents.Count;
        public IReadOnlyList<HoldableItem> Contents
        {
            get { contents.RemoveAll(item => item == null); return contents; }
        }
        private int SlotsPerLayer => DrawerSize.x * DrawerSize.z;

        public void Initialize(string id, SlidingDrawer owner, Bounds interior)
        {
            Id = id; drawer = owner; center = interior.center; size = interior.size;
            if (drawer != null) drawer.Interior = this;
            // Unity's missing-component wrappers compare equal to null without being CLR null.
            var collider = GetComponent<BoxCollider>();
            if (collider == null) collider = gameObject.AddComponent<BoxCollider>();
            collider.center = center; collider.size = size; collider.isTrigger = true;
        }

        public bool TryGetPosition(HoldableItem item, out Vector3 position, bool requireOpen = true)
        {
            bool found = TryFindPlacement(item, center, false, requireOpen, out Placement placement);
            position = placement.Position;
            return found;
        }

        public bool TryGetPosition(HoldableItem item, Vector3 worldPoint, out Vector3 position, bool requireOpen = true)
        {
            bool found = TryFindPlacement(item, transform.InverseTransformPoint(worldPoint), true, requireOpen, out Placement placement);
            position = placement.Position;
            return found;
        }

        public bool TryGetPosition(HoldableItem item, Ray ray, out Vector3 position)
        {
            bool found = TryGetPlacement(item, ray, out Placement placement);
            position = placement.Position;
            return found;
        }

        internal bool TryGetPlacement(HoldableItem item, Ray ray, out Placement placement)
        {
            var plane = new Plane(transform.up, transform.TransformPoint(LocalBounds.min));
            placement = new Placement(center);
            return plane.Raycast(ray, out float distance) && TryFindPlacement(item, transform.InverseTransformPoint(ray.GetPoint(distance)), true, true, out placement);
        }

        internal bool TryGetPlacement(HoldableItem item, Vector3? worldPoint, out Placement placement) =>
            TryFindPlacement(item, worldPoint.HasValue ? transform.InverseTransformPoint(worldPoint.Value) : center, worldPoint.HasValue, true, out placement);

        private bool TryFindPlacement(HoldableItem item, Vector3 point, bool atPoint, bool checkObstacles, out Placement placement)
        {
            placement = new Placement(point);
            if (item == null || (checkObstacles && !CanRetrieve)) return false;
            Vector3 itemSize = LocalSize(item), offset = LocalOffset(item);
            Vector3 margin = (size - itemSize) * 0.5f;
            Vector3 origin = center - Vector3.up * margin.y;
            point.y = atPoint && IsContainer ? Mathf.Clamp(point.y, origin.y, center.y) : origin.y;
            placement = new Placement(point - offset);
            if (margin.x < -0.0001f || margin.y < -0.0001f || margin.z < -0.0001f) return false;
            if (IsContainer) return TryFindDrawerSlot(item, itemSize, offset, point, checkObstacles, out placement);

            if (atPoint)
            {
                if (!checkObstacles)
                {
                    point.x = Mathf.Clamp(point.x, origin.x - margin.x, origin.x + margin.x);
                    point.z = Mathf.Clamp(point.z, origin.z - margin.z, origin.z + margin.z);
                }
                placement = new Placement(point - offset);
                return FitsDesktop(item, placement.Position, checkObstacles);
            }

            float nearest = float.PositiveInfinity;
            for (int z = -1; z <= 1; z++) for (int x = -1; x <= 1; x++)
            {
                Vector3 candidate = origin + new Vector3(x * margin.x, 0, z * margin.z) - offset;
                float distance = (candidate + offset - origin).sqrMagnitude;
                if (distance >= nearest || !FitsDesktop(item, candidate, checkObstacles)) continue;
                nearest = distance;
                placement = new Placement(candidate);
            }
            return nearest < float.PositiveInfinity;
        }

        private Vector3 CellSize => new Vector3(size.x / DrawerSize.x, size.y / DrawerSize.y, size.z / DrawerSize.z);
        private Vector3Int FirstCell(int slot)
        {
            int index = slot - 1;
            return new Vector3Int(index % DrawerSize.x, index / SlotsPerLayer, index / DrawerSize.x % DrawerSize.z);
        }

        private static Vector3Int Footprint(Vector3 itemSize, Vector3 cellSize)
        {
            int Cells(float length, float unit) => Mathf.Max(1, Mathf.CeilToInt(length / unit - 0.0001f));
            return new Vector3Int(Cells(itemSize.x, cellSize.x), Cells(itemSize.y, cellSize.y), Cells(itemSize.z, cellSize.z));
        }

        private bool TryFindDrawerSlot(HoldableItem item, Vector3 itemSize, Vector3 offset, Vector3 point, bool checkObstacles, out Placement placement)
        {
            placement = new Placement(point - offset);
            float nearest = float.PositiveInfinity;
            Vector3Int footprint = Footprint(itemSize, CellSize);
            for (int slot = 1; slot <= SlotsPerLayer * DrawerSize.y; slot++)
            {
                if (!TryDrawerPosition(item, slot, itemSize, footprint, out Vector3 target)) continue;
                float distance = (target - point).sqrMagnitude;
                if (distance >= nearest || checkObstacles && OverlapsScene(item, target)) continue;
                nearest = distance;
                placement = new Placement(target - offset, slot);
            }
            return nearest < float.PositiveInfinity;
        }

        private bool TryDrawerPosition(HoldableItem item, int slot, Vector3 itemSize, Vector3Int footprint, out Vector3 position)
        {
            position = default;
            if (slot < 1 || slot > SlotsPerLayer * DrawerSize.y) return false;
            Vector3Int first = FirstCell(slot), end = first + footprint;
            if (end.x > DrawerSize.x || end.y > DrawerSize.y || end.z > DrawerSize.z) return false;

            Vector3 cell = CellSize, floor = LocalBounds.min;
            Vector3 centerOfCells = floor + new Vector3((first.x + footprint.x * 0.5f) * cell.x, 0, (first.z + footprint.z * 0.5f) * cell.z);
            float bottom = floor.y;
            bool supported = first.y == 0;
            foreach (HoldableItem other in Contents)
            {
                if (other == item || other.DrawerSlot == 0) continue;
                Vector3 otherSize = LocalSize(other);
                Vector3Int otherFirst = FirstCell(other.DrawerSlot), otherEnd = otherFirst + Footprint(otherSize, cell);
                if (end.x <= otherFirst.x || otherEnd.x <= first.x || end.z <= otherFirst.z || otherEnd.z <= first.z) continue;
                if (end.y > otherFirst.y && otherEnd.y > first.y) return false;
                Vector3 otherCenter = other.transform.localPosition + LocalOffset(other);
                // The upper layer rests on the highest surface under the item's footprint.
                if (otherEnd.y == first.y &&
                    Mathf.Abs(otherCenter.x - centerOfCells.x) < (itemSize.x + otherSize.x) * 0.5f &&
                    Mathf.Abs(otherCenter.z - centerOfCells.z) < (itemSize.z + otherSize.z) * 0.5f)
                {
                    bottom = Mathf.Max(bottom, otherCenter.y + otherSize.y * 0.5f);
                    supported = true;
                }
            }
            position = new Vector3(centerOfCells.x, bottom + itemSize.y * 0.5f, centerOfCells.z);
            return supported && InsideSurface(position, itemSize);
        }

        private bool InsideSurface(Vector3 target, Vector3 itemSize)
        {
            Vector3 margin = (size - itemSize) * 0.5f;
            return Mathf.Abs(target.x - center.x) <= margin.x + 0.0001f &&
                Mathf.Abs(target.y - center.y) <= margin.y + 0.0001f &&
                Mathf.Abs(target.z - center.z) <= margin.z + 0.0001f;
        }

        private bool FitsDesktop(HoldableItem item, Vector3 position, bool checkObstacles)
        {
            Vector3 itemSize = LocalSize(item), target = position + LocalOffset(item);
            if (!InsideSurface(target, itemSize)) return false;
            var bounds = new Bounds(target, itemSize + Vector3.one * 0.004f);
            foreach (HoldableItem other in Contents)
                if (other != item && bounds.Intersects(new Bounds(other.transform.localPosition + LocalOffset(other), LocalSize(other)))) return false;
            return !checkObstacles || !OverlapsScene(item, target);
        }

        private bool OverlapsScene(HoldableItem item, Vector3 target)
        {
            Vector3 worldSize = item.PlacementSize;
            var extents = new Vector3(Mathf.Max(0.0001f, worldSize.x * 0.5f - 0.001f), Mathf.Max(0.0001f, worldSize.y * 0.5f - 0.001f),
                Mathf.Max(0.0001f, worldSize.z * 0.5f - 0.001f));
            foreach (Collider obstacle in Physics.OverlapBox(transform.TransformPoint(target), extents, transform.rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                if (obstacle.transform.IsChildOf(item.transform) || obstacle.GetComponentInParent<ItemPlacementSurface>() == this ||
                    (drawer != null && obstacle.GetComponentInParent<SlidingDrawer>() == drawer)) continue;
                return true;
            }
            return false;
        }

        internal bool PlaceAt(HoldableItem item, Placement placement, bool restoring = false)
        {
            if (item == null || (!restoring && !CanRetrieve)) return false;
            Vector3 position = placement.Position;
            if (IsContainer)
            {
                Vector3 itemSize = LocalSize(item);
                if (!TryDrawerPosition(item, placement.DrawerSlot, itemSize, Footprint(itemSize, CellSize), out Vector3 target) ||
                    (!restoring && OverlapsScene(item, target))) return false;
                position = target - LocalOffset(item);
            }
            else if (!FitsDesktop(item, position, !restoring)) return false;
            item.Store(this, position, placement.DrawerSlot);
            if (!contents.Contains(item)) contents.Add(item);
            return true;
        }

        internal bool RestorePlacement(HoldableItem item, Vector3? position = null, int drawerSlot = 0)
        {
            if (item == null) return false;
            if (position.HasValue && PlaceAt(item, new Placement(position.Value, drawerSlot), true)) return true;
            if (position.HasValue && !IsContainer && item.Use != HeldItemUse.Newspaper) return false;
            Vector3 point = position.HasValue ? position.Value + LocalOffset(item) : center;
            return TryFindPlacement(item, point, position.HasValue, false, out Placement placement) && PlaceAt(item, placement, true);
        }

        internal void Remove(HoldableItem item) => contents.Remove(item);

        internal void SettleContents(Action<HoldableItem> moved)
        {
            if (!IsContainer) return;
            foreach (HoldableItem item in Contents)
            {
                int slot = item.DrawerSlot;
                if (slot <= SlotsPerLayer) continue;
                Vector3 itemSize = LocalSize(item);
                Vector3Int footprint = Footprint(itemSize, CellSize);
                if (!TryDrawerPosition(item, slot, itemSize, footprint, out Vector3 target))
                {
                    slot -= SlotsPerLayer;
                    if (!TryDrawerPosition(item, slot, itemSize, footprint, out target)) continue;
                }
                Vector3 position = target - LocalOffset(item);
                if (slot == item.DrawerSlot && (position - item.transform.localPosition).sqrMagnitude < 0.00000001f) continue;
                item.Store(this, position, slot);
                moved?.Invoke(item);
            }
        }

        private Vector3 LocalSize(HoldableItem item)
        {
            Vector3 scale = transform.lossyScale, world = item.PlacementSize;
            return new Vector3(world.x / Mathf.Abs(scale.x), world.y / Mathf.Abs(scale.y), world.z / Mathf.Abs(scale.z));
        }
        private Vector3 LocalOffset(HoldableItem item)
        {
            Vector3 scale = transform.lossyScale, offset = item.PlacementOffset;
            return new Vector3(offset.x / scale.x, offset.y / scale.y, offset.z / scale.z);
        }
    }
}
