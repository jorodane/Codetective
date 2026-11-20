#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Codetective.Terminal.Tests.UI
{
    public sealed class TerminalFocusMaskTests
    {
        [TestCase(0f)]
        [TestCase(35f)]
        public void MaskVerticesStayOnTheModelsScreenProjection(float yaw)
        {
            var root = new GameObject("Mask projection", typeof(RectTransform), typeof(Canvas));
            var cameraObject = new GameObject("Reference camera", typeof(Camera));
            var body = new GameObject("Terminal", typeof(MeshFilter), typeof(MeshRenderer));
            var source = new Mesh { vertices = new[] { new Vector3(-0.2f, 0.1f, 0), new Vector3(0.3f, 0.1f, 0), new Vector3(0, -0.4f, 0) }, triangles = new[] { 0, 1, 2 } };
            try
            {
                root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                Camera camera = cameraObject.GetComponent<Camera>(); camera.enabled = false; camera.fieldOfView = 35;
                camera.transform.rotation = Quaternion.Euler(5, yaw, 0);
                body.transform.SetPositionAndRotation(camera.transform.TransformPoint(new Vector3(0.15f, -0.1f, 3)), Quaternion.Euler(10, 20, 5));
                body.GetComponent<MeshFilter>().sharedMesh = source;
                TerminalFocusMaskGraphic mask = CreateMask(root.transform); Canvas.ForceUpdateCanvases(); mask.Configure(camera, body.transform, null);
                using (var vertices = new VertexHelper())
                {
                    Populate(mask, vertices); var stream = new List<UIVertex>(); vertices.GetUIVertexStream(stream);
                    Assert.That(stream.Count, Is.EqualTo(3));
                    for (int i = 0; i < 3; i++)
                    {
                        Vector2 actual = RectTransformUtility.WorldToScreenPoint(null, mask.transform.TransformPoint(stream[i].position));
                        Vector3 expected = camera.WorldToScreenPoint(body.transform.TransformPoint(source.vertices[i]));
                        Assert.That(Vector2.Distance(actual, expected), Is.LessThan(0.02f));
                    }
                }
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(body); Object.DestroyImmediate(source); }
        }
        [Test]
        public void GeometryBehindTheCameraDoesNotProduceAMaskAndCrossingTheNearPlaneIsClipped()
        {
            var root = new GameObject("Mask clipping", typeof(RectTransform), typeof(Canvas));
            var cameraObject = new GameObject("Reference camera", typeof(Camera));
            var body = new GameObject("Terminal", typeof(MeshFilter), typeof(MeshRenderer));
            var source = new Mesh { vertices = new[] { new Vector3(-0.01f, -0.01f, 0.05f), new Vector3(0.01f, -0.01f, 0.2f), new Vector3(0, 0.01f, 0.2f) }, triangles = new[] { 0, 1, 2 } };
            try
            {
                root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                Camera camera = cameraObject.GetComponent<Camera>(); camera.enabled = false; camera.nearClipPlane = 0.1f;
                body.GetComponent<MeshFilter>().sharedMesh = source;
                TerminalFocusMaskGraphic mask = CreateMask(root.transform); Canvas.ForceUpdateCanvases();
                mask.Configure(camera, body.transform, null);
                using (var vertices = new VertexHelper())
                {
                    Populate(mask, vertices); Assert.That(vertices.currentIndexCount, Is.EqualTo(6));
                    var stream = new List<UIVertex>(); vertices.GetUIVertexStream(stream);
                    Assert.That(stream.All(v => !float.IsNaN(v.position.x) && !float.IsInfinity(v.position.x) && !float.IsNaN(v.position.y) && !float.IsInfinity(v.position.y)), Is.True);
                    body.transform.position = new Vector3(0, 0, -2); mask.RefreshProjection(); Populate(mask, vertices);
                    Assert.That(vertices.currentIndexCount, Is.Zero);
                }
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(body); Object.DestroyImmediate(source); }
        }
        [Test]
        public void TheActualTerminalModelIsReadableAndFitsTheOverlayMask()
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Retro Desktop Terminal/Models/Terminal.fbx");
            Assert.That(model, Is.Not.Null);
            Mesh[] meshes = model.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).Where(m => m != null).ToArray();
            Assert.That(meshes, Is.Not.Empty); Assert.That(meshes.All(m => m.isReadable), Is.True);
            Assert.That(meshes.Sum(m => m.triangles.Length) + 16 * 12 * 6, Is.LessThan(65000));
        }
        private static TerminalFocusMaskGraphic CreateMask(Transform parent)
        {
            var go = new GameObject("Stencil", typeof(RectTransform)); go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            return go.AddComponent<TerminalFocusMaskGraphic>();
        }
        private static void Populate(TerminalFocusMaskGraphic mask, VertexHelper vertices) => typeof(TerminalFocusMaskGraphic)
            .GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(mask, new object[] { vertices });
    }
}
#endif
