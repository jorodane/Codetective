using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Codetective.Interaction
{
    internal sealed class InspectionHoverView : IDisposable
    {
        private readonly RectTransform root, tag;
        private readonly Text label;
        private readonly bool supportsOutline;
        private readonly List<(MeshFilter filter, Renderer renderer)> parts = new List<(MeshFilter, Renderer)>();
        private readonly List<(Mesh mesh, Renderer renderer)> visibleParts = new List<(Mesh, Renderer)>();
        private readonly List<(Mesh mesh, Renderer renderer)> visibleContents = new List<(Mesh, Renderer)>();
        private readonly List<MeshFilter> contentFilters = new List<MeshFilter>();
        private Transform visualRoot;

        internal InspectionHoverView(Font font)
        {
            root = (RectTransform)new GameObject("InspectionFeedback", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)).transform;
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 120;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = 0.5f;

            tag = (RectTransform)new GameObject("TargetName", typeof(RectTransform), typeof(Image), typeof(Outline)).transform;
            tag.SetParent(root, false); tag.pivot = new Vector2(0.5f, 0);
            tag.anchorMin = tag.anchorMax = new Vector2(0.5f, 0.5f);
            Image plate = tag.GetComponent<Image>();
            plate.color = new Color(0.06f, 0.06f, 0.06f); plate.raycastTarget = false;
            Outline border = tag.GetComponent<Outline>();
            border.effectColor = new Color(0.52f, 0.52f, 0.52f);
            border.effectDistance = Vector2.one;
            label = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            RectTransform text = label.rectTransform; text.SetParent(tag, false);
            text.anchorMin = Vector2.zero; text.anchorMax = Vector2.one; text.offsetMin = text.offsetMax = Vector2.zero;
            label.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 15; label.alignment = TextAnchor.MiddleCenter; label.raycastTarget = label.supportRichText = false;
            label.color = new Color(0.92f, 0.92f, 0.92f);

            Shader shader = Resources.Load<Shader>("InspectionHighlight");
            supportsOutline = shader != null && shader.isSupported;
            Hide();
        }

        internal void Draw(Camera camera, Transform visual, string name, BoxCollider frontFace = null, ItemPlacementSurface interior = null)
        {
            if (!supportsOutline) { Hide(); return; }
            if (visualRoot != visual)
            {
                visualRoot = visual;
                parts.Clear();
                foreach (MeshFilter filter in visual.GetComponentsInChildren<MeshFilter>(true))
                {
                    HoldableItem item = filter.GetComponentInParent<HoldableItem>();
                    if (item != null && item.transform != visual) continue;
                    if (filter.sharedMesh != null && filter.TryGetComponent(out Renderer renderer)) parts.Add((filter, renderer));
                }
            }

            visibleParts.Clear();
            Bounds bounds = default;
            foreach (var part in parts)
            {
                if (!TryVisibleMesh(camera, part.filter, part.renderer, out Mesh mesh)) continue;
                if (visibleParts.Count == 0) bounds = part.renderer.bounds;
                else bounds.Encapsulate(part.renderer.bounds);
                visibleParts.Add((mesh, part.renderer));
            }
            if (visibleParts.Count == 0) { Hide(); return; }

            Vector3 labelPoint = frontFace != null
                ? frontFace.transform.TransformPoint(frontFace.center + Vector3.up * (frontFace.size.y * 0.5f))
                : bounds.center;
            Vector3 screen = camera.WorldToScreenPoint(labelPoint);
            if (screen.z <= camera.nearClipPlane) { Hide(); return; }
            float top = screen.y;
            if (frontFace == null)
            {
                for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = camera.WorldToScreenPoint(bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z)));
                    if (corner.z > camera.nearClipPlane) top = Mathf.Max(top, corner.y);
                }
            }

            visibleContents.Clear();
            if (interior != null)
            {
                foreach (HoldableItem item in interior.Contents)
                {
                    item.GetComponentsInChildren(true, contentFilters);
                    foreach (MeshFilter filter in contentFilters)
                        if (filter.TryGetComponent(out Renderer renderer) && TryVisibleMesh(camera, filter, renderer, out Mesh mesh))
                            visibleContents.Add((mesh, renderer));
                }
            }
            InspectionOutlineFeature.Show(camera, visibleParts, visibleContents);
            tag.gameObject.SetActive(true);
            if (label.text != name) label.text = name;
            tag.sizeDelta = new Vector2(Mathf.Max(52, label.preferredWidth + 18), 25);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, new Vector2(screen.x, top), null, out Vector2 point);
            Rect rect = root.rect;
            float halfWidth = tag.sizeDelta.x * 0.5f + 6;
            tag.anchoredPosition = new Vector2(Mathf.Clamp(point.x, rect.xMin + halfWidth, rect.xMax - halfWidth),
                Mathf.Clamp(point.y + 7, rect.yMin + 6, rect.yMax - tag.sizeDelta.y - 6));
        }

        private static bool TryVisibleMesh(Camera camera, MeshFilter filter, Renderer renderer, out Mesh mesh)
        {
            mesh = filter != null ? filter.sharedMesh : null;
            // Static batch meshes may also include other objects, in batch space.
            return mesh != null && mesh.subMeshCount > 0 && renderer != null && renderer.enabled &&
                !renderer.forceRenderingOff && renderer.gameObject.activeInHierarchy && !renderer.isPartOfStaticBatch &&
                (camera.cullingMask & (1 << renderer.gameObject.layer)) != 0;
        }

        internal void Hide()
        {
            InspectionOutlineFeature.Hide(visibleParts);
            if (tag != null && tag.gameObject.activeSelf) tag.gameObject.SetActive(false);
        }

        public void Dispose()
        {
            Hide();
            if (root != null) Object.Destroy(root.gameObject);
        }
    }
}
