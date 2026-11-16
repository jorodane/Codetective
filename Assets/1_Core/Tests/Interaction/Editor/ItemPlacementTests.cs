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
