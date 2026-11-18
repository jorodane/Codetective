using Codetective.Interaction;
using Codetective.Operations;
using Codetective.Terminal;
using UnityEngine;

namespace Codetective.Gameplay
{
    [DefaultExecutionOrder(-140), DisallowMultipleComponent]
    public sealed class MailNotificationPresenter : MonoBehaviour
    {
        private readonly MailArrivalTracker arrivals = new MailArrivalTracker();
        private KernelGameController game;
        private KernelRuntime runtime;
        private InspectionController inspection;
        private MonitorPageHost host;
        private KernelGameUI hud;
        private TerminalMailNotice notice;
        private AudioSource speaker;
        private AudioClip sound;
        private float nextCheck;

        public void Initialize(KernelGameController owner, InspectionController navigation, MonitorPageHost monitor, PresentationSettings settings)
        {
            game = owner; runtime = game.Runtime; inspection = navigation; host = monitor;
            hud = game.GetComponent<KernelGameUI>();
            arrivals.Initialize(runtime); runtime.NarrativeChanged += Observe;
            var panel = new GameObject("MailNotification", typeof(RectTransform)); panel.transform.SetParent(host.transform, false); panel.layer = 5;
            notice = panel.AddComponent<TerminalMailNotice>(); notice.Initialize(host, settings != null ? settings.mailBlinkSeconds : 0.55f);
            notice.Acknowledged += arrivals.Acknowledge;
            speaker = gameObject.AddComponent<AudioSource>(); speaker.playOnAwake = false; speaker.spatialBlend = 0;
            speaker.volume = settings != null ? Mathf.Clamp01(settings.mailArrivalVolume) : 0.45f;
            sound = settings != null && settings.mailArrivalSound != null ? settings.mailArrivalSound : Resources.Load<AudioClip>("MailArrival");
            host.AutoReadMailAllowed = () => CanAttend && WatchingTerminal;
        }
        private bool WatchingTerminal => game.CurrentView == "monitor" || game.CurrentView == "counsel";
        private bool CanAttend => isActiveAndEnabled && game.IsSessionActive && game.isActiveAndEnabled && inspection.Mode != InspectionMode.Menu && !inspection.PresentingOnTerminal &&
            !game.IsViewTransitioning && !game.IsVisitPresenting && !inspection.ModalInputBlocked && !notice.Covered &&
            (!inspection.NavigationBlocked || game.IsDateTransitioning && runtime.DayIntroSeen);
        private void Observe() => arrivals.Observe(runtime);
        private void Update()
        {
            if (notice == null) return;
            // Also sample direct fact/command changes and mail registered without a narrative event.
            if (Time.unscaledTime >= nextCheck) { Observe(); nextCheck = Time.unscaledTime + 0.15f; }
            if (!CanAttend) { if (notice.IsVisible) notice.Hide(); return; }
            if (WatchingTerminal)
            {
                if (arrivals.PendingCount > 0 && hud != null && hud.ShowMailReceived(arrivals.PendingCount)) arrivals.Acknowledge();
                if (notice.IsVisible) notice.Hide(); return;
            }
            // Keep live gramophone input intact; show the envelope after the field loses focus.
            if (inspection.TextInputFocused) { PlaySignal(); if (notice.IsVisible) notice.Hide(); return; }
            if (arrivals.PendingCount == 0) { if (notice.IsVisible) notice.Hide(); return; }
            if (notice.Show(arrivals.PendingCount)) PlaySignal();
        }
        private void PlaySignal() { if (arrivals.TakeSignal() && sound != null) speaker.PlayOneShot(sound); }
        private void OnDisable() { if (notice != null) notice.Hide(); if (speaker != null) speaker.Stop(); }
        private void OnDestroy()
        {
            if (runtime != null) runtime.NarrativeChanged -= Observe;
            if (notice != null) { notice.Acknowledged -= arrivals.Acknowledge; ProfileModelUtility.Dispose(notice.gameObject); }
            if (host != null) host.AutoReadMailAllowed = null;
            ProfileModelUtility.Dispose(speaker);
        }
    }
}
