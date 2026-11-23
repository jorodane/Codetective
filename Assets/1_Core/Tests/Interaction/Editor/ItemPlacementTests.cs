using Codetective.Interaction;
using Codetective.Operations;
using NUnit.Framework;
using UnityEngine;

namespace Codetective.Tests.Interaction
{
    public sealed class ItemPlacementTests
    {
        [Test]
        public void ANewDrawerInteriorCreatesAndReusesItsCollider()
        {
            var root = new GameObject("DrawerInterior");
            try
            {
                var surface = root.AddComponent<ItemPlacementSurface>();
                Bounds interior = new Bounds(new Vector3(1, 2, 3), new Vector3(0.4f, 0.1f, 0.4f));
                Assert.That(() => surface.Initialize("drawer", null, interior), Throws.Nothing);
                BoxCollider collider = root.GetComponent<BoxCollider>();
                Assert.That(collider, Is.Not.Null); Assert.That(collider.isTrigger, Is.True);
                Assert.That(collider.center, Is.EqualTo(interior.center)); Assert.That(collider.size, Is.EqualTo(interior.size));
                surface.Initialize("drawer", null, interior);
                Assert.That(root.GetComponents<BoxCollider>().Length, Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(root); }
        }
        [Test]
        public void AClosedMovingOrTooSmallDrawerRejectsPlacement()
        {
            var root = new GameObject("Drawer"); var paper = new GameObject("Paper");
            try
            {
                var runtime = new KernelRuntime();
                SlidingDrawer drawer = root.AddComponent<SlidingDrawer>(); drawer.Initialize(runtime, "drawer"); drawer.SetInteractionEnabled(true);
                ItemPlacementSurface surface = root.AddComponent<ItemPlacementSurface>(); surface.Initialize("drawer", drawer, new Bounds(Vector3.zero, new Vector3(0.4f, 0.1f, 0.4f)));
                HoldableItem item = paper.AddComponent<HoldableItem>(); item.Configure("paper", "신문", HeldItemUse.Newspaper);
                Assert.That(surface.TryGetPosition(item, out _), Is.False);
                drawer.Interact(); Assert.That(surface.TryGetPosition(item, out _), Is.False); // Still sliding open.
                drawer.Initialize(runtime, "drawer"); Assert.That(surface.TryGetPosition(item, out _), Is.True);
                surface.Initialize("drawer", drawer, new Bounds(Vector3.zero, new Vector3(0.1f, 0.1f, 0.1f)));
                Assert.That(surface.TryGetPosition(item, out _), Is.False);
            }
            finally { Object.DestroyImmediate(paper); Object.DestroyImmediate(root); }
        }
        [Test]
        public void ANewspaperSnapsIntoANarrowDrawerEvenAtItsOuterEdge()
        {
            var root = new GameObject("Drawer"); var paper = new GameObject("Paper");
            try
            {
                var runtime = new KernelRuntime(); runtime.SetDrawerOpen("drawer", true);
                SlidingDrawer drawer = root.AddComponent<SlidingDrawer>(); drawer.Initialize(runtime, "drawer"); drawer.SetInteractionEnabled(true);
                ItemPlacementSurface surface = root.AddComponent<ItemPlacementSurface>();
                var interior = new Bounds(Vector3.zero, new Vector3(HoldableItem.NewspaperRollSize.x, 0.1f, 0.4f));
                surface.Initialize("drawer", drawer, interior);
                HoldableItem item = paper.AddComponent<HoldableItem>(); item.Configure("paper", "신문", HeldItemUse.Newspaper);

                Vector3 edge = surface.transform.TransformPoint(new Vector3(interior.max.x, 0, 0));
                Assert.That(surface.TryGetPosition(item, edge, out Vector3 snapped), Is.True);
                Assert.That(snapped.x, Is.Zero.Within(0.001f));
                Assert.That(snapped.y - item.PlacementSize.y * 0.5f, Is.EqualTo(interior.min.y).Within(0.001f));
            }
            finally { Object.DestroyImmediate(paper); Object.DestroyImmediate(root); }
        }
        [Test]
        public void DrawerContentsDoNotOverlapAndPickupFreesTheirOccupiedSpace()
        {
            var root = new GameObject("Carry"); var camera = new GameObject("Camera"); var drawer = new GameObject("Drawer");
            var paper = new GameObject("Paper"); var second = new GameObject("Second");
            try
            {
                ItemCarryController carry = root.AddComponent<ItemCarryController>(); carry.Initialize(camera.transform);
                HoldableItem a = paper.AddComponent<HoldableItem>(), b = second.AddComponent<HoldableItem>();
                a.Configure("a", "신문", HeldItemUse.Newspaper); b.Configure("b", "신문", HeldItemUse.Newspaper);
                carry.Register(a); carry.Register(b);
                ItemPlacementSurface surface = drawer.AddComponent<ItemPlacementSurface>();
                surface.Initialize("drawer", null, new Bounds(Vector3.zero, a.PlacementSize));
                Assert.That(carry.RestoreStorage(a, surface), Is.True);
                Assert.That(surface.TryGetPosition(b, out _), Is.False);
                Assert.That(carry.TryPickup(a), Is.True); Assert.That(a.IsStored, Is.False);
                Assert.That(surface.TryGetPosition(b, out _), Is.True);
                carry.ManipulationAllowed = () => false;
                Assert.That(carry.TryUse(false), Is.False); Assert.That(carry.BeginPlacement(), Is.False);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(paper); Object.DestroyImmediate(second); Object.DestroyImmediate(drawer); Object.DestroyImmediate(camera); }
        }

        [Test]
        public void TheDrawerGridFitsEightFullWidthRollsAndRejectsTheNinth()
        {
            var root = new GameObject("Grid test");
            try
            {
                var carry = root.AddComponent<ItemCarryController>(); carry.Initialize(root.transform);
                var drawerObject = new GameObject("Drawer"); drawerObject.transform.SetParent(root.transform, false);
                SlidingDrawer drawer = drawerObject.AddComponent<SlidingDrawer>(); drawer.Initialize(new KernelRuntime(), "drawer");
                var surface = drawerObject.AddComponent<ItemPlacementSurface>();
                surface.Initialize("drawer", drawer, new Bounds(Vector3.zero, new Vector3(0.32f, 0.1f, 0.44f)));
                Assert.That(ItemPlacementSurface.DrawerSize, Is.EqualTo(new Vector3Int(4, 2, 8)));
                var occupied = new System.Collections.Generic.HashSet<int>();
                for (int i = 0; i < 9; i++)
                {
                    var paper = new GameObject("Paper " + i); paper.transform.SetParent(root.transform, false);
                    HoldableItem item = paper.AddComponent<HoldableItem>();
                    item.Configure("paper" + i, "신문", HeldItemUse.Newspaper); carry.Register(item);
                    Assert.That(carry.RestoreStorage(item, surface), Is.EqualTo(i < 8));
                    if (i < 8)
                    {
                        Assert.That(occupied.Add(item.DrawerSlot), Is.True);
                        Assert.That(item.transform.localPosition.x, Is.Zero.Within(0.0001f));
                    }
                }
                Assert.That(surface.ItemCount, Is.EqualTo(8));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void ClosingKeepsAStackInPlaceAndMovingItsSupportSettlesTheUpperItem()
        {
            var root = new GameObject("Stack test"); root.transform.position = Vector3.one * 50;
            try
            {
                var carry = root.AddComponent<ItemCarryController>(); carry.Initialize(root.transform);
                ItemPlacementSurface Surface(string id, Vector3 position, bool container)
                {
                    var holder = new GameObject(id); holder.transform.SetParent(root.transform, false); holder.transform.localPosition = position;
                    SlidingDrawer drawer = null;
                    if (container)
                    {
                        var runtime = new KernelRuntime(); runtime.SetDrawerOpen(id, true);
                        drawer = holder.AddComponent<SlidingDrawer>(); drawer.Initialize(runtime, id); drawer.SetInteractionEnabled(true);
                    }
                    var surface = holder.AddComponent<ItemPlacementSurface>();
                    surface.Initialize(id, drawer, new Bounds(Vector3.zero, new Vector3(0.4f, 0.1f, 0.8f)));
                    return surface;
                }
                HoldableItem Paper(string id)
                {
                    var paper = new GameObject(id); paper.transform.SetParent(root.transform, false);
                    var item = paper.AddComponent<HoldableItem>(); item.Configure(id, "기록", HeldItemUse.Profile);
                    item.ConfigurePaper(new Vector3(0.09f, 0.02f, 0.09f)); carry.Register(item);
                    return item;
                }
                ItemPlacementSurface drawerSurface = Surface("drawer", Vector3.forward, true);
                ItemPlacementSurface desktop = Surface("desk", Vector3.right, false);
                HoldableItem lower = Paper("lower"), upper = Paper("upper");
                Assert.That(carry.RestoreStorage(lower, drawerSurface, Vector3.zero, 1), Is.True);
                Assert.That(carry.RestoreStorage(upper, drawerSurface, Vector3.zero, 33), Is.True);
                Vector3 lowerPosition = lower.transform.localPosition, upperPosition = upper.transform.localPosition;
                Assert.That(upperPosition.y - lowerPosition.y, Is.EqualTo(0.02f).Within(0.0001f));
                Assert.That(carry.TryPickup(lower), Is.True); carry.PutDown();
                Assert.That(lower.DrawerSlot, Is.EqualTo(1));
                Assert.That(lower.transform.localPosition, Is.EqualTo(lowerPosition));
                Assert.That(upper.transform.localPosition, Is.EqualTo(upperPosition));

                Assert.That(carry.TryPickup(lower), Is.True);
                Assert.That(carry.BeginPlacement(), Is.True);
                Assert.That(carry.TryPlaceOn(desktop), Is.True);
                Assert.That(upper.DrawerSlot, Is.EqualTo(1));
                Assert.That(upper.transform.localPosition, Is.EqualTo(lowerPosition));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void CursorPlacementUsesTheChosenPointAndRejectsOccupiedOrOutOfBoundsPositions()
        {
            var system = new GameObject("Carry"); var camera = new GameObject("Camera"); var desk = new GameObject("Desk");
            var first = new GameObject("First"); var second = new GameObject("Second");
            try
            {
                desk.transform.SetPositionAndRotation(new Vector3(3, 2, 1), Quaternion.Euler(0, 35, 0));
                desk.transform.localScale = Vector3.one * 0.5f;
                ItemCarryController carry = system.AddComponent<ItemCarryController>(); carry.Initialize(camera.transform);
                HoldableItem a = first.AddComponent<HoldableItem>(), b = second.AddComponent<HoldableItem>();
                a.ConfigurePaper(new Vector3(0.12f, 0.004f, 0.12f)); b.ConfigurePaper(new Vector3(0.12f, 0.004f, 0.12f));
                ItemPlacementSurface surface = desk.AddComponent<ItemPlacementSurface>(); surface.Initialize("desk.top", null, new Bounds(Vector3.zero, Vector3.one));
                Vector3 point = desk.transform.TransformPoint(new Vector3(0.2f, 0, -0.15f));
                Assert.That(carry.TryPickup(a), Is.True); Assert.That(carry.IsExamining, Is.True);
                Assert.That(carry.BeginPlacement(), Is.True); Assert.That(carry.IsExamining, Is.False);
                Assert.That(carry.TryPlaceOn(surface, point), Is.True);
                Assert.That(a.transform.localPosition.x, Is.EqualTo(0.2f).Within(0.001));
                Assert.That(a.transform.localPosition.z, Is.EqualTo(-0.15f).Within(0.001));
                Assert.That(surface.TryGetPosition(b, point, out _), Is.False);
                Assert.That(surface.TryGetPosition(b, desk.transform.TransformPoint(Vector3.right), out _), Is.False);
                carry.TryPickup(a); Assert.That(surface.TryGetPosition(b, point, out _), Is.True);
            }
            finally { Object.DestroyImmediate(system); Object.DestroyImmediate(first); Object.DestroyImmediate(second); Object.DestroyImmediate(desk); Object.DestroyImmediate(camera); }
        }
        [Test]
        public void ScaledDrawersUseWorldDimensionsAndDoNotShrinkStoredObjects()
        {
            var root = new GameObject("Carry"); var camera = new GameObject("Camera"); var drawer = new GameObject("Drawer"); var paper = new GameObject("Paper");
            try
            {
                drawer.transform.localScale = Vector3.one * 0.5f;
                ItemCarryController carry = root.AddComponent<ItemCarryController>(); carry.Initialize(camera.transform);
                HoldableItem item = paper.AddComponent<HoldableItem>(); item.Configure("paper", "신문", HeldItemUse.Newspaper);
                item.ConfigurePaper(new Vector3(0.44f, 0.004f, 0.28f)); carry.Register(item);
                ItemPlacementSurface surface = drawer.AddComponent<ItemPlacementSurface>();
                surface.Initialize("drawer", null, new Bounds(Vector3.zero, new Vector3(0.4f, 0.1f, 0.4f)));
                Assert.That(surface.TryGetPosition(item, out _), Is.False); // Only 20cm wide in the world.
                surface.Initialize("drawer", null, new Bounds(Vector3.zero, Vector3.one));
                Assert.That(carry.RestoreStorage(item, surface), Is.True);
                Assert.That(Vector3.Distance(item.transform.lossyScale, Vector3.one), Is.LessThan(0.001f));
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(paper); Object.DestroyImmediate(drawer); Object.DestroyImmediate(camera); }
        }
    }
}
