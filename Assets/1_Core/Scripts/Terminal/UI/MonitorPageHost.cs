using System;
using System.Collections.Generic;
using System.Linq;
using Codetective.Operations;
using Codetective.Gameplay;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Codetective.Terminal
{
    [DefaultExecutionOrder(-150), DisallowMultipleComponent]
    public sealed class MonitorPageHost : MonoBehaviour
    {
        [SerializeField] private TerminalController terminal;
        [SerializeField] private Text heading;
        [SerializeField] private RectTransform navigationRoot;
        [SerializeField] private string initialPage = "home";
        [Tooltip("Portraits are keyed by world profile ID. Availability uses the same story triggers as permissions.")]
        [SerializeField] private ProfilePortrait[] profilePortraits = Array.Empty<ProfilePortrait>();
        private readonly Dictionary<string, int> journalPages = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, MonitorPage> pages = new Dictionary<string, MonitorPage>(StringComparer.Ordinal);
        private readonly Dictionary<string, Button> tabs = new Dictionary<string, Button>(StringComparer.Ordinal);
        private readonly PageNavigation navigation = new PageNavigation();
        private readonly List<MonitorPage> navigationPages = new List<MonitorPage>();
        private RectTransform tabContent;
        private CrtScrollRect tabScroll;
        private Button back;
        private Button forward;
        private Button home;
        private KernelRuntime runtime;
        private bool initialized;
        private ProfileCatalog profiles;
        private readonly Dictionary<string, PhotoAsset> dataPhotos = new Dictionary<string, PhotoAsset>(StringComparer.Ordinal);
        private readonly HashSet<string> worldDataIds = new HashSet<string>(StringComparer.Ordinal);
        private float nextMailRefresh;
        private Image headerBackground;
        private Color headingColor, mailBannerColor;
        private float mailBannerUntil;
        private int receivedMailCount;
        private readonly HashSet<object> overlays = new HashSet<object>();
        private readonly object legacyOverlay = new object();
        public MonitorPage CurrentPage { get; private set; }
        public IReadOnlyList<MonitorPage> NavigationPages => navigationPages;
        public string CurrentMenuId => CurrentPage != null ? CurrentPage.NavigationPageId : null;
        public string SelectedProfileId { get; set; } = "client";
        public int SelectedScheduleDay { get; set; }
        public int ProfilePageRevision { get; private set; }
        public bool SystemMenuVisible { get; private set; }
        public bool MailNotificationVisible => initialized && Time.unscaledTime < mailBannerUntil;
        public Font DisplayFont => Font;
        public Action WaitForAppointment { get; set; }
        public Action FinishConsultation { get; set; }
        public Func<bool> ClinicActionsAllowed { get; set; }
        public Func<bool> AutoReadMailAllowed { get; set; }
        public bool HasOtherOverlay(object owner) => overlays.Any(value => !ReferenceEquals(value, owner));
        public int JournalPageFor(string profileId) => profileId != null && journalPages.TryGetValue(profileId, out int page) ? page : 0;
        public void SelectJournalPage(string profileId, int page)
        {
            if (profileId == null || page < 0 || JournalPageFor(profileId) == page) return;
            journalPages[profileId] = page; ProfilePageRevision++;
        }
        public void AttachProfiles(ProfileCatalog value) { profiles = value; ProfilePageRevision++; }
        public void AttachWorldData(WorldDefinition value)
        {
            dataPhotos.Clear(); worldDataIds.Clear();
            if (value != null) foreach (EntityInstanceAsset asset in value.AllObjects)
            { worldDataIds.Add(asset.instanceId); if (asset is PhotoAsset photo) dataPhotos.Add(asset.instanceId, photo); }
            ProfilePageRevision++;
        }
        public Sprite DataPhotoFor(string id)
        {
            if (runtime == null || string.IsNullOrEmpty(id) || !dataPhotos.TryGetValue(id, out PhotoAsset photo) || runtime.LastKnownValue(id, "image").text != id) return null;
            return photo.image;
        }
        public Sprite PortraitFor(string profileId)
        {
            if (runtime == null || !runtime.VisibleProfileIds.Contains(profileId)) return null;
            if (worldDataIds.Contains(profileId)) return DataPhotoFor(runtime.ViewData(profileId).PhotoId);
            CharacterProfileAsset profile = profiles != null ? profiles.Character(profileId) : null;
            if (profile != null)
            {
                if (profile.availability != null && !profile.availability.Matches(runtime)) return null;
                if (profile.photo != null) return profile.photo;
            }
            foreach (ProfilePortrait portrait in profilePortraits ?? Array.Empty<ProfilePortrait>())
                if (portrait != null && portrait.profileId == profileId && (portrait.availability == null || portrait.availability.Matches(runtime))) return portrait.photo;
            return null;
        }
        public bool CanGoBack => navigation.CanGoBack;
        public Sprite EvidencePhotoFor(string id)
        {
            if (runtime == null || !runtime.Evidence.Any(e => e.Id == id)) return null;
            if (worldDataIds.Contains(id)) return DataPhotoFor(runtime.ViewData(id).PhotoId);
            VisualProfileAsset profile = profiles != null ? profiles.Find(id) : null;
            return profile != null && (profile.availability == null || profile.availability.Matches(runtime)) ? profile.photo : null;
        }
        public bool CanGoForward => navigation.CanGoForward;
        public bool IsCompositionProtected
        {
            get
            {
                if (CurrentPage == null) return false;
                foreach (CompletingInputField input in CurrentPage.GetComponentsInChildren<CompletingInputField>(true))
                    if (input.isFocused && input.IsCompositionProtected) return true;
                return false;
            }
        }
        public event Action<MonitorPage> PageChanged;
        private Font Font => heading != null && heading.font != null ? heading.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        private void Awake() => Initialize();
        private void LateUpdate()
        {
            if (initialized) RefreshHeader();
            if (initialized && runtime != null && Time.unscaledTime >= nextMailRefresh)
            {
                nextMailRefresh = Time.unscaledTime + 0.2f;
                if (tabs.TryGetValue("mail", out Button mailTab))
                {
                    int unread = runtime.UnreadMailCount(Mailbox.Kernel) + runtime.UnreadMailCount(Mailbox.Clinic);
                    mailTab.GetComponentInChildren<Text>().text = pages["mail"].Title + (unread > 0 ? " (" + unread + ")" : "");
                }
            }
#if ENABLE_INPUT_SYSTEM
            // EventSystem has already selected any clicked input. Never steal its Tab/completion key.
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.tabKey.wasPressedThisFrame)
                TryCycleTab(keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed ? -1 : 1);
#endif
        }
        public void Initialize()
        {
            if (initialized || navigationRoot == null) return;
            terminal = terminal != null ? terminal : GetComponent<TerminalController>();
            runtime = runtime ?? terminal?.Runtime;
            BuildHeader();
            BuildNavigation();
            navigation.Changed += Display;
            initialized = true;
            foreach (MonitorPage page in GetComponentsInChildren<MonitorPage>(true)) Register(page);
            if (!TryNavigate(initialPage))
                foreach (string id in pages.Keys) { TryNavigate(id); break; }
        }
        public void AttachRuntime(KernelRuntime value)
        {
            runtime = value;
            if (!initialized) return;
            foreach (MonitorPage page in pages.Values) InitializeContent(page);
        }
        public void Register(MonitorPage page)
        {
            if (page == null || page.transform == transform || !page.transform.IsChildOf(transform))
                throw new ArgumentException("페이지는 모니터 아래의 별도 루트여야 합니다.");
            foreach (MonitorPage other in pages.Values)
                if (page.transform.IsChildOf(other.transform) || other.transform.IsChildOf(page.transform))
                    throw new ArgumentException("페이지 루트끼리는 중첩할 수 없습니다.");
            navigation.Register(page.Id, page.ShowInNavigation);
            pages.Add(page.Id, page);
            page.gameObject.SetActive(false);
            InitializeContent(page);
            if (page.ShowInNavigation)
            {
                navigationPages.Add(page);
                string id = page.Id;
                int index = tabs.Count;
                tabs.Add(id, MonitorWidgets.Button(tabContent, Font, new Vector2(index * 140, 0), new Vector2(132, 44), page.Title, () => ShowPage(id)));
                tabContent.sizeDelta = new Vector2(tabs.Count * 140 - 8, 44);
            }
        }
        private void InitializeContent(MonitorPage page)
        {
            if (runtime == null) return;
            foreach (MonoBehaviour component in page.GetComponentsInChildren<MonoBehaviour>(true))
                if (component is IMonitorPageContent content) content.Initialize(runtime, Font);
        }
        public bool TryNavigate(string id) => initialized && !SystemMenuVisible && navigation.Navigate(id);
        public void ShowPage(string id) => TryNavigate(id);
        public void ShowHome() => ShowPage("home");
        public MonitorPage FindPage(string id) => id != null && pages.TryGetValue(id, out MonitorPage page) ? page : null;
        public void GoBack() { if (!SystemMenuVisible) navigation.Back(); }
        public void GoForward() { if (!SystemMenuVisible) navigation.Forward(); }
        public void SetSystemMenuVisible(bool visible) => SetOverlayVisible(legacyOverlay, visible);
        public void SetOverlayVisible(object owner, bool visible)
        {
            if (visible) overlays.Add(owner); else overlays.Remove(owner);
            bool suspended = overlays.Count > 0;
            if (SystemMenuVisible == suspended) return;
            TextInputFocus.ReleaseAll();
            SystemMenuVisible = suspended;
            if (suspended) { if (CurrentPage != null) CurrentPage.Hide(); if (terminal != null) terminal.SetPageVisible(false); }
            else { if (CurrentPage != null) CurrentPage.Show(); if (terminal != null) terminal.SetPageVisible(CurrentPage != null && CurrentPage.TerminalPage); }
        }
        public bool TryCycleTab(int direction)
        {
            if (!initialized || SystemMenuVisible || !isActiveAndEnabled || terminal == null || !terminal.InteractionEnabled ||
                TextInputFocus.IsActive || IsCompositionProtected || terminal.IsCompositionProtected) return false;
            if (!navigation.CycleTab(direction, CurrentMenuId)) return false;
            if (tabs.TryGetValue(CurrentPage.Id, out Button tab)) tab.Select();
            return true;
        }

        private void Display(string id)
        {
            if (CurrentPage != null)
            {
                // Commit IME before hiding the page. Pending Enter/completion actions must not run while hidden.
                foreach (CompletingInputField input in CurrentPage.GetComponentsInChildren<CompletingInputField>(true)) input.ReleaseFocus();
                GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                if (selected != null && selected.transform.IsChildOf(CurrentPage.transform)) EventSystem.current.SetSelectedGameObject(null);
                terminal?.SetPageVisible(false);
                CurrentPage.Hide();
            }
            CurrentPage = pages[id];
            CurrentPage.Show();
            terminal?.SetPageVisible(CurrentPage.TerminalPage);
            RefreshHeader();
            back.interactable = CanGoBack; forward.interactable = CanGoForward;
            home.interactable = id != "home" && pages.ContainsKey("home");
            RevealTab(CurrentMenuId);
            PageChanged?.Invoke(CurrentPage);
        }
        private void RevealTab(string id)
        {
            if (!tabs.TryGetValue(id, out Button button)) return;
            Canvas.ForceUpdateCanvases();
            RectTransform tab = (RectTransform)button.transform;
            float visible = tabScroll.viewport.rect.width;
            float maximum = Mathf.Max(0, tabContent.rect.width - visible);
            float left = -tabContent.anchoredPosition.x;
            if (tab.anchoredPosition.x < left) left = tab.anchoredPosition.x;
            else if (tab.anchoredPosition.x + tab.rect.width > left + visible) left = tab.anchoredPosition.x + tab.rect.width - visible;
            tabContent.anchoredPosition = new Vector2(-Mathf.Clamp(left, 0, maximum), 0);
        }
        private void BuildNavigation()
        {
            back = MonitorWidgets.Button(navigationRoot, Font, Vector2.zero, new Vector2(32, 44), "<", GoBack);
            forward = MonitorWidgets.Button(navigationRoot, Font, new Vector2(40, 0), new Vector2(32, 44), ">", GoForward);
            home = MonitorWidgets.Button(navigationRoot, Font, new Vector2(80, 0), new Vector2(60, 44), "홈", ShowHome);
            back.interactable = forward.interactable = false;
            RectTransform viewport = MonitorWidgets.Rect("Pages", navigationRoot, new Vector2(156, 0), new Vector2(navigationRoot.rect.width - 156, 44));
            viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.01f);
            viewport.gameObject.AddComponent<CrtMeshEffect>(); viewport.gameObject.AddComponent<RectMask2D>();
            tabContent = MonitorWidgets.Rect("Tabs", viewport, Vector2.zero, new Vector2(0, 44));
            tabScroll = viewport.gameObject.AddComponent<CrtScrollRect>();
            tabScroll.content = tabContent; tabScroll.viewport = viewport; tabScroll.horizontal = true; tabScroll.vertical = false;
            tabScroll.movementType = ScrollRect.MovementType.Clamped; tabScroll.scrollSensitivity = 40;
        }
        private void BuildHeader()
        {
            if (heading == null) return;
            headingColor = heading.color;
            RectTransform bar = MonitorWidgets.Rect("TerminalHeaderBackground", transform, Vector2.zero, Vector2.zero);
            bar.anchorMin = new Vector2(0, 1); bar.anchorMax = Vector2.one;
            bar.offsetMin = new Vector2(16, -72); bar.offsetMax = new Vector2(-16, -12);
            bar.SetAsFirstSibling();
            headerBackground = bar.gameObject.AddComponent<Image>(); headerBackground.raycastTarget = false; headerBackground.color = Color.clear;
            bar.gameObject.AddComponent<CrtMeshEffect>();
        }
        public bool ShowMailReceived(int count, PresentationSettings settings)
        {
            if (!initialized || heading == null || headerBackground == null || SystemMenuVisible || count <= 0) return false;
            receivedMailCount = MailNotificationVisible ? receivedMailCount + count : count;
            mailBannerUntil = Time.unscaledTime + Mathf.Max(0.5f, settings != null ? settings.mailBannerSeconds : 3f);
            mailBannerColor = settings != null ? settings.mailBannerColor : new Color(0.33f, 0.22f, 0.06f, 0.98f);
            RefreshHeader(); return true;
        }
        private void RefreshHeader()
        {
            bool notifying = MailNotificationVisible && !SystemMenuVisible;
            if (headerBackground != null) headerBackground.color = notifying ? mailBannerColor : Color.clear;
            if (heading != null)
            {
                heading.text = notifying ? "메일 수신 · " + receivedMailCount + "건" : "CODETECTIVE / " + (CurrentPage != null ? CurrentPage.Title : "");
                heading.color = notifying ? new Color(1, 0.92f, 0.62f) : headingColor;
            }
            TintHeaderButton(back, MonitorWidgets.ButtonColor, notifying);
            TintHeaderButton(forward, MonitorWidgets.ButtonColor, notifying);
            TintHeaderButton(home, MonitorWidgets.ButtonColor, notifying);
            foreach (var tab in tabs)
                TintHeaderButton(tab.Value, tab.Key == CurrentMenuId ? new Color(0.16f, 0.32f, 0.23f) : MonitorWidgets.ButtonColor, notifying);
        }
        private void TintHeaderButton(Button button, Color normal, bool notifying)
        { if (button != null) button.image.color = notifying ? Color.Lerp(normal, mailBannerColor, 0.85f) : normal; }
        private void OnDestroy() => navigation.Changed -= Display;
    }
}
