using System;
using System.Collections;
using System.Collections.Generic;
using Codetective.CameraViews;
using Codetective.Terminal;
using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Gameplay
{
    [DefaultExecutionOrder(500)]
    [DisallowMultipleComponent, RequireComponent(typeof(CameraAngleController))]
    public sealed class SceneTransitionController : MonoBehaviour
    {
        private static bool pendingSceneReveal;
        private CameraAngleController views;
        private PresentationSettings settings;
        private GameObject root;
        private Image background, full;
        private RawImage overlap;
        private Material dimMaterial;
        private Material overlapMaterial;
        private Material clearStencilMaterial, terminalStencilMaterial;
        private Image clearStencil;
        private TerminalFocusMaskGraphic terminalMask;
        private RenderTexture capturedFrame;
        private Coroutine angleRoutine, focusRoutine;
        private readonly HashSet<string> focusOwners = new HashSet<string>();
        private float focusAlpha, effectAlpha;
        public bool IsBusy { get; private set; }
        public bool TerminalFocused => focusOwners.Count > 0;
        public PresentationSettings Settings => settings;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSceneReveal() => pendingSceneReveal = false;

        public void Initialize(CameraAngleController cameraViews, CrtScreen screen, PresentationSettings configuration)
            => Initialize(cameraViews, screen, configuration, null);
        public void Initialize(CameraAngleController cameraViews, CrtScreen screen, PresentationSettings configuration, Transform body)
        {
            views = cameraViews; settings = configuration;
            if (root != null) return;
            root = new GameObject("PresentationEffects", typeof(RectTransform), typeof(Canvas));
            Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 1500;
            // Clear/write the reserved stencil bit in this same overlay, before drawing the dimmer.
            // There is no separate camera render target or projection state to get out of sync.
            clearStencil = Layer<Image>("ClearTerminalStencil");
            terminalMask = Layer<TerminalFocusMaskGraphic>("TerminalStencil");
            terminalMask.Configure(views != null ? views.ControlledCamera : null, body, screen);
            background = Layer<Image>("TerminalBackground"); full = Layer<Image>("FullScreenFade"); overlap = Layer<RawImage>("ScreenOverlap");
            Canvas fullCanvas = full.gameObject.AddComponent<Canvas>(); fullCanvas.overrideSorting = true; fullCanvas.sortingOrder = 4000;
            Canvas overlapCanvas = overlap.gameObject.AddComponent<Canvas>(); overlapCanvas.overrideSorting = true; overlapCanvas.sortingOrder = 4100;
            Shader shader = Resources.Load<Shader>("TerminalDimming");
            if (shader != null) { dimMaterial = new Material(shader); background.material = dimMaterial; }
            Shader overlapShader = Resources.Load<Shader>("ScreenOverlap");
            if (overlapShader != null) { overlapMaterial = new Material(overlapShader) { hideFlags = HideFlags.HideAndDontSave }; overlap.material = overlapMaterial; }
            Shader bodyShader = Resources.Load<Shader>("TerminalBodyMask");
            if (bodyShader != null)
            {
                clearStencilMaterial = new Material(bodyShader) { hideFlags = HideFlags.HideAndDontSave };
                clearStencilMaterial.SetInt("_StencilRef", 0); clearStencil.material = clearStencilMaterial;
                terminalStencilMaterial = new Material(bodyShader) { hideFlags = HideFlags.HideAndDontSave };
                terminalStencilMaterial.SetInt("_StencilRef", 128); terminalMask.material = terminalStencilMaterial;
            }
            clearStencil.enabled = terminalMask.enabled = false;
            SetAlpha(background, 0); SetAlpha(full, pendingSceneReveal ? 1 : 0); SetAlpha(overlap, 0);
        }
        private T Layer<T>(string label) where T : Graphic
        {
            var go = new GameObject(label, typeof(RectTransform)); go.transform.SetParent(root.transform, false); go.layer = 5;
            var rect = (RectTransform)go.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            T image = go.AddComponent<T>(); image.raycastTarget = false; image.color = Color.black; return image;
        }
        private static void SetAlpha(Graphic graphic, float alpha) { Color c = graphic.color; c.a = alpha; graphic.color = c; }
        private void LateUpdate()
        {
            if (background == null) return;
            SetAlpha(background, Mathf.Max(focusAlpha, effectAlpha));
            bool masked = background.color.a > 0 && terminalStencilMaterial != null;
            clearStencil.enabled = terminalMask.enabled = masked;
            if (masked) terminalMask.RefreshProjection();
        }
        public void SetTerminalFocus(string owner, bool enabled)
        {
            if (enabled) focusOwners.Add(owner); else focusOwners.Remove(owner);
            if (root == null) return;
            if (focusRoutine != null) { StopCoroutine(focusRoutine); focusRoutine = null; }
            float target = TerminalFocused ? settings != null ? settings.terminalFocusOpacity : 0.94f : 0;
            // Sibling OnDisable callbacks may arrive while the whole camera hierarchy is shutting down.
            if (!Application.isPlaying || !gameObject.activeInHierarchy || !this.enabled || !isActiveAndEnabled) { focusAlpha = target; return; }
            focusRoutine = StartCoroutine(Tween(focusAlpha, target, settings?.menu != null ? settings.menu.duration : 0.18f, value => focusAlpha = value));
        }
        public bool TryChangeAngle(CameraAnglePreset angle, Action changed)
            => ChangeAngle(angle, angle != null && angle.Transition != null ? angle.Transition : settings?.defaultAngle, changed);
        public TransitionPreset ReturnEffectFor(CameraAnglePreset source) => source != null && source.ExitTransition != null ? source.ExitTransition :
            settings != null && settings.defaultReturn != null ? settings.defaultReturn : settings?.defaultAngle;
        public bool TryReturnToOverview(CameraAnglePreset source, CameraAnglePreset overview, Action changed)
            => TryReturnToView(source, overview, changed);
        public bool TryReturnToView(CameraAnglePreset source, CameraAnglePreset destination, Action changed)
            => ChangeAngle(destination, ReturnEffectFor(source), changed);
        private bool ChangeAngle(CameraAnglePreset angle, TransitionPreset effect, Action changed)
        {
            if (IsBusy || views == null || views.AngleSet == null || angle == null || views.AngleSet.IndexOf(angle) < 0) return false;
            if (!Application.isPlaying || effect == null || effect.style == TransitionStyle.CameraMove || effect.style == TransitionStyle.Cut)
            {
                bool move = effect != null && effect.style == TransitionStyle.CameraMove;
                if (!views.TryGoToIndex(views.AngleSet.IndexOf(angle), effect != null && effect.style == TransitionStyle.Cut,
                    move ? effect.duration : -1, move ? effect.cameraCurve : null)) return false;
                changed?.Invoke(); return true;
            }
            Coroutine running = StartCoroutine(Play(effect, TransitionStyle.CameraMove, () => { views.TryGoToIndex(views.AngleSet.IndexOf(angle), true); changed?.Invoke(); }));
            angleRoutine = IsBusy ? running : null;
            return true;
        }
        public IEnumerator Play(TransitionPreset preset, TransitionStyle fallback, Action covered = null)
        {
            while (IsBusy) yield return null;
            IsBusy = true;
            TransitionStyle style = preset != null ? preset.style : fallback;
            float duration = preset != null ? Mathf.Max(0, preset.duration) : 0.25f, hold = preset != null ? Mathf.Max(0, preset.hold) : 0.05f;
            try
            {
                if (style == TransitionStyle.ScreenOverlap && Application.isPlaying)
                {
                    yield return new WaitForEndOfFrame();
                    try
                    {
                        // The finished framebuffer is sRGB. A screenshot Texture2D can be marked linear,
                        // which encodes its pixels a second time in a Linear project and causes an initial flash.
                        capturedFrame = RenderTexture.GetTemporary(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                        ScreenCapture.CaptureScreenshotIntoRenderTexture(capturedFrame);
                    }
                    catch (Exception error)
                    {
                        if (capturedFrame != null) { RenderTexture.ReleaseTemporary(capturedFrame); capturedFrame = null; }
                        Debug.LogWarning("화면 오버랩 캡처 실패: " + error.Message, this);
                    }
                    if (capturedFrame != null)
                    {
                        overlap.texture = capturedFrame; overlap.color = Color.white;
                        if (overlapMaterial != null) overlapMaterial.SetFloat("_Darkness", preset != null ? preset.overlapDarkness : 0.6f);
                    }
                    else SetAlpha(full, 1);
                    covered?.Invoke(); yield return Hold(hold);
                    yield return Tween(1, 0, duration, value => SetAlpha(capturedFrame != null ? (Graphic)overlap : full, value));
                }
                else if (style == TransitionStyle.TerminalFade || style == TransitionStyle.FullScreenFade)
                {
                    bool masked = style == TransitionStyle.TerminalFade;
                    if (!masked) { Color tint = preset != null ? preset.color : Color.black; tint.a = 0; full.color = tint; }
                    yield return Tween(0, 1, duration, value => { if (masked) effectAlpha = value; else SetAlpha(full, value); });
                    covered?.Invoke(); yield return Hold(hold);
                    yield return Tween(1, 0, duration, value => { if (masked) effectAlpha = value; else SetAlpha(full, value); });
                }
                else { covered?.Invoke(); yield return Hold(hold); }
            }
            finally { ClearTransient(); IsBusy = false; angleRoutine = null; }
        }
        public void CancelAngle()
        {
            if (angleRoutine == null) return;
            StopCoroutine(angleRoutine); angleRoutine = null; ClearTransient(); IsBusy = false;
        }
        private void ClearTransient()
        {
            effectAlpha = 0;
            if (full != null) SetAlpha(full, 0);
            if (overlap != null) { SetAlpha(overlap, 0); overlap.texture = null; }
            if (capturedFrame != null) { RenderTexture.ReleaseTemporary(capturedFrame); capturedFrame = null; }
        }
        public IEnumerator CoverScene()
        {
            CancelAngle(); while (IsBusy) yield return null;
            IsBusy = true; float from = full.color.a; Color tint = settings?.scene != null ? settings.scene.color : Color.black;
            tint.a = from; full.color = tint;
            yield return Tween(from, 1, settings?.scene != null ? settings.scene.duration : 0.3f, value => SetAlpha(full, value));
        }
        public void PrepareSceneReveal() => pendingSceneReveal = true;
        public void CancelSceneReveal() => pendingSceneReveal = false;
        public void RevealLoadedScene()
        {
            if (!pendingSceneReveal) return;
            pendingSceneReveal = false; StartCoroutine(RevealScene());
        }
        public IEnumerator RevealScene()
        {
            IsBusy = true;
            yield return Tween(1, 0, settings?.scene != null ? settings.scene.duration : 0.3f, value => SetAlpha(full, value));
            IsBusy = false;
        }
        public static IEnumerator Hold(float seconds)
        { for (float time = 0; time < seconds; time += Time.unscaledDeltaTime) yield return null; }
        private static IEnumerator Tween(float from, float to, float seconds, Action<float> apply)
        {
            if (seconds <= 0) { apply(to); yield break; }
            for (float time = 0; time < seconds; time += Time.unscaledDeltaTime) { apply(Mathf.Lerp(from, to, Mathf.SmoothStep(0, 1, time / seconds))); yield return null; }
            apply(to);
        }
        private void OnDisable() { StopAllCoroutines(); focusRoutine = angleRoutine = null; ClearTransient(); focusOwners.Clear(); focusAlpha = 0; if (background != null) SetAlpha(background, 0); if (clearStencil != null) clearStencil.enabled = false; if (terminalMask != null) terminalMask.enabled = false; IsBusy = false; }
        private void OnDestroy() { ProfileModelUtility.Dispose(root); ProfileModelUtility.Dispose(dimMaterial); ProfileModelUtility.Dispose(overlapMaterial); ProfileModelUtility.Dispose(clearStencilMaterial); ProfileModelUtility.Dispose(terminalStencilMaterial); }
    }
}
