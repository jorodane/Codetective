using Codetective.CameraViews;
using Codetective.Terminal;
using Codetective.Gameplay;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
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
        private RaycastHit[] pointerHits = new RaycastHit[32];
        private static readonly IComparer<RaycastHit> NearestFirst =
            Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));
        private InspectionHoverView hover;
        private HoldableItem pressedItem;
        private Vector2 pressPosition;

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
        private bool CanInteractWithWorld => flow.Mode != InspectionMode.Menu && !NavigationBlocked &&
            !ViewBusy && !ModalInputBlocked && !PresentingOnTerminal;

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
        public ItemPlacementSurface Desktop { get; set; }
        public Func<InspectionTarget, string> HoverCaption { get; set; }
        public bool TextInputFocused => TextInputFocus.IsActive || (activeTerminal != null && activeTerminal.IsCompositionProtected);
        public void InitializePointerFeedback(Font font) => hover = hover ?? new InspectionHoverView(font);

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
            pressedItem = null;
            if (menu != null)
            {
                menu.ResumeRequested -= Resume;
                menu.Hide();
            }
            RestorePause();
            if (Transitions != null) { Transitions.SetTerminalFocus("menu", false); Transitions.SetTerminalFocus("day", false); }
            PresentingOnTerminal = false; AllowPresentationMenu = false;
            if (Carry != null && Carry.IsDragging) Carry.DeferDragCancellation();
            else Carry?.SetPresentationVisible(true);
            hover?.Hide();
            DisableTerminal();
            flow.Resume();
            flow.ReturnToOverview();
            inspectedAngle = null; returningToOverview = false; hasInformationReturn = false;
        }
        private void OnDestroy() => hover?.Dispose();
        private void OnApplicationFocus(bool focused) { if (!focused) CancelPointerDrag(); }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && (!NavigationBlocked || PresentingOnTerminal && AllowPresentationMenu))
            {
                HandleEscape();
                return;
            }
            Mouse mouse = Mouse.current;
            if (pressedItem != null || (Carry != null && Carry.IsDragging))
            {
                if (mouse == null || mouse.rightButton.wasPressedThisFrame) CancelPointerDrag();
                else UpdateWorldPointer(mouse.position.ReadValue(), false, mouse.leftButton.isPressed);
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

            if (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) && !TextInputFocused)
            {
                if (!ViewBusy && activeTerminal != null && activeTerminal.TryContinueConversation()) return;
                if (TryFocusKernel()) return;
            }

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

            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                UpdateWorldPointer(mouse.position.ReadValue(), true, mouse.leftButton.isPressed);
