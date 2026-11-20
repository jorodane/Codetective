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
        [SerializeField] private Transform profilePaperAnchor;
        [Tooltip("Explicit paper stacks/surfaces supporting the profile sheet. No scene-wide name search.")]
        [SerializeField] private Transform[] profilePaperSupports = Array.Empty<Transform>();
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
        private PaperDocument profileDocument;
        private int paperRevision = -1;
        private string printedProfile;
        private int paperPageRevision = -1;
        private readonly List<ItemPlacementSurface> storageSurfaces = new List<ItemPlacementSurface>();
        private ClinicVisitPresentation visits;
        private int summaryShownDay;
        private readonly List<SlidingDrawer> drawers = new List<SlidingDrawer>();
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
        private const string CommandLanguagePreference = "Codetective.CommandLanguage";
        private bool narrativeDirty;
        private float narrativeSaveAt;
        private bool initialized;
        private bool presentationInitialized;
        private bool sceneBasisCaptured;
        private bool changingScene;
        private enum SessionStart { Title, NewGame, Continue }
        private static SessionStart nextStart = SessionStart.Title;
        private static string nextSaveId;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSessionStart() { nextStart = SessionStart.Title; nextSaveId = null; }
        private KernelRuntime CreateRuntime() => scenario != null ? scenario.CreateRuntime() : ClinicScenario.Create();

        private void Awake()
        {
            foldedScale = curtainCloth != null ? curtainCloth.localScale : Vector3.one;
            hourBasis = ClockBasis(clockHour);
            minuteBasis = ClockBasis(clockMinute);
            sceneBasisCaptured = true;
            SystemMenuMonitorView.ApplySavedSettings();
            SessionStart start = nextStart; string saveId = nextSaveId; nextStart = SessionStart.Title; nextSaveId = null;
            Runtime = CreateRuntime(); IsSessionActive = start != SessionStart.Title;
            if (start == SessionStart.Continue)
            {
                if (TryReadSave(saveId, out KernelRuntime restored, out string error)) { Runtime = restored; Feedback = "날짜 기록을 불러왔습니다."; }
                else { IsSessionActive = false; Feedback = error; }
            }
            ClinicScenario.EnsureKernelProfile(Runtime);
            int preferredLanguage = PlayerPrefs.GetInt(CommandLanguagePreference, (int)(scenario != null ? scenario.commandLanguage : CommandLocale.Korean));
            if (Enum.IsDefined(typeof(CommandLocale), preferredLanguage)) Runtime.Language = (CommandLocale)preferredLanguage;
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
            inspection.Transitions = Transitions;
            Monitor?.AttachProfiles(scenario != null ? scenario.profiles : null);
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
            ui.Initialize(this, inspection, font);
            if (Monitor != null) gameObject.AddComponent<MailNotificationPresenter>().Initialize(this, inspection, Monitor, Transitions.Settings);
            inspection.BackHandler = ui.Back;
            inspection.QuickSaveRequested = QuickSave;
            inspection.PointerBlocked = ui.BlocksPointer;
            CreateCoffeeCopy();
            CreateCalendarText();
            InitializeDrawers();
            InitializeDesktop();
            newspaper = gameObject.AddComponent<DeskNewspaper>();
            newspaper.Initialize(newspaperAnchor, deskSurface, font, Carry, scenario != null ? scenario.newspaper : null); newspaper.Refresh(Runtime);
            newspaper.Document.AreaClicked += ReadPrintedArea;
            InitializeProfilePaper();
            RestoreStoredItems();
            initialLights = Array.ConvertAll(sceneLights, light => light != null && light.enabled);
            if (Runtime.Facts.Contains("turnOffLights")) SetLightsOff();
            visits = GetComponent<ClinicVisitPresentation>();
            if (visits == null) visits = gameObject.AddComponent<ClinicVisitPresentation>();
            visits.Configure(scenario != null ? scenario.profiles : null, Transitions);
            visits.Initialize(Runtime);
            if (Runtime.CurrentAppointment != null) SelectedProfileId = Runtime.CurrentAppointment.profileId;
            if (views.ActiveAngle == null && views.AngleSet != null) views.TryGoToIndex(views.AngleSet.InitialIndex, true);
            presentationInitialized = true; RefreshRoomTime(); RefreshProfilePaper();
            inspection.GetComponent<PauseMenuView>().Configure(new SystemMenuActions {
                NewGame = () => RestartScene(SessionStart.NewGame), Load = LoadGame, Save = SaveNow,
                Title = ReturnToTitle, Quit = QuitGame, ReadSaves = ReadSaveInfos,
                GetLanguage = () => Runtime.Language, SetLanguage = SetCommandLanguage });
            if (!IsSessionActive) { inspection.ShowTitleMenu(); Transitions.RevealLoadedScene(); return; }
            Transitions.RevealLoadedScene();
            Runtime.RetrySceneActions();
            if (!Runtime.IsDayEnded && !Runtime.DayIntroSeen) StartCoroutine(IntroduceDay());
        }

        private void Update()
        {
            if (!presentationInitialized || !IsSessionActive || changingScene || inspection.Mode == InspectionMode.Menu || ui.SummaryVisible || Transitions.IsBusy) return;
            if (!IsDateTransitioning) Runtime.Tick(Time.deltaTime / Mathf.Max(0.1f, scenario != null ? scenario.secondsPerGameMinute : 2f));
            if (!IsDateTransitioning && Runtime.IsClinicTransitioning && !IsVisitPresenting) StartCoroutine(PresentVisits());
            if (Runtime.IsDayEnded && !Runtime.IsClinicTransitioning && !IsDateTransitioning && !IsVisitPresenting && summaryShownDay != Runtime.Day &&
                !ui.IsCompositionProtected && !(terminalRoot?.GetComponentInChildren<TerminalController>(true)?.IsCompositionProtected ?? false))
                ShowDaySummary();
            if (narrativeDirty && !quickSaving && Time.unscaledTime >= narrativeSaveAt) AutoSave();
            foreach (SlidingDrawer drawer in drawers) drawer.SetInteractionEnabled(CanOperateDrawers);
            if (profileDocument != null && (paperRevision != Runtime.Revision || printedProfile != SelectedProfileId || paperPageRevision != (Monitor?.ProfilePageRevision ?? 0)))
            { RefreshProfilePaper(); newspaper.Refresh(Runtime); paperRevision = Runtime.Revision; }
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
            CurrentView = angle.Id; pendingView = angle.Id;
            if (ui != null) ui.HideContext();
        }
        private void LeaveView()
        {
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
            Runtime.SetItemStorage(item.Id, null);
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
        private void InitializeProfilePaper()
        {
            Transform anchor = profilePaperAnchor;
            if (anchor == null)
            {
                InspectionTarget target = inspection.GetTarget("profile");
                if (target != null) anchor = target.transform;
            }
            var paper = new GameObject("PatientProfilePaper"); paper.transform.SetParent(transform, false);
            Vector3 position = anchor != null ? anchor.position + Vector3.up * 0.008f : new Vector3(-0.76f, 0.918f, -2.312f);
            foreach (Transform stack in profilePaperSupports)
            {
                if (stack == null) continue;
                foreach (Renderer renderer in stack.GetComponentsInChildren<Renderer>())
                    if (Vector2.Distance(new Vector2(renderer.bounds.center.x, renderer.bounds.center.z), new Vector2(position.x, position.z)) < 0.32f)
                        position.y = Mathf.Max(position.y, renderer.bounds.max.y + 0.004f);
            }
            paper.transform.position = position;
            HoldableItem item = paper.AddComponent<HoldableItem>(); item.Configure("desk.profile", "인물 프로필", HeldItemUse.Profile, SelectedProfileId);
            profileDocument = paper.AddComponent<PaperDocument>();
            profileDocument.Initialize(item, Carry, views.ControlledCamera, font, new Vector2(0.27f, 0.35f)); Carry.Register(item);
            profileDocument.AreaClicked += ReadPrintedArea;
            profileDocument.PageChanged += index => Monitor?.SelectJournalPage(printedProfile, index);
            RefreshProfilePaper();
        }
        private void RefreshProfilePaper()
        {
            if (!Runtime.VisibleProfileIds.Contains(SelectedProfileId)) SelectedProfileId = Runtime.VisibleProfileIds.FirstOrDefault();
            printedProfile = SelectedProfileId;
            paperPageRevision = Monitor?.ProfilePageRevision ?? 0;
            string label = (printedProfile != null ? Runtime.Entity(printedProfile)?.Label : null) ?? printedProfile ?? "미등록";
            profileDocument.Item.Configure("desk.profile", label + " / 프로필", HeldItemUse.Profile, printedProfile);
            if (printedProfile == null)
            {
                profileDocument.SetContent("인물 프로필", new[] { new PaperSection { title = "미등록", text = "열람 가능한 프로필이 없습니다." } });
                return;
            }
            PersonProfile profile = Runtime.ProfileCard(printedProfile); Sprite portrait = Monitor?.PortraitFor(printedProfile);
            int count = Math.Max(1, Runtime.NotesFor(printedProfile).Count());
            PaperSection[] pages = Enumerable.Range(0, count).Select(index => new PaperSection { title = "상담 일지 " + (index + 1) + " / " + count,
                profile = profile, portrait = portrait, text = Runtime.ProfileJournalText(printedProfile, index) }).ToArray();
            profileDocument.SetPagedContent("인물 프로필 / " + label, pages, Monitor?.JournalPageFor(printedProfile) ?? 0);
        }
        private void RestoreStoredItems()
        {
            foreach (StoredItemState state in Runtime.StoredItems)
            {
                HoldableItem item = state.itemId == newspaper.Item.Id ? newspaper.Item : state.itemId == profileDocument.Item.Id ? profileDocument.Item :
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
                Runtime.MarkDayIntroSeen();
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
            foreach (Transform child in deskSurface.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Desk_big_Drawer_", StringComparison.Ordinal)).OrderBy(t => t.name, StringComparer.Ordinal))
            {
                SlidingDrawer drawer = child.GetComponent<SlidingDrawer>();
                if (drawer == null) drawer = child.gameObject.AddComponent<SlidingDrawer>();
                drawer.Initialize(Runtime, "desk." + child.name); drawers.Add(drawer);
                var storage = new GameObject("DrawerInterior"); storage.transform.SetParent(child, false);
                ItemPlacementSurface surface = storage.AddComponent<ItemPlacementSurface>();
                Bounds local = new Bounds(Vector3.zero, new Vector3(0.4f, 0.14f, 0.4f)); bool found = false;
                foreach (Renderer renderer in child.GetComponentsInChildren<Renderer>())
                {
                    Bounds world = renderer.bounds;
                    for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 corner = child.InverseTransformPoint(world.center + Vector3.Scale(world.extents, new Vector3(x, y, z)));
                        if (!found) { local = new Bounds(corner, Vector3.zero); found = true; } else local.Encapsulate(corner);
                    }
                }
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
            Renderer[] renderers = deskSurface.GetComponentsInChildren<Renderer>().Where(r => r.GetComponentInParent<SlidingDrawer>() == null).ToArray();
            if (renderers.Length == 0) return;
            Bounds local = LocalBounds(deskSurface, renderers); Vector3 scale = deskSurface.lossyScale;
            float height = 0.25f / Mathf.Abs(scale.y);
            var desktop = new GameObject("DesktopPlacement"); desktop.transform.SetParent(deskSurface, false);
            ItemPlacementSurface surface = desktop.AddComponent<ItemPlacementSurface>();
            surface.Initialize("desk.top", null, new Bounds(new Vector3(local.center.x, local.max.y + height * 0.5f + 0.002f / Mathf.Abs(scale.y), local.center.z),
                new Vector3(local.size.x, height, local.size.z)));
            storageSurfaces.Add(surface);
        }
        private static Bounds LocalBounds(Transform root, Renderer[] renderers)
        {
            Bounds result = default; bool found = false;
            foreach (Renderer renderer in renderers)
                for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 point = root.InverseTransformPoint(renderer.bounds.center + Vector3.Scale(renderer.bounds.extents, new Vector3(x, y, z)));
                    if (!found) { result = new Bounds(point, Vector3.zero); found = true; } else result.Encapsulate(point);
                }
            return result;
        }

        public void SetCommandLanguage(CommandLocale language)
        {
            Runtime.Language = language;
            PlayerPrefs.SetInt(CommandLanguagePreference, (int)language);
            PlayerPrefs.Save();
            if (ui != null) ui.RefreshCommandLanguage();
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
            if (sceneLights != null && initialLights != null) for (int i = 0; i < Math.Min(sceneLights.Length, initialLights.Length); i++) if (sceneLights[i] != null) sceneLights[i].enabled = initialLights[i];
            if (sceneBasisCaptured && curtainCloth != null) curtainCloth.localScale = foldedScale;
            if (Carry != null) { Carry.PickedUp -= PickedUpItem; Carry.Placed -= StoredItem; Carry.ManipulationAllowed = null; Carry.PutDown(); }
            if (profileDocument != null) Destroy(profileDocument.gameObject);
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
