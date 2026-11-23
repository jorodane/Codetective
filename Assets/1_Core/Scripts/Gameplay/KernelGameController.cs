using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Codetective.CameraViews;
using Codetective.Interaction;
using Codetective.Operations;
using Codetective.Terminal;
using UnityEngine;

namespace Codetective.Gameplay
{
    [DefaultExecutionOrder(-350)]
    [RequireComponent(typeof(KernelGameUI))]
    public sealed partial class KernelGameController : MonoBehaviour
    {
        [SerializeField] private KernelScenarioDefinition scenario;
        [SerializeField] private Transform terminalRoot;
        [Tooltip("Physical terminal model preserved with the screen during terminal-only fades.")]
        [SerializeField] private Transform terminalBody;
        [SerializeField] private InspectionController inspection;
        [SerializeField] private Transform curtainCloth;
        [SerializeField] private Transform guestCoffeeTemplate;
        [SerializeField] private Transform coffeeAnchor;
        [SerializeField] private Transform deskSurface;
        [SerializeField] private Transform clockHour;
        [SerializeField] private Transform clockMinute;
        [SerializeField] private Transform calendar;
        [SerializeField] private Font font;
        [SerializeField] private DialoguePresentationDefinition dialoguePresentation;
        [SerializeField] private Transform newspaperAnchor;
        [Tooltip("Left paper stack used when exchanging person profiles.")]
        [SerializeField] private Transform profileArchiveStack;
        [Tooltip("Existing desk sheet used as the physical profile interface.")]
        [SerializeField] private Transform profileSheet;
        [Tooltip("Lights controlled by the headquarters light-off command.")]
        [SerializeField] private Light[] sceneLights = Array.Empty<Light>();

        public KernelRuntime Runtime { get; private set; }
        public bool IsSessionActive { get; private set; }
        public SceneTransitionController Transitions { get; private set; }
        public string CurrentView { get; private set; } = "overview";
        public bool IsViewTransitioning => views != null && (views.IsTransitioning || (Transitions != null && Transitions.IsBusy));
        public DialoguePresentationDefinition DialoguePresentation => dialoguePresentation;
        public ItemCarryController Carry { get; private set; }
        private MonitorPageHost monitor;
        public MonitorPageHost Monitor => monitor != null ? monitor : terminalRoot != null ? monitor = terminalRoot.GetComponentInChildren<MonitorPageHost>(true) : null;
        public PaperDocument NewspaperDocument => newspaper?.Document;
        public bool IsDateTransitioning { get; private set; }
        public bool IsVisitPresenting { get; private set; }
        public string SelectedProfileId
        {
            get => Monitor != null ? Monitor.SelectedProfileId : selectedProfile;
            set { selectedProfile = value; MonitorPageHost host = Monitor; if (host != null) host.SelectedProfileId = value; }
        }
        private string selectedProfile = "client";
        private DeskNewspaper newspaper;
        private DeskProfilePresenter profilePaper;
        private int newspaperRevision = -1;
        private readonly List<ItemPlacementSurface> storageSurfaces = new List<ItemPlacementSurface>();
        private ClinicVisitPresentation visits;
        private int summaryShownDay;
        private readonly List<SlidingDrawer> drawers = new List<SlidingDrawer>();
        private ItemPlacementSurface desktopPlacement;
        public IReadOnlyList<SlidingDrawer> Drawers => drawers;
        public bool CanOperateDrawers => inspection != null && inspection.Mode == InspectionMode.Inspecting && CurrentView == "drawer" &&
            !IsViewTransitioning && !inspection.ModalInputBlocked && !IsDateTransitioning && !IsVisitPresenting;
        private string LegacySavePath => Path.Combine(Application.persistentDataPath, "Codetective", "daily-autosave.json");
        public string Feedback { get; private set; } = "Kernel 작전망 연결";
        private KernelGameUI ui;
        private CameraAngleController views;
        private Vector3 foldedScale;
        private Quaternion hourBasis;
        private Quaternion minuteBasis;
        private GameObject ownCoffee;
        private HoldableItem coffeeItem;
        private HoldableItem guestCoffeeItem;
        private TextMesh dayText;
        private string pendingView;
        private bool delivering;
        private bool[] initialLights;
        private const string PlayLanguagePreference = "Codetective.PlayLanguage";
        private bool narrativeDirty;
        private float narrativeSaveAt;
        private bool initialized;
        private bool presentationInitialized;
        private bool sceneBasisCaptured;
        private bool changingScene;
        private enum SessionStart { Title, NewGame, Continue }
        private static SessionStart nextStart = SessionStart.Title;
        private static string nextSaveId;
        private static KernelRuntime nextRuntime;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSessionStart() { nextStart = SessionStart.Title; nextSaveId = null; nextRuntime = null; }
        private KernelRuntime CreateRuntime() => scenario != null ? scenario.CreateRuntime() : ClinicScenario.Create();

