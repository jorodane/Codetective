using System;
using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    /// <summary>A notification on the physical CRT. It borrows the monitor without navigating or reading any message.</summary>
    public sealed class TerminalMailNotice : MonoBehaviour
    {
        private static readonly Color DimTint = new Color(0.60f, 0.72f, 0.64f);
        private MonitorPageHost host;
        private Image display;
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
            // The envelope and its background use a single textured quad on the CRT.
            // They share the same curvature, depth and draw order at every angle.
            display = gameObject.AddComponent<Image>();
            display.sprite = Resources.Load<Sprite>("MailNoticeScreen");
            display.useSpriteMesh = false;
            if (display.sprite == null)
                Debug.LogError("The mail notice sprite MailNoticeScreen could not be loaded.", this);
            gameObject.AddComponent<CrtMeshEffect>();
            Button dismiss = gameObject.AddComponent<Button>();
            dismiss.targetGraphic = display;
            dismiss.transition = Selectable.Transition.None;
            var navigation = dismiss.navigation; navigation.mode = Navigation.Mode.None; dismiss.navigation = navigation;
            dismiss.onClick.AddListener(() => { Hide(); Acknowledged?.Invoke(); });
            gameObject.SetActive(false);
        }

        public bool Show(int unreadArrivals)
        {
            if (host == null || display.sprite == null || Covered || unreadArrivals <= 0) return false;
            if (IsVisible) return true;
            began = Time.unscaledTime;
            display.color = Color.white;
            host.SetOverlayVisible(this, true);
            transform.SetAsLastSibling();
            gameObject.SetActive(true);
            return true;
        }

        private void Update()
        {
            bool bright = Mathf.Repeat(Time.unscaledTime - began, blinkSeconds * 2) < blinkSeconds;
            display.color = bright ? Color.white : DimTint;
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            if (host != null) host.SetOverlayVisible(this, false);
        }
        private void OnDestroy() { if (host != null) host.SetOverlayVisible(this, false); }
    }
}
