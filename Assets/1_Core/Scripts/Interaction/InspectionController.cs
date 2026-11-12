using Codetective.CameraViews;
using Codetective.Terminal;
using System;
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
        public event Action<CameraAnglePreset> AngleEntered;
        public event Action OverviewEntered;
        public Func<bool> BackHandler { get; set; }
        public Func<Vector2, bool> PointerBlocked { get; set; }
        public bool ModalInputBlocked { get; set; }
        public bool NavigationBlocked { get; set; }
        public ItemCarryController Carry { get; set; }
        public bool TextInputFocused => TextInputFocus.IsActive || (activeTerminal != null && activeTerminal.IsCompositionProtected);

        private void Awake()
        {
            views = GetComponent<CameraAngleController>();
            menu = GetComponent<PauseMenuView>();
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
                // Let the IME cancel/commit its composition before treating Escape as navigation.
                if (activeTerminal == null || !activeTerminal.InteractionEnabled || !activeTerminal.IsCompositionProtected)
                {
                    if (BackHandler != null && BackHandler()) return;
                    HandleEscape();
                }
                return;
            }
            if (flow.Mode == InspectionMode.Menu)
                return;
            if (ModalInputBlocked) return;

            if (enableAngleShortcuts && keyboard != null && views.AngleSet != null)
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
            if (flow.Mode == InspectionMode.Overview && mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                if (!InteractAtScreenPoint(mouse.position.ReadValue())) InspectAtScreenPoint(mouse.position.ReadValue());
            }
            else if (flow.Mode == InspectionMode.Inspecting && mouse != null && mouse.leftButton.wasPressedThisFrame)
                InteractAtScreenPoint(mouse.position.ReadValue());
#endif
        }

        private void LateUpdate()
        {
            // The click that starts the zoom must not also activate a terminal control.
            bool ready = flow.Mode == InspectionMode.Inspecting && !views.IsTransitioning && !ModalInputBlocked && !NavigationBlocked;
            if (activeTerminal != null && activeTerminal.InteractionEnabled != ready)
                activeTerminal.SetInteractionEnabled(ready);
#if ENABLE_INPUT_SYSTEM
            // Run after EventSystem so clicking an input and pressing E together cannot use an object.
            if (NavigationBlocked || flow.Mode == InspectionMode.Menu || views.IsTransitioning || ModalInputBlocked || Carry == null) return;
            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) Carry.TryUse(TextInputFocused);
            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame && !TextInputFocused) Carry.PutDown();
#endif
        }

        public void InspectAtScreenPoint(Vector2 point)
        {
            if (flow.Mode != InspectionMode.Overview || NavigationBlocked || ModalInputBlocked || (PointerBlocked != null && PointerBlocked(point)))
                return;
            Ray ray = views.ControlledCamera.ScreenPointToRay(point);
            if (Physics.Raycast(ray, out RaycastHit hit, clickDistance, inspectionLayers, QueryTriggerInteraction.Collide))
            {
                InspectionTarget target = hit.collider.GetComponentInParent<InspectionTarget>();
                if (target != null && target.isActiveAndEnabled)
                    InspectTarget(target);
            }
        }

        public void InspectTarget(InspectionTarget target)
        {
            if (target != null && target.isActiveAndEnabled)
                InspectAngle(target.Angle, target.Terminal);
        }

        public bool InteractAtScreenPoint(Vector2 point)
        {
            if (flow.Mode == InspectionMode.Menu || NavigationBlocked || views.IsTransitioning || ModalInputBlocked ||
                (PointerBlocked != null && PointerBlocked(point))) return false;
            RaycastHit[] hits = Physics.RaycastAll(views.ControlledCamera.ScreenPointToRay(point), clickDistance, inspectionLayers, QueryTriggerInteraction.Collide);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                InspectionActionTarget action = hit.collider.GetComponentInParent<InspectionActionTarget>();
                if (action != null && action.CanInteract(views.ActiveAngle?.Id)) { action.Interact(); return true; }
                // Coarse inspection volumes select an angle; they must not obstruct its detailed controls.
                if (hit.collider.GetComponentInParent<InspectionTarget>() == null && !hit.collider.isTrigger) return false;
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
            foreach (var target in FindObjectsOfType<InspectionTarget>())
                if (target.Angle == angle)
                    return target.Terminal;
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
            if (flow.Mode == InspectionMode.Inspecting && !RequestOverview())
                return;

            switch (flow.Escape())
            {
                case EscapeAction.ReturnToOverview:
                    DisableTerminal();
                    OverviewEntered?.Invoke();
                    break;
                case EscapeAction.OpenMenu:
                    DisableTerminal();
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
