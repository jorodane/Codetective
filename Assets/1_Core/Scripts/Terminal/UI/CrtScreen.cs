using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class CrtScreen : MonoBehaviour
    {
        [Tooltip("Center protrusion toward the viewer in this panel's local units. 32 = 1 cm in MainScene. Zero is flat.")]
        [SerializeField, Range(0f, 96f)] private float centerBulge = 32f;
        [Tooltip("Maximum size of a curved mesh cell in panel units. Smaller cells give smoother curvature.")]
        [SerializeField, Range(8f, 128f)] private float segmentSize = 48f;

        private readonly List<UIVertex> sourceVertices = new List<UIVertex>();
        private RectTransform panel;
        private bool dirty = true;

        private RectTransform Panel => panel != null ? panel : panel = (RectTransform)transform;
        public bool IsCurved => isActiveAndEnabled && centerBulge > 0f &&
                                Panel.rect.width > 0f && Panel.rect.height > 0f;

        private void OnEnable() => dirty = true;
        private void OnValidate() => dirty = true;
        private void OnRectTransformDimensionsChange() => dirty = true;
        private void OnDisable() => RefreshGraphics();

        private void Update()
        {
            if (!dirty)
                return;
            dirty = false;
            RefreshGraphics();
        }

        private void RefreshGraphics()
        {
            foreach (var child in GetComponentsInChildren<Graphic>(true))
                child.SetVerticesDirty();
            // The InputField owns a separate mesh for its caret and text selection.
            foreach (var field in GetComponentsInChildren<TerminalInputField>(true))
                field.ForceLabelUpdate();
        }

        public Vector3 BendPoint(Transform owner, Vector3 point)
        {
            Vector3 local = Panel.InverseTransformPoint(owner.TransformPoint(point));
            Rect rect = Panel.rect;
            local.z += (float)CrtSurfaceMath.Height(local.x - rect.center.x, local.y - rect.center.y,
                rect.width * 0.5f, rect.height * 0.5f, centerBulge);
            return owner.InverseTransformPoint(Panel.TransformPoint(local));
        }

        public void BendMesh(VertexHelper mesh, Transform owner)
        {
            if (!IsCurved || mesh.currentVertCount == 0)
                return;

            sourceVertices.Clear();
            mesh.GetUIVertexStream(sourceVertices);
            mesh.Clear();
            Matrix4x4 toPanel = Panel.worldToLocalMatrix * owner.localToWorldMatrix;
            Matrix4x4 fromPanel = owner.worldToLocalMatrix * Panel.localToWorldMatrix;
            Rect rect = Panel.rect;

            // uGUI Text and simple Images emit two triangles per quad. Subdivide large
            // quads first: moving only their four corners cannot make a convex screen.
            for (int i = 0; i + 5 < sourceVertices.Count; i += 6)
            {
                UIVertex a = sourceVertices[i];
                UIVertex b = sourceVertices[i + 1];
                UIVertex c = sourceVertices[i + 2];
                UIVertex d = sourceVertices[i + 4];
                Vector3 pa = toPanel.MultiplyPoint3x4(a.position);
                Vector3 pb = toPanel.MultiplyPoint3x4(b.position);
                Vector3 pc = toPanel.MultiplyPoint3x4(c.position);
                Vector3 pd = toPanel.MultiplyPoint3x4(d.position);
                int columns = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max((pa - pd).magnitude, (pb - pc).magnitude) /
                    segmentSize), 1, 64);
                int rows = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max((pa - pb).magnitude, (pd - pc).magnitude) /
                    segmentSize), 1, 64);

                for (int row = 0; row < rows; row++)
                {
                    float v0 = (float)row / rows;
                    float v1 = (float)(row + 1) / rows;
                    for (int column = 0; column < columns; column++)
                    {
                        float u0 = (float)column / columns;
                        float u1 = (float)(column + 1) / columns;
                        int start = mesh.currentVertCount;
                        AddVertex(mesh, Sample(a, b, c, d, u0, v0), toPanel, fromPanel, rect);
                        AddVertex(mesh, Sample(a, b, c, d, u0, v1), toPanel, fromPanel, rect);
                        AddVertex(mesh, Sample(a, b, c, d, u1, v1), toPanel, fromPanel, rect);
                        AddVertex(mesh, Sample(a, b, c, d, u1, v0), toPanel, fromPanel, rect);
                        mesh.AddTriangle(start, start + 1, start + 2);
                        mesh.AddTriangle(start + 2, start + 3, start);
                    }
                }
            }
            sourceVertices.Clear();
        }

        private void AddVertex(VertexHelper mesh, UIVertex vertex, Matrix4x4 toPanel,
            Matrix4x4 fromPanel, Rect rect)
        {
            Vector3 p = toPanel.MultiplyPoint3x4(vertex.position);
            p.z += (float)CrtSurfaceMath.Height(p.x - rect.center.x, p.y - rect.center.y,
                rect.width * 0.5f, rect.height * 0.5f, centerBulge);
            vertex.position = fromPanel.MultiplyPoint3x4(p);
            mesh.AddVert(vertex);
        }

        private static UIVertex Sample(UIVertex a, UIVertex b, UIVertex c, UIVertex d, float u, float v)
        {
            return Lerp(Lerp(a, d, u), Lerp(b, c, u), v);
        }

        private static UIVertex Lerp(UIVertex a, UIVertex b, float t)
        {
            a.position = Vector3.LerpUnclamped(a.position, b.position, t);
            a.color = Color32.Lerp(a.color, b.color, t);
            a.uv0 = Vector4.LerpUnclamped(a.uv0, b.uv0, t);
            a.uv1 = Vector4.LerpUnclamped(a.uv1, b.uv1, t);
            a.uv2 = Vector4.LerpUnclamped(a.uv2, b.uv2, t);
            a.uv3 = Vector4.LerpUnclamped(a.uv3, b.uv3, t);
            return a;
        }

        public bool TryProjectPointer(Vector2 screenPosition, Camera camera, bool requireInside,
            out Vector2 flatScreenPosition, out Vector3 worldHit)
        {
            flatScreenPosition = screenPosition;
            worldHit = default;
            if (!IsCurved || camera == null)
                return false;

            Ray ray = camera.ScreenPointToRay(screenPosition);
            Vector3 origin = Panel.InverseTransformPoint(ray.origin);
            Vector3 direction = Panel.InverseTransformVector(ray.direction);
            Rect rect = Panel.rect;
            if (!CrtSurfaceMath.TryIntersect(origin.x - rect.center.x, origin.y - rect.center.y, origin.z,
                    direction.x, direction.y, direction.z, rect.width * 0.5f, rect.height * 0.5f,
                    centerBulge, requireInside, out double distance))
                return false;

            Vector3 hit = origin + direction * (float)distance;
            worldHit = Panel.TransformPoint(hit);
            hit.z = 0f;
            flatScreenPosition = camera.WorldToScreenPoint(Panel.TransformPoint(hit));
            return true;
        }

        public Vector3 WorldNormal(Vector3 worldHit)
        {
            Vector3 p = Panel.InverseTransformPoint(worldHit);
            Rect rect = Panel.rect;
            var normal = new Vector3(4f * centerBulge * (p.x - rect.center.x) / (rect.width * rect.width),
                4f * centerBulge * (p.y - rect.center.y) / (rect.height * rect.height), -1f);
            return Panel.worldToLocalMatrix.transpose.MultiplyVector(normal).normalized;
        }
    }
}
