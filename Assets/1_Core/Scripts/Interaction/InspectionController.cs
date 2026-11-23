using Codetective.CameraViews;
using Codetective.Terminal;
using Codetective.Gameplay;
using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Codetective.Interaction
{
    // Handle global navigation before uGUI processes the selected terminal field.
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CameraAngleController), typeof(PauseMenuView))]
    public sealed class InspectionController : MonoBehaviour
    {
        [SerializeField] private CameraAnglePreset overviewAngle;
        [SerializeField] private Transform terminalRoot;
        [SerializeField, Min(0.1f)] private float clickDistance = 10f;
        [SerializeField] private LayerMask inspectionLayers = ~0;
        [Tooltip("Optional prototype shortcuts, routed through the same navigation as object clicks.")]
        [SerializeField] private bool enableAngleShortcuts = true;
        [SerializeField] private string[] numberAngles = { "monitor", "counsel", "profile", "drawer", "gramophone" };
        [SerializeField] private string[] terminalAngles = { "monitor", "counsel", "gramophone" };
        [Tooltip("Scene-authored angle targets. Runtime targets can call RegisterTarget / UnregisterTarget.")]
        [SerializeField] private InspectionTarget[] inspectionTargets = Array.Empty<InspectionTarget>();
        private readonly List<InspectionTarget> targets = new List<InspectionTarget>();

        private readonly InspectionFlow flow = new InspectionFlow();
        private CameraAngleController views;
        private PauseMenuView menu;
        private TerminalController terminal;
        private TerminalController activeTerminal;
        private bool ownsPause;
        private float previousTimeScale;
        private bool previousBlendPause;
        private bool previousAudioPause;
        private Pose menuReturnPose;
        private float menuReturnFieldOfView;
        private CameraAnglePreset menuReturnAngle;
        private bool showingTitle;
        private bool started;
        private bool focusKernelOnArrival;
        private bool returningToOverview;
        private CameraAnglePreset inspectedAngle;
        private CameraAnglePreset informationReturnAngle;
        private InspectionMode informationReturnMode;
        private bool hasInformationReturn;
        private Pose presentationReturnPose;
        private float presentationReturnFov;
        private CameraAnglePreset presentationReturnAngle;
        private bool presentationNavigationBlocked;
        public SceneTransitionController Transitions { get; set; }
        public bool PresentingOnTerminal { get; private set; }
        public bool AllowPresentationMenu { get; private set; }
        private bool ViewBusy => views.IsTransitioning || (Transitions != null && Transitions.IsBusy);

        public InspectionMode Mode => flow.Mode;
        public bool IsTitleMenu => flow.Mode == InspectionMode.Menu && showingTitle;
        public bool CanInspectByPointer => flow.CanInspectByPointer(views != null ? views.ActiveAngle?.Id : null);
        public event Action<CameraAnglePreset> AngleEntered;
        public event Action OverviewEntered;
        public Func<bool> BackHandler { get; set; }
        public Action QuickSaveRequested { get; set; }
        public Func<Vector2, bool> PointerBlocked { get; set; }
        private bool modalInputBlocked;
        public bool ModalInputBlocked
        {
            get => modalInputBlocked;
            set { modalInputBlocked = value; if (value && activeTerminal != null) activeTerminal.SetInteractionEnabled(false); }
        }
        private bool navigationBlocked;
        public bool NavigationBlocked
        {
            get => navigationBlocked;
            set
            {
                navigationBlocked = value;
                // Cinematics start before EventSystem.Update. Cancel queued input immediately, retaining the current view for reactivation.
                if (value && activeTerminal != null) activeTerminal.SetInteractionEnabled(false);
            }
        }
        public ItemCarryController Carry { get; set; }
        public bool TextInputFocused => TextInputFocus.IsActive || (activeTerminal != null && activeTerminal.IsCompositionProtected);

        private void Awake()
        {
            views = GetComponent<CameraAngleController>();
            menu = GetComponent<PauseMenuView>();
            foreach (InspectionTarget target in inspectionTargets) RegisterTarget(target);
            terminal = terminalRoot != null ? terminalRoot.GetComponentInChildren<TerminalController>(true) : null;
            menu.BindTerminal(terminal != null ? terminal.GetComponent<MonitorPageHost>() : null);
            if (terminal != null)
                terminal.SetInteractionEnabled(false);
        }

        private void OnEnable()
        {
            menu.ResumeRequested += Resume;
            if (started)
                ReturnToOverview();
        }

        private void Start()
        {
            started = true;
            ReturnToOverview();
        }

        private void OnDisable()
        {
            if (menu != null)
            {
                menu.ResumeRequested -= Resume;
                menu.Hide();
            }
            RestorePause();
            if (Transitions != null) { Transitions.SetTerminalFocus("menu", false); Transitions.SetTerminalFocus("day", false); }
            PresentingOnTerminal = false; AllowPresentationMenu = false;
            Carry?.SetPresentationVisible(true);
            DisableTerminal();
            flow.Resume();
            flow.ReturnToOverview();
            inspectedAngle = null; returningToOverview = false; hasInformationReturn = false;
        }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && (!NavigationBlocked || PresentingOnTerminal && AllowPresentationMenu))
            {
                HandleEscape();
                return;
            }
            if (NavigationBlocked || (Transitions != null && Transitions.IsBusy)) return;
            if (keyboard != null && keyboard.f5Key.wasPressedThisFrame && !TextInputFocused)
            { QuickSaveRequested?.Invoke(); return; }
            if (flow.Mode == InspectionMode.Menu)
                return;
            if (keyboard != null && keyboard.qKey.wasPressedThisFrame && !TextInputFocused)
            {
                BackHandler?.Invoke();
                if (!ModalInputBlocked) ReturnToOverview();
                return;
            }
            if (ModalInputBlocked) return;

            if (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) &&
                !TextInputFocused && TryFocusKernel()) return;

            if (enableAngleShortcuts && keyboard != null && !TextInputFocused)
            {
                Key[] digits = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5 };
                Key[] numpad = { Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4, Key.Numpad5 };
                for (int i = 0; i < digits.Length; i++)
                    if (keyboard[digits[i]].wasPressedThisFrame || keyboard[numpad[i]].wasPressedThisFrame)
                    { TryInspectNumber(i + 1); return; }
            }

            if (enableAngleShortcuts && keyboard != null && views.AngleSet != null && !TextInputFocused)
            {
                for (int i = 0; i < Mathf.Min(12, views.AngleSet.Count); i++)
                {
                    if (i == 4) continue; // F5 belongs to quick save, never a camera angle.
                    if (!keyboard[(Key)((int)Key.F1 + i)].wasPressedThisFrame)
                        continue;
                    InspectAngle(views.AngleSet.Get(i));
                    return;
                }
            }

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                if (!InteractAtScreenPoint(mouse.position.ReadValue())) InspectAtScreenPoint(mouse.position.ReadValue());
            }