#endif
        }

        private void LateUpdate()
        {
            // The click that starts the zoom must not also activate a terminal control.
            bool ready = flow.Mode == InspectionMode.Inspecting && !ViewBusy && !ModalInputBlocked && !NavigationBlocked &&
                (Carry == null || Carry.HeldItem == null);
            if (flow.Mode == InspectionMode.Menu || PresentingOnTerminal)
            {
                hover?.Hide();
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
            Mouse mouse = Mouse.current;
            if (mouse == null || NavigationBlocked || ViewBusy || ModalInputBlocked || (Carry != null && Carry.IsExamining))
            { hover?.Hide(); Carry?.HidePlacementPreview(); return; }
            if (Carry != null && Carry.IsPlacing && mouse.rightButton.wasPressedThisFrame && !TextInputFocused)
            { Carry.CancelPlacement(); hover?.Hide(); return; }
            Vector2 point = mouse.position.ReadValue();
            bool blocked = PointerBlocked?.Invoke(point) ?? false;
            Component target = blocked ? null : ResolvePointerTarget(point, out _);
            if (Carry != null && Carry.IsPlacing)
                Carry.UpdatePlacement(point, BlocksPlacement(blocked, target), target as ItemPlacementSurface);
            DrawPointerFeedback(target);
#endif
        }

        public void InspectTarget(InspectionTarget target)
        {
            if (CanInspectByPointer && target != null && target.isActiveAndEnabled)
                InspectAngle(target.Angle, target.Terminal ?? (target.Angle != null ? FindTerminalFor(target.Angle) : null));
        }

        private void UpdateWorldPointer(Vector2 point, bool pressed, bool held)
        {
            if (!CanInteractWithWorld) { CancelPointerDrag(); return; }
            if (pressed) InteractAtScreenPoint(point, deferItemClick: true, expectedItem: null);
            if (pressedItem != null)
            {
                int threshold = EventSystem.current != null ? Mathf.Max(1, EventSystem.current.pixelDragThreshold) : 5;
                if ((point - pressPosition).sqrMagnitude >= threshold * threshold)
                {
                    HoldableItem item = pressedItem;
                    pressedItem = null;
                    if (Carry != null && item.CanInteract(views.ActiveAngle?.Id)) Carry.BeginDrag(item);
                }
                else if (!held)
                {
                    HoldableItem item = pressedItem;
                    pressedItem = null;
                    InteractAtScreenPoint(point, deferItemClick: false, expectedItem: item);
                }
            }
            if (!held && Carry != null && Carry.IsDragging)
            {
                bool blocked = PointerBlocked?.Invoke(point) ?? false;
                Component target = blocked ? null : ResolvePointerTarget(point, out _);
                Carry.FinishDrag(point, BlocksPlacement(blocked, target), target as ItemPlacementSurface);
            }
        }

        private static bool BlocksPlacement(bool pointerBlocked, Component target) =>
            pointerBlocked || target is InspectionTarget || target is InspectionActionTarget;

        private void CancelPointerDrag()
        {
            pressedItem = null;
            Carry?.CancelDrag();
        }

        public bool InteractAtScreenPoint(Vector2 point) => InteractAtScreenPoint(point, deferItemClick: false, expectedItem: null);

        private bool InteractAtScreenPoint(Vector2 point, bool deferItemClick, HoldableItem expectedItem)
        {
            if (!CanInteractWithWorld || (PointerBlocked?.Invoke(point) ?? false)) return false;
            if (Carry != null && Carry.IsExamining) return true;
            if (TextInputFocused) TextInputFocus.ReleaseAll();
            Component target = ResolvePointerTarget(point, out RaycastHit? obstruction);
            if (expectedItem != null && target != expectedItem) return false;
            if (deferItemClick && target is HoldableItem item)
            {
                pressedItem = item;
                pressPosition = point;
                return true;
            }
            bool insideMonitorView = views.ActiveAngle?.Id == "monitor";
            if (insideMonitorView && CanReturnByPointer(point, obstruction))
            { ReturnToOverview(); return true; }
            if (target is InspectionActionTarget action)
            { action.Interact(); return true; }
            if (target is InspectionTarget angle)
            {
                InspectAngle(angle.Angle, angle.Terminal ?? FindTerminalFor(angle.Angle)); return true;
            }
            if (target is ItemPlacementSurface tray)
            { Carry.TryPlaceAtScreenPoint(point, tray); return true; }
            if (!insideMonitorView && CanReturnByPointer(point, obstruction))
            { ReturnToOverview(); return true; }
            if (Carry != null && Carry.IsPlacing)
            { Carry.TryPlaceAtScreenPoint(point); return true; }
            return false;
        }

        private Component ResolvePointerTarget(Vector2 point, out RaycastHit? obstruction)
        {
            Ray ray = views.ControlledCamera.ScreenPointToRay(point);
            int count = Physics.RaycastNonAlloc(ray, pointerHits, clickDistance, inspectionLayers, QueryTriggerInteraction.Collide);
            while (count == pointerHits.Length)
            {
                Array.Resize(ref pointerHits, pointerHits.Length * 2);
                count = Physics.RaycastNonAlloc(ray, pointerHits, clickDistance, inspectionLayers, QueryTriggerInteraction.Collide);
            }
            Array.Sort(pointerHits, 0, count, NearestFirst);
            obstruction = null;
            bool placing = Carry != null && Carry.IsPlacing;
            string angleId = views.ActiveAngle?.Id;
            ItemPlacementSurface tray = null;
            InspectionTarget angleTarget = null;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = pointerHits[i];
                Collider part = hit.collider;
                ItemPlacementSurface surface = part.GetComponent<ItemPlacementSurface>();
                InspectionActionTarget action = part.GetComponentInParent<InspectionActionTarget>();
                if (tray != null)
                {
                    // The tray owns this hit. An exposed item in that tray can still be picked up.
                    if (action is HoldableItem stored && stored.Storage == tray && stored.CanInteract(angleId))
                        return stored;
                    if (part.isTrigger && surface == null && !(action is DrawerFrontTarget)) continue;
                    break;
                }
                if (surface != null)
                {
                    if (surface.Drawer != null && surface.Drawer.IsOpen && surface.Drawer.CanInteract(angleId))
                    {
                        if (placing) return surface;
                        tray = surface;
                    }
                    continue;
                }
                if (action != null && action.CanInteract(angleId)) return action;
                InspectionTarget candidate = part.GetComponentInParent<InspectionTarget>();
                if (candidate == null && (CanInspectByPointer || placing))
                {
                    bool drawerPart = action is SlidingDrawer || action is DrawerFrontTarget;
                    foreach (InspectionTarget registered in targets)
                        if (IsSelectableAngle(registered, placing) && (registered.Angle.Id != "drawer" || drawerPart) &&
                            part.transform.IsChildOf(registered.VisualRoot))
                        { candidate = registered; break; }
                }
                if (IsSelectableAngle(candidate, placing))
                {
                    if (!part.isTrigger) return candidate;
                    if (angleTarget == null) angleTarget = candidate;
                }
                if (!part.isTrigger) { obstruction = hit; break; }
            }
            return tray != null ? (Component)tray.Drawer : angleTarget;
        }

        private bool IsSelectableAngle(InspectionTarget target, bool placing) =>
            target != null && target.isActiveAndEnabled && target.Angle != null && target.Angle != views.ActiveAngle &&
            (CanInspectByPointer || (placing && target.Angle.Id == "drawer"));

        private bool CanReturnByPointer(Vector2 point, RaycastHit? obstruction)
        {
            if (flow.Mode != InspectionMode.Inspecting) return false;
            string angle = views.ActiveAngle?.Id;
            bool placing = Carry != null && Carry.IsPlacing;
            if (angle == "drawer") return Desktop != null && CanReturnFromDrawer(point, obstruction, placing);
            if (placing) return false;
            Transform hit = obstruction?.collider.transform;
            if (angle == "monitor")
            {
                Transform body = GetTarget("monitor")?.VisualRoot;
                return hit == null || body == null || !hit.IsChildOf(body);
            }
            if (Desktop == null || hit == null) return false;
            Transform desk = Desktop.transform.parent;
            if (angle == "gramophone") return desk != null && hit.IsChildOf(desk);
            if (angle != "counsel" || hit != desk) return false;
            RaycastHit surface = obstruction.Value;
            float height = Desktop.transform.InverseTransformPoint(surface.point).y;
            return Vector3.Dot(surface.normal, Desktop.transform.up) > 0.5f &&
                (Desktop.LocalBounds.min.y - height) * Mathf.Abs(Desktop.transform.lossyScale.y) < 0.035f;
        }

        private bool CanReturnFromDrawer(Vector2 point, RaycastHit? obstruction, bool placing)
        {
            Camera camera = views.ControlledCamera;
            Bounds bounds = Desktop.LocalBounds;
            Vector3 min = bounds.min, max = bounds.max;
            Vector3 Project(float x, float z) => camera.WorldToScreenPoint(Desktop.transform.TransformPoint(new Vector3(x, min.y, z)));
            // The tabletop and the drawer fronts face local +Z.
            Vector3 frontLeft = Project(min.x, max.z), frontRight = Project(max.x, max.z);
            Vector3 backLeft = Project(min.x, min.z), backRight = Project(max.x, min.z);
            if (Mathf.Min(frontLeft.z, frontRight.z, backLeft.z, backRight.z) <= camera.nearClipPlane) return false;

            float edge = placing ? Mathf.Max(frontLeft.y, frontRight.y, backLeft.y, backRight.y) :
                Mathf.Lerp(frontLeft.y, frontRight.y, Mathf.InverseLerp(frontLeft.x, frontRight.x, point.x));
            if (point.y < edge) return false;
            if (!obstruction.HasValue) return true;

            RaycastHit hit = obstruction.Value;
            // The placement volume sits on the desk body. Its own tabletop can take a return click.
            if (!placing && hit.collider.transform == Desktop.transform.parent) return true;
            float farDepth = Mathf.Max(frontLeft.z, frontRight.z, backLeft.z, backRight.z);
            return camera.WorldToScreenPoint(hit.point).z > farDepth;
        }

        private void DrawPointerFeedback(Component target)
        {
            if (hover == null) return;
            if (target is DrawerFrontTarget front) target = front.Drawer;
            if (target is InspectionTarget angle && angle.ShowHoverFeedback)
                hover.Draw(views.ControlledCamera, angle.VisualRoot, HoverCaption?.Invoke(angle) ?? angle.DisplayName);
            else if (target is SlidingDrawer drawer)
            {
                int count = drawer.Interior != null ? drawer.Interior.ItemCount : 0;
                string status = count == 0 ? "비어 있음" : $"{count}개 보관 중";
                hover.Draw(views.ControlledCamera, drawer.transform, $"{drawer.DisplayName} - {status}", drawer.FrontFace, drawer.Interior);
            }
            else if (target is HoldableItem item) hover.Draw(views.ControlledCamera, item.transform, item.DisplayName);
            else hover.Hide();
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
            pressedItem = null;
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
            CancelPointerDrag(); Carry?.CancelPlacement(); Carry?.SetPresentationVisible(false);
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
            Transitions?.CancelAngle(); TextInputFocus.ReleaseAll(); CancelPointerDrag();
            Carry?.CancelPlacement(); Carry?.SetPresentationVisible(false);
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
