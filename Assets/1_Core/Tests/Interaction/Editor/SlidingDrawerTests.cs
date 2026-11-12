using Codetective.Interaction;
using Codetective.Operations;
using NUnit.Framework;
using UnityEngine;

namespace Codetective.Tests.Interaction
{
    public sealed class SlidingDrawerTests
    {
        [Test]
        public void DrawersRequireInspectionPermissionAndRestoreSavedOpenPositions()
        {
            var root = new GameObject("Drawer"); Vector3 closed = new Vector3(2, 3, 4); root.transform.localPosition = closed;
            try
            {
                var runtime = new KernelRuntime(); runtime.SetDrawerOpen("one", true);
                SlidingDrawer drawer = root.AddComponent<SlidingDrawer>(); drawer.Initialize(runtime, "one");
                Assert.That(root.transform.localPosition.z, Is.EqualTo(closed.z + 0.34f).Within(0.001));
                Assert.That(drawer.CanInteract("drawer"), Is.False);
                drawer.Interact(); Assert.That(runtime.IsDrawerOpen("one"), Is.True);
                drawer.SetInteractionEnabled(true);
                Assert.That(drawer.CanInteract("drawer"), Is.True); Assert.That(drawer.CanInteract("monitor"), Is.False);
                drawer.Interact(); Assert.That(runtime.IsDrawerOpen("one"), Is.False);
                drawer.Interact(); Assert.That(runtime.IsDrawerOpen("one"), Is.True);
            }
            finally { Object.DestroyImmediate(root); }
        }
        [Test]
        public void TogglingOneDrawerDoesNotChangeOtherDrawers()
        {
            var one = new GameObject("One"); var two = new GameObject("Two");
            try
            {
                var runtime = new KernelRuntime();
                SlidingDrawer a = one.AddComponent<SlidingDrawer>(), b = two.AddComponent<SlidingDrawer>();
                a.Initialize(runtime, "one"); b.Initialize(runtime, "two"); a.SetInteractionEnabled(true); b.SetInteractionEnabled(true);
                a.Interact(); Assert.That(a.IsOpen, Is.True); Assert.That(b.IsOpen, Is.False);
            }
            finally { Object.DestroyImmediate(one); Object.DestroyImmediate(two); }
        }
    }
}