        private void Awake()
        {
            foldedScale = curtainCloth != null ? curtainCloth.localScale : Vector3.one;
            hourBasis = ClockBasis(clockHour);
            minuteBasis = ClockBasis(clockMinute);
            sceneBasisCaptured = true;
            SystemMenuMonitorView.ApplySavedSettings();
            SessionStart start = nextStart; string saveId = nextSaveId; KernelRuntime prepared = nextRuntime;
            nextStart = SessionStart.Title; nextSaveId = null; nextRuntime = null;
            Runtime = start == SessionStart.Continue && prepared != null ? prepared : CreateRuntime(); IsSessionActive = start != SessionStart.Title;
            if (start == SessionStart.Continue && prepared == null)
            {
                if (TryReadSave(saveId, out KernelRuntime restored, out string error)) { Runtime = restored; Feedback = "날짜 기록을 불러왔습니다."; }
                else { IsSessionActive = false; Feedback = error; }
            }
            ClinicScenario.EnsureKernelProfile(Runtime);
            CommandLocale defaultLanguage = Runtime.DisplayLanguage == "en" ? CommandLocale.English : CommandLocale.Korean;
            int preferredLanguage = PlayerPrefs.GetInt(PlayLanguagePreference, (int)defaultLanguage);
            ApplyPlayLanguage(Enum.IsDefined(typeof(CommandLocale), preferredLanguage) ? (CommandLocale)preferredLanguage : defaultLanguage);
            if (IsSessionActive) Runtime.DeferDayMailUntilIntro();
            Runtime.SceneActionRequested += SceneAction;
            Runtime.DayEnded += AutoSave;
            Runtime.NarrativeChanged += QueueNarrativeSave;
            if (terminalRoot != null) terminalRoot.GetComponentInChildren<TerminalController>(true).AttachRuntime(Runtime);
            ui = GetComponent<KernelGameUI>();
            views = inspection.GetComponent<CameraAngleController>();
            Transitions = inspection.GetComponent<SceneTransitionController>();
            if (Transitions == null) Transitions = inspection.gameObject.AddComponent<SceneTransitionController>();
            Transitions.Initialize(views, terminalRoot != null ? terminalRoot.GetComponentInChildren<CrtScreen>(true) : null, scenario != null ? scenario.presentation : null, terminalBody);
            if (start != SessionStart.Title) Transitions.BeginLoadedSceneCovered();
            inspection.Transitions = Transitions;
            Monitor?.AttachProfiles(scenario != null ? scenario.profiles : null);
            Monitor?.AttachWorldData(scenario != null ? scenario.worldData : null);
            inspection.AngleEntered += EnterView;
            inspection.OverviewEntered += LeaveView;
            initialized = true;
        }

