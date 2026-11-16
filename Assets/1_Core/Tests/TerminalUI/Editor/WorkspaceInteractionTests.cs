#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Codetective.CameraViews;
using Codetective.Interaction;
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
                foreach (int index in new[] { 2, 5 })
                {
                    Call(inspection, "InspectAngle", presets[index]); Call(inspection, "LateUpdate");
                    Assert.That(terminal.InteractionEnabled, Is.True);
                    Call(inspection, "HandleEscape");
                    Assert.That(views.ActiveAngle, Is.SameAs(presets[index])); Assert.That(terminal.InteractionEnabled, Is.False);
                    Call(inspection, "Resume"); Call(inspection, "LateUpdate");
                    Assert.That(views.ActiveAngle, Is.SameAs(presets[index])); Assert.That(terminal.InteractionEnabled, Is.True);
                }
                Assert.That(Call(inspection, "TryInspectNumber", 4), Is.True); Assert.That(views.ActiveAngle.Id, Is.EqualTo("drawer"));
                Call(inspection, "ReturnToOverview"); Assert.That(views.ActiveAngle.Id, Is.EqualTo("overview"));
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(canvas); Object.DestroyImmediate(set); foreach (var angle in presets) Object.DestroyImmediate(angle); Time.timeScale = oldTime; }
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
        public void PrintedPaperIsReadableWhenHeldAndRollsOnlyWhenStored()
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
                ItemPlacementSurface surface = drawer.AddComponent<ItemPlacementSurface>(); surface.Initialize("drawer", null, new Bounds(Vector3.zero, Vector3.one));
                Assert.That(carry.BeginPlacement(), Is.True); Assert.That(carry.TryPlaceOn(surface), Is.True);
                Assert.That(ink.gameObject.activeSelf, Is.False); Assert.That(paper.transform.Find("RolledNewspaper").gameObject.activeSelf, Is.True);
                Assert.That(carry.TryPickup(item), Is.True); Assert.That(ink.gameObject.activeSelf, Is.True);
            }
            finally { Object.DestroyImmediate(system); Object.DestroyImmediate(paper); Object.DestroyImmediate(drawer); Object.DestroyImmediate(camera); }
        }
    }
}
#endif
