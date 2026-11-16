#if UNITY_EDITOR
using System;
using System.Collections.Generic;
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
        private static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        private static object Call(object target, string name, params object[] args) => target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(target, args);

        [Test]
        public void CounselAndGramophoneKeepTheTerminalInteractiveAndEscapeKeepsTheAngle()
        {
            var root = new GameObject("Navigation", typeof(Camera), typeof(CameraAngleController), FindType("Codetective.Interaction.PauseMenuView")); root.SetActive(false);
            var canvas = new GameObject("Monitor", typeof(RectTransform), typeof(Canvas));
            var set = ScriptableObject.CreateInstance<CameraAngleSet>();
            var presets = new List<CameraAnglePreset>(); float oldTime = Time.timeScale;
            var hotspot = new GameObject("MonitorHotspot");
            try
            {
                foreach (string id in new[] { "overview", "monitor", "counsel", "profile", "drawer", "gramophone" })
                { var angle = ScriptableObject.CreateInstance<CameraAnglePreset>(); Set(angle, "angleId", id); presets.Add(angle); }
                Set(set, "angles", presets); CameraAngleController views = root.GetComponent<CameraAngleController>(); Set(views, "angleSet", set);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/1_Core/Prefabs/TerminalPanel.prefab");
                GameObject monitor = Object.Instantiate(prefab, canvas.transform); monitor.GetComponent<MonitorPageHost>().ShowPage("kernel");
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
                foreach (int index in new[] { 2, 5 })
                {
                    Call(inspection, "InspectAngle", presets[index]); Call(inspection, "LateUpdate");
                    Assert.That(terminal.InteractionEnabled, Is.True);
                    Call(inspection, "InspectTarget", target);
                    Assert.That(views.ActiveAngle, Is.SameAs(presets[index]), "Clicking the monitor must keep the working angle.");
                    Assert.That(terminal.InteractionEnabled, Is.True);
                    Call(inspection, "HandleEscape");
                    Assert.That(views.ActiveAngle, Is.SameAs(presets[index])); Assert.That(terminal.InteractionEnabled, Is.False);
                    Call(inspection, "Resume"); Call(inspection, "LateUpdate");
                    Assert.That(views.ActiveAngle, Is.SameAs(presets[index])); Assert.That(terminal.InteractionEnabled, Is.True);
                }
                Assert.That(Call(inspection, "TryInspectNumber", 4), Is.True); Assert.That(views.ActiveAngle.Id, Is.EqualTo("drawer"));
                Call(inspection, "InspectTarget", target); Assert.That(views.ActiveAngle.Id, Is.EqualTo("monitor"));
                Call(inspection, "ReturnToOverview"); Assert.That(views.ActiveAngle.Id, Is.EqualTo("overview"));
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(canvas); Object.DestroyImmediate(hotspot); Object.DestroyImmediate(set); foreach (var angle in presets) Object.DestroyImmediate(angle); Time.timeScale = oldTime; }
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
        public void PrintedPaperIsFlatOnTheDeskAndRollsOnlyInsideADrawer()
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
                carry.Register(item); Assert.That(carry.TryPickup(item), Is.True);
                Canvas ink = paper.GetComponentInChildren<Canvas>(); Assert.That(ink.renderMode, Is.EqualTo(RenderMode.WorldSpace));
                Assert.That(paper.GetComponentsInChildren<Text>().Any(t => t.text.Contains("종이에 인쇄된 본문")), Is.True);
                ItemPlacementSurface surface = drawer.AddComponent<ItemPlacementSurface>(); surface.Initialize("desk.top", null, new Bounds(Vector3.zero, Vector3.one));
                Assert.That(carry.BeginPlacement(), Is.True); Assert.That(carry.TryPlaceOn(surface), Is.True);
                Assert.That(ink.gameObject.activeSelf, Is.True); Assert.That(item.IsRolled, Is.False);
                carry.TryPickup(item);
                var runtime = new KernelRuntime(); runtime.SetDrawerOpen("drawer", true);
                SlidingDrawer owner = drawer.AddComponent<SlidingDrawer>(); owner.Initialize(runtime, "drawer"); owner.SetInteractionEnabled(true);
                surface.Initialize("drawer", owner, new Bounds(Vector3.zero, Vector3.one));
                Assert.That(carry.BeginPlacement(), Is.True); Assert.That(carry.TryPlaceOn(surface), Is.True);
                Assert.That(ink.gameObject.activeSelf, Is.False); Assert.That(paper.transform.Find("RolledNewspaper").gameObject.activeSelf, Is.True);
                Assert.That(carry.TryPickup(item), Is.True); Assert.That(ink.gameObject.activeSelf, Is.True);
            }
            finally { Object.DestroyImmediate(system); Object.DestroyImmediate(paper); Object.DestroyImmediate(drawer); Object.DestroyImmediate(camera); }
        }

        [Test]
        public void PlacementSurvivesAngleChangesAndFrontClicksStillToggleTheDrawer()
        {
            var navigation = new GameObject("Navigation", typeof(Camera), typeof(CameraAngleController), FindType("Codetective.Interaction.PauseMenuView")); navigation.SetActive(false);
            var system = new GameObject("Carry"); var itemRoot = new GameObject("Item"); var drawerRoot = new GameObject("Drawer");
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
                Call(inspection, "TryInspectNumber", 4);
                var runtime = new KernelRuntime(); drawerRoot.transform.position = new Vector3(0, 0, 2);
                SlidingDrawer drawer = drawerRoot.AddComponent<SlidingDrawer>(); drawer.Initialize(runtime, "drawer"); drawer.SetInteractionEnabled(true);
                var face = new GameObject("Front"); face.transform.SetParent(drawerRoot.transform, false);
                face.AddComponent<DrawerFrontTarget>().Initialize(drawer, new Bounds(Vector3.zero, new Vector3(0.6f, 0.3f, 0.025f)));
                Physics.SyncTransforms();
                Vector2 point = navigation.GetComponent<Camera>().WorldToScreenPoint(face.transform.position);
                Assert.That(Call(inspection, "InteractAtScreenPoint", point), Is.True); Assert.That(drawer.IsOpen, Is.True);
                Assert.That(Call(inspection, "InteractAtScreenPoint", point), Is.True); Assert.That(drawer.IsOpen, Is.False);
                Assert.That(carry.IsPlacing, Is.True); Assert.That(carry.HeldItem, Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(system); Object.DestroyImmediate(itemRoot); Object.DestroyImmediate(drawerRoot); Object.DestroyImmediate(navigation);
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
                if (ui != null) foreach (string name in new[] { "root", "dateRoot" })
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