#endif
        }

        private void LateUpdate()
        {
            // The click that starts the zoom must not also activate a terminal control.
            bool ready = flow.Mode == InspectionMode.Inspecting && !ViewBusy && !ModalInputBlocked && !NavigationBlocked &&
                (Carry == null || Carry.HeldItem == null);
            if (flow.Mode == InspectionMode.Menu || PresentingOnTerminal)
            {
                // Only system controls are shown; command input and page navigation remain suspended.
                bool allow = Transitions == null || !Transitions.IsBusy;
                if (terminal != null && terminal.InteractionEnabled != allow) terminal.SetInteractionEnabled(allow);
                return;
            }
            if (activeTerminal != null && activeTerminal.InteractionEnabled != ready)
                activeTerminal.SetInteractionEnabled(ready);
            if (focusKernelOnArrival && ready && activeTerminal != null)
            {
                focusKernelOnArrival = false;
                activeTerminal.TryFocusCommandInput();
            }
#if ENABLE_INPUT_SYSTEM
            if (Carry == null) return;
            if (NavigationBlocked || flow.Mode == InspectionMode.Menu || views.IsTransitioning || ModalInputBlocked)
            { Carry.HidePlacementPreview(); return; }
            if (Carry.IsPlacing && Mouse.current != null)
                Carry.UpdatePlacement(Mouse.current.position.ReadValue(), PointerBlocked != null && PointerBlocked(Mouse.current.position.ReadValue()));
            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame && !TextInputFocused)
            { if (Carry.IsPlacing) Carry.CancelPlacement(); }