        private void Start()
        {
            if (!initialized) { enabled = false; return; }
            Carry = GetComponent<ItemCarryController>();
            if (Carry == null) Carry = gameObject.AddComponent<ItemCarryController>();
            Carry.Initialize(views.ControlledCamera.transform); inspection.Carry = Carry;
            Carry.ManipulationAllowed = () => !IsDateTransitioning && !IsVisitPresenting && !IsViewTransitioning &&
                inspection.Mode != InspectionMode.Menu && !inspection.ModalInputBlocked && !inspection.NavigationBlocked && !inspection.TextInputFocused;
            Carry.PickedUp += PickedUpItem; Carry.Placed += StoredItem;
            if (Monitor != null)
            {
                Monitor.WaitForAppointment = WaitForNextAppointment;
                Monitor.FinishConsultation = FinishConsultation;
                Monitor.ClinicActionsAllowed = () => IsSessionActive && !changingScene && !IsDateTransitioning && !IsVisitPresenting &&
                    inspection.Mode != InspectionMode.Menu && !inspection.ModalInputBlocked && (Carry == null || Carry.HeldItem == null) && !ui.SummaryVisible;
            }
            // Bind and build the overlay before optional desk/paper presentation can fail or emit view events.
            ui.Initialize(this, inspection, font, views.ControlledCamera, terminalBody);
            gameObject.AddComponent<TitleBrandingView>().Initialize(inspection, views.ControlledCamera, terminalBody, font);
            if (Monitor != null) gameObject.AddComponent<MailNotificationPresenter>().Initialize(this, inspection, Monitor, Transitions.Settings);
            inspection.BackHandler = ui.Back;
            inspection.QuickSaveRequested = QuickSave;
            inspection.PointerBlocked = ui.BlocksPointer;
            inspection.InitializePointerFeedback(font);
            CreateCoffeeCopy();
            CreateCalendarText();
            InitializeDrawers();
            InitializeDesktop();
            newspaper = gameObject.AddComponent<DeskNewspaper>();
            newspaper.Initialize(newspaperAnchor, desktopPlacement, font, Carry, scenario != null ? scenario.newspaper : null); newspaper.Refresh(Runtime);
            newspaper.Document.AreaClicked += ReadPrintedArea;
            InitializeProfileSheet();
            RestoreStoredItems();
            initialLights = Array.ConvertAll(sceneLights, light => light != null && light.enabled);
            if (Runtime.Facts.Contains("turnOffLights")) SetLightsOff();
            visits = GetComponent<ClinicVisitPresentation>();
            if (visits == null) visits = gameObject.AddComponent<ClinicVisitPresentation>();
            visits.Configure(scenario != null ? scenario.profiles : null, Transitions);
            visits.Initialize(Runtime);
            if (Runtime.CurrentAppointment != null) SelectedProfileId = Runtime.CurrentAppointment.profileId;
            if (views.ActiveAngle == null && views.AngleSet != null) views.TryGoToIndex(views.AngleSet.InitialIndex, true);
            presentationInitialized = true; RefreshRoomTime();
            inspection.GetComponent<PauseMenuView>().Configure(new SystemMenuActions {
                NewGame = () => RestartScene(SessionStart.NewGame), Load = LoadGame, Save = SaveNow,
                Title = ReturnToTitle, Quit = QuitGame, ReadSaves = ReadSaveInfos,
                GetLanguage = () => Runtime.Language, SetLanguage = SetCommandLanguage });
            if (!IsSessionActive) inspection.ShowTitleMenu();
            StartCoroutine(StartReadySession());
        }
        private IEnumerator StartReadySession()
        {
            yield return Transitions.RevealLoadedSceneRoutine();
            if (!IsSessionActive) yield break;
            Runtime.RetrySceneActions();
            if (!Runtime.IsDayEnded && !Runtime.DayIntroSeen) yield return IntroduceDay();
        }

        private void Update()
        {
            if (!presentationInitialized || !IsSessionActive || changingScene || inspection.Mode == InspectionMode.Menu || ui.SummaryVisible) return;
            Runtime.AdvanceDayMailDelay(Time.unscaledDeltaTime);
            if (Transitions.IsBusy) return;
            if (!IsDateTransitioning) Runtime.Tick(Time.deltaTime / Mathf.Max(0.1f, scenario != null ? scenario.secondsPerGameMinute : 2f));
            if (!IsDateTransitioning && Runtime.IsClinicTransitioning && !IsVisitPresenting) StartCoroutine(PresentVisits());
            if (Runtime.IsDayEnded && !Runtime.IsClinicTransitioning && !IsDateTransitioning && !IsVisitPresenting && summaryShownDay != Runtime.Day &&
                !ui.IsCompositionProtected && !(terminalRoot?.GetComponentInChildren<TerminalController>(true)?.IsCompositionProtected ?? false))
                ShowDaySummary();
            if (narrativeDirty && !quickSaving && Time.unscaledTime >= narrativeSaveAt) AutoSave();
            foreach (SlidingDrawer drawer in drawers) drawer.SetInteractionEnabled(CanOperateDrawers);
            if (newspaperRevision != Runtime.Revision)
            { newspaper.Refresh(Runtime); newspaperRevision = Runtime.Revision; }
            if (pendingView != null && !views.IsTransitioning && !IsDateTransitioning && !IsVisitPresenting)
            {
                ui.ShowContext(pendingView);
                pendingView = null;
            }
            RefreshRoomTime();
        }

