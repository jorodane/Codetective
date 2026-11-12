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

        public KernelRuntime Runtime { get; private set; }
        public string CurrentView { get; private set; } = "overview";
        public bool IsViewTransitioning => views != null && views.IsTransitioning;
        public DialoguePresentationDefinition DialoguePresentation => dialoguePresentation;
        private readonly List<SlidingDrawer> drawers = new List<SlidingDrawer>();
        public IReadOnlyList<SlidingDrawer> Drawers => drawers;
        public bool CanOperateDrawers => inspection != null && inspection.Mode == InspectionMode.Inspecting && CurrentView == "drawer" &&
            !IsViewTransitioning && !inspection.ModalInputBlocked;
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

        private void Awake()
        {
            Runtime = scenario != null ? scenario.CreateRuntime() : ClinicScenario.Create();
            if (scenario != null && scenario.loadDailySave && File.Exists(SavePath))
            {
                try { Runtime.Restore(JsonUtility.FromJson<KernelSnapshot>(File.ReadAllText(SavePath))); Feedback = "날짜 기록을 불러왔습니다."; }
                catch (Exception error) { Runtime = scenario.CreateRuntime(); Feedback = "저장 기록을 읽지 못해 새 작전을 시작합니다: " + error.Message; }
            }
            int preferredLanguage = PlayerPrefs.GetInt(CommandLanguagePreference, (int)(scenario != null ? scenario.commandLanguage : CommandLocale.Korean));
            if (Enum.IsDefined(typeof(CommandLocale), preferredLanguage)) Runtime.Language = (CommandLocale)preferredLanguage;
            Runtime.SceneActionRequested += SceneAction;
            Runtime.DayEnded += SaveNow;
            Runtime.NarrativeChanged += QueueNarrativeSave;
            if (terminalRoot != null) terminalRoot.GetComponentInChildren<TerminalController>(true).AttachRuntime(Runtime);
            foldedScale = curtainCloth != null ? curtainCloth.localScale : Vector3.one;
            hourBasis = ClockBasis(clockHour);
            minuteBasis = ClockBasis(clockMinute);
            ui = GetComponent<KernelGameUI>();
            views = inspection.GetComponent<CameraAngleController>();
            inspection.AngleEntered += EnterView;
            inspection.OverviewEntered += LeaveView;
        }

        private void Start()
        {
            CreateCoffeeCopy();
            CreateCalendarText();
            InitializeDrawers();
            ui.Initialize(this, inspection, font);
            sceneLights = FindObjectsOfType<Light>();
            initialLights = Array.ConvertAll(sceneLights, light => light.enabled);
            if (Runtime.Facts.Contains("turnOffLights")) SetLightsOff();
            inspection.BackHandler = ui.Back;
            inspection.PointerBlocked = ui.BlocksPointer;
            Runtime.RetrySceneActions();
        }

        private void Update()
        {
            Runtime.Tick(Time.deltaTime / Mathf.Max(0.1f, scenario != null ? scenario.secondsPerGameMinute : 2f));
            if (narrativeDirty && Time.unscaledTime >= narrativeSaveAt) { narrativeDirty = false; SaveNow(); }
            foreach (SlidingDrawer drawer in drawers) drawer.SetInteractionEnabled(CanOperateDrawers);
            if (pendingView != null && !views.IsTransitioning)
            {
                ui.ShowContext(pendingView);
                pendingView = null;
            }
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

        private void EnterView(CameraAnglePreset angle) { CurrentView = angle.Id; pendingView = angle.Id; ui.HideContext(); }
        private void LeaveView() { CurrentView = "overview"; pendingView = null; ui.HideContext(); }
        public void ShowMonitor()
        {
            int index = views.AngleSet.FindId("monitor");
            if (index >= 0) inspection.InspectAngle(views.AngleSet.Get(index));
        }
        public void ShowMonitorPage(string pageId)
        {
            if (inspection.Mode == InspectionMode.Menu) return;
            terminalRoot?.GetComponentInChildren<MonitorPageHost>(true)?.ShowPage(pageId);
            ShowMonitor();
        }
        private void InitializeDrawers()
        {
            if (deskSurface == null) return;
            foreach (Transform child in deskSurface.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Desk_big_Drawer_", StringComparison.Ordinal)).OrderBy(t => t.name, StringComparer.Ordinal))
            {
                SlidingDrawer drawer = child.GetComponent<SlidingDrawer>() ?? child.gameObject.AddComponent<SlidingDrawer>();
                drawer.Initialize(Runtime, "desk." + child.name); drawers.Add(drawer);
            }
        }
        public void ToggleDrawer(int index)
        {
            if (CanOperateDrawers && index >= 0 && index < drawers.Count) drawers[index].Interact();
        }

        public void SaveNow()
        {
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
            if (narrativeDirty) SaveNow();
            StopAllCoroutines();
            if (sceneLights != null) for (int i = 0; i < sceneLights.Length; i++) if (sceneLights[i] != null) sceneLights[i].enabled = initialLights[i];
            if (curtainCloth != null) curtainCloth.localScale = foldedScale;
            if (inspection != null) { inspection.AngleEntered -= EnterView; inspection.OverviewEntered -= LeaveView; inspection.BackHandler = null; inspection.PointerBlocked = null; }
            Runtime.SceneActionRequested -= SceneAction;
            Runtime.DayEnded -= SaveNow;
            Runtime.NarrativeChanged -= QueueNarrativeSave;
        }
    }
}
