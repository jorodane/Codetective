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
    public sealed class KernelGameController : MonoBehaviour
    {
        [SerializeField] private KernelScenarioDefinition scenario;
        [SerializeField] private Transform terminalRoot;
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

        public KernelRuntime Runtime { get; private set; }
        public string CurrentView { get; private set; } = "overview";
        public bool IsViewTransitioning => views != null && views.IsTransitioning;
        public DialoguePresentationDefinition DialoguePresentation => dialoguePresentation;
        public ItemCarryController Carry { get; private set; }
        public MonitorPageHost Monitor => terminalRoot?.GetComponentInChildren<MonitorPageHost>(true);
        public PaperDocument NewspaperDocument => newspaper?.Document;
        public bool IsDateTransitioning { get; private set; }
        public bool IsVisitPresenting { get; private set; }
        public string SelectedProfileId
        {
            get => terminalRoot?.GetComponentInChildren<MonitorPageHost>(true)?.SelectedProfileId ?? selectedProfile;
            set { selectedProfile = value; MonitorPageHost host = terminalRoot?.GetComponentInChildren<MonitorPageHost>(true); if (host != null) host.SelectedProfileId = value; }
        }
        private string selectedProfile = "client";
        private DeskNewspaper newspaper;
        private PaperDocument profileDocument;
        private int paperRevision = -1;
        private string printedProfile;
        private bool pendingPlacement;
        private readonly List<ItemPlacementSurface> storageSurfaces = new List<ItemPlacementSurface>();
        private ClinicVisitPresentation visits;
        private int summaryShownDay;
        private readonly List<SlidingDrawer> drawers = new List<SlidingDrawer>();
        public IReadOnlyList<SlidingDrawer> Drawers => drawers;
        public bool CanOperateDrawers => inspection != null && inspection.Mode == InspectionMode.Inspecting && CurrentView == "drawer" &&
            !IsViewTransitioning && !inspection.ModalInputBlocked && !IsDateTransitioning && !IsVisitPresenting;
        public string SavePath => Path.Combine(Application.persistentDataPath, "Codetective", "daily-autosave.json");
        public string Feedback { get; private set; } = "Kernel 작전망 연결";
        private KernelGameUI ui;
        private CameraAngleController views;
        private Vector3 foldedScale;
        private Quaternion hourBasis;
        private Quaternion minuteBasis;
        private GameObject ownCoffee;
        private TextMesh dayText;
        private string pendingView;
        private bool delivering;
        private Light[] sceneLights;
        private bool[] initialLights;
        private const string CommandLanguagePreference = "Codetective.CommandLanguage";
        private bool narrativeDirty;
        private float narrativeSaveAt;
        private bool initialized;
        private bool sceneBasisCaptured;

        private void Awake()
        {
            foldedScale = curtainCloth != null ? curtainCloth.localScale : Vector3.one;
            hourBasis = ClockBasis(clockHour);
            minuteBasis = ClockBasis(clockMinute);
            sceneBasisCaptured = true;
            Runtime = scenario != null ? scenario.CreateRuntime() : ClinicScenario.Create();
            if (scenario != null && scenario.loadDailySave && File.Exists(SavePath))
            {
                try { Runtime.Restore(JsonUtility.FromJson<KernelSnapshot>(File.ReadAllText(SavePath))); Feedback = "날짜 기록을 불러왔습니다."; }
                catch (Exception error) { Runtime = scenario.CreateRuntime(); Feedback = "저장 기록을 읽지 못해 새 작전을 시작합니다: " + error.Message; }
            }
            ClinicScenario.EnsureKernelProfile(Runtime);
            int preferredLanguage = PlayerPrefs.GetInt(CommandLanguagePreference, (int)(scenario != null ? scenario.commandLanguage : CommandLocale.Korean));
            if (Enum.IsDefined(typeof(CommandLocale), preferredLanguage)) Runtime.Language = (CommandLocale)preferredLanguage;
            Runtime.SceneActionRequested += SceneAction;
            Runtime.DayEnded += SaveNow;
            Runtime.NarrativeChanged += QueueNarrativeSave;
            if (terminalRoot != null) terminalRoot.GetComponentInChildren<TerminalController>(true).AttachRuntime(Runtime);
            ui = GetComponent<KernelGameUI>();
            views = inspection.GetComponent<CameraAngleController>();
            inspection.AngleEntered += EnterView;
            inspection.OverviewEntered += LeaveView;
            initialized = true;
        }

        private void Start()
        {
            if (!initialized) { enabled = false; return; }
            CreateCoffeeCopy();
            CreateCalendarText();
            InitializeDrawers();
            Carry = GetComponent<ItemCarryController>() ?? gameObject.AddComponent<ItemCarryController>();
            Carry.Initialize(views.ControlledCamera.transform); Carry.Used += UseItem; inspection.Carry = Carry;
            Carry.ManipulationAllowed = () => !IsDateTransitioning && !IsVisitPresenting && !IsViewTransitioning &&
                inspection.Mode != InspectionMode.Menu && !inspection.ModalInputBlocked && !inspection.NavigationBlocked && !inspection.TextInputFocused;
            Carry.PickedUp += PickedUpItem; Carry.Placed += StoredItem;
            newspaper = gameObject.AddComponent<DeskNewspaper>();
            newspaper.Initialize(newspaperAnchor, deskSurface, font, Carry); newspaper.Refresh(Runtime);
            newspaper.Document.AreaClicked += ReadPrintedArea;
            InitializeProfilePaper();
            RestoreStoredItems();
            ui.Initialize(this, inspection, font);
            sceneLights = FindObjectsOfType<Light>();
            initialLights = Array.ConvertAll(sceneLights, light => light.enabled);
            if (Runtime.Facts.Contains("turnOffLights")) SetLightsOff();
            inspection.BackHandler = ui.Back;
            inspection.PointerBlocked = ui.BlocksPointer;
            visits = GetComponent<ClinicVisitPresentation>() ?? gameObject.AddComponent<ClinicVisitPresentation>(); visits.Initialize(Runtime);
            if (Runtime.CurrentAppointment != null) SelectedProfileId = Runtime.CurrentAppointment.profileId;
            Runtime.RetrySceneActions();
            if (!Runtime.IsDayEnded && !Runtime.DayIntroSeen) StartCoroutine(IntroduceDay());
        }

        private void Update()
        {
            if (!initialized) return;
            if (!IsDateTransitioning) Runtime.Tick(Time.deltaTime / Mathf.Max(0.1f, scenario != null ? scenario.secondsPerGameMinute : 2f));
            if (!IsDateTransitioning && Runtime.IsClinicTransitioning && !IsVisitPresenting) StartCoroutine(PresentVisits());
            if (Runtime.IsDayEnded && !Runtime.IsClinicTransitioning && !IsDateTransitioning && !IsVisitPresenting && summaryShownDay != Runtime.Day &&
                !ui.IsCompositionProtected && !(terminalRoot?.GetComponentInChildren<TerminalController>(true)?.IsCompositionProtected ?? false))
                ShowDaySummary();
            if (narrativeDirty && Time.unscaledTime >= narrativeSaveAt) { narrativeDirty = false; SaveNow(); }
            foreach (SlidingDrawer drawer in drawers) drawer.SetInteractionEnabled(CanOperateDrawers);
            if (profileDocument != null && (paperRevision != Runtime.Revision || printedProfile != SelectedProfileId))
            { RefreshProfilePaper(); newspaper.Refresh(Runtime); paperRevision = Runtime.Revision; }
            if (pendingView != null && !views.IsTransitioning && !IsDateTransitioning && !IsVisitPresenting)
            {
                ui.ShowContext(pendingView);
                pendingView = null;
            }
            if (pendingPlacement && (CurrentView != "drawer" || inspection.Mode == InspectionMode.Menu || Carry.HeldItem == null)) pendingPlacement = false;
            if (pendingPlacement && !IsViewTransitioning && !IsDateTransitioning && !IsVisitPresenting)
            { pendingPlacement = false; Carry.BeginPlacement(); }
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

        private void EnterView(CameraAnglePreset angle) { CurrentView = angle.Id; pendingView = angle.Id; if (angle.Id != "drawer") pendingPlacement = false; ui.HideContext(); }
        private void LeaveView() { CurrentView = "overview"; pendingView = null; pendingPlacement = false; ui.HideContext(); }
        public void ShowMonitor()
        {
            int index = views.AngleSet.FindId("monitor");
            if (index >= 0) inspection.InspectAngle(views.AngleSet.Get(index));
        }
        public void ShowMonitorPage(string pageId)
        {
            if (inspection.Mode == InspectionMode.Menu || IsVisitPresenting || IsDateTransitioning) return;
            terminalRoot?.GetComponentInChildren<MonitorPageHost>(true)?.ShowPage(pageId);
            ShowMonitor();
        }
        private void UseItem(HoldableItem item)
        {
            PaperDocument document = item.GetComponent<PaperDocument>();
            if (document != null) ui.ShowDocument(document, 0);
        }
        private void PickedUpItem(HoldableItem item)
        {
            Runtime.SetItemStorage(item.Id, null);
            if (item.Use == HeldItemUse.Newspaper) { Runtime.ReadNewspaper(); newspaper.Refresh(Runtime); }
        }
        private void StoredItem(HoldableItem item) => Runtime.SetItemStorage(item.Id, item.Storage?.Id);
        private void ReadPrintedArea(PaperDocument document, int index)
        {
            if (inspection.Mode == InspectionMode.Menu || inspection.ModalInputBlocked || IsViewTransitioning || IsDateTransitioning || IsVisitPresenting) return;
            if (document.Item.Use == HeldItemUse.Newspaper && !document.Item.IsHeld) Carry.TryPickup(document.Item);
            else ui.ShowDocument(document, index);
        }
        private void InitializeProfilePaper()
        {
            Transform anchor = profilePaperAnchor;
            if (anchor == null) anchor = FindObjectsOfType<InspectionTarget>().FirstOrDefault(t => t.Angle != null && t.Angle.Id == "profile")?.transform;
            var paper = new GameObject("PatientProfilePaper"); paper.transform.SetParent(transform, false);
            Vector3 position = anchor != null ? anchor.position + Vector3.up * 0.008f : new Vector3(-0.76f, 0.918f, -2.312f);
            foreach (Transform stack in FindObjectsOfType<Transform>().Where(t => t.name.StartsWith("Paper_Stack", StringComparison.Ordinal)))
                foreach (Renderer renderer in stack.GetComponentsInChildren<Renderer>())
                    if (Vector2.Distance(new Vector2(renderer.bounds.center.x, renderer.bounds.center.z), new Vector2(position.x, position.z)) < 0.32f)
                        position.y = Mathf.Max(position.y, renderer.bounds.max.y + 0.004f);
            paper.transform.position = position;
            HoldableItem item = paper.AddComponent<HoldableItem>(); item.Configure("desk.profile", "인물 프로필", HeldItemUse.Profile, SelectedProfileId);
            profileDocument = paper.AddComponent<PaperDocument>();
            profileDocument.Initialize(item, Carry, views.ControlledCamera, font, new Vector2(0.27f, 0.35f)); Carry.Register(item);
            profileDocument.AreaClicked += ReadPrintedArea; profileDocument.ChangeProfile += ChangePrintedProfile;
            RefreshProfilePaper();
        }
        private void ChangePrintedProfile(int direction)
        {
            if (!Carry.CanManipulate) return;
            string[] profiles = Runtime.VisibleProfileIds.ToArray(); if (profiles.Length == 0) return;
            int current = Math.Max(0, Array.IndexOf(profiles, SelectedProfileId));
            SelectedProfileId = profiles[(current + direction + profiles.Length) % profiles.Length]; RefreshProfilePaper();
        }
        private void RefreshProfilePaper()
        {
            if (!Runtime.VisibleProfileIds.Contains(SelectedProfileId)) SelectedProfileId = Runtime.VisibleProfileIds.FirstOrDefault();
            printedProfile = SelectedProfileId;
            string label = (printedProfile != null ? Runtime.Entity(printedProfile)?.Label : null) ?? printedProfile ?? "미등록";
            profileDocument.Item.Configure("desk.profile", label + " / 프로필", HeldItemUse.Profile, printedProfile);
            profileDocument.SetContent("인물 프로필 / " + label, new[] { new PaperSection { title = "신원 · 기록한 단서 · 상담 기록",
                text = printedProfile != null ? Runtime.ProfileSummary(printedProfile) : "열람 가능한 프로필이 없습니다." } });
        }
        private void RestoreStoredItems()
        {
            foreach (StoredItemState state in Runtime.StoredItems)
            {
                HoldableItem item = state.itemId == newspaper.Item.Id ? newspaper.Item : state.itemId == profileDocument.Item.Id ? profileDocument.Item : null;
                ItemPlacementSurface surface = storageSurfaces.FirstOrDefault(s => s.Id == state.containerId);
                if (item != null && surface != null) Carry.RestoreStorage(item, surface);
            }
        }
        public void BeginItemPlacement()
        {
            if (IsDateTransitioning || IsVisitPresenting || inspection.Mode == InspectionMode.Menu || inspection.TextInputFocused || Carry.HeldItem == null) return;
            int index = views.AngleSet.FindId("drawer"); if (index < 0) return;
            ui.Back(); inspection.InspectAngle(views.AngleSet.Get(index));
            if (views.IsTransitioning) pendingPlacement = true; else Carry.BeginPlacement();
        }
        public void UseHeldItem()
        {
            if (!IsDateTransitioning && !IsVisitPresenting && inspection.Mode != InspectionMode.Menu && !IsViewTransitioning)
                Carry.TryUse(inspection.TextInputFocused);
        }
        public void PutDownItem() { if (!IsDateTransitioning && !inspection.TextInputFocused) Carry.PutDown(); }
        public void BeginNextDay()
        {
            if (!Runtime.IsDayEnded) { Feedback = "18시 이후 다음 날로 진행할 수 있습니다."; return; }
            if (!IsDateTransitioning && !IsVisitPresenting && !Runtime.IsClinicTransitioning && inspection.Mode != InspectionMode.Menu) StartCoroutine(ChangeDate());
        }
        public void ShowDaySummary()
        {
            if (!Runtime.IsDayEnded) { Feedback = "18시 이후 업무 정산을 확인할 수 있습니다."; return; }
            if (Runtime.IsClinicTransitioning || IsDateTransitioning || IsVisitPresenting || inspection.Mode == InspectionMode.Menu) return;
            DailyRecord record = Runtime.FinalizeDay(); SaveNow(); ui.ShowDaySummary(record); summaryShownDay = Runtime.Day;
        }
        public void CloseDaySummary() => ui.CloseDaySummary();
        private IEnumerator PresentVisits()
        {
            IsVisitPresenting = true; inspection.NavigationBlocked = true; ui.HideContext();
            while (Runtime.IsClinicTransitioning)
            {
                AppointmentProgress visit = Runtime.CurrentAppointment;
                if (visit.phase == VisitPhase.Arriving) SelectedProfileId = visit.profileId;
                yield return visits.Play(Runtime, visit); SaveNow();
            }
            inspection.NavigationBlocked = false; IsVisitPresenting = false;
        }
        public void WaitForNextAppointment()
        {
            if ((Runtime.CanWaitForNextAppointment || Runtime.CanWaitUntilClosing) && !IsDateTransitioning && !IsVisitPresenting && inspection.Mode != InspectionMode.Menu) StartCoroutine(WaitForReservation());
        }
        private IEnumerator WaitForReservation()
        {
            IsDateTransitioning = true; inspection.NavigationBlocked = true; ui.HideContext();
            bool closing = Runtime.NextAppointment == null;
            string subtitle = KernelRuntime.TimeOfDay(Runtime.NextAppointment?.startMinute ?? 1080) + (closing ? " 업무 마감" : " 다음 예약");
            yield return ui.FadeDate(0, 1, 0.25f, "기다리는 중", subtitle);
            if (closing) Runtime.WaitUntilClosing(); else Runtime.WaitForNextAppointment(); SaveNow();
            yield return ui.FadeDate(1, 0, 0.35f, "기다리는 중", subtitle);
            inspection.NavigationBlocked = false; IsDateTransitioning = false;
        }
        private IEnumerator IntroduceDay()
        {
            IsDateTransitioning = true; inspection.NavigationBlocked = true; ui.HideContext();
            string label = "DAY " + Runtime.Day;
            string subtitle = Runtime.DaySubtitle + "\n09:00  업무 시작";
            yield return ui.FadeDate(1, 1, 1.8f, label, subtitle);
            yield return ui.FadeDate(1, 0, 0.6f, label, subtitle);
            Runtime.MarkDayIntroSeen(); SaveNow();
            inspection.NavigationBlocked = false; IsDateTransitioning = false;
        }
        private IEnumerator ChangeDate()
        {
            IsDateTransitioning = true; inspection.NavigationBlocked = true;
            ui.HideContext(); Carry.PutDown();
            yield return ui.FadeDate(0, 1, 0.45f, "");
            Carry.ReturnToDesk(newspaper.Item); Runtime.SetItemStorage(newspaper.Item.Id, null);
            Runtime.NextDay(); newspaper.Refresh(Runtime); SaveNow();
            inspection.ReturnToOverview();
            yield return IntroduceDay();
        }
        private void InitializeDrawers()
        {
            if (deskSurface == null) return;
            foreach (Transform child in deskSurface.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Desk_big_Drawer_", StringComparison.Ordinal)).OrderBy(t => t.name, StringComparer.Ordinal))
            {
                SlidingDrawer drawer = child.GetComponent<SlidingDrawer>() ?? child.gameObject.AddComponent<SlidingDrawer>();
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
                if (!child.GetComponentsInChildren<Collider>().Any(c => !c.isTrigger))
                {
                    BoxCollider front = child.gameObject.AddComponent<BoxCollider>(); front.isTrigger = true;
                    front.center = new Vector3(local.center.x, local.center.y, local.max.z);
                    front.size = new Vector3(local.size.x, local.size.y, 0.025f / Mathf.Abs(scale.z));
                }
            }
        }
        public void ToggleDrawer(int index)
        {
            if (CanOperateDrawers && index >= 0 && index < drawers.Count) drawers[index].Interact();
        }

        public void SaveNow()
        {
            if (!initialized || Runtime == null) return;
            narrativeDirty = false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SavePath));
                string temporary = SavePath + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(Runtime.Capture(), true));
                if (File.Exists(SavePath)) File.Replace(temporary, SavePath, SavePath + ".bak");
                else File.Move(temporary, SavePath);
                Feedback = "DAY " + Runtime.Day + " 기록 저장 완료";
            }
            catch (Exception error) { Feedback = "저장 실패: " + error.Message; Debug.LogWarning(Feedback, this); }
        }
        private void QueueNarrativeSave() { narrativeDirty = true; narrativeSaveAt = Time.unscaledTime + 0.3f; }
        private void OnApplicationPause(bool paused) { if (paused && narrativeDirty) { narrativeDirty = false; SaveNow(); } }
        private void OnApplicationQuit() { if (narrativeDirty) { narrativeDirty = false; SaveNow(); } }

        public void SetCommandLanguage(CommandLocale language)
        {
            Runtime.Language = language;
            PlayerPrefs.SetInt(CommandLanguagePreference, (int)language);
            PlayerPrefs.Save();
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
            ownCoffee.SetActive(Runtime.CoffeeReceived);
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
            if (initialized && narrativeDirty) SaveNow();
            StopAllCoroutines();
            if (sceneLights != null && initialLights != null) for (int i = 0; i < Math.Min(sceneLights.Length, initialLights.Length); i++) if (sceneLights[i] != null) sceneLights[i].enabled = initialLights[i];
            if (sceneBasisCaptured && curtainCloth != null) curtainCloth.localScale = foldedScale;
            if (Carry != null) { Carry.PutDown(); Carry.Used -= UseItem; Carry.PickedUp -= PickedUpItem; Carry.Placed -= StoredItem; Carry.ManipulationAllowed = null; }
            if (profileDocument != null) Destroy(profileDocument.gameObject);
            if (inspection != null) { inspection.AngleEntered -= EnterView; inspection.OverviewEntered -= LeaveView; inspection.BackHandler = null; inspection.PointerBlocked = null; inspection.Carry = null; inspection.NavigationBlocked = false; }
            if (Runtime != null)
            {
                Runtime.SceneActionRequested -= SceneAction;
                Runtime.DayEnded -= SaveNow;
                Runtime.NarrativeChanged -= QueueNarrativeSave;
            }
        }
    }
}
