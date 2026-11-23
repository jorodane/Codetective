#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Reflection;
using Codetective.CameraViews;
using Codetective.Interaction;
using Codetective.Operations;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Codetective.Terminal.Tests.UI
{
    public sealed class WorkspaceInteractionTests
    {
        private static void Drain(IEnumerator routine)
        { while (routine.MoveNext()) if (routine.Current is IEnumerator nested) Drain(nested); }
        [Test]
        public void DayStartsOverTheOfficeAndDeliversMailOnlyAfterBothTitleLinesAppear()
        {
            var system = new GameObject("Morning sequence"); system.SetActive(false);
            var settings = ScriptableObject.CreateInstance<Codetective.Gameplay.PresentationSettings>();
            GameObject card = null;
            try
            {
                settings.dayTitleFadeSeconds = settings.daySubtitleDelay = settings.daySubtitleFadeSeconds = settings.dayCardSeconds = settings.dayTextFadeSeconds = 0;
                Component sequence = system.AddComponent(FindType("Codetective.Gameplay.DayStartPresentation"));
                Call(sequence, "Initialize", Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"), settings);
                card = (GameObject)sequence.GetType().GetField("root", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(sequence);
                Assert.That(card.GetComponentsInChildren<Image>(true), Is.Empty); Assert.That(card.GetComponent<GraphicRaycaster>(), Is.Null);
                Text title = (Text)sequence.GetType().GetField("dayText", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(sequence);
                Assert.That(title.rectTransform.anchorMax.x - title.rectTransform.anchorMin.x, Is.EqualTo(0.52f).Within(0.001f));
                KernelRuntime runtime = ClinicScenario.Create(); runtime.RegisterMail(new StoryMail { id = "morning", mailbox = Mailbox.Kernel, subject = "새 임무" }); runtime.DeferDayMailUntilIntro();
                int deliveries = 0;
                Action fading = () =>
                {
                    foreach (string field in new[] { "dayAlpha", "subtitleAlpha" })
                        Assert.That(((CanvasGroup)sequence.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(sequence)).alpha, Is.EqualTo(1));
                    Assert.That(title.text, Is.EqualTo("DAY 1")); Assert.That(card.activeSelf, Is.True);
                    Assert.That(runtime.Inbox(Mailbox.Kernel), Is.Empty); runtime.MarkDayIntroSeen(); deliveries++;
                };
                Drain((IEnumerator)Call(sequence, "Play", 1, "Kernel 프로젝트의 시작", fading));
                Assert.That(deliveries, Is.EqualTo(1)); Assert.That(card.activeSelf, Is.False);
                Assert.That(runtime.UnreadMailCount(Mailbox.Kernel), Is.EqualTo(1)); Assert.That(runtime.IsMailRead("morning"), Is.False);
            }
            finally { if (card != null) Object.DestroyImmediate(card); Object.DestroyImmediate(system); Object.DestroyImmediate(settings); }
        }
        private static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        private static object Call(object target, string name, params object[] args) => target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(target, args);

        [Test]
        public void LoadedScenesStayCoveredThroughAngleCleanupAndRevealAfterTheirFirstReadyFrame()
        {
            var system = new GameObject("Loaded scene", typeof(Camera), typeof(CameraAngleController)); system.SetActive(false);
            var settings = ScriptableObject.CreateInstance<Codetective.Gameplay.PresentationSettings>();
            var effect = ScriptableObject.CreateInstance<TransitionPreset>(); effect.duration = 0; settings.scene = effect;
            Component transitions = null;
            try
            {
                transitions = system.AddComponent(FindType("Codetective.Gameplay.SceneTransitionController"));
                Call(transitions, "Initialize", system.GetComponent<CameraAngleController>(), null, settings);
                Call(transitions, "BeginLoadedSceneCovered");
                Image cover = (Image)transitions.GetType().GetField("sceneFade", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(transitions);
                Call(transitions, "ClearTransient");
                Assert.That(cover.color.a, Is.EqualTo(1)); Assert.That(transitions.GetType().GetProperty("IsBusy").GetValue(transitions), Is.True);
                IEnumerator reveal = (IEnumerator)Call(transitions, "RevealLoadedSceneRoutine");
                Assert.That(reveal.MoveNext(), Is.True); Assert.That(cover.color.a, Is.EqualTo(1));
                Drain(reveal); Assert.That(cover.color.a, Is.Zero); Assert.That(transitions.GetType().GetProperty("IsBusy").GetValue(transitions), Is.False);
            }
            finally
            {
                if (transitions != null) { Call(transitions, "CancelSceneReveal"); Call(transitions, "OnDestroy"); }
                Object.DestroyImmediate(system); Object.DestroyImmediate(settings); Object.DestroyImmediate(effect);
            }
        }

        [Test]
        public void EscapePutsTheHeldItemDownBeforeReturningToOverviewAndThenOpeningTheMenu()
        {
            var navigation = new GameObject("Escape sequence", typeof(Camera), typeof(CameraAngleController), FindType("Codetective.Interaction.PauseMenuView")); navigation.SetActive(false);
            var canvas = new GameObject("Monitor", typeof(RectTransform), typeof(Canvas)); var carryRoot = new GameObject("Carry"); var itemRoot = new GameObject("Evidence");
            var set = ScriptableObject.CreateInstance<CameraAngleSet>(); var presets = new List<CameraAnglePreset>();
            float oldTime = Time.timeScale; bool oldAudio = AudioListener.pause;
            try
            {
                foreach (string id in new[] { "overview", "monitor" })
                { var angle = ScriptableObject.CreateInstance<CameraAnglePreset>(); Set(angle, "angleId", id); presets.Add(angle); }
                Set(set, "angles", presets); CameraAngleController views = navigation.GetComponent<CameraAngleController>(); Set(views, "angleSet", set);
                GameObject monitor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/1_Core/Prefabs/TerminalPanel.prefab"), canvas.transform);
                MonitorPageHost host = monitor.GetComponent<MonitorPageHost>(); host.Initialize();
                Component inspection = navigation.AddComponent(FindType("Codetective.Interaction.InspectionController"));
                Set(inspection, "overviewAngle", presets[0]); Set(inspection, "terminalRoot", monitor.transform); Call(inspection, "Awake");
                ItemCarryController carry = carryRoot.AddComponent<ItemCarryController>(); carry.Initialize(navigation.transform);
                inspection.GetType().GetProperty("Carry").SetValue(inspection, carry); Call(inspection, "InspectAngle", presets[1]);
                Assert.That(carry.TryPickup(itemRoot.AddComponent<HoldableItem>()), Is.True);
                Call(inspection, "HandleEscape"); Assert.That(carry.HeldItem, Is.Null); Assert.That(itemRoot.transform.parent, Is.Null);
                Assert.That(views.ActiveAngle, Is.SameAs(presets[1])); Assert.That(host.SystemMenuVisible, Is.False);
                Call(inspection, "HandleEscape"); Assert.That(views.ActiveAngle, Is.SameAs(presets[0])); Assert.That(host.SystemMenuVisible, Is.False);
                Call(inspection, "HandleEscape"); Assert.That(host.SystemMenuVisible, Is.True);
                Call(inspection, "PrepareForSceneChange");
            }
            finally
            {
                Object.DestroyImmediate(carryRoot); Object.DestroyImmediate(itemRoot); Object.DestroyImmediate(navigation); Object.DestroyImmediate(canvas); Object.DestroyImmediate(set);
                foreach (var preset in presets) Object.DestroyImmediate(preset); Time.timeScale = oldTime; AudioListener.pause = oldAudio;
            }
        }

        [Test]
        public void TitleBrandingUsesTheSpaceBesideTheTerminalAndShowsTheBuildVersionAtBottomLeft()
        {
            var system = new GameObject("Title presentation"); system.SetActive(false);
            var camera = new GameObject("Title camera", typeof(Camera), typeof(CameraAngleController), FindType("Codetective.Interaction.PauseMenuView")); camera.SetActive(false);
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube); body.transform.position = new Vector3(1, 0, 3);
            try
            {
                Component inspection = camera.AddComponent(FindType("Codetective.Interaction.InspectionController"));
                var flow = (InspectionFlow)inspection.GetType().GetField("flow", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(inspection);
                flow.OpenMenu(); Set(inspection, "showingTitle", true);
                Component branding = system.AddComponent(FindType("Codetective.Gameplay.TitleBrandingView"));
                Call(branding, "Initialize", inspection, camera.GetComponent<Camera>(), body.transform, Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
                Call(branding, "LateUpdate");
                GameObject overlay = (GameObject)branding.GetType().GetField("root", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(branding);
                Text logo = overlay.GetComponentsInChildren<Text>().Single(t => t.name == "TitleLogo");
                Text version = overlay.GetComponentsInChildren<Text>().Single(t => t.name == "BuildVersion");
                Assert.That(overlay.activeSelf, Is.True); Assert.That(overlay.GetComponent<Canvas>().renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
                Assert.That(logo.rectTransform.anchorMax.x, Is.LessThan(0.5f)); Assert.That(logo.transform.IsChildOf(body.transform), Is.False);
                Assert.That(version.rectTransform.anchorMin, Is.EqualTo(Vector2.zero)); Assert.That(version.text, Is.EqualTo("v" + Application.version));
                Assert.That(overlay.GetComponent<GraphicRaycaster>(), Is.Null);
                Set(inspection, "showingTitle", false); Call(branding, "LateUpdate"); Assert.That(overlay.activeSelf, Is.False);
            }
            finally { Object.DestroyImmediate(system); Object.DestroyImmediate(camera); Object.DestroyImmediate(body); }
        }

        [Test]
        public void SelectingAProfilePrintsOnTheAuthoredSheetWithoutAHoldableCopy()
        {
            var system = new GameObject("Profile archive system"); system.SetActive(false);
            var stack = GameObject.CreatePrimitive(PrimitiveType.Cube); stack.name = "Upper-left paper stack";
            var camera = new GameObject("Reference camera", typeof(Camera), typeof(CameraAngleController), FindType("Codetective.Interaction.PauseMenuView")); camera.SetActive(false);
            var carryRoot = new GameObject("Carry");
            var sheet = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                Component game = system.AddComponent(FindType("Codetective.Gameplay.KernelGameController"));
                Component inspection = camera.AddComponent(FindType("Codetective.Interaction.InspectionController"));
                ItemCarryController carry = carryRoot.AddComponent<ItemCarryController>(); carry.Initialize(camera.transform);
                Set(game, "profileArchiveStack", stack.transform); Set(game, "profileSheet", sheet.transform);
                Set(game, "inspection", inspection); Set(game, "views", camera.GetComponent<CameraAngleController>()); Set(game, "<Carry>k__BackingField", carry);
                Set(game, "<Runtime>k__BackingField", ClinicScenario.Create()); Call(game, "InitializeProfileSheet");
                Assert.That(sheet.GetComponentInChildren<ProfileFormView>(), Is.Not.Null);
                Assert.That(system.GetComponentsInChildren<HoldableItem>(true), Is.Empty);
                Assert.That(Call(game, "SelectProfile", "kernel"), Is.False);
                Set(game, "presentationInitialized", true); Set(game, "<IsSessionActive>k__BackingField", true); Set(game, "<CurrentView>k__BackingField", "profile");
                Assert.That(Call(game, "SelectProfile", "kernel"), Is.True);
                Assert.That(game.GetType().GetProperty("DisplayedProfileId").GetValue(game), Is.EqualTo("kernel"));
                inspection.GetType().GetProperty("ModalInputBlocked").SetValue(inspection, true);
                Assert.That(Call(game, "SelectProfile", "client"), Is.False);
            }
            finally { Object.DestroyImmediate(system); Object.DestroyImmediate(stack); Object.DestroyImmediate(camera); Object.DestroyImmediate(carryRoot); Object.DestroyImmediate(sheet); }
        }

        [Test]
        public void EscapeReturnsToOverviewBeforeOpeningTheInstantTerminalMenu()
        {
            var root = new GameObject("Navigation", typeof(Camera), typeof(CameraAngleController), FindType("Codetective.Interaction.PauseMenuView")); root.SetActive(false);
            var canvas = new GameObject("Monitor", typeof(RectTransform), typeof(Canvas));
            var set = ScriptableObject.CreateInstance<CameraAngleSet>();
            var presets = new List<CameraAnglePreset>(); float oldTime = Time.timeScale;
            var hotspot = new GameObject("MonitorHotspot");
            try
            {
                foreach (string id in new[] { "overview", "monitor", "counsel", "profile", "drawer", "gramophone" })
                {
                    var angle = ScriptableObject.CreateInstance<CameraAnglePreset>(); Set(angle, "angleId", id);
                    Set(angle, "position", new Vector3(presets.Count, 1, 2)); Set(angle, "fieldOfView", 16f + presets.Count); presets.Add(angle);
                }
                Set(set, "angles", presets); CameraAngleController views = root.GetComponent<CameraAngleController>(); Set(views, "angleSet", set);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/1_Core/Prefabs/TerminalPanel.prefab");
                GameObject monitor = Object.Instantiate(prefab, canvas.transform); monitor.GetComponent<MonitorPageHost>().ShowPage("kernel");
                MonitorPageHost host = monitor.GetComponent<MonitorPageHost>();
                TerminalController terminal = monitor.GetComponent<TerminalController>();
                Component inspection = root.AddComponent(FindType("Codetective.Interaction.InspectionController"));
                Set(inspection, "overviewAngle", presets[0]); Set(inspection, "terminalRoot", monitor.transform); Call(inspection, "Awake");
                Component target = hotspot.AddComponent(FindType("Codetective.Interaction.InspectionTarget"));
                Set(target, "closeUpAngle", presets[1]); Set(target, "terminalRoot", monitor.transform);
                Assert.That(Call(inspection, "GetTarget", "monitor"), Is.Null);
                Call(inspection, "RegisterTarget", target);
                Assert.That(Call(inspection, "GetTarget", "MONITOR"), Is.SameAs(target));
                Call(inspection, "UnregisterTarget", target);
                Assert.That(Call(inspection, "GetTarget", "monitor"), Is.Null);
                Call(inspection, "RegisterTarget", target);
                foreach (int index in new[] { 0, 2, 4, 5 })
                {
                    Call(inspection, "InspectAngle", presets[index]); Call(inspection, "LateUpdate");
                    bool workingTerminal = index == 2 || index == 5;
                    Assert.That(terminal.InteractionEnabled, Is.EqualTo(workingTerminal));
                    if (index == 0 || index == 4) Call(inspection, "InspectAngle", presets[2]);
                    Call(inspection, "InspectTarget", target);
                    Assert.That(views.ActiveAngle, Is.SameAs(presets[workingTerminal ? index : 2]), "Clicking the monitor must keep the working angle.");
                    Call(inspection, "InspectAngle", presets[index]); Call(inspection, "LateUpdate");
                    host.ShowPage("kernel");
                    monitor.GetComponentInChildren<TerminalInputField>(true).SetTextWithoutNotify("save draft");
                    Call(inspection, "HandleEscape");
                    if (index != 0)
                    {
                        Assert.That(views.ActiveAngle, Is.SameAs(presets[0]));
                        Assert.That(host.SystemMenuVisible, Is.False); Assert.That(Time.timeScale, Is.EqualTo(oldTime));
                        Call(inspection, "HandleEscape");
                    }
                    Assert.That(views.ActiveAngle, Is.SameAs(presets[1])); Assert.That(views.IsTransitioning, Is.False);
                    Assert.That(Vector3.Distance(views.CapturePose().position, presets[1].Pose.position), Is.LessThan(0.0001f));
                    Assert.That(terminal.InteractionEnabled, Is.True); Assert.That(terminal.PageVisible, Is.False);
                    Assert.That(host.SystemMenuVisible, Is.True); Assert.That(Time.timeScale, Is.Zero);
                    Call(inspection, "Resume"); Call(inspection, "LateUpdate");
                    Assert.That(views.ActiveAngle, Is.SameAs(presets[0])); Assert.That(views.IsTransitioning, Is.False);
                    Assert.That(Vector3.Distance(views.CapturePose().position, presets[0].Pose.position), Is.LessThan(0.0001f));
                    Assert.That(views.ControlledCamera.fieldOfView, Is.EqualTo(presets[0].FieldOfView)); Assert.That(Time.timeScale, Is.EqualTo(oldTime));
                    Assert.That(terminal.InteractionEnabled, Is.False); Assert.That(host.SystemMenuVisible, Is.False);
                    Assert.That(monitor.GetComponentInChildren<TerminalInputField>(true).text, Is.EqualTo("save draft"));
                }
                Assert.That(Call(inspection, "TryInspectNumber", 4), Is.True); Assert.That(views.ActiveAngle.Id, Is.EqualTo("drawer"));
                Call(inspection, "InspectTarget", target); Assert.That(views.ActiveAngle.Id, Is.EqualTo("monitor"));
                Call(inspection, "ReturnToOverview"); Assert.That(views.ActiveAngle.Id, Is.EqualTo("overview"));
                Call(inspection, "BeginTerminalPresentation", true); Call(inspection, "ReturnToOverview");
                Assert.That(views.ActiveAngle.Id, Is.EqualTo("monitor"));
                Call(inspection, "HandleEscape"); Call(inspection, "HandleEscape");
                Assert.That(inspection.GetType().GetProperty("PresentingOnTerminal").GetValue(inspection), Is.True);
                Call(inspection, "EndTerminalPresentation"); Assert.That(views.ActiveAngle.Id, Is.EqualTo("overview"));
                Call(inspection, "ShowTitleMenu"); Call(inspection, "HandleEscape"); Call(inspection, "Resume");
                Assert.That(inspection.GetType().GetProperty("IsTitleMenu").GetValue(inspection), Is.True);
                Assert.That(Time.timeScale, Is.Zero); Assert.That(host.SystemMenuVisible, Is.True);
                Call(inspection, "PrepareForSceneChange"); Assert.That(Time.timeScale, Is.EqualTo(oldTime));
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(canvas); Object.DestroyImmediate(hotspot); Object.DestroyImmediate(set); foreach (var angle in presets) Object.DestroyImmediate(angle); Time.timeScale = oldTime; }
        }

        [Test]
        public void ReturnEffectsComeFromTheSourceAngleAndHaveAnIndependentDefault()
        {
            var root = new GameObject("Transition effects", typeof(Camera), typeof(CameraAngleController)); root.SetActive(false);
            var settings = ScriptableObject.CreateInstance<Codetective.Gameplay.PresentationSettings>();
            var source = ScriptableObject.CreateInstance<CameraAnglePreset>(); var overview = ScriptableObject.CreateInstance<CameraAnglePreset>();
            var entry = ScriptableObject.CreateInstance<TransitionPreset>(); var exit = ScriptableObject.CreateInstance<TransitionPreset>();
            var fallback = ScriptableObject.CreateInstance<TransitionPreset>(); var set = ScriptableObject.CreateInstance<CameraAngleSet>();
            try
            {
                Component transitions = root.AddComponent(FindType("Codetective.Gameplay.SceneTransitionController"));
                CameraAngleController views = root.GetComponent<CameraAngleController>(); Set(set, "angles", new List<CameraAnglePreset> { overview, source }); Set(views, "angleSet", set);
                Set(transitions, "views", views); Set(transitions, "settings", settings);
                Set(source, "transition", entry); Set(source, "exitTransition", exit); Set(overview, "transition", entry);
                exit.style = TransitionStyle.Cut; settings.defaultAngle = entry; settings.defaultReturn = fallback;
                Assert.That(Call(transitions, "ReturnEffectFor", source), Is.SameAs(exit));
                bool changed = false; Assert.That(Call(transitions, "TryReturnToOverview", source, overview, (Action)(() => changed = true)), Is.True);
                Assert.That(changed, Is.True); Assert.That(views.ActiveAngle, Is.SameAs(overview));
                Set(source, "exitTransition", null); Assert.That(Call(transitions, "ReturnEffectFor", source), Is.SameAs(fallback));
                settings.defaultReturn = null; Assert.That(Call(transitions, "ReturnEffectFor", source), Is.SameAs(entry));
            }
            finally
            {
                Object.DestroyImmediate(root); Object.DestroyImmediate(settings); Object.DestroyImmediate(source); Object.DestroyImmediate(overview);
                Object.DestroyImmediate(entry); Object.DestroyImmediate(exit); Object.DestroyImmediate(fallback); Object.DestroyImmediate(set);
            }
        }

        [Test]
        public void ClosingTheCalendarOrPhotoReturnsToTheViewThatOpenedIt()
        {
            var root = new GameObject("Information views", typeof(Camera), typeof(CameraAngleController), FindType("Codetective.Interaction.PauseMenuView")); root.SetActive(false);
            var set = ScriptableObject.CreateInstance<CameraAngleSet>(); var presets = new List<CameraAnglePreset>();
            try
            {
                foreach (string id in new[] { "overview", "drawer", "clock", "portrait" })
                { var preset = ScriptableObject.CreateInstance<CameraAnglePreset>(); Set(preset, "angleId", id); presets.Add(preset); }
                Set(set, "angles", presets); CameraAngleController views = root.GetComponent<CameraAngleController>(); Set(views, "angleSet", set);
                Component inspection = root.AddComponent(FindType("Codetective.Interaction.InspectionController")); Set(inspection, "overviewAngle", presets[0]); Call(inspection, "Awake");
                foreach (int source in new[] { 0, 1 }) foreach (int info in new[] { 2, 3 })
                {
                    Call(inspection, "InspectAngle", presets[source]); Call(inspection, "InspectAngle", presets[info]);
                    Assert.That(Call(inspection, "CloseInformationView"), Is.True); Assert.That(views.ActiveAngle, Is.SameAs(presets[source]));
                    Assert.That(Call(inspection, "CloseInformationView"), Is.False);
                }
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(set); foreach (var preset in presets) Object.DestroyImmediate(preset); }
        }
        [Test]
        public void ViewEventsBeforeOverlayInitializationDoNotAccessUnboundReferences()
        {
            var root = new GameObject("Uninitialized overlay"); root.SetActive(false);
            var angle = ScriptableObject.CreateInstance<CameraAnglePreset>(); Set(angle, "angleId", "counsel");
            try
            {
                Component game = root.AddComponent(FindType("Codetective.Gameplay.KernelGameController"));
                Component overlay = root.GetComponent(FindType("Codetective.Gameplay.KernelGameUI")); Set(game, "ui", overlay);
                Assert.That(() => Call(game, "EnterView", angle), Throws.Nothing);
                Assert.That(() => Call(overlay, "ShowContext", "counsel"), Throws.Nothing);
                Assert.That(() => Call(overlay, "HideContext"), Throws.Nothing);
                Assert.That(Call(overlay, "Back"), Is.False);
                Assert.That(overlay.GetType().GetProperty("IsInitialized").GetValue(overlay), Is.False);
                Assert.That(() => Call(game, "LeaveView"), Throws.Nothing);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(angle); }
        }

        [Test]
        public void SelectedTextInputsPreventNumberNavigation()
        {
            var root = new GameObject("Navigation", typeof(Camera), typeof(CameraAngleController), FindType("Codetective.Interaction.PauseMenuView")); root.SetActive(false);
            EventSystem previous = EventSystem.current; var events = new GameObject("Events", typeof(EventSystem));
            var field = new GameObject("Input", typeof(RectTransform), typeof(InputField));
            var set = ScriptableObject.CreateInstance<CameraAngleSet>(); var angle = ScriptableObject.CreateInstance<CameraAnglePreset>();
            try
            {
                EventSystem.current = events.GetComponent<EventSystem>(); Set(angle, "angleId", "monitor"); Set(set, "angles", new List<CameraAnglePreset> { angle });
                Set(root.GetComponent<CameraAngleController>(), "angleSet", set);
                Component inspection = root.AddComponent(FindType("Codetective.Interaction.InspectionController")); Call(inspection, "Awake");
                EventSystem.current.SetSelectedGameObject(field);
                Assert.That(Call(inspection, "TryInspectNumber", 1), Is.False);
                Assert.That(root.GetComponent<CameraAngleController>().ActiveAngle, Is.Null);
                EventSystem.current.SetSelectedGameObject(null);
                Assert.That(Call(inspection, "TryInspectNumber", 1), Is.True);
            }
            finally { Object.DestroyImmediate(field); Object.DestroyImmediate(events); EventSystem.current = previous; Object.DestroyImmediate(root); Object.DestroyImmediate(set); Object.DestroyImmediate(angle); }
        }

        [Test]
        public void DrawerInteriorOwnsTheClickAndAnEmptyClickAboveTheDeskReturnsToOverview()
        {
            var navigation = new GameObject("Drawer view", typeof(Camera), typeof(CameraAngleController)); navigation.SetActive(false);
            var upper = new GameObject("Upper drawer"); var lower = new GameObject("Lower drawer");
            var desktop = new GameObject("Desktop"); var prop = new GameObject("Foreground prop", typeof(BoxCollider));
            var set = ScriptableObject.CreateInstance<CameraAngleSet>();
            var overview = ScriptableObject.CreateInstance<CameraAnglePreset>(); var drawer = ScriptableObject.CreateInstance<CameraAnglePreset>();
            try
            {
                var origin = new Vector3(80, 80, 80);
                Set(overview, "angleId", "overview"); Set(overview, "position", origin);
                Set(drawer, "angleId", "drawer"); Set(drawer, "position", origin);
                Set(set, "angles", new List<CameraAnglePreset> { overview, drawer });
                var views = navigation.GetComponent<CameraAngleController>(); Set(views, "angleSet", set);
                Camera camera = navigation.GetComponent<Camera>();
                camera.orthographic = true; camera.orthographicSize = 1; camera.pixelRect = new Rect(0, 0, 640, 480);
                Component inspection = navigation.AddComponent(FindType("Codetective.Interaction.InspectionController"));
                Set(inspection, "overviewAngle", overview); Call(inspection, "Awake"); Call(inspection, "InspectAngle", drawer);

                var runtime = new KernelRuntime(); runtime.SetDrawerOpen("upper", true);
                upper.transform.position = origin + Vector3.forward * 2;
                lower.transform.position = origin + Vector3.forward * 3;
                SlidingDrawer top = upper.AddComponent<SlidingDrawer>(); top.Initialize(runtime, "upper"); top.SetInteractionEnabled(true);
                SlidingDrawer bottom = lower.AddComponent<SlidingDrawer>(); bottom.Initialize(runtime, "lower"); bottom.SetInteractionEnabled(true);
                upper.AddComponent<ItemPlacementSurface>().Initialize("upper", top, new Bounds(Vector3.zero, Vector3.one * 0.4f));
                var front = new GameObject("Drawer front");
                front.transform.SetParent(lower.transform, false);
                front.AddComponent<DrawerFrontTarget>().Initialize(bottom, new Bounds(Vector3.zero, Vector3.one * 0.4f));
                Assert.That(bottom.FrontFace, Is.SameAs(front.GetComponent<BoxCollider>()));
                desktop.transform.position = origin + new Vector3(0, -0.65f, 3);
                var surface = desktop.AddComponent<ItemPlacementSurface>(); surface.Initialize("desk.top", null, new Bounds(Vector3.zero, Vector3.one * 0.4f));
                inspection.GetType().GetProperty("Desktop").SetValue(inspection, surface);
                prop.transform.position = origin + new Vector3(0, 0.65f, 2); prop.transform.localScale = Vector3.one * 0.2f;
                Physics.SyncTransforms();

                Assert.That(Call(inspection, "InteractAtScreenPoint", (Vector2)camera.WorldToScreenPoint(upper.transform.position)), Is.True);
                Assert.That(top.IsOpen, Is.False); Assert.That(bottom.IsOpen, Is.False);
                Vector2 above = camera.WorldToScreenPoint(prop.transform.position);
                Assert.That(Call(inspection, "InteractAtScreenPoint", above), Is.False);
                Assert.That(views.ActiveAngle, Is.SameAs(drawer));
                prop.SetActive(false); Physics.SyncTransforms();
                Assert.That(Call(inspection, "InteractAtScreenPoint", above), Is.True);
                Assert.That(views.ActiveAngle, Is.SameAs(overview));
            }
            finally
            {
                foreach (Object value in new Object[] { navigation, upper, lower, desktop, prop, overview, drawer, set }) Object.DestroyImmediate(value);
            }
        }

        [TestCase("drawer", false)]
        [TestCase("counsel", false)]
        [TestCase("gramophone", true)]
        public void DeskClicksReturnAccordingToTheCurrentViewAndStillAllowItemPlacement(string angleId, bool frontReturns)
        {
            var navigation = new GameObject("Drawer view", typeof(Camera), typeof(CameraAngleController)); navigation.SetActive(false);
            var desk = new GameObject("Desk", typeof(BoxCollider));
            var desktop = new GameObject("Desktop"); desktop.transform.SetParent(desk.transform, false);
            var carryRoot = new GameObject("Carry"); var itemRoot = new GameObject("Paper");
            var set = ScriptableObject.CreateInstance<CameraAngleSet>();
            var overview = ScriptableObject.CreateInstance<CameraAnglePreset>(); var drawer = ScriptableObject.CreateInstance<CameraAnglePreset>();
            try
            {
                var origin = new Vector3(81, 81, 81);
                Set(overview, "angleId", "overview"); Set(overview, "position", origin);
                Set(drawer, "angleId", angleId); Set(drawer, "position", origin);
                Set(drawer, "eulerAngles", new Vector3(27.5f, 0, 0));
                Set(set, "angles", new List<CameraAnglePreset> { overview, drawer });
                var views = navigation.GetComponent<CameraAngleController>(); Set(views, "angleSet", set);
                Camera camera = navigation.GetComponent<Camera>(); camera.pixelRect = new Rect(0, 0, 640, 480);
                Component inspection = navigation.AddComponent(FindType("Codetective.Interaction.InspectionController"));
                Set(inspection, "overviewAngle", overview); Call(inspection, "Awake"); Call(inspection, "InspectAngle", drawer);

                desk.transform.SetPositionAndRotation(origin + new Vector3(0, -0.65f, 2), Quaternion.Euler(0, 180, 0));
                BoxCollider deskBody = desk.GetComponent<BoxCollider>();
                deskBody.center = new Vector3(0, -0.15f, 0); deskBody.size = new Vector3(2, 0.3f, 1.4f);
                var surface = desktop.AddComponent<ItemPlacementSurface>();
                surface.Initialize("desk.top", null, new Bounds(new Vector3(0, 0.125f, 0), new Vector3(2, 0.25f, 1.4f)));
                inspection.GetType().GetProperty("Desktop").SetValue(inspection, surface);
                Physics.SyncTransforms();

                Vector2 front = camera.WorldToScreenPoint(desk.transform.TransformPoint(new Vector3(0, -0.15f, 0.7f)));
                Assert.That(Call(inspection, "InteractAtScreenPoint", front), Is.EqualTo(frontReturns));
                Assert.That(views.ActiveAngle, Is.SameAs(frontReturns ? overview : drawer));
                if (frontReturns) Call(inspection, "InspectAngle", drawer);

                Vector2 tabletop = camera.WorldToScreenPoint(desk.transform.position);
                Assert.That(Call(inspection, "InteractAtScreenPoint", tabletop), Is.True);
                Assert.That(views.ActiveAngle, Is.SameAs(overview));

                Call(inspection, "InspectAngle", drawer);
                ItemCarryController carry = carryRoot.AddComponent<ItemCarryController>(); carry.Initialize(navigation.transform);
                inspection.GetType().GetProperty("Carry").SetValue(inspection, carry);
                HoldableItem item = itemRoot.AddComponent<HoldableItem>();
                Assert.That(carry.TryPickup(item), Is.True);
                Assert.That(carry.BeginPlacement(), Is.True);
                Assert.That(Call(inspection, "InteractAtScreenPoint", tabletop), Is.True);
                Assert.That(item.Storage, Is.SameAs(surface));
                Assert.That(views.ActiveAngle, Is.SameAs(drawer));
            }
            finally
            {
                foreach (Object value in new Object[] { carryRoot, itemRoot, navigation, desk, overview, drawer, set }) Object.DestroyImmediate(value);
            }
        }

        [Test]
        public void TheTerminalReturnsOnlyWhenClickingOutsideTheCaseAndItsControls()
        {
            var navigation = new GameObject("Terminal view", typeof(Camera), typeof(CameraAngleController)); navigation.SetActive(false);
            var terminal = new GameObject("Terminal", typeof(BoxCollider));
            var targetType = FindType("Codetective.Interaction.InspectionTarget");
            var hotspot = new GameObject("Terminal hotspot", targetType);
            var set = ScriptableObject.CreateInstance<CameraAngleSet>();
            var overview = ScriptableObject.CreateInstance<CameraAnglePreset>();
            var monitor = ScriptableObject.CreateInstance<CameraAnglePreset>();
            try
            {
                var origin = new Vector3(82, 82, 82);
                Set(overview, "angleId", "overview"); Set(overview, "position", origin);
                Set(monitor, "angleId", "monitor"); Set(monitor, "position", origin);
                Set(set, "angles", new List<CameraAnglePreset> { overview, monitor });
                var views = navigation.GetComponent<CameraAngleController>(); Set(views, "angleSet", set);
                Camera camera = navigation.GetComponent<Camera>(); camera.pixelRect = new Rect(0, 0, 640, 480);
                camera.orthographic = true; camera.orthographicSize = 1;
                Component inspection = navigation.AddComponent(FindType("Codetective.Interaction.InspectionController"));
                Set(inspection, "overviewAngle", overview); Call(inspection, "Awake");
                Component target = hotspot.GetComponent(targetType);
                Set(target, "closeUpAngle", monitor); Set(target, "visualRoot", terminal.transform);
                Call(inspection, "RegisterTarget", target); Call(inspection, "InspectAngle", monitor);

                terminal.transform.position = origin + Vector3.forward * 3;
                Physics.SyncTransforms();
                Vector2 inside = camera.WorldToScreenPoint(terminal.transform.position);
                Vector2 outside = camera.WorldToScreenPoint(terminal.transform.position + Vector3.right);
                Assert.That(Call(inspection, "InteractAtScreenPoint", inside), Is.False);
                Assert.That(views.ActiveAngle, Is.SameAs(monitor));
                inspection.GetType().GetProperty("PointerBlocked").SetValue(inspection, (Func<Vector2, bool>)(_ => true));
                Assert.That(Call(inspection, "InteractAtScreenPoint", outside), Is.False);
                Assert.That(views.ActiveAngle, Is.SameAs(monitor));
                inspection.GetType().GetProperty("PointerBlocked").SetValue(inspection, null);
                Assert.That(Call(inspection, "InteractAtScreenPoint", outside), Is.True);
                Assert.That(views.ActiveAngle, Is.SameAs(overview));
            }
            finally
            {
                foreach (Object value in new Object[] { navigation, terminal, hotspot, overview, monitor, set }) Object.DestroyImmediate(value);
            }
        }

        [Test]
        public void NewspaperRollsOnTheDeskAndInDrawersAndOpensOnlyWhenExamined()
        {
            var system = new GameObject("Carry"); var camera = new GameObject("Camera", typeof(Camera)); var paper = new GameObject("Paper"); var drawer = new GameObject("Drawer");
            try
            {
                ItemCarryController carry = system.AddComponent<ItemCarryController>(); carry.Initialize(camera.transform);
                HoldableItem item = paper.AddComponent<HoldableItem>(); item.Configure("paper", "신문", HeldItemUse.Newspaper);
                Component document = paper.AddComponent(FindType("Codetective.Gameplay.PaperDocument"));
                Call(document, "Initialize", item, carry, camera.GetComponent<Camera>(), Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"), new Vector2(0.44f, 0.28f));
                Type sectionType = FindType("Codetective.Gameplay.PaperSection"); object section = Activator.CreateInstance(sectionType);
                sectionType.GetField("title").SetValue(section, "기사 제목"); sectionType.GetField("text").SetValue(section, "종이에 인쇄된 본문");
                Array sections = Array.CreateInstance(sectionType, 1); sections.SetValue(section, 0); Call(document, "SetContent", "DAY 01", sections);
                carry.Register(item);
                Canvas ink = paper.GetComponentInChildren<Canvas>(true); Assert.That(ink.renderMode, Is.EqualTo(RenderMode.WorldSpace));
                Assert.That(item.IsRolled, Is.True); Assert.That(ink.gameObject.activeSelf, Is.False);
                Assert.That(carry.TryPickup(item), Is.True);
                Assert.That(paper.GetComponentsInChildren<Text>().Any(t => t.text.Contains("종이에 인쇄된 본문")), Is.True);
                ItemPlacementSurface surface = drawer.AddComponent<ItemPlacementSurface>(); surface.Initialize("desk.top", null, new Bounds(Vector3.zero, Vector3.one));
                Assert.That(carry.BeginPlacement(), Is.True); Assert.That(carry.TryPlaceOn(surface), Is.True);
                Assert.That(ink.gameObject.activeSelf, Is.False); Assert.That(item.IsRolled, Is.True);
                Vector3 onDesk = item.transform.localPosition;
                Assert.That(carry.TryPickup(item), Is.True);
                carry.PutDown();
                Assert.That(item.Storage, Is.SameAs(surface)); Assert.That(item.transform.localPosition, Is.EqualTo(onDesk));
                carry.TryPickup(item);
                var runtime = new KernelRuntime(); runtime.SetDrawerOpen("drawer", true);
                SlidingDrawer owner = drawer.AddComponent<SlidingDrawer>(); owner.Initialize(runtime, "drawer"); owner.SetInteractionEnabled(true);
                surface.Initialize("drawer", owner, new Bounds(Vector3.zero, Vector3.one));
                Assert.That(carry.BeginPlacement(), Is.True); Assert.That(carry.TryPlaceOn(surface), Is.True);
                Assert.That(ink.gameObject.activeSelf, Is.False); Assert.That(paper.transform.Find("RolledNewspaper").gameObject.activeSelf, Is.True);
                Assert.That(carry.TryPickup(item), Is.True); Assert.That(ink.gameObject.activeSelf, Is.True);
                carry.ReturnToDesk(item);
                Assert.That(item.Storage, Is.Null); Assert.That(item.transform.parent, Is.Null); Assert.That(item.IsRolled, Is.True);
            }
            finally { Object.DestroyImmediate(system); Object.DestroyImmediate(paper); Object.DestroyImmediate(drawer); Object.DestroyImmediate(camera); }
        }

        [Test]
        public void ProfilePaperTurnsJournalPagesWithoutPuttingTheEvidenceDown()
        {
            var system = new GameObject("Carry"); var camera = new GameObject("Camera", typeof(Camera)); var paper = new GameObject("Profile");
            try
            {
                KernelRuntime runtime = ClinicScenario.Create(); runtime.AddConsultationNote("kernel", "첫 번째 Kernel 일지", 1, 540, 570);
                runtime.AddConsultationNote("kernel", "두 번째 Kernel 일지", 1, 600, 630);
                ItemCarryController carry = system.AddComponent<ItemCarryController>(); carry.Initialize(camera.transform);
                HoldableItem item = paper.AddComponent<HoldableItem>(); item.Configure("profile", "Kernel 프로필", HeldItemUse.Profile, "kernel");
                Component document = paper.AddComponent(FindType("Codetective.Gameplay.PaperDocument"));
                Call(document, "Initialize", item, carry, camera.GetComponent<Camera>(), Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"), new Vector2(0.27f, 0.35f));
                Type sectionType = FindType("Codetective.Gameplay.PaperSection"); Array pages = Array.CreateInstance(sectionType, 2);
                for (int i = 0; i < 2; i++)
                {
                    object page = Activator.CreateInstance(sectionType); sectionType.GetField("profile").SetValue(page, runtime.ProfileCard("kernel"));
                    sectionType.GetField("title").SetValue(page, "상담 일지 " + (i + 1));
                    sectionType.GetField("text").SetValue(page, runtime.ProfileJournalText("kernel", i)); pages.SetValue(page, i);
                }
                Call(document, "SetPagedContent", "Kernel 프로필", pages, 0); carry.Register(item); carry.TryPickup(item);
                Assert.That(paper.GetComponentsInChildren<ProfileFormView>().Length, Is.EqualTo(1));
                Assert.That(paper.GetComponentsInChildren<Text>().Any(t => t.text.Contains("첫 번째 Kernel 일지")), Is.True);
                int changed = -1; document.GetType().GetEvent("PageChanged").AddEventHandler(document, (Action<int>)(index => changed = index));
                Button next = (Button)document.GetType().GetField("nextPage", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(document); next.onClick.Invoke();
                Assert.That(changed, Is.EqualTo(1)); Assert.That(item.IsHeld, Is.True);
                Assert.That(paper.GetComponentsInChildren<ProfileFormView>().Length, Is.EqualTo(1));
                Assert.That(paper.GetComponentsInChildren<Text>().Any(t => t.text.Contains("두 번째 Kernel 일지")), Is.True);
                Assert.That(paper.GetComponentsInChildren<Text>().Any(t => t.text.Contains("첫 번째 Kernel 일지")), Is.False);
                Assert.That(paper.GetComponentsInChildren<Text>().Any(t => t.text == "이름: [데이터 말소됨]"), Is.True);
                Button previous = (Button)document.GetType().GetField("previousPage", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(document); previous.onClick.Invoke();
                Assert.That(changed, Is.Zero); Assert.That(item.IsHeld, Is.True);
            }
            finally { Object.DestroyImmediate(system); Object.DestroyImmediate(paper); Object.DestroyImmediate(camera); }
        }

        [Test]
        public void PlacementSurvivesAngleChangesAndFrontClicksStillToggleTheDrawer()
        {
            var navigation = new GameObject("Navigation", typeof(Camera), typeof(CameraAngleController), FindType("Codetective.Interaction.PauseMenuView")); navigation.SetActive(false);
            var system = new GameObject("Carry"); var itemRoot = new GameObject("Item"); var drawerRoot = new GameObject("Drawer"); var hotspot = new GameObject("Drawer angle");
            var set = ScriptableObject.CreateInstance<CameraAngleSet>(); var presets = new List<CameraAnglePreset>();
            try
            {
                foreach (string id in new[] { "overview", "monitor", "drawer" })
                { var angle = ScriptableObject.CreateInstance<CameraAnglePreset>(); Set(angle, "angleId", id); presets.Add(angle); }
                Set(set, "angles", presets); Set(navigation.GetComponent<CameraAngleController>(), "angleSet", set);
                Component inspection = navigation.AddComponent(FindType("Codetective.Interaction.InspectionController"));
                Set(inspection, "overviewAngle", presets[0]); Call(inspection, "Awake");
                ItemCarryController carry = system.AddComponent<ItemCarryController>(); carry.Initialize(navigation.transform);
                inspection.GetType().GetProperty("Carry").SetValue(inspection, carry);
                carry.TryPickup(itemRoot.AddComponent<HoldableItem>()); Assert.That(carry.BeginPlacement(), Is.True);
                Assert.That(Call(inspection, "TryInspectNumber", 4), Is.True); Assert.That(carry.IsPlacing, Is.True);
                Assert.That(Call(inspection, "TryInspectNumber", 1), Is.True); Assert.That(carry.IsPlacing, Is.True);
                Call(inspection, "TryInspectNumber", 1);
                var runtime = new KernelRuntime(); drawerRoot.transform.position = new Vector3(0, 0, 2);
                SlidingDrawer drawer = drawerRoot.AddComponent<SlidingDrawer>(); drawer.Initialize(runtime, "drawer"); drawer.SetInteractionEnabled(true);
                hotspot.transform.position = new Vector3(0, 0, 1.5f); hotspot.AddComponent<BoxCollider>().isTrigger = true;
                Component target = hotspot.AddComponent(FindType("Codetective.Interaction.InspectionTarget")); Set(target, "closeUpAngle", presets[2]); Call(inspection, "RegisterTarget", target);
                var face = new GameObject("Front"); face.transform.SetParent(drawerRoot.transform, false);
                face.AddComponent<DrawerFrontTarget>().Initialize(drawer, new Bounds(Vector3.zero, new Vector3(0.6f, 0.3f, 0.025f)));
                Physics.SyncTransforms();
                Vector2 point = navigation.GetComponent<Camera>().WorldToScreenPoint(face.transform.position);
                Assert.That(Call(inspection, "InteractAtScreenPoint", point), Is.True);
                Assert.That(navigation.GetComponent<CameraAngleController>().ActiveAngle.Id, Is.EqualTo("drawer")); Assert.That(carry.IsPlacing, Is.True); Assert.That(drawer.IsOpen, Is.False);
                Assert.That(Call(inspection, "InteractAtScreenPoint", point), Is.True); Assert.That(drawer.IsOpen, Is.True);
                Assert.That(Call(inspection, "InteractAtScreenPoint", point), Is.True); Assert.That(drawer.IsOpen, Is.False);
                Assert.That(carry.IsPlacing, Is.True); Assert.That(carry.HeldItem, Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(system); Object.DestroyImmediate(itemRoot); Object.DestroyImmediate(drawerRoot); Object.DestroyImmediate(hotspot); Object.DestroyImmediate(navigation);
                Object.DestroyImmediate(set); foreach (var angle in presets) Object.DestroyImmediate(angle);
            }
        }

        [Test]
        public void BodyCluesAreCollectedByClickingTheirRenderedLetters()
        {
            var canvas = new GameObject("Reader", typeof(RectTransform), typeof(Canvas)); canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            try
            {
                var label = new GameObject("Body", typeof(RectTransform)); label.transform.SetParent(canvas.transform, false);
                RectTransform rect = (RectTransform)label.transform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.sizeDelta = new Vector2(500, 160);
                Text text = (Text)label.AddComponent(FindType("Codetective.Gameplay.PaperClueText"));
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.fontSize = 24; text.alignment = TextAnchor.UpperLeft;
                text.text = "ALPHA CLUE OMEGA"; text.supportRichText = false;
                Type clueType = FindType("Codetective.Gameplay.PaperClue"); object clue = Activator.CreateInstance(clueType); int collected = 0;
                clueType.GetField("label").SetValue(clue, "CLUE"); clueType.GetField("collect").SetValue(clue, (Action)(() => collected++));
                Array clues = Array.CreateInstance(clueType, 1); clues.SetValue(clue, 0); text.GetType().GetField("Clues").SetValue(text, clues);
                Canvas.ForceUpdateCanvases();
                // Pick the center of the actual glyph geometry, independently of the link hit rectangles.
                foreach (int character in new[] { 0, 6 })
                {
                    IList<UIVertex> vertices = text.cachedTextGenerator.verts;
                    Vector3 local = (vertices[character * 4].position + vertices[character * 4 + 2].position) * (0.5f / text.pixelsPerUnit);
                    ((IPointerClickHandler)text).OnPointerClick(new PointerEventData(null) { position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(local)) });
                    Assert.That(collected, Is.EqualTo(character == 0 ? 0 : 1));
                }
            }
            finally { Object.DestroyImmediate(canvas); }
        }

        [Test]
        public void EvidenceReaderPreservesThePrintLayoutAndHasOnlyACloseButton()
        {
            var system = new GameObject("Game"); system.SetActive(false);
            var navigation = new GameObject("Navigation", typeof(Camera), typeof(CameraAngleController), FindType("Codetective.Interaction.PauseMenuView")); navigation.SetActive(false);
            var paper = new GameObject("Paper"); var texture = new Texture2D(4, 4);
            Sprite picture = Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.zero);
            Component ui = null;
            try
            {
                Component inspection = navigation.AddComponent(FindType("Codetective.Interaction.InspectionController")); Call(inspection, "Awake");
                Component game = system.AddComponent(FindType("Codetective.Gameplay.KernelGameController"));
                Set(game, "<Runtime>k__BackingField", ClinicScenario.Create());
                ItemCarryController carry = system.AddComponent<ItemCarryController>(); carry.Initialize(navigation.transform);
                Set(game, "<Carry>k__BackingField", carry);
                ui = system.GetComponent(FindType("Codetective.Gameplay.KernelGameUI"));
                Call(ui, "Initialize", game, inspection, Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
                Call(ui, "ShowSaveStatus", "빠른 저장 중...", 2f);
                GameObject notice = (GameObject)ui.GetType().GetField("saveNoticeRoot", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ui);
                Assert.That(notice.activeSelf, Is.True); Assert.That(notice.GetComponentsInChildren<GraphicRaycaster>(), Is.Empty);
                Assert.That(notice.GetComponentsInChildren<Graphic>().All(g => !g.raycastTarget), Is.True);
                Assert.That(notice.GetComponentInChildren<Text>().text, Is.EqualTo("빠른 저장 중..."));
                Assert.That(notice.transform.GetChild(0).GetComponent<RectTransform>().anchorMax, Is.EqualTo(Vector2.one));
                HoldableItem item = paper.AddComponent<HoldableItem>(); item.Configure("paper", "신문", HeldItemUse.Newspaper);
                Component document = paper.AddComponent(FindType("Codetective.Gameplay.PaperDocument"));
                Call(document, "Initialize", item, carry, navigation.GetComponent<Camera>(), Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"), new Vector2(0.44f, 0.28f));
                Type sectionType = FindType("Codetective.Gameplay.PaperSection"); object section = Activator.CreateInstance(sectionType);
                sectionType.GetField("title").SetValue(section, "기사 제목"); sectionType.GetField("text").SetValue(section, "본문 전체\n둘째 문단");
                Type layoutType = sectionType.GetField("layout").FieldType; object layout = Activator.CreateInstance(layoutType);
                layoutType.GetField("width").SetValue(layout, 600f); layoutType.GetField("height").SetValue(layout, 1000f);
                Type blockType = layoutType.GetField("blocks").FieldType.GetElementType();
                object body = Activator.CreateInstance(blockType), image = Activator.CreateInstance(blockType);
                blockType.GetField("kind").SetValue(body, Enum.Parse(blockType.GetField("kind").FieldType, "Body"));
                blockType.GetField("kind").SetValue(image, Enum.Parse(blockType.GetField("kind").FieldType, "Image"));
                blockType.GetField("position").SetValue(image, new Vector2(220, 400)); blockType.GetField("size").SetValue(image, new Vector2(180, 140));
                blockType.GetField("image").SetValue(image, picture); blockType.GetField("clueKeyword").SetValue(image, "사진");
                Array blocks = Array.CreateInstance(blockType, 2); blocks.SetValue(body, 0); blocks.SetValue(image, 1);
                layoutType.GetField("blocks").SetValue(layout, blocks); sectionType.GetField("layout").SetValue(section, layout);
                Type clueType = FindType("Codetective.Gameplay.PaperClue"); object clue = Activator.CreateInstance(clueType); int collected = 0;
                clueType.GetField("label").SetValue(clue, "사진"); clueType.GetField("collect").SetValue(clue, (Action)(() => collected++));
                Array clues = Array.CreateInstance(clueType, 1); clues.SetValue(clue, 0); sectionType.GetField("clues").SetValue(section, clues);
                Array sections = Array.CreateInstance(sectionType, 1); sections.SetValue(section, 0); Call(document, "SetContent", "조간", sections);
                carry.Register(item); carry.TryPickup(item); Call(ui, "ShowDocument", document, 0);
                RectTransform reader = (RectTransform)ui.GetType().GetField("newspaperReader", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ui);
                Button[] buttons = reader.GetComponentsInChildren<Button>(); Assert.That(buttons.Length, Is.EqualTo(1));
                Assert.That(buttons[0].GetComponentInChildren<Text>().text, Is.EqualTo("닫기"));
                Assert.That(reader.GetComponentsInChildren<Text>().Any(t => t.text == "본문 전체\n둘째 문단"), Is.True);
                Image expanded = reader.GetComponentsInChildren<Image>().Single(i => i.sprite == picture);
                Image printed = paper.GetComponentsInChildren<Image>().Single(i => i.sprite == picture);
                Assert.That(expanded.rectTransform.anchoredPosition, Is.EqualTo(printed.rectTransform.anchoredPosition));
                Assert.That(expanded.rectTransform.sizeDelta, Is.EqualTo(new Vector2(180, 140)));
                ((IPointerClickHandler)expanded.GetComponent(FindType("Codetective.Gameplay.PaperImageClue"))).OnPointerClick(new PointerEventData(null));
                Assert.That(collected, Is.EqualTo(1)); Assert.That(item.IsHeld, Is.True);
                buttons[0].onClick.Invoke(); Assert.That(reader.gameObject.activeSelf, Is.False); Assert.That(item.IsHeld, Is.True);
            }
            finally
            {
                if (ui != null)
                {
                    Component morning = (Component)ui.GetType().GetField("dayStart", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ui);
                    if (morning != null) Object.DestroyImmediate((GameObject)morning.GetType().GetField("root", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(morning));
                }
                if (ui != null) foreach (string name in new[] { "root", "saveNoticeRoot" })
                {
                    var field = ui.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
                    Object.DestroyImmediate((GameObject)field.GetValue(ui)); field.SetValue(ui, null);
                }
                Object.DestroyImmediate(system); Object.DestroyImmediate(paper); Object.DestroyImmediate(navigation); Object.DestroyImmediate(picture); Object.DestroyImmediate(texture);
            }
        }
    }
}
#endif
