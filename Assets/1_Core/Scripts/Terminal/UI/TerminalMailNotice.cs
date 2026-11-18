using System;
using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    /// <summary>A notification on the physical CRT. It borrows the monitor without navigating or reading any message.</summary>
    public sealed class TerminalMailNotice : MonoBehaviour
    {
        private MonitorPageHost host;
        private CanvasGroup pulse;
        private Text count;
        private float blinkSeconds, began;
        public event Action Acknowledged;
        public bool IsVisible => gameObject.activeSelf;
        public bool Covered => host != null && host.HasOtherOverlay(this);

        public void Initialize(MonitorPageHost owner, float interval)
        {
            host = owner; blinkSeconds = Mathf.Max(0.15f, interval);
            RectTransform rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            Image background = gameObject.AddComponent<Image>(); background.color = new Color(0.012f, 0.027f, 0.019f);
            gameObject.AddComponent<CrtMeshEffect>();
            Button dismiss = gameObject.AddComponent<Button>(); dismiss.targetGraphic = background; dismiss.transition = Selectable.Transition.None;
            var navigation = dismiss.navigation; navigation.mode = Navigation.Mode.None; dismiss.navigation = navigation;
            dismiss.onClick.AddListener(() => { Hide(); Acknowledged?.Invoke(); });
            var content = new GameObject("MailPulse", typeof(RectTransform), typeof(CanvasGroup)); content.transform.SetParent(transform, false); content.layer = 5;
            RectTransform body = (RectTransform)content.transform; body.anchorMin = Vector2.zero; body.anchorMax = Vector2.one; body.offsetMin = body.offsetMax = Vector2.zero;
            pulse = content.GetComponent<CanvasGroup>(); pulse.blocksRaycasts = false;
            var envelope = new GameObject("Envelope", typeof(RectTransform)); envelope.transform.SetParent(body, false); envelope.layer = 5;
            RectTransform icon = (RectTransform)envelope.transform; icon.anchorMin = new Vector2(0.30f, 0.38f); icon.anchorMax = new Vector2(0.70f, 0.78f); icon.offsetMin = icon.offsetMax = Vector2.zero;
            MailEnvelopeGraphic graphic = envelope.AddComponent<MailEnvelopeGraphic>(); graphic.color = MonitorWidgets.Foreground; graphic.raycastTarget = false;
            envelope.AddComponent<CrtMeshEffect>();
            count = MonitorWidgets.Label(body, host.DisplayFont, Vector2.zero, Vector2.zero, "", 38);
            count.alignment = TextAnchor.MiddleCenter;
            count.rectTransform.anchorMin = new Vector2(0.1f, 0.20f); count.rectTransform.anchorMax = new Vector2(0.9f, 0.34f);
            count.rectTransform.offsetMin = count.rectTransform.offsetMax = Vector2.zero;
            gameObject.SetActive(false);
        }
        public bool Show(int unreadArrivals)
        {
            if (host == null || Covered || unreadArrivals <= 0) return false;
            count.text = "새 메일 " + unreadArrivals + "건";
            if (!IsVisible) { began = Time.unscaledTime; pulse.alpha = 1; }
            host.SetOverlayVisible(this, true); transform.SetAsLastSibling(); gameObject.SetActive(true);
            return true;
        }
        private void Update() => pulse.alpha = Mathf.Repeat(Time.unscaledTime - began, blinkSeconds * 2) < blinkSeconds ? 1 : 0.12f;
        public void Hide()
        {
            gameObject.SetActive(false);
            if (host != null) host.SetOverlayVisible(this, false);
        }
        private void OnDestroy() { if (host != null) host.SetOverlayVisible(this, false); }
    }
}
