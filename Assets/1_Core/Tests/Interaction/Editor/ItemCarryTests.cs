using System.Reflection;
using Codetective.Interaction;
using NUnit.Framework;
using UnityEngine;

namespace Codetective.Tests.Interaction
{
    public sealed class ItemCarryTests
    {
        [Test]
        public void SceneItemsBecomeInteractableOnlyAfterExplicitRegistration()
        {
            var system = new GameObject("Carry"); var camera = new GameObject("Camera"); var paper = new GameObject("Paper");
            try
            {
                HoldableItem item = paper.AddComponent<HoldableItem>();
                ItemCarryController carry = system.AddComponent<ItemCarryController>(); carry.Initialize(camera.transform);
                Assert.That(item.CanInteract(null), Is.False);
                carry.Register(item); Assert.That(item.CanInteract(null), Is.True);
                Assert.That(carry.TryPickup(item), Is.True);
            }
            finally { Object.DestroyImmediate(system); Object.DestroyImmediate(paper); Object.DestroyImmediate(camera); }
        }
        [Test]
        public void PickupDisablesPhysicsAndPutDownRestoresTheAuthoredPoseAndColliderStates()
        {
            var desk = new GameObject("Desk"); var camera = new GameObject("Camera"); var system = new GameObject("Carry");
            GameObject paper = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                paper.transform.SetParent(desk.transform, false); paper.transform.localPosition = new Vector3(1, 2, 3);
                paper.transform.localRotation = Quaternion.Euler(0, 25, 0); paper.transform.localScale = new Vector3(0.4f, 0.01f, 0.3f);
                Collider collider = paper.GetComponent<Collider>(); Collider disabled = paper.AddComponent<SphereCollider>(); disabled.enabled = false;
                Rigidbody body = paper.AddComponent<Rigidbody>(); body.isKinematic = false;
                ItemCarryController carry = system.AddComponent<ItemCarryController>(); carry.Initialize(camera.transform);
                HoldableItem item = paper.AddComponent<HoldableItem>(); item.Configure("paper", "신문", HeldItemUse.Newspaper); carry.Register(item);
                Assert.That(carry.TryPickup(item), Is.True); Assert.That(paper.transform.parent, Is.SameAs(camera.transform));
                Assert.That(collider.enabled, Is.False); Assert.That(body.isKinematic, Is.True);
                carry.SetPresentationVisible(false); Assert.That(paper.GetComponent<Renderer>().enabled, Is.False);
                carry.PutDown();
                Assert.That(paper.transform.parent, Is.SameAs(desk.transform)); Assert.That(paper.transform.localPosition, Is.EqualTo(new Vector3(1, 2, 3)));
                Assert.That(Quaternion.Angle(paper.transform.localRotation, Quaternion.Euler(0, 25, 0)), Is.LessThan(0.001));
                Assert.That(paper.transform.localScale, Is.EqualTo(new Vector3(0.4f, 0.01f, 0.3f)));
                Assert.That(collider.enabled, Is.True); Assert.That(disabled.enabled, Is.False); Assert.That(body.isKinematic, Is.False);
                Assert.That(paper.GetComponent<Renderer>().enabled, Is.True);
            }
            finally { Object.DestroyImmediate(system); Object.DestroyImmediate(camera); Object.DestroyImmediate(desk); }
        }
        [TestCase(16)]
        [TestCase(60)]
        public void ExaminedObjectsFitTheLeftSideAndRotateWithoutChangingTheirPlacementSize(float fov)
        {
            var system = new GameObject("Carry"); var camera = new GameObject("Camera", typeof(Camera));
            GameObject cup = GameObject.CreatePrimitive(PrimitiveType.Cube); var desk = new GameObject("Desk");
            try
            {
                Camera view = camera.GetComponent<Camera>(); view.fieldOfView = fov; view.aspect = 16f / 9f;
                cup.transform.localScale = new Vector3(0.08f, 0.12f, 0.08f);
                HoldableItem item = cup.AddComponent<HoldableItem>();
                ItemCarryController carry = system.AddComponent<ItemCarryController>(); carry.Initialize(camera.transform);
                Assert.That(carry.TryPickup(item), Is.True);
                Bounds local = cup.GetComponent<MeshFilter>().sharedMesh.bounds;
                for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 point = view.WorldToViewportPoint(cup.transform.TransformPoint(local.center + Vector3.Scale(local.extents, new Vector3(x, y, z))));
                    Assert.That(point.x, Is.InRange(0.035f, 0.61f)); Assert.That(point.y, Is.InRange(0.13f, 0.91f));
                    Assert.That(point.z, Is.GreaterThan(view.nearClipPlane));
                }
                Quaternion front = cup.transform.localRotation;
                Vector3 scale = cup.transform.localScale, position = cup.transform.localPosition;
                var frame = typeof(ItemCarryController).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
                carry.RotateInspection(new Vector2(75f, 25f)); frame.Invoke(carry, null);
                Assert.That(Quaternion.Angle(front, cup.transform.localRotation), Is.GreaterThan(60));
                Assert.That(Vector3.Distance(cup.transform.localScale, scale), Is.LessThan(0.001f));
                Assert.That(Vector3.Distance(cup.transform.localPosition, position), Is.LessThan(0.001f));
                carry.ResetInspectionRotation(); frame.Invoke(carry, null);
                Assert.That(Quaternion.Angle(front, cup.transform.localRotation), Is.LessThan(0.1f));
                ItemPlacementSurface surface = desk.AddComponent<ItemPlacementSurface>(); surface.Initialize("desk.top", null, new Bounds(new Vector3(4, 0, 0), Vector3.one));
                Assert.That(carry.BeginPlacement(), Is.True); Assert.That(carry.TryPlaceOn(surface), Is.True);
                Assert.That(Vector3.Distance(cup.transform.lossyScale, new Vector3(0.08f, 0.12f, 0.08f)), Is.LessThan(0.001f));
            }
            finally { Object.DestroyImmediate(system); Object.DestroyImmediate(cup); Object.DestroyImmediate(desk); Object.DestroyImmediate(camera); }
        }
        [Test]
        public void UseRequestsAreBlockedDuringTextInputAndOnlyOneObjectCanBeExamined()
        {
            var system = new GameObject("Carry"); var camera = new GameObject("Camera"); var a = new GameObject("A"); var b = new GameObject("B");
            try
            {
                ItemCarryController carry = system.AddComponent<ItemCarryController>(); carry.Initialize(camera.transform);
                HoldableItem one = a.AddComponent<HoldableItem>(), two = b.AddComponent<HoldableItem>();
                int uses = 0; carry.Used += item => uses++;
                Assert.That(carry.TryPickup(one), Is.True); Assert.That(carry.TryPickup(two), Is.False);
                Assert.That(carry.TryUse(true), Is.False); Assert.That(uses, Is.Zero);
                Assert.That(carry.TryUse(false), Is.True); Assert.That(uses, Is.EqualTo(1));
                carry.PutDown(); Assert.That(carry.TryUse(false), Is.False); Assert.That(carry.TryPickup(two), Is.True);
                system.SetActive(false); Assert.That(carry.HeldItem, Is.SameAs(two)); Assert.That(carry.IsExamining, Is.False);
                Assert.That(carry.TryUse(false), Is.False);
                system.SetActive(true); Assert.That(carry.IsExamining, Is.True);
                carry.PutDown(); Assert.That(b.transform.parent, Is.Null);
            }
            finally { Object.DestroyImmediate(system); Object.DestroyImmediate(camera); Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
        }
        [Test]
        public void DisablingTheCameraWhileHoldingAnItemNeverReparentsDuringActivationCallbacks()
        {
            var camera = new GameObject("Main Camera", typeof(Camera)); var desk = new GameObject("Desk");
            GameObject paper = GameObject.CreatePrimitive(PrimitiveType.Cube); paper.transform.SetParent(desk.transform, false);
            try
            {
                ItemCarryController carry = camera.AddComponent<ItemCarryController>(); carry.Initialize(camera.transform);
                HoldableItem item = paper.AddComponent<HoldableItem>(); item.Configure("newspaper", "신문", HeldItemUse.Newspaper);
                Assert.That(carry.TryPickup(item), Is.True); Assert.That(paper.transform.parent, Is.SameAs(camera.transform));
                camera.SetActive(false);
                Assert.That(paper.transform.parent, Is.SameAs(camera.transform));
                Assert.That(paper.GetComponent<Renderer>().enabled, Is.False);
                carry.SetPresentationVisible(true); Assert.That(paper.GetComponent<Renderer>().enabled, Is.False);
                camera.SetActive(true); Assert.That(paper.GetComponent<Renderer>().enabled, Is.True);
                carry.PutDown(); Assert.That(paper.transform.parent, Is.SameAs(desk.transform));
                carry.TryPickup(item); camera.SetActive(false); carry.EndSession();
                Assert.That(carry.HeldItem, Is.Null); Assert.That(paper.transform.parent, Is.SameAs(camera.transform));
            }
            finally { Object.DestroyImmediate(paper); Object.DestroyImmediate(camera); Object.DestroyImmediate(desk); }
        }
    }
}