        private void RefreshRoomTime()
        {
            // Both meshes turn clockwise around local +Z. The hour mesh's zero is 09:00;
            // the minute mesh's zero is :00. Fractional minutes also advance the hour hand.
            if (clockHour != null) clockHour.localRotation = hourBasis * Quaternion.AngleAxis((float)((Runtime.Minute * 0.5 + 90) % 360), Vector3.forward);
            if (clockMinute != null) clockMinute.localRotation = minuteBasis * Quaternion.AngleAxis((float)(Runtime.Minute % 60 * 6), Vector3.forward);
            if (dayText != null) dayText.text = "DAY " + Runtime.Day.ToString("D2");
        }

        private static Quaternion ClockBasis(Transform hand)
        {
            if (hand == null) return Quaternion.identity;
            Vector3 tilt = hand.localEulerAngles;
            tilt.z = 0f;
            return Quaternion.Euler(tilt);
        }

        private void EnterView(CameraAnglePreset angle)
        {
            if (angle.Id != "profile" && profilePaper != null) profilePaper.CancelSwap();
            CurrentView = angle.Id; pendingView = angle.Id;
            if (ui != null) ui.HideContext();
        }
        private void LeaveView()
        {
            if (profilePaper != null) profilePaper.CancelSwap();
            CurrentView = "overview"; pendingView = null;
            if (ui != null) ui.HideContext();
        }
        public void ShowMonitor()
        {
            int index = views.AngleSet.FindId("monitor");
            if (index >= 0) inspection.InspectAngle(views.AngleSet.Get(index));
        }
        public void ShowMonitorPage(string pageId)
        {
            if (inspection.Mode == InspectionMode.Menu || IsVisitPresenting || IsDateTransitioning) return;
            Monitor?.ShowPage(pageId);
            ShowMonitor();
        }
        private void PickedUpItem(HoldableItem item)
        {
            if (item.Use == HeldItemUse.Newspaper) { Runtime.ReadNewspaper(); newspaper.Refresh(Runtime); }
        }
        private void StoredItem(HoldableItem item)
        {
            Vector3 position = item.transform.localPosition;
            Runtime.SetItemStorage(new StoredItemState { itemId = item.Id, containerId = item.Storage?.Id,
                hasPosition = true, x = position.x, y = position.y, z = position.z });
        }
        private void ReadPrintedArea(PaperDocument document, int index)
        {
            if (inspection.Mode == InspectionMode.Menu || inspection.ModalInputBlocked || IsViewTransitioning || IsDateTransitioning || IsVisitPresenting) return;
            if (!document.Item.IsHeld) Carry.TryPickup(document.Item);
            else ui.ShowDocument(document, index);
        }
        private void InitializeProfileSheet()
        {
            if (profileSheet == null) return;
            profilePaper = gameObject.AddComponent<DeskProfilePresenter>();
            profilePaper.Initialize(Runtime, profileSheet, profileArchiveStack, views.ControlledCamera, font, SelectedProfileId,
                id => Monitor != null ? Monitor.PortraitFor(id) : null, () => CanReadProfile,
                () => inspection.Mode == InspectionMode.Menu || inspection.NavigationBlocked || IsDateTransitioning || IsVisitPresenting || IsViewTransitioning,
                id => SelectedProfileId = id);
        }
        public string DisplayedProfileId => profilePaper != null ? profilePaper.ProfileId : null;
        public bool CanReadProfile => presentationInitialized && IsSessionActive && CurrentView == "profile" && inspection != null &&
            inspection.Mode != InspectionMode.Menu && !inspection.ModalInputBlocked && !inspection.NavigationBlocked && !IsViewTransitioning &&
            !IsDateTransitioning && !IsVisitPresenting && Carry != null && Carry.HeldItem == null;
        public bool CanSelectProfile => CanReadProfile && profilePaper != null && !profilePaper.IsSwapping;
        public bool SelectProfile(string id) => CanSelectProfile && profilePaper.SelectProfile(id);
        private void RestoreStoredItems()
        {
            foreach (StoredItemState state in Runtime.StoredItems)
            {
                HoldableItem item = state.itemId == newspaper.Item.Id ? newspaper.Item :
                    coffeeItem != null && state.itemId == coffeeItem.Id ? coffeeItem : guestCoffeeItem != null && state.itemId == guestCoffeeItem.Id ? guestCoffeeItem : null;
                ItemPlacementSurface surface = storageSurfaces.FirstOrDefault(s => s.Id == state.containerId);
                if (item == null || surface == null) continue;
                if (state.hasPosition) Carry.RestoreStorage(item, surface, new Vector3(state.x, state.y, state.z));
                else Carry.RestoreStorage(item, surface);
            }
        }
        public void BeginItemPlacement()
        {
            if (IsDateTransitioning || IsVisitPresenting || inspection.Mode == InspectionMode.Menu || inspection.TextInputFocused || Carry.HeldItem == null) return;
            if (!IsViewTransitioning) Carry.BeginPlacement();
        }
        public void BeginNextDay()
        {
            if (!Runtime.IsDayEnded) { Feedback = "18시 이후 다음 날로 진행할 수 있습니다."; return; }
            if (!IsDateTransitioning && !IsVisitPresenting && !IsViewTransitioning && !Runtime.IsClinicTransitioning && inspection.Mode != InspectionMode.Menu) StartCoroutine(ChangeDate());
        }
        public void ShowDaySummary()
        {
            if (!Runtime.IsDayEnded) { Feedback = "18시 이후 업무 정산을 확인할 수 있습니다."; return; }
            if (Runtime.IsClinicTransitioning || IsDateTransitioning || IsVisitPresenting || IsViewTransitioning || inspection.Mode == InspectionMode.Menu) return;
            DailyRecord record = Runtime.FinalizeDay(); AutoSave(); inspection.BeginTerminalPresentation(true); ui.ShowDaySummary(record); summaryShownDay = Runtime.Day;
        }
        public void CloseDaySummary() { ui.CloseDaySummary(); inspection.EndTerminalPresentation(); }
        private IEnumerator PresentVisits()
        {
            IsVisitPresenting = true; inspection.NavigationBlocked = true; ui.HideContext();
            while (Runtime.IsClinicTransitioning)
            {
                AppointmentProgress visit = Runtime.CurrentAppointment;
                if (visit.phase == VisitPhase.Arriving) SelectedProfileId = visit.profileId;
                yield return visits.Play(Runtime, visit); AutoSave();
            }
            inspection.NavigationBlocked = inspection.PresentingOnTerminal; IsVisitPresenting = false;
        }
        public void WaitForNextAppointment()
        {
            if (IsSessionActive && (Monitor?.ClinicActionsAllowed?.Invoke() ?? false) && (Runtime.CanWaitForNextAppointment || Runtime.CanWaitUntilClosing)) StartCoroutine(WaitForReservation());
        }
        public void FinishConsultation()
        {
            if (IsSessionActive && (Monitor?.ClinicActionsAllowed?.Invoke() ?? false) && Runtime.CurrentAppointment?.phase == VisitPhase.Talking) Runtime.FinishConsultation();
        }
        private IEnumerator WaitForReservation()
        {
            IsDateTransitioning = true; inspection.NavigationBlocked = true; ui.HideContext();
            inspection.BeginTerminalPresentation(false);
            bool closing = Runtime.NextAppointment == null;
            string subtitle = KernelRuntime.TimeOfDay(Runtime.NextAppointment?.startMinute ?? 1080) + (closing ? " 업무 마감" : " 다음 예약");
            ui.ShowDayIntro("기다리는 중", subtitle);
            yield return Transitions.Play(Transitions.Settings?.day, TransitionStyle.TerminalFade, () =>
            { if (closing) Runtime.WaitUntilClosing(); else Runtime.WaitForNextAppointment(); AutoSave(); });
            if (Runtime.IsClinicTransitioning) yield return PresentVisits();
            ui.HideDay(); inspection.EndTerminalPresentation(); inspection.NavigationBlocked = false; IsDateTransitioning = false;
        }
        private IEnumerator IntroduceDay()
        {
            IsDateTransitioning = true; inspection.NavigationBlocked = true;
            while (Transitions.IsBusy) yield return null;
            ui.HideContext(); ui.HideDay(); inspection.SnapToOverview(); Runtime.DeferDayMailUntilIntro();
            yield return ui.PlayDayStart(Runtime.Day, Runtime.DaySubtitle, () =>
            {
                float mailDelay = Runtime.Day == 1 ? Transitions.Settings?.firstDayMailDelaySeconds ?? 5f : 0f;
                Runtime.MarkDayIntroSeen(mailDelay);
                AutoSave();
            });
            ui.HideDay(); inspection.NavigationBlocked = false; IsDateTransitioning = false;
        }
        private IEnumerator ChangeDate()
        {
            IsDateTransitioning = true; inspection.NavigationBlocked = true;
            ui.HideContext(); Carry.PutDown(); inspection.BeginTerminalPresentation(false);
            ui.ShowDayIntro("업무 종료", "다음 날을 준비합니다.");
            yield return Transitions.Play(Transitions.Settings?.day, TransitionStyle.TerminalFade, () =>
            {
                ui.HideDay(); inspection.EndTerminalPresentation(); inspection.NavigationBlocked = true;
                Carry.ReturnToDesk(newspaper.Item); Runtime.SetItemStorage(newspaper.Item.Id, null);
                Runtime.NextDay(true); newspaper.Refresh(Runtime);
                inspection.SnapToOverview(); RefreshRoomTime(); AutoSave();
            });
            yield return IntroduceDay();
        }
        private void InitializeDrawers()
        {
            if (deskSurface == null) return;
            const string prefix = "Desk_big_Drawer_";
            foreach (Transform child in deskSurface.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith(prefix, StringComparison.Ordinal)).OrderBy(t => t.name, StringComparer.Ordinal))
            {
                SlidingDrawer drawer = child.GetComponent<SlidingDrawer>();
                if (drawer == null) drawer = child.gameObject.AddComponent<SlidingDrawer>();
                drawer.Initialize(Runtime, "desk." + child.name, "서랍 " + child.name.Substring(prefix.Length)); drawers.Add(drawer);
                var storage = new GameObject("DrawerInterior"); storage.transform.SetParent(child, false);
                ItemPlacementSurface surface = storage.AddComponent<ItemPlacementSurface>();
                Bounds local = LocalBounds(child, child.GetComponentsInChildren<Renderer>(), new Bounds(Vector3.zero, new Vector3(0.4f, 0.14f, 0.4f)));
                Vector3 scale = child.lossyScale;
                Vector3 size = new Vector3(Mathf.Max(0.01f, local.size.x - 0.04f / Mathf.Abs(scale.x)), Mathf.Max(0.01f, local.size.y - 0.025f / Mathf.Abs(scale.y)), Mathf.Max(0.01f, local.size.z - 0.06f / Mathf.Abs(scale.z)));
                surface.Initialize(drawer.Id, drawer, new Bounds(local.center + Vector3.up * 0.008f, size)); storageSurfaces.Add(surface);
                var front = new GameObject("DrawerFront"); front.transform.SetParent(child, false);
                front.AddComponent<DrawerFrontTarget>().Initialize(drawer, new Bounds(new Vector3(local.center.x, local.center.y, local.max.z),
                    new Vector3(local.size.x, local.size.y, 0.025f / Mathf.Abs(scale.z))));
            }
        }
        private void InitializeDesktop()
        {
            if (deskSurface == null) return;
            Renderer tabletop = deskSurface.GetComponent<Renderer>();
            if (tabletop == null) return;
            Bounds local = tabletop.localBounds;
            float height = 0.25f / Mathf.Abs(deskSurface.lossyScale.y);
            var desktop = new GameObject("DesktopPlacement"); desktop.transform.SetParent(deskSurface, false);
            desktopPlacement = desktop.AddComponent<ItemPlacementSurface>();
            desktopPlacement.Initialize("desk.top", null, new Bounds(new Vector3(local.center.x, local.max.y + height * 0.5f, local.center.z),
                new Vector3(local.size.x, height, local.size.z)));
            storageSurfaces.Add(desktopPlacement);
            inspection.Desktop = desktopPlacement;
        }
        private static Bounds LocalBounds(Transform root, Renderer[] renderers, Bounds fallback = default)
        {
            Bounds result = fallback; bool found = false;
            foreach (Renderer renderer in renderers)
            {
                Matrix4x4 toRoot = root.worldToLocalMatrix * renderer.localToWorldMatrix;
                Bounds local = renderer.localBounds;
                for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = toRoot.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents, new Vector3(x, y, z)));
                    if (!found) { result = new Bounds(corner, Vector3.zero); found = true; } else result.Encapsulate(corner);
                }
            }
            return result;
        }

        public void SetCommandLanguage(CommandLocale language)
        {
            ApplyPlayLanguage(language);
            PlayerPrefs.SetInt(PlayLanguagePreference, (int)language);
            PlayerPrefs.Save();
            if (ui != null) ui.RefreshCommandLanguage();
        }

        private void ApplyPlayLanguage(CommandLocale language)
        {
            Runtime.Language = language;
            Runtime.DisplayLanguage = language == CommandLocale.Korean ? "ko" : "en";
        }

        private void SceneAction(string agent, string action)
        {
            if (action == "turnOffLights") { SetLightsOff(); return; }
            if (action != "coffee" || delivering) return;
            if (curtainCloth == null || guestCoffeeTemplate == null || ownCoffee == null)
            {
                Runtime.Report("커피 전달 대기: 커튼/커피 메시 연결을 확인하세요.");
                return;
            }
            StartCoroutine(DeliverCoffee(agent));
        }

        private void SetLightsOff()
        {
            if (sceneLights == null) return;
            foreach (Light light in sceneLights) if (light != null) light.enabled = false;
        }

        private IEnumerator DeliverCoffee(string agent)
        {
            delivering = true;
            Runtime.Report(agent + ": 커튼을 닫고 보안 전달을 시작합니다.");
            Vector3 covered = new Vector3(foldedScale.x, foldedScale.y, 1f);
            yield return ScaleCurtain(covered, 0.5f);
            ownCoffee.SetActive(true);
            yield return WaitWorkingSeconds(0.4f);
            yield return ScaleCurtain(foldedScale, 0.5f);
            delivering = false;
            Runtime.CompleteCoffee(agent);
        }

        private IEnumerator ScaleCurtain(Vector3 target, float duration)
        {
            Vector3 start = curtainCloth.localScale;
            float elapsed = 0;
            while (elapsed < duration)
            {
                elapsed += Runtime.IsDayEnded ? 0f : Time.deltaTime;
                curtainCloth.localScale = Vector3.Lerp(start, target, Mathf.SmoothStep(0, 1, elapsed / duration));
                yield return null;
            }
            curtainCloth.localScale = target;
        }

        private IEnumerator WaitWorkingSeconds(float duration)
        {
            for (float elapsed = 0; elapsed < duration; elapsed += Runtime.IsDayEnded ? 0f : Time.deltaTime) yield return null;
        }

        private void CreateCoffeeCopy()
        {
            if (guestCoffeeTemplate == null) return;
            ownCoffee = Instantiate(guestCoffeeTemplate.gameObject);
            ownCoffee.name = "KernelCoffee";
            ownCoffee.transform.SetParent(transform, true);
            ownCoffee.transform.localScale = guestCoffeeTemplate.lossyScale;
            Vector3 position = coffeeAnchor != null ? coffeeAnchor.position : new Vector3(0.1f, 0.9f, -2.55f);
            if (deskSurface != null)
            {
                Renderer[] renderers = deskSurface.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    Bounds bounds = renderers[0].bounds;
                    foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
                    position.y = bounds.max.y + 0.004f;
                }
            }
            ownCoffee.transform.SetPositionAndRotation(position, coffeeAnchor != null ? coffeeAnchor.rotation : guestCoffeeTemplate.rotation);
            Renderer[] cupRenderers = ownCoffee.GetComponentsInChildren<Renderer>();
            if (cupRenderers.Length > 0)
            {
                Bounds bounds = cupRenderers[0].bounds;
                foreach (Renderer renderer in cupRenderers) bounds.Encapsulate(renderer.bounds);
                ownCoffee.transform.position += Vector3.up * (position.y - bounds.min.y);
            }
            coffeeItem = RegisterCoffeeItem(ownCoffee, "desk.coffee", "커피");
            guestCoffeeItem = RegisterCoffeeItem(guestCoffeeTemplate.gameObject, "desk.guest-coffee", "손님용 커피");
            ownCoffee.SetActive(Runtime.CoffeeReceived);
        }
        private HoldableItem RegisterCoffeeItem(GameObject cup, string id, string label)
        {
            HoldableItem item = cup.GetComponent<HoldableItem>();
            if (item == null) item = cup.AddComponent<HoldableItem>();
            item.Configure(id, label, HeldItemUse.Custom);
            if (cup.GetComponentsInChildren<Collider>().Length == 0)
            {
                BoxCollider collider = cup.AddComponent<BoxCollider>(); Bounds local = LocalBounds(cup.transform, cup.GetComponentsInChildren<Renderer>());
                collider.center = local.center; collider.size = local.size;
            }
            Carry.Register(item); return item;
        }

        private void CreateCalendarText()
        {
            if (calendar == null || font == null) return;
            var go = new GameObject("CalendarDay", typeof(TextMesh));
            go.transform.SetParent(calendar, true);
            var renderer = calendar.GetComponentInChildren<Renderer>();
            Vector3 position = renderer != null ? renderer.bounds.center : calendar.position;
            if (renderer != null) position.y = renderer.bounds.max.y + 0.004f;
            go.transform.SetPositionAndRotation(position, calendar.rotation);
            dayText = go.GetComponent<TextMesh>();
            dayText.font = font; dayText.fontSize = 48; dayText.characterSize = 0.015f;
            dayText.anchor = TextAnchor.MiddleCenter; dayText.color = new Color(0.1f, 0.16f, 0.1f);
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }

        private void OnDestroy()
        {
            if (initialized && (narrativeDirty || quickSaving)) TrySave(GameSaveKind.Auto);
            StopAllCoroutines();
            if (profilePaper != null) profilePaper.CancelSwap();
            if (sceneLights != null && initialLights != null) for (int i = 0; i < Math.Min(sceneLights.Length, initialLights.Length); i++) if (sceneLights[i] != null) sceneLights[i].enabled = initialLights[i];
            if (sceneBasisCaptured && curtainCloth != null) curtainCloth.localScale = foldedScale;
            if (Carry != null) { Carry.PickedUp -= PickedUpItem; Carry.Placed -= StoredItem; Carry.ManipulationAllowed = null; Carry.EndSession(); }
            if (inspection != null) { inspection.AngleEntered -= EnterView; inspection.OverviewEntered -= LeaveView; inspection.BackHandler = null; inspection.QuickSaveRequested = null; inspection.PointerBlocked = null; inspection.Carry = null; inspection.Transitions = null; inspection.NavigationBlocked = false; }
            MonitorPageHost host = Monitor;
            if (host != null) { host.WaitForAppointment = null; host.FinishConsultation = null; host.ClinicActionsAllowed = null; }
            if (Runtime != null)
            {
                Runtime.SceneActionRequested -= SceneAction;
                Runtime.DayEnded -= AutoSave;
                Runtime.NarrativeChanged -= QueueNarrativeSave;
            }
        }
    }
}
