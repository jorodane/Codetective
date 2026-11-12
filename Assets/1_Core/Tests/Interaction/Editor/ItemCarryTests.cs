using Codetective.Interaction;
using NUnit.Framework;
using UnityEngine;

namespace Codetective.Tests.Interaction
{
    public sealed class ItemCarryTests
    {
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
        [Test]
        public void EUseIsBlockedDuringTextInputAndOnlyOneObjectCanBeHeld()
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
                system.SetActive(false); Assert.That(carry.HeldItem, Is.Null); Assert.That(b.transform.parent, Is.Null);
            }
            finally { Object.DestroyImmediate(system); Object.DestroyImmediate(camera); Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
        }
    }
}
