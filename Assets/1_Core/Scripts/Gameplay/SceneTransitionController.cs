using System;
using System.Collections;
using System.Collections.Generic;
using Codetective.CameraViews;
using Codetective.Terminal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Codetective.Gameplay
{
    [DefaultExecutionOrder(500)]
    [DisallowMultipleComponent, RequireComponent(typeof(CameraAngleController))]
    public sealed class SceneTransitionController : MonoBehaviour
    {
        private static bool pendingSceneReveal;
        private CameraAngleController views;
        private CrtScreen terminal;
        private PresentationSettings settings;
        private GameObject root;
        private Image background, full;
        private RawImage overlap;
        private Material dimMaterial;
        private Material bodyMaskMaterial;
        private Renderer[] bodyRenderers = Array.Empty<Renderer>();
        private int[] bodySubmeshes = Array.Empty<int>();
        private RenderTexture bodyMask;
        private CommandBuffer bodyCommands;
        private Texture2D capturedFrame;
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
            views = cameraViews; terminal = screen; settings = configuration;
            bodyRenderers = body != null ? body.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
            bodySubmeshes = Array.ConvertAll(bodyRenderers, renderer => renderer.sharedMaterials.Length);
            if (root != null) return;
            root = new GameObject("PresentationEffects", typeof(RectTransform), typeof(Canvas));
            Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 1500;
            background = Layer<Image>("TerminalBackground"); full = Layer<Image>("FullScreenFade"); overlap = Layer<RawImage>("ScreenOverlap");
            Canvas fullCanvas = full.gameObject.AddComponent<Canvas>(); fullCanvas.overrideSorting = true; fullCanvas.sortingOrder = 4000;
            Canvas overlapCanvas = overlap.gameObject.AddComponent<Canvas>(); overlapCanvas.overrideSorting = true; overlapCanvas.sortingOrder = 4100;
            Shader shader = Resources.Load<Shader>("TerminalDimming");
            if (shader != null) { dimMaterial = new Material(shader); background.material = dimMaterial; }
            Shader bodyShader = Resources.Load<Shader>("TerminalBodyMask");
            if (bodyShader != null) bodyMaskMaterial = new Material(bodyShader) { hideFlags = HideFlags.HideAndDontSave };
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
            if (dimMaterial == null) return;
            Camera camera = views != null ? views.ControlledCamera : null;
            UpdateBodyMask(camera);
            bool valid = terminal != null && terminal.isActiveAndEnabled && camera != null;
            RectTransform panel = valid ? (RectTransform)terminal.transform : null;
            if (!valid || panel.rect.width <= 0 || panel.rect.height <= 0) { dimMaterial.SetVector("_Panel", Vector4.zero); return; }
            Rect rect = panel.rect;
            dimMaterial.SetVector("_Panel", new Vector4(rect.width * 0.5f, rect.height * 0.5f, terminal.CenterBulge, 1));
            string[] suffix = { "00", "10", "01", "11" };
            for (int i = 0; i < 4; i++)
            {
                Vector3 point = new Vector3((i % 2) * Screen.width, (i / 2) * Screen.height, 0);
                Ray ray = camera.ScreenPointToRay(point); Vector3 origin = panel.InverseTransformPoint(ray.origin);
                origin.x -= rect.center.x; origin.y -= rect.center.y;
                point.z = camera.farClipPlane;
                Vector3 direction = panel.InverseTransformVector(camera.ScreenToWorldPoint(point) - ray.origin);
                dimMaterial.SetVector("_Origin" + suffix[i], origin); dimMaterial.SetVector("_Direction" + suffix[i], direction);
            }
        }
        private void UpdateBodyMask(Camera camera)
        {
            dimMaterial.SetFloat("_BodyMaskEnabled", 0);
            if (!Application.isPlaying || background.color.a <= 0 || camera == null || bodyMaskMaterial == null || bodyRenderers.Length == 0 ||
                camera.pixelWidth <= 0 || camera.pixelHeight <= 0) return;
            if (bodyMask == null || bodyMask.width != camera.pixelWidth || bodyMask.height != camera.pixelHeight)
            {
                ReleaseBodyMask();
                bodyMask = new RenderTexture(camera.pixelWidth, camera.pixelHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                    { name = "Terminal body silhouette", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                bodyMask.Create();
            }
            if (bodyCommands == null) bodyCommands = new CommandBuffer { name = "Preserve physical terminal" };
            bodyCommands.Clear(); bodyCommands.SetRenderTarget(bodyMask); bodyCommands.ClearRenderTarget(false, true, Color.clear);
            bodyCommands.SetViewProjectionMatrices(camera.worldToCameraMatrix, GL.GetGPUProjectionMatrix(camera.projectionMatrix, true));
            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                Renderer renderer = bodyRenderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0) continue;
                for (int submesh = 0; submesh < bodySubmeshes[i]; submesh++) bodyCommands.DrawRenderer(renderer, bodyMaskMaterial, submesh, 0);
            }
            bodyCommands.SetViewProjectionMatrices(camera.worldToCameraMatrix, GL.GetGPUProjectionMatrix(camera.projectionMatrix, camera.targetTexture != null));
            RenderTexture previous = RenderTexture.active;
            try { Graphics.ExecuteCommandBuffer(bodyCommands); }
            finally { RenderTexture.active = previous; }
            dimMaterial.SetTexture("_BodyMask", bodyMask);
            Rect viewport = camera.rect;
            dimMaterial.SetVector("_BodyViewport", new Vector4(viewport.x, viewport.y, viewport.width, viewport.height));
            dimMaterial.SetFloat("_BodyMaskEnabled", 1);
        }
        private void ReleaseBodyMask()
        {
            if (bodyMask != null) { bodyMask.Release(); ProfileModelUtility.Dispose(bodyMask); bodyMask = null; }
        }
        public void SetTerminalFocus(string owner, bool enabled)
        {
            if (enabled) focusOwners.Add(owner); else focusOwners.Remove(owner);
            if (root == null) return;
            if (focusRoutine != null) { StopCoroutine(focusRoutine); focusRoutine = null; }
            float target = TerminalFocused ? settings != null ? settings.terminalFocusOpacity : 0.995f : 0;
            // Sibling OnDisable callbacks may arrive while the whole camera hierarchy is shutting down.
            if (!Application.isPlaying || !gameObject.activeInHierarchy || !this.enabled || !isActiveAndEnabled) { focusAlpha = target; return; }
            focusRoutine = StartCoroutine(Tween(focusAlpha, target, settings?.menu != null ? settings.menu.duration : 0.18f, value => focusAlpha = value));
        }
        public bool TryChangeAngle(CameraAnglePreset angle, Action changed)
        {
            if (IsBusy || views == null || views.AngleSet == null || angle == null || views.AngleSet.IndexOf(angle) < 0) return false;
            TransitionPreset effect = angle.Transition != null ? angle.Transition : settings?.defaultAngle;
            if (!Application.isPlaying || effect == null || effect.style == TransitionStyle.CameraMove || effect.style == TransitionStyle.Cut)
            {
                if (!views.TryGoToIndex(views.AngleSet.IndexOf(angle), effect != null && effect.style == TransitionStyle.Cut)) return false;
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
                    try { capturedFrame = ScreenCapture.CaptureScreenshotAsTexture(); }
                    catch (Exception error) { Debug.LogWarning("화면 오버랩 캡처 실패: " + error.Message, this); }
                    if (capturedFrame != null) { overlap.texture = capturedFrame; overlap.color = Color.white; } else SetAlpha(full, 1);
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
            if (capturedFrame != null) { Destroy(capturedFrame); capturedFrame = null; }
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
        private void OnDisable() { StopAllCoroutines(); focusRoutine = angleRoutine = null; ClearTransient(); focusOwners.Clear(); focusAlpha = 0; if (background != null) SetAlpha(background, 0); IsBusy = false; ReleaseBodyMask(); }
        private void OnDestroy() { ReleaseBodyMask(); bodyCommands?.Release(); ProfileModelUtility.Dispose(root); ProfileModelUtility.Dispose(dimMaterial); ProfileModelUtility.Dispose(bodyMaskMaterial); }
    }
}