#endif
        }

        public void InspectAtScreenPoint(Vector2 point)
        {
            if (!CanInspectByPointer || (Carry != null && Carry.IsExamining) || ViewBusy || NavigationBlocked || ModalInputBlocked || (PointerBlocked != null && PointerBlocked(point)))
                return;
            Ray ray = views.ControlledCamera.ScreenPointToRay(point);
            RaycastHit[] hits = Physics.RaycastAll(ray, clickDistance, inspectionLayers, QueryTriggerInteraction.Collide);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                InspectionTarget target = hit.collider.GetComponentInParent<InspectionTarget>();
                if (target != null && target.isActiveAndEnabled)
                { InspectTarget(target); return; }
                // Drawer/item action volumes may sit in front of the camera-angle hotspot.
                if (!hit.collider.isTrigger && hit.collider.GetComponentInParent<InspectionActionTarget>() == null) return;
            }
        }

        public void InspectTarget(InspectionTarget target)
        {
            if (CanInspectByPointer && target != null && target.isActiveAndEnabled)
                InspectAngle(target.Angle, target.Terminal ?? (target.Angle != null ? FindTerminalFor(target.Angle) : null));
        }

        public bool InteractAtScreenPoint(Vector2 point)
        {
            if (flow.Mode == InspectionMode.Menu || NavigationBlocked || ViewBusy || ModalInputBlocked ||
                (PointerBlocked != null && PointerBlocked(point))) return false;
            if (Carry != null && Carry.IsExamining) return true;
            if (TextInputFocused) TextInputFocus.ReleaseAll();
            RaycastHit[] hits = Physics.RaycastAll(views.ControlledCamera.ScreenPointToRay(point), clickDistance, inspectionLayers, QueryTriggerInteraction.Collide);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            if (TryInspectPlacementTarget(hits)) return true;
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.GetComponent<ItemPlacementSurface>() != null)
                {
                    if (Carry != null && Carry.IsPlacing) { Carry.TryPlaceAtScreenPoint(point); return true; }
                    continue;
                }
                InspectionActionTarget action = hit.collider.GetComponentInParent<InspectionActionTarget>();
                if (action is DrawerFrontTarget front && front.CanInteract(views.ActiveAngle?.Id)) { front.Interact(); return true; }
                // Prefab body colliders must not turn the entire open tray into a close button.
                if (action is SlidingDrawer) continue;
                if (hit.collider.isTrigger && hit.collider.GetComponentInParent<InspectionTarget>() != null) continue;
                if (Carry != null && Carry.IsPlacing) { Carry.TryPlaceAtScreenPoint(point); return true; }
                if (action != null && action.CanInteract(views.ActiveAngle?.Id)) { action.Interact(); return true; }
                // Coarse inspection volumes select an angle; they must not obstruct its detailed controls.
                if (hit.collider.GetComponentInParent<InspectionTarget>() == null && !hit.collider.isTrigger) return false;
            }
            if (Carry != null && Carry.IsPlacing) { Carry.TryPlaceAtScreenPoint(point); return true; }
            return false;
        }
        private bool TryInspectPlacementTarget(RaycastHit[] hits)
        {
            if (Carry == null || !Carry.IsPlacing) return false;
            foreach (RaycastHit hit in hits)
            {
                InspectionTarget target = hit.collider.GetComponentInParent<InspectionTarget>();
                if (target != null && target.isActiveAndEnabled && target.Angle != null && target.Angle != views.ActiveAngle &&
                    (CanInspectByPointer || target.Angle.Id == "drawer"))
                { InspectAngle(target.Angle); return true; }
                bool drawer = hit.collider.GetComponentInParent<SlidingDrawer>() != null || hit.collider.GetComponent<DrawerFrontTarget>() != null;
                if (drawer && views.ActiveAngle?.Id != "drawer")
                {
                    CameraAnglePreset angle = target != null && target.Angle != null && target.Angle.Id == "drawer" ? target.Angle : GetTarget("drawer")?.Angle;
                    if (angle == null && views.AngleSet != null) angle = views.AngleSet.Get(views.AngleSet.FindId("drawer"));
                    if (angle == null) return false;
                    InspectAngle(angle); return true;
                }
                // A desk placement surface or solid foreground object still occludes drawers behind it.
                if (!hit.collider.isTrigger) return false;
            }
            return false;
        }

        public void InspectAngle(CameraAnglePreset angle)
        {
            if (angle == overviewAngle)
                ReturnToOverview();
            else if (angle != null)
                InspectAngle(angle, FindTerminalFor(angle));
        }

        public bool TryInspectNumber(int number)
        {
            if (TextInputFocused || ModalInputBlocked || NavigationBlocked || flow.Mode == InspectionMode.Menu ||
                views.AngleSet == null || number < 1 || number > numberAngles.Length) return false;
            int index = views.AngleSet.FindId(numberAngles[number - 1]);
            if (index < 0) return false;
            InspectAngle(views.AngleSet.Get(index)); return true;
        }

        private bool TryFocusKernel()
        {
            if (terminal == null || PresentingOnTerminal || ViewBusy || (Carry != null && Carry.HeldItem != null)) return false;
            if (activeTerminal != null)
            {
                if (activeTerminal.InteractionEnabled) return activeTerminal.TryFocusCommandInput();
                focusKernelOnArrival = true;
                return true;
            }
            int index = views.AngleSet != null ? views.AngleSet.FindId("monitor") : -1;
            if (index < 0) return false;
            CameraAnglePreset angle = views.AngleSet.Get(index);
            InspectAngle(angle, FindTerminalFor(angle), true);
            return true;
        }

        private void InspectAngle(CameraAnglePreset angle, TerminalController targetTerminal, bool focusKernel = false)
        {
            if (flow.Mode == InspectionMode.Menu || NavigationBlocked || angle == null || views.AngleSet == null)
                return;
            if (flow.Mode == InspectionMode.Inspecting && views.ActiveAngle == angle)
                return;
            CameraAnglePreset previous = inspectedAngle; bool wasReturning = returningToOverview;
            CameraAnglePreset previousInformationReturn = informationReturnAngle;
            InspectionMode previousInformationMode = informationReturnMode; bool hadInformationReturn = hasInformationReturn;
            if (IsInformationAngle(angle))
            {
                if (!hasInformationReturn)
                { informationReturnAngle = views.ActiveAngle ?? overviewAngle; informationReturnMode = flow.Mode; hasInformationReturn = true; }
            }
            else hasInformationReturn = false;
            inspectedAngle = angle; returningToOverview = false;
            if (!ChangeAngle(angle, () =>
            {
                DisableTerminal(); activeTerminal = targetTerminal;
                focusKernelOnArrival = focusKernel;
                activeTerminal?.SetInteractionEnabled(false);
                flow.Inspect(); AngleEntered?.Invoke(angle);
            }))
            {
                inspectedAngle = previous; returningToOverview = wasReturning; informationReturnAngle = previousInformationReturn;
                informationReturnMode = previousInformationMode; hasInformationReturn = hadInformationReturn; return;
            }
            activeTerminal?.SetInteractionEnabled(false);
        }
        private bool ChangeAngle(CameraAnglePreset angle, Action changed)
        {
            if (Transitions != null) return Transitions.TryChangeAngle(angle, changed);
            if (!views.TryGoToIndex(views.AngleSet.IndexOf(angle))) return false;
            changed?.Invoke(); return true;
        }
        private static bool IsInformationAngle(CameraAnglePreset angle) => angle != null && (angle.Id == "clock" || angle.Id == "portrait");
        public bool CloseInformationView()
        {
            if (!hasInformationReturn || !IsInformationAngle(inspectedAngle ?? views.ActiveAngle) || flow.Mode == InspectionMode.Menu || NavigationBlocked || ViewBusy) return false;
            CameraAnglePreset source = inspectedAngle ?? views.ActiveAngle, destination = informationReturnAngle ?? overviewAngle;
            InspectionMode mode = informationReturnMode;
            Action changed = () =>
            {
                hasInformationReturn = false; DisableTerminal();
                if (mode == InspectionMode.Overview || destination == overviewAngle)
                { inspectedAngle = null; flow.ReturnToOverview(); OverviewEntered?.Invoke(); }
                else
                { inspectedAngle = destination; activeTerminal = FindTerminalFor(destination); flow.Inspect(); AngleEntered?.Invoke(destination); }
            };
            bool accepted = Transitions != null ? Transitions.TryReturnToView(source, destination, changed) : ChangeAngle(destination, changed);
            if (accepted) returningToOverview = mode == InspectionMode.Overview || destination == overviewAngle;
            return accepted;
        }

        private TerminalController FindTerminalFor(CameraAnglePreset angle)
        {
            if (terminal != null && Array.Exists(terminalAngles, id => string.Equals(id, angle.Id, StringComparison.OrdinalIgnoreCase))) return terminal;
            InspectionTarget target = GetTarget(angle.Id);
            return target != null ? target.Terminal : null;
        }
        public void RegisterTarget(InspectionTarget target)
        { if (target != null && !targets.Contains(target)) targets.Add(target); }
        public void UnregisterTarget(InspectionTarget target) => targets.Remove(target);
        public InspectionTarget GetTarget(string angleId)
        {
            foreach (InspectionTarget target in targets)
                if (target != null && target.isActiveAndEnabled && target.Angle != null &&
                    string.Equals(target.Angle.Id, angleId, StringComparison.OrdinalIgnoreCase)) return target;
            return null;
        }

        public void ReturnToOverview()
        {
            if (flow.Mode == InspectionMode.Menu || NavigationBlocked || PresentingOnTerminal || overviewAngle == null || views.AngleSet == null)
                return;
            CameraAnglePreset source = inspectedAngle ?? views.ActiveAngle;
            if (Transitions != null) Transitions.CancelAngle();
            Action changed = () => { inspectedAngle = null; hasInformationReturn = false; DisableTerminal(); flow.ReturnToOverview(); OverviewEntered?.Invoke(); };
            bool accepted = Transitions != null ? Transitions.TryReturnToOverview(source, overviewAngle, changed) : ChangeAngle(overviewAngle, changed);
            if (accepted)
            {
                returningToOverview = true;
                activeTerminal?.SetInteractionEnabled(false);
            }
        }
        public bool SnapToOverview()
        {
            if (flow.Mode == InspectionMode.Menu || overviewAngle == null || views.AngleSet == null) return false;
            Transitions?.CancelAngle();
            if (!views.TryGoToIndex(views.AngleSet.IndexOf(overviewAngle), true)) return false;
            inspectedAngle = null; returningToOverview = false; hasInformationReturn = false;
            DisableTerminal(); flow.ReturnToOverview(); OverviewEntered?.Invoke(); return true;
        }

        public void HandleEscape()
        {
            if (NavigationBlocked && !(PresentingOnTerminal && AllowPresentationMenu)) return;
            if (flow.Mode == InspectionMode.Menu)
            { if (!menu.HandleEscape()) Resume(); return; }
            if (!PresentingOnTerminal && Carry != null && Carry.HeldItem != null)
            { TextInputFocus.ReleaseAll(); BackHandler?.Invoke(); Carry.PutDown(); return; }
            if (!PresentingOnTerminal)
            {
                if (returningToOverview) { if (!SnapToOverview()) return; }
                else if (flow.Mode == InspectionMode.Inspecting || inspectedAngle != null || ViewBusy || views.ActiveAngle != overviewAngle)
                {
                    TextInputFocus.ReleaseAll(); BackHandler?.Invoke(); ReturnToOverview(); return;
                }
            }
            OpenSystemMenu(false);
        }

        public void ShowTitleMenu()
        {
            PresentingOnTerminal = false; AllowPresentationMenu = false; NavigationBlocked = false;
            Transitions?.SetTerminalFocus("day", false);
            if (flow.Mode != InspectionMode.Menu) { OpenSystemMenu(true); return; }
            showingTitle = true; menu.Show(true);
        }
        public void ShowPauseMenu() => OpenSystemMenu(false);
        private void OpenSystemMenu(bool title)
        {
            if (!menu.CanShow || flow.Mode == InspectionMode.Menu) return;
            Transitions?.CancelAngle();
            menuReturnPose = views.CapturePose(); menuReturnFieldOfView = views.ControlledCamera.fieldOfView;
            menuReturnAngle = views.ActiveAngle;
            TextInputFocus.ReleaseAll();
            activeTerminal?.SetInteractionEnabled(false);
            Carry?.CancelPlacement(); Carry?.SetPresentationVisible(false);
            previousTimeScale = Time.timeScale; previousBlendPause = views.PauseTransitions;
            previousAudioPause = AudioListener.pause; ownsPause = true;
            Time.timeScale = 0f; AudioListener.pause = true; views.PauseTransitions = true;
            showingTitle = title; returningToOverview = false; flow.OpenMenu();
            if (views.AngleSet != null) views.TryGoToIndex(views.AngleSet.FindId("monitor"), true);
            menu.Show(title); terminal?.SetInteractionEnabled(true);
            Transitions?.SetTerminalFocus("menu", true);
        }

        public void Resume()
        {
            if (showingTitle || (Transitions != null && Transitions.IsBusy) || !flow.Resume())
                return;
            menu.Hide();
            terminal?.SetInteractionEnabled(false);
            views.RestoreView(menuReturnPose, menuReturnFieldOfView, menuReturnAngle);
            Carry?.SetPresentationVisible(true);
            RestorePause();
            Transitions?.SetTerminalFocus("menu", false);
        }
        public void BeginTerminalPresentation(bool allowMenu)
        {
            if (PresentingOnTerminal) { AllowPresentationMenu = allowMenu; return; }
            Transitions?.CancelAngle(); TextInputFocus.ReleaseAll(); Carry?.CancelPlacement(); Carry?.SetPresentationVisible(false);
            presentationReturnPose = views.CapturePose(); presentationReturnFov = views.ControlledCamera.fieldOfView; presentationReturnAngle = views.ActiveAngle;
            presentationNavigationBlocked = NavigationBlocked; PresentingOnTerminal = true; AllowPresentationMenu = allowMenu; NavigationBlocked = true;
            if (views.AngleSet != null) views.TryGoToIndex(views.AngleSet.FindId("monitor"), true);
            Transitions?.SetTerminalFocus("day", true);
        }
        public void EndTerminalPresentation()
        {
            if (!PresentingOnTerminal) return;
            PresentingOnTerminal = false; AllowPresentationMenu = false; NavigationBlocked = presentationNavigationBlocked;
            views.RestoreView(presentationReturnPose, presentationReturnFov, presentationReturnAngle);
            Carry?.SetPresentationVisible(true); Transitions?.SetTerminalFocus("day", false);
        }

        public void PrepareForSceneChange()
        {
            menu.Hide(); RestorePause(); DisableTerminal();
            flow.Resume(); flow.ReturnToOverview(); showingTitle = false;
            PresentingOnTerminal = false; Transitions?.SetTerminalFocus("day", false); Transitions?.SetTerminalFocus("menu", false);
        }

        private void DisableTerminal()
        {
            if (activeTerminal != null)
                activeTerminal.SetInteractionEnabled(false);
            if (terminal != null && terminal != activeTerminal)
                terminal.SetInteractionEnabled(false);
            activeTerminal = null;
            focusKernelOnArrival = false;
        }

        private void RestorePause()
        {
            if (!ownsPause)
                return;
            Time.timeScale = previousTimeScale;
            AudioListener.pause = previousAudioPause;
            if (views != null)
                views.PauseTransitions = previousBlendPause;
            ownsPause = false;
        }
    }
}
