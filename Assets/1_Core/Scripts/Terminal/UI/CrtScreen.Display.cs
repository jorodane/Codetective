using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    public sealed partial class CrtScreen
    {
        [Header("CRT signal")]
        [SerializeField, Range(0, 1)] private float signalStrength = 0.55f;
        [SerializeField, Range(0, 0.2f)] private float scanlineOpacity = 0.07f;
        [SerializeField, Min(1)] private float scanlineCount = 320;
        [Tooltip("Seconds for a faint horizontal refresh band to pass down the screen. Uses unscaled time, including menus.")]
        [SerializeField, Min(1)] private float scanSeconds = 7.5f;
        [Tooltip("Maximum horizontal sync slip in panel units; intentionally less than a text character.")]
        [SerializeField, Range(0, 8)] private float syncSlip = 1.75f;

        private readonly Dictionary<Material, Material> displayMaterials = new Dictionary<Material, Material>();
        private Shader displayShader;
        private bool displayDirty = true;
        public bool DisplayEnabled => isActiveAndEnabled && signalStrength > 0 && Panel.rect.width > 0 && Panel.rect.height > 0;

        private void EnsureDisplayCanvas()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null) canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
        }
        internal Material DisplayMaterialFor(Material original)
        {
            if (!DisplayEnabled || original == null) return original;
            string shaderName = original.shader != null ? original.shader.name : "";
            if (shaderName != "UI/Default" && shaderName != "UI/Default Font" && shaderName != "GUI/Text Shader") return original;
            if (displayShader == null) displayShader = Resources.Load<Shader>("CrtDisplay");
            if (displayShader == null) return original;
            if (!displayMaterials.TryGetValue(original, out Material effect) || effect == null)
            {
                effect = new Material(displayShader) { name = original.name + " / CRT", hideFlags = HideFlags.HideAndDontSave };
                // Preserve UI stencil, clipping, tint and font texture properties.
                effect.CopyPropertiesFromMaterial(original); displayMaterials[original] = effect; ApplyDisplayParameters(effect);
            }
            return effect;
        }
        private void ApplyDisplayParameters(Material material)
        {
            material.SetVector("_CrtSignal", new Vector4(signalStrength, scanlineOpacity, scanlineCount, syncSlip));
            material.SetFloat("_CrtTime", Application.isPlaying ? Time.unscaledTime : 0);
            material.SetFloat("_CrtScanSeconds", Mathf.Max(1, scanSeconds));
            material.SetVector("_CrtRight", Panel.TransformVector(Vector3.right));
        }
        private void UpdateDisplayMaterials()
        {
            if (displayDirty) { displayDirty = false; EnsureDisplayCanvas(); dirty = true; }
            foreach (Material material in displayMaterials.Values) if (material != null) ApplyDisplayParameters(material);
        }
        private void ReleaseDisplayMaterials()
        {
            foreach (Material material in displayMaterials.Values)
                if (material != null) { if (Application.isPlaying) Destroy(material); else DestroyImmediate(material); }
            displayMaterials.Clear();
        }
    }
}
