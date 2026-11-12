#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using Codetective.Operations;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Codetective.Terminal.Tests.UI
{
    public sealed class MonitorPageUiTests
    {
        private GameObject canvas;
        private GameObject root;
        private MonitorPageHost host;
        private TerminalController terminal;
        private TerminalInputField input;
        private KernelRuntime runtime;

        [SetUp]
        public void SetUp()
        {
            canvas = new GameObject("Monitor regression", typeof(RectTransform), typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/1_Core/Prefabs/TerminalPanel.prefab");
            Assert.That(prefab, Is.Not.Null);
            root = Object.Instantiate(prefab, canvas.transform);
            host = root.GetComponent<MonitorPageHost>(); terminal = root.GetComponent<TerminalController>();
            input = root.GetComponentInChildren<TerminalInputField>(true);
            runtime = ClinicScenario.Create(); terminal.AttachRuntime(runtime);
            terminal.SetInteractionEnabled(false); host.Initialize();
        }
        [TearDown] public void Cleanup() { if (canvas != null) Object.DestroyImmediate(canvas); }

        [Test]
        public void SwitchingPagesReusesTheirViewsAndKeepsTheCommandDraft()
        {
            Assert.That(host.CurrentPage.Id, Is.EqualTo("clinic"));
            Assert.That(terminal.PageVisible, Is.False);
            input.SetTextWithoutNotify("save 작성중인단서");
            host.ShowPage("kernel");
            MonitorPage kernel = host.CurrentPage;
            Assert.That(terminal.PageVisible, Is.True);
            host.ShowPage("clinic-records"); host.GoBack();
            Assert.That(host.CurrentPage, Is.SameAs(kernel));
            Assert.That(input.text, Is.EqualTo("save 작성중인단서"));
            Assert.That(root.GetComponentsInChildren<MonitorPage>(true).Count(p => p.gameObject.activeSelf), Is.EqualTo(1));
            Assert.That(terminal.isActiveAndEnabled, Is.True);
        }
        [Test]
        public void HiddenConsoleReceivesReportsAndRejectsPendingSubmissions()
        {
            Invoke(terminal, "Start");
            host.ShowPage("kernel"); input.SetTextWithoutNotify("save draft");
            host.ShowPage("clinic");
            terminal.SetInteractionEnabled(true);
            Assert.That(root.GetComponent<CanvasGroup>().interactable, Is.True);
            Invoke(terminal, "Submit", "save must_not_run");
            terminal.SetInteractionEnabled(false);
            runtime.Report("페이지 전환 중에도 수신되는 보고"); Invoke(terminal, "LateUpdate");
            Assert.That(runtime.Data.ContainsKey("must_not_run"), Is.False);
            host.ShowPage("kernel"); Invoke(terminal, "LateUpdate");
            Text transcript = Field<Text>(terminal, "transcript");
            Assert.That(transcript.text, Does.Contain("페이지 전환 중에도 수신되는 보고"));
            Assert.That(input.text, Is.EqualTo("save draft"));
        }
        [Test]
        public void BackgroundReportsPreserveAnOlderScrollPositionWhenReturningToTheConsole()
        {
            Invoke(terminal, "Start"); host.ShowPage("kernel");
            for (int i = 0; i < 45; i++) runtime.Report("보고 " + i + " / 이전 기록을 읽는 동안 새 보고가 도착합니다.");
            Invoke(terminal, "LateUpdate"); Canvas.ForceUpdateCanvases();
            ScrollRect scroll = Field<ScrollRect>(terminal, "transcriptScroll");
            Assert.That(scroll.content.rect.height, Is.GreaterThan(scroll.viewport.rect.height));
            scroll.verticalNormalizedPosition = 1;
            Vector2 readingPosition = scroll.content.anchoredPosition;
            host.ShowPage("clinic");
            runtime.Report("다른 페이지를 보는 중 수신한 새 보고"); Invoke(terminal, "LateUpdate");
            host.ShowPage("kernel"); Invoke(terminal, "LateUpdate");
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(readingPosition.y).Within(1));
        }
        [Test]
        public void ClinicPagesUseTheSameKnowledgeAndMemoryAsTheKernelConsole()
        {
            ClinicMonitorPage clinic = host.CurrentPage.GetComponent<ClinicMonitorPage>();
            Button clue = Field<Button>(clinic, "clue"); clue.onClick.Invoke();
            Assert.That(runtime.Facts, Does.Contain("basement_noise"));
            host.ShowPage("kernel");
            Assert.That(terminal.Runtime, Is.SameAs(runtime));
            Assert.That(runtime.Execute("record = archive.inspect()").Success, Is.True);
            host.ShowPage("clinic-records");
            Assert.That(Field<Text>(host.CurrentPage.GetComponent<ClinicMonitorPage>(), "body").text, Does.Contain("[기록됨]"));
        }
        [Test]
        public void AdditionalPageRootsAndLinksExtendTheMonitorWithoutReplacingItsCanvas()
        {
            var pageObject = new GameObject("Custom page", typeof(RectTransform)); pageObject.transform.SetParent(root.transform, false);
            MonitorPage page = pageObject.AddComponent<MonitorPage>(); SetField(page, "pageId", "custom"); SetField(page, "title", "추가 페이지");
            host.Register(page);
            var linkObject = new GameObject("Link", typeof(RectTransform), typeof(Image), typeof(Button)); linkObject.transform.SetParent(root.transform, false);
            MonitorPageLink link = linkObject.AddComponent<MonitorPageLink>(); SetField(link, "pageId", "custom");
            link.OpenPage();
            Assert.That(host.CurrentPage, Is.SameAs(page));
            host.GoBack(); Assert.That(host.CurrentPage.Id, Is.EqualTo("clinic"));
            Assert.That(canvas.GetComponentsInChildren<Canvas>(true).Length, Is.EqualTo(1));
        }
        private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        private static void SetField(object owner, string name, object value) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
        private static void Invoke(object owner, string name, params object[] arguments) => owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(owner, arguments);
    }
}
#endif
