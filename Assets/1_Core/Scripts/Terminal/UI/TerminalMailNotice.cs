using System;
using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    /// <summary>A notification on the physical CRT that opens the arriving message when selected.</summary>
    public sealed class TerminalMailNotice : MonoBehaviour
    {
        private static readonly Color DimTint = new Color(0.60f, 0.72f, 0.64f);
        private MonitorPageHost host;
        private Image display;
        private float blinkSeconds, began;
        public event Action OpenRequested;
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
            Button open = gameObject.AddComponent<Button>();
            open.targetGraphic = display;
            open.transition = Selectable.Transition.None;
            var navigation = open.navigation; navigation.mode = Navigation.Mode.None; open.navigation = navigation;
            open.onClick.AddListener(() => OpenRequested?.Invoke());
            gameObject.SetActive(false);
        }

        public bool OpenMessage(string id)
        {
            if (!IsVisible || Covered) return false;
            MailMonitorPage mailPage = host.FindPage("mail")?.GetComponent<MailMonitorPage>();
            // Select while the current page is suspended so restoring it cannot mark a different message read.
            if (mailPage == null || !mailPage.ShowMessage(id)) return false;
            Hide();
            return host.CurrentPage?.Id == "mail" || host.TryNavigate("mail");
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
