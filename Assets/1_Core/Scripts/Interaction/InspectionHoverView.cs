using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Codetective.Interaction
{
    internal sealed class InspectionHoverView : IDisposable
    {
        private readonly RectTransform root, tag;
        private readonly Text label;
        private readonly Material silhouetteMask, outline;
        private readonly List<(MeshFilter filter, Renderer renderer)> parts = new List<(MeshFilter, Renderer)>();
        private Transform visualRoot;

        internal InspectionHoverView(Font font)
        {
            root = (RectTransform)new GameObject("InspectionFeedback", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)).transform;
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 120;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = 0.5f;

            tag = (RectTransform)new GameObject("TargetName", typeof(RectTransform), typeof(Image)).transform;
            tag.SetParent(root, false); tag.pivot = new Vector2(0.5f, 0);
            tag.anchorMin = tag.anchorMax = new Vector2(0.5f, 0.5f);
            Image plate = tag.GetComponent<Image>();
            plate.color = new Color(0.11f, 0.09f, 0.035f, 0.9f); plate.raycastTarget = false;
            label = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            RectTransform text = label.rectTransform; text.SetParent(tag, false);
            text.anchorMin = Vector2.zero; text.anchorMax = Vector2.one; text.offsetMin = text.offsetMax = Vector2.zero;
            label.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 15; label.alignment = TextAnchor.MiddleCenter; label.raycastTarget = label.supportRichText = false;
            label.color = new Color(1f, 0.89f, 0.55f);

            Shader shader = Resources.Load<Shader>("InspectionHighlight");
            if (shader != null)
            {
                silhouetteMask = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                outline = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, renderQueue = silhouetteMask.renderQueue + 1 };
                outline.SetFloat("_Outline", 1);
                outline.SetInt("_ColorMask", (int)ColorWriteMask.All);
                outline.SetInt("_Cull", (int)CullMode.Front);
                outline.SetInt("_StencilComp", (int)CompareFunction.NotEqual);
                outline.SetInt("_StencilWrite", 0);
            }
            Hide();
        }

        internal void Draw(Camera camera, Transform visual, string name, Collider hit, bool showOutline = false)
        {
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

            bool found = false;
            Bounds bounds = hit != null ? hit.bounds : new Bounds(visual.position, Vector3.zero);
            foreach (var part in parts)
            {
                if (part.filter == null || part.renderer == null || !part.renderer.enabled || part.renderer.forceRenderingOff || !part.renderer.gameObject.activeInHierarchy ||
                    (camera.cullingMask & (1 << part.renderer.gameObject.layer)) == 0) continue;
                if (found) bounds.Encapsulate(part.renderer.bounds);
                else { bounds = part.renderer.bounds; found = true; }
                // Static batches can share mesh data that includes other scene objects in batch space.
                if (!showOutline || silhouetteMask == null || part.renderer.isPartOfStaticBatch) continue;
                Mesh mesh = part.filter.sharedMesh;
                if (mesh == null) continue;
                var parameters = new RenderParams(silhouetteMask)
                {
                    camera = camera, worldBounds = part.renderer.bounds, layer = part.renderer.gameObject.layer,
                    receiveShadows = false, shadowCastingMode = ShadowCastingMode.Off
                };
                Matrix4x4 matrix = part.renderer.localToWorldMatrix;
                for (int i = 0; i < mesh.subMeshCount; i++) Graphics.RenderMesh(parameters, mesh, i, matrix);
                parameters.material = outline;
                for (int i = 0; i < mesh.subMeshCount; i++) Graphics.RenderMesh(parameters, mesh, i, matrix);
            }

            Vector3 screen = camera.WorldToScreenPoint(bounds.center);
            float top = screen.y;
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = camera.WorldToScreenPoint(bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z)));
                if (corner.z > camera.nearClipPlane) top = Mathf.Max(top, corner.y);
            }
            if (screen.z <= camera.nearClipPlane) { Hide(); return; }
            tag.gameObject.SetActive(true);
            if (label.text != name) label.text = name;
            tag.sizeDelta = new Vector2(Mathf.Max(52, label.preferredWidth + 18), 25);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, new Vector2(screen.x, top), null, out Vector2 point);
            Rect rect = root.rect;
            float halfWidth = tag.sizeDelta.x * 0.5f + 6;
            tag.anchoredPosition = new Vector2(Mathf.Clamp(point.x, rect.xMin + halfWidth, rect.xMax - halfWidth),
                Mathf.Clamp(point.y + 7, rect.yMin + 6, rect.yMax - tag.sizeDelta.y - 6));
        }

        internal void Hide() { if (tag != null && tag.gameObject.activeSelf) tag.gameObject.SetActive(false); }

        public void Dispose()
        {
            if (root != null) Object.Destroy(root.gameObject);
            if (silhouetteMask != null) Object.Destroy(silhouetteMask);
            if (outline != null) Object.Destroy(outline);
        }
    }
}
