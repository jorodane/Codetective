using Codetective.Interaction;
using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Gameplay
{
    /// <summary>Title branding lives in the space beside the terminal, above its background dimmer.</summary>
    public sealed class TitleBrandingView : MonoBehaviour
    {
        private InspectionController inspection;
        private Camera view;
        private Renderer[] terminalParts;
        private GameObject root;
        private RectTransform logo;
        public bool IsVisible => root != null && root.activeSelf;
        public void Initialize(InspectionController controller, Camera camera, Transform body, Font typeface)
        {
            inspection = controller; view = camera; terminalParts = body != null ? body.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            root = new GameObject("TitleBranding", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 2000;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = 0.5f;
            Font font = typeface != null ? typeface : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Text title = OverlayWidgets.Label(root.transform, font, Vector2.zero, Vector2.zero, "CODETECTIVE", 80);
            title.name = "TitleLogo"; title.alignment = TextAnchor.MiddleCenter; title.resizeTextForBestFit = true;
            title.resizeTextMinSize = 10; title.resizeTextMaxSize = 80; logo = title.rectTransform;
            Text version = OverlayWidgets.Label(root.transform, font, new Vector2(24, 18), new Vector2(560, 36), "v" + Application.version, 20);
            version.name = "BuildVersion"; version.rectTransform.anchorMin = version.rectTransform.anchorMax = version.rectTransform.pivot = Vector2.zero;
            root.SetActive(false);
        }
        private void LateUpdate()
        {
            if (root == null) return;
            bool visible = inspection != null && inspection.IsTitleMenu;
            root.SetActive(visible); if (!visible) return;
            float left = 0.5f;
            if (view != null)
                foreach (Renderer part in terminalParts)
                {
                    if (part == null || !part.enabled || !part.gameObject.activeInHierarchy) continue;
                    Bounds bounds = part.bounds;
                    for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 point = view.WorldToViewportPoint(bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z)));
                        if (point.z > view.nearClipPlane) left = Mathf.Min(left, point.x);
                    }
                }
            logo.anchorMin = new Vector2(0.02f, 0.43f); logo.anchorMax = new Vector2(Mathf.Clamp(left - 0.025f, 0.09f, 0.48f), 0.65f);
            logo.offsetMin = logo.offsetMax = Vector2.zero;
        }
        private void OnDisable() { if (root != null) root.SetActive(false); }
        private void OnDestroy() { if (root != null) ProfileModelUtility.Dispose(root); }
    }
}
