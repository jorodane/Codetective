using System.Collections.Generic;
using Codetective.Interaction;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Codetective.Gameplay
{
    /// <summary>Render the held object after the room has been blurred.</summary>
    [DefaultExecutionOrder(180)]
    internal sealed class ItemInspectionPresentation : MonoBehaviour
    {
        private ItemCarryController carry;
        private Camera view, foreground;
        private UniversalAdditionalCameraData viewData, foregroundData;
        private HoldableItem item;
        private readonly List<Transform> parts = new List<Transform>();
        private readonly Dictionary<GameObject, int> originalLayers = new Dictionary<GameObject, int>();
        private int layer, originalMask;
        private bool originalPostProcessing, isolating;

        public void Initialize(ItemCarryController carry, Camera view)
        {
            this.carry = carry; this.view = view;
            layer = LayerMask.NameToLayer("Inspected Item");
            if (layer < 0 || view == null || !view.TryGetComponent(out viewData)) enabled = false;
        }

        private void LateUpdate()
        {
            HoldableItem next = carry != null && carry.IsExamining && view != null && view.isActiveAndEnabled ? carry.HeldItem : null;
            if (next != item || (next == null && isolating))
            {
                Restore();
                if (next != null) Isolate(next);
            }
            if (!isolating) return;

            // A new spread can rebuild the world-space ink while the item is held.
            parts.Clear();
            item.GetComponentsInChildren(true, parts);
            foreach (Transform part in parts)
            {
                GameObject target = part.gameObject;
                if (target.layer == layer) continue;
                if (!originalLayers.ContainsKey(target)) originalLayers.Add(target, target.layer);
                target.layer = layer;
            }
            foreground.CopyFrom(view);
            foreground.cullingMask = 1 << layer;
            foreground.useOcclusionCulling = false;
        }

        private void Isolate(HoldableItem next)
        {
            List<Camera> stack = viewData.cameraStack;
            if (stack == null) { enabled = false; return; }
            if (foreground == null)
            {
                var root = new GameObject("HeldItemCamera", typeof(Camera));
                root.SetActive(false);
                root.transform.SetParent(view.transform, false);
                foreground = root.GetComponent<Camera>();
                foregroundData = root.AddComponent<UniversalAdditionalCameraData>();
                // URP overlays clear depth by default, so the evidence isn't occluded by the office.
                foregroundData.renderType = CameraRenderType.Overlay;
                foregroundData.renderShadows = false;
            }

            item = next;
            originalMask = view.cullingMask;
            originalPostProcessing = viewData.renderPostProcessing;
            view.cullingMask &= ~(1 << layer);
            viewData.renderPostProcessing = false;
            foregroundData.renderPostProcessing = originalPostProcessing;
            foregroundData.volumeLayerMask = viewData.volumeLayerMask;
            foregroundData.volumeTrigger = viewData.volumeTrigger != null ? viewData.volumeTrigger : view.transform;
            stack.Add(foreground);
            foreground.gameObject.SetActive(true);
            EvidenceBackgroundBlur.View = view;
            isolating = true;
        }

        private void Restore()
        {
            if (!isolating) return;
            foreach (var pair in originalLayers)
                if (pair.Key != null) pair.Key.layer = pair.Value;
            originalLayers.Clear();
            if (view != null) view.cullingMask = originalMask;
            if (viewData != null)
            {
                viewData.renderPostProcessing = originalPostProcessing;
                viewData.cameraStack?.Remove(foreground);
            }
            if (foreground != null) foreground.gameObject.SetActive(false);
            if (EvidenceBackgroundBlur.View == view) EvidenceBackgroundBlur.View = null;
            item = null;
            isolating = false;
        }

        private void OnDisable() => Restore();
        private void OnDestroy()
        {
            Restore();
            if (foreground == null) return;
            if (Application.isPlaying) Destroy(foreground.gameObject); else DestroyImmediate(foreground.gameObject);
        }
    }
}
