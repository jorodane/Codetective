using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    /// <summary>Projects the actual terminal triangles into the fade's own overlay stencil.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TerminalFocusMaskGraphic : Graphic
    {
        private sealed class Part
        {
            public MeshFilter filter;
            public Renderer renderer;
            public Vector3[] vertices, world;
            public int[] indices;
            public Matrix4x4 matrix;
            public bool visible;
        }
        private readonly List<Part> parts = new List<Part>();
        private readonly Vector3[] clipped = new Vector3[4];
        private Camera view, overlayCamera;
        private CrtScreen screen;
        private Matrix4x4 lastView, lastProjection, lastCanvas, lastScreen;
        private Rect lastRect, lastViewport, lastScreenRect;
        private float lastBulge, nearDistance;
        private bool hasProjection, screenVisible;

        public void Configure(Camera camera, Transform body, CrtScreen panel)
        {
            view = camera; screen = panel; raycastTarget = false; parts.Clear(); hasProjection = false;
            if (body != null)
                foreach (MeshFilter filter in body.GetComponentsInChildren<MeshFilter>(true))
                {
                    Mesh mesh = filter.sharedMesh; Renderer renderer = filter.GetComponent<Renderer>();
                    if (mesh == null || renderer == null) continue;
                    if (!mesh.isReadable) { Debug.LogWarning("Terminal focus needs Read/Write enabled on its model: " + filter.name, filter); continue; }
                    Vector3[] vertices = mesh.vertices;
                    parts.Add(new Part { filter = filter, renderer = renderer, vertices = vertices, indices = mesh.triangles,
                        world = new Vector3[vertices.Length] });
                }
            RefreshProjection();
        }
        public void RefreshProjection()
        {
            if (view == null) return;
            bool changed = !hasProjection || lastView != view.worldToCameraMatrix || lastProjection != view.projectionMatrix ||
                lastViewport != view.pixelRect || lastCanvas != rectTransform.localToWorldMatrix || lastRect != rectTransform.rect;
            bool visible = screen != null && screen.isActiveAndEnabled;
            if (visible != screenVisible || visible && (lastScreen != screen.transform.localToWorldMatrix || lastBulge != screen.CenterBulge || lastScreenRect != ((RectTransform)screen.transform).rect)) changed = true;
            foreach (Part part in parts)
            {
                bool active = part.filter != null && part.renderer != null && part.renderer.enabled && part.filter.gameObject.activeInHierarchy &&
                    (view.cullingMask & (1 << part.filter.gameObject.layer)) != 0;
                Matrix4x4 matrix = active ? part.filter.transform.localToWorldMatrix : Matrix4x4.identity;
                if (active != part.visible || matrix != part.matrix) changed = true;
                part.visible = active; part.matrix = matrix;
            }
            lastView = view.worldToCameraMatrix; lastProjection = view.projectionMatrix; lastViewport = view.pixelRect;
            nearDistance = view.nearClipPlane; lastCanvas = rectTransform.localToWorldMatrix; lastRect = rectTransform.rect; screenVisible = visible;
            if (visible) { lastScreen = screen.transform.localToWorldMatrix; lastBulge = screen.CenterBulge; lastScreenRect = ((RectTransform)screen.transform).rect; }
            hasProjection = true;
            if (changed) SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (view == null) return;
            overlayCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            foreach (Part part in parts)
            {
                if (!part.visible) continue;
                for (int i = 0; i < part.vertices.Length; i++) part.world[i] = part.matrix.MultiplyPoint3x4(part.vertices[i]);
                for (int i = 0; i + 2 < part.indices.Length; i += 3)
                    AddTriangle(mesh, part.world[part.indices[i]], part.world[part.indices[i + 1]], part.world[part.indices[i + 2]]);
            }
            if (!screenVisible) return;
            // Include the curved canvas itself, even if the model has an open screen aperture.
            Rect rect = ((RectTransform)screen.transform).rect;
            const int columns = 16, rows = 12;
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < columns; x++)
                {
                    Vector3 a = ScreenPoint(rect, (float)x / columns, (float)y / rows);
                    Vector3 b = ScreenPoint(rect, (float)(x + 1) / columns, (float)y / rows);
                    Vector3 c = ScreenPoint(rect, (float)(x + 1) / columns, (float)(y + 1) / rows);
                    Vector3 d = ScreenPoint(rect, (float)x / columns, (float)(y + 1) / rows);
                    AddTriangle(mesh, a, b, c); AddTriangle(mesh, a, c, d);
                }
        }
        private Vector3 ScreenPoint(Rect rect, float x, float y) => screen.transform.TransformPoint(
            screen.BendPoint(screen.transform, new Vector3(Mathf.Lerp(rect.xMin, rect.xMax, x), Mathf.Lerp(rect.yMin, rect.yMax, y), 0)));
        private float EyeDepth(Vector3 point) => -lastView.MultiplyPoint3x4(point).z - nearDistance;
        private void AddTriangle(VertexHelper mesh, Vector3 a, Vector3 b, Vector3 c)
        {
            // Clip before perspective division: points behind the camera must never form
            // enormous screen-space triangles extending away from the terminal.
            int count = 0; Vector3 previous = c; float previousDepth = EyeDepth(c);
            for (int i = 0; i < 3; i++)
            {
                Vector3 current = i == 0 ? a : i == 1 ? b : c; float depth = EyeDepth(current);
                if ((depth >= 0) != (previousDepth >= 0)) clipped[count++] = Vector3.LerpUnclamped(previous, current, previousDepth / (previousDepth - depth));
                if (depth >= 0) clipped[count++] = current;
                previous = current; previousDepth = depth;
            }
            if (count < 3 || mesh.currentVertCount + count > 65000) return;
            int start = mesh.currentVertCount;
            for (int i = 0; i < count; i++)
            {
                Vector3 pixel = view.WorldToScreenPoint(clipped[i]);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, pixel, overlayCamera, out Vector2 local);
                mesh.AddVert(local, Color.white, Vector2.zero);
            }
            for (int i = 1; i + 1 < count; i++) mesh.AddTriangle(start, start + i, start + i + 1);
        }
    }
}
