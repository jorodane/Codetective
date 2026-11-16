using Codetective.CameraViews;
using Codetective.Terminal;
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
        private bool started;

        public InspectionMode Mode => flow.Mode;
        public bool CanInspectByPointer => flow.CanInspectByPointer(views != null ? views.ActiveAngle?.Id : null);
        public event Action<CameraAnglePreset> AngleEntered;
        public event Action OverviewEntered;
        public Func<bool> BackHandler { get; set; }
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
            DisableTerminal();
            flow.Resume();
            flow.ReturnToOverview();
        }

        private void Update()
        {
            if (NavigationBlocked) return;
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                HandleEscape();
                return;
            }
            if (flow.Mode == InspectionMode.Menu)
                return;
            if (keyboard != null && keyboard.qKey.wasPressedThisFrame && !TextInputFocused)
            {
                BackHandler?.Invoke();
                if (!ModalInputBlocked) ReturnToOverview();
                return;
            }
            if (ModalInputBlocked) return;

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
            bool ready = flow.Mode == InspectionMode.Inspecting && !views.IsTransitioning && !ModalInputBlocked && !NavigationBlocked &&
                (Carry == null || Carry.HeldItem == null);
            if (activeTerminal != null && activeTerminal.InteractionEnabled != ready)
                activeTerminal.SetInteractionEnabled(ready);
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
            if (!CanInspectByPointer || (Carry != null && Carry.IsExamining) || views.IsTransitioning || NavigationBlocked || ModalInputBlocked || (PointerBlocked != null && PointerBlocked(point)))
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
            if (flow.Mode == InspectionMode.Menu || NavigationBlocked || views.IsTransitioning || ModalInputBlocked ||
                (PointerBlocked != null && PointerBlocked(point))) return false;
            if (Carry != null && Carry.IsExamining) return true;
            if (TextInputFocused) TextInputFocus.ReleaseAll();
            RaycastHit[] hits = Physics.RaycastAll(views.ControlledCamera.ScreenPointToRay(point), clickDistance, inspectionLayers, QueryTriggerInteraction.Collide);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
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
                if (Carry != null && Carry.IsPlacing) { Carry.TryPlaceAtScreenPoint(point); return true; }
                if (action != null && action.CanInteract(views.ActiveAngle?.Id)) { action.Interact(); return true; }
                // Coarse inspection volumes select an angle; they must not obstruct its detailed controls.
                if (hit.collider.GetComponentInParent<InspectionTarget>() == null && !hit.collider.isTrigger) return false;
            }
            if (Carry != null && Carry.IsPlacing) { Carry.TryPlaceAtScreenPoint(point); return true; }
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

        private void InspectAngle(CameraAnglePreset angle, TerminalController targetTerminal)
        {
            if (flow.Mode == InspectionMode.Menu || NavigationBlocked || angle == null || views.AngleSet == null)
                return;
            if (flow.Mode == InspectionMode.Inspecting && views.ActiveAngle == angle)
                return;
            if (!views.TryGoToIndex(views.AngleSet.IndexOf(angle)))
                return;

            DisableTerminal();
            activeTerminal = targetTerminal;
            if (activeTerminal != null)
                activeTerminal.SetInteractionEnabled(false);
            flow.Inspect();
            AngleEntered?.Invoke(angle);
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
            if (flow.Mode == InspectionMode.Menu || !RequestOverview())
                return;
            DisableTerminal();
            flow.ReturnToOverview();
            OverviewEntered?.Invoke();
        }

        private bool RequestOverview()
        {
            return overviewAngle != null && views.AngleSet != null &&
                   views.TryGoToIndex(views.AngleSet.IndexOf(overviewAngle));
        }

        public void HandleEscape()
        {
            if (NavigationBlocked) return;
            switch (flow.Escape())
            {
                case EscapeAction.OpenMenu:
                    TextInputFocus.ReleaseAll();
                    if (activeTerminal != null) activeTerminal.SetInteractionEnabled(false);
                    Carry?.CancelPlacement();
                    previousTimeScale = Time.timeScale;
                    previousBlendPause = views.PauseTransitions;
                    ownsPause = true;
                    Time.timeScale = 0f;
                    views.PauseTransitions = true;
                    menu.Show();
                    break;
                case EscapeAction.CloseMenu:
                    menu.Hide();
                    RestorePause();
                    break;
            }
        }

        public void Resume()
        {
            if (!flow.Resume())
                return;
            menu.Hide();
            RestorePause();
        }

        private void DisableTerminal()
        {
            if (activeTerminal != null)
                activeTerminal.SetInteractionEnabled(false);
            if (terminal != null && terminal != activeTerminal)
                terminal.SetInteractionEnabled(false);
            activeTerminal = null;
        }

        private void RestorePause()
        {
            if (!ownsPause)
                return;
            Time.timeScale = previousTimeScale;
            if (views != null)
                views.PauseTransitions = previousBlendPause;
            ownsPause = false;
        }
    }
}
