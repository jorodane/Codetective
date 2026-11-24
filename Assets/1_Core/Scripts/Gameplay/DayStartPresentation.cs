using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Gameplay
{
    /// <summary>A transparent title card over the office with a cue when its text starts to fade out.</summary>
    public sealed class DayStartPresentation : MonoBehaviour
    {
        private GameObject root;
        private CanvasGroup dayAlpha, subtitleAlpha;
        private Text dayText, subtitle;
        private PresentationSettings settings;
        public bool IsVisible => root != null && root.activeSelf;
        public void Initialize(Font font, PresentationSettings configuration)
        {
            settings = configuration;
            if (root != null) return;
            root = new GameObject("DayStartCard", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 1600;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = 0.5f;
            CanvasGroup group = root.GetComponent<CanvasGroup>(); group.blocksRaycasts = group.interactable = false;
            float width = Mathf.Clamp(settings != null ? settings.dayTitleWidth : 0.52f, 0.3f, 0.8f);
            dayText = Label("DayNumber", font, new Vector2((1 - width) * 0.5f, 0.40f), new Vector2((1 + width) * 0.5f, 0.84f), 320);
            dayText.resizeTextForBestFit = true; dayText.resizeTextMinSize = 48; dayText.resizeTextMaxSize = 320;
            dayAlpha = dayText.gameObject.AddComponent<CanvasGroup>();
            subtitle = Label("DaySubtitle", font, new Vector2(0.1f, 0.28f), new Vector2(0.9f, 0.40f), 42);
            subtitleAlpha = subtitle.gameObject.AddComponent<CanvasGroup>();
            root.SetActive(false);
        }
        private Text Label(string label, Font font, Vector2 min, Vector2 max, int size)
        {
            var go = new GameObject(label, typeof(RectTransform)); go.transform.SetParent(root.transform, false); go.layer = 5;
            RectTransform rect = (RectTransform)go.transform; rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
            Text text = go.AddComponent<Text>(); text.font = font; text.fontSize = size; text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(0.92f, 0.94f, 0.88f); text.raycastTarget = false; text.supportRichText = false;
            Shadow shadow = go.AddComponent<Shadow>(); shadow.effectColor = new Color(0, 0, 0, 0.8f); shadow.effectDistance = new Vector2(3, -3);
            return text;
        }
        public IEnumerator Play(int day, string description, Action textFading) => Play("DAY " + day, description, textFading);

        public IEnumerator Play(string heading, string description, Action textFading = null)
        {
            if (IsVisible) yield break;
            dayText.text = heading; subtitle.text = description;
            dayAlpha.alpha = subtitleAlpha.alpha = 0; root.SetActive(true);
            try
            {
                yield return Fade(0, 1, settings != null ? settings.dayTitleFadeSeconds : 0.4f, value => dayAlpha.alpha = value);
                yield return SceneTransitionController.Hold(settings != null ? Mathf.Max(0, settings.daySubtitleDelay) : 0.25f);
                yield return Fade(0, 1, settings != null ? settings.daySubtitleFadeSeconds : 0.4f, value => subtitleAlpha.alpha = value);
                yield return SceneTransitionController.Hold(settings != null ? Mathf.Max(0, settings.dayCardSeconds) : 1.3f);
                textFading?.Invoke();
                yield return Fade(1, 0, settings != null ? settings.dayTextFadeSeconds : 0.9f, value => dayAlpha.alpha = subtitleAlpha.alpha = value);
            }
            finally { if (root != null) root.SetActive(false); }
        }
        private static IEnumerator Fade(float from, float to, float seconds, Action<float> apply)
        {
            if (seconds <= 0) { apply(to); yield break; }
            for (float time = 0; time < seconds; time += Time.unscaledDeltaTime)
            { apply(Mathf.Lerp(from, to, Mathf.SmoothStep(0, 1, time / seconds))); yield return null; }
            apply(to);
        }
        private void OnDestroy() => ProfileModelUtility.Dispose(root);
    }
}
