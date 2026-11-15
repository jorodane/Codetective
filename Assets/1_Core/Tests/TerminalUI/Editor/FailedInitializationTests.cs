#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Codetective.Terminal.Tests.UI
{
    public sealed class FailedInitializationTests
    {
        [Test]
        public void AControllerWithoutARuntimeCanStopWithoutAnotherExceptionOrChangingTheCurtain()
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Codetective.Gameplay.KernelGameController")).First(t => t != null);
            var root = new GameObject("Uninitialized controller"); root.SetActive(false);
            var curtain = new GameObject("Curtain"); curtain.transform.SetParent(root.transform, false);
            curtain.transform.localScale = new Vector3(2, 3, 0.1f);
            try
            {
                Component controller = root.AddComponent(type);
                type.GetField("curtainCloth", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(controller, curtain.transform);
                Assert.That(type.GetProperty("Runtime").GetValue(controller), Is.Null);
                Assert.That(() => Invoke(controller, "OnDestroy"), Throws.Nothing);
                Assert.That(curtain.transform.localScale, Is.EqualTo(new Vector3(2, 3, 0.1f)));
                Assert.That(() => Invoke(controller, "Start"), Throws.Nothing);
                Assert.That(((Behaviour)controller).enabled, Is.False);
                Assert.That(() => Invoke(controller, "Update"), Throws.Nothing);
                Assert.That(() => type.GetMethod("SaveNow").Invoke(controller, null), Throws.Nothing);
            }
            finally { Object.DestroyImmediate(root); }
        }
        private static void Invoke(Component controller, string method) => controller.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, null);
    }
}
#endif
