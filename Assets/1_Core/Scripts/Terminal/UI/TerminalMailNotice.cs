using System;
using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    /// <summary>A notification on the physical CRT. It borrows the monitor without navigating or reading any message.</summary>
    public sealed class TerminalMailNotice : MonoBehaviour
    {
        private static readonly Color DimAlert = new Color(0.045f, 0.14f, 0.085f);
        private MonitorPageHost host;
        private Image background;
        private float blinkSeconds, began;
        public event Action Acknowledged;
        public bool IsVisible => gameObject.activeSelf;
        public bool Covered => host != null && host.HasOtherOverlay(this);

        public void Initialize(MonitorPageHost owner, float interval)
        {
            host = owner;
            blinkSeconds = Mathf.Max(0.15f, interval);
            RectTransform rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            background = gameObject.AddComponent<Image>();
            background.color = MonitorWidgets.SelectedButtonColor;
            gameObject.AddComponent<CrtMeshEffect>();
            Button dismiss = gameObject.AddComponent<Button>(); dismiss.targetGraphic = background; dismiss.transition = Selectable.Transition.None;
            var navigation = dismiss.navigation; navigation.mode = Navigation.Mode.None; dismiss.navigation = navigation;
            dismiss.onClick.AddListener(() => { Hide(); Acknowledged?.Invoke(); });
            RectTransform icon = MonitorWidgets.Rect("Envelope", transform, Vector2.zero, Vector2.zero);
            icon.anchorMin = new Vector2(0.24f, 0.27f);
            icon.anchorMax = new Vector2(0.76f, 0.73f);
            icon.offsetMin = icon.offsetMax = Vector2.zero;
            MailEnvelopeGraphic graphic = icon.gameObject.AddComponent<MailEnvelopeGraphic>();
            graphic.color = MonitorWidgets.Foreground;
            graphic.raycastTarget = false;
            icon.gameObject.AddComponent<CrtMeshEffect>();
            gameObject.SetActive(false);
        }

        public bool Show(int unreadArrivals)
        {
            if (host == null || Covered || unreadArrivals <= 0) return false;
            if (IsVisible) return true;
            began = Time.unscaledTime;
            background.color = MonitorWidgets.SelectedButtonColor;
            host.SetOverlayVisible(this, true);
            transform.SetAsLastSibling();
            gameObject.SetActive(true);
            return true;
        }

        private void Update()
        {
            bool bright = Mathf.Repeat(Time.unscaledTime - began, blinkSeconds * 2) < blinkSeconds;
            background.color = bright ? MonitorWidgets.SelectedButtonColor : DimAlert;
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            if (host != null) host.SetOverlayVisible(this, false);
        }
        private void OnDestroy() { if (host != null) host.SetOverlayVisible(this, false); }
    }
}
