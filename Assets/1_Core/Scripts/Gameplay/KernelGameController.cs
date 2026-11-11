using System;
using System.Collections;
using System.IO;
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

        public KernelRuntime Runtime { get; private set; }
        public string SavePath => Path.Combine(Application.persistentDataPath, "Codetective", "daily-autosave.json");
        public string Feedback { get; private set; } = "Kernel 작전망 연결";
        private KernelGameUI ui;
        private CameraAngleController views;
        private Vector3 foldedScale;
        private Quaternion hourZero;
        private Quaternion minuteZero;
        private GameObject ownCoffee;
        private TextMesh dayText;
        private string pendingView;
        private bool delivering;
        private Light[] sceneLights;
        private bool[] initialLights;

        private void Awake()
        {
            Runtime = scenario != null ? scenario.CreateRuntime() : ClinicScenario.Create();
            if (scenario != null && scenario.loadDailySave && File.Exists(SavePath))
            {
                try { Runtime.Restore(JsonUtility.FromJson<KernelSnapshot>(File.ReadAllText(SavePath))); Feedback = "날짜 기록을 불러왔습니다."; }
                catch (Exception error) { Runtime = scenario.CreateRuntime(); Feedback = "저장 기록을 읽지 못해 새 작전을 시작합니다: " + error.Message; }
            }
            Runtime.SceneActionRequested += SceneAction;
            Runtime.DayEnded += SaveNow;
            if (terminalRoot != null) terminalRoot.GetComponentInChildren<TerminalController>(true).AttachRuntime(Runtime);
            foldedScale = curtainCloth != null ? curtainCloth.localScale : Vector3.one;
            hourZero = clockHour != null ? clockHour.localRotation : Quaternion.identity;
            minuteZero = clockMinute != null ? clockMinute.localRotation : Quaternion.identity;
            ui = GetComponent<KernelGameUI>();
            views = inspection.GetComponent<CameraAngleController>();
            inspection.AngleEntered += EnterView;
            inspection.OverviewEntered += LeaveView;
        }

        private void Start()
        {
            CreateCoffeeCopy();
            CreateCalendarText();
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
            if (pendingView != null && !views.IsTransitioning)
            {
                ui.ShowContext(pendingView);
                pendingView = null;
            }
            if (clockHour != null) clockHour.localRotation = hourZero * Quaternion.AngleAxis(-(float)(Runtime.Minute / 60 % 12) * 30f, Vector3.forward);
            if (clockMinute != null) clockMinute.localRotation = minuteZero * Quaternion.AngleAxis(-(float)(Runtime.Minute % 60) * 6f, Vector3.forward);
            if (dayText != null) dayText.text = "DAY " + Runtime.Day.ToString("D2");
        }

        private void EnterView(CameraAnglePreset angle) { pendingView = angle.Id; ui.HideContext(); }
        private void LeaveView() { pendingView = null; ui.HideContext(); }
        public void ShowMonitor()
        {
            int index = views.AngleSet.FindId("monitor");
            if (index >= 0) inspection.InspectAngle(views.AngleSet.Get(index));
        }

        public void SaveNow()
        {
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
            StopAllCoroutines();
            if (sceneLights != null) for (int i = 0; i < sceneLights.Length; i++) if (sceneLights[i] != null) sceneLights[i].enabled = initialLights[i];
            if (curtainCloth != null) curtainCloth.localScale = foldedScale;
            if (inspection != null) { inspection.AngleEntered -= EnterView; inspection.OverviewEntered -= LeaveView; inspection.BackHandler = null; inspection.PointerBlocked = null; }
            Runtime.SceneActionRequested -= SceneAction;
            Runtime.DayEnded -= SaveNow;
        }
    }
}
