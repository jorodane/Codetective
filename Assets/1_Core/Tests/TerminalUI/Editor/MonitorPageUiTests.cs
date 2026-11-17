#if UNITY_EDITOR
using System;
using System.Collections.Generic;
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
        public void HomeAndHeaderShareTheFourMenusAndEachCardOpensItsDestination()
        {
            Assert.That(host.CurrentPage.Id, Is.EqualTo("home")); Assert.That(terminal.PageVisible, Is.False);
            Assert.That(input.isFocused, Is.False);
            string[] names = { "커널", "상담소", "메일", "스케쥴" };
            Assert.That(host.NavigationPages.Select(p => p.Title), Is.EqualTo(names));
            var tabs = Field<Dictionary<string, Button>>(host, "tabs");
            Assert.That(tabs.Values.Select(b => b.GetComponentInChildren<Text>().text), Is.EqualTo(names));
            MonitorPage home = host.CurrentPage; Button[] cards = home.GetComponentsInChildren<Button>(true);
            Assert.That(cards.Select(b => b.GetComponentInChildren<Text>().text), Is.EqualTo(names));
            for (int i = 0; i < cards.Length; i++)
            {
                cards[i].onClick.Invoke(); Assert.That(host.CurrentPage.Id, Is.EqualTo(host.NavigationPages[i].Id));
                Assert.That(terminal.PageVisible, Is.EqualTo(i == 0)); Assert.That(input.isFocused, Is.False);
                Field<Button>(host, "home").onClick.Invoke(); Assert.That(host.CurrentPage, Is.SameAs(home));
            }
        }
        [Test]
        public void ClinicRecordsUseTheClinicMenuForHighlightAndKeyboardCycling()
        {
            WithEventSystem(events =>
            {
                terminal.SetInteractionEnabled(true); host.ShowPage("clinic-records");
                Assert.That(host.CurrentPage.ShowInNavigation, Is.False); Assert.That(host.CurrentMenuId, Is.EqualTo("clinic"));
                var tabs = Field<Dictionary<string, Button>>(host, "tabs");
                Assert.That(tabs["clinic"].image.color, Is.Not.EqualTo(tabs["kernel"].image.color));
                Assert.That(host.TryCycleTab(1), Is.True); Assert.That(host.CurrentPage.Id, Is.EqualTo("mail"));
                host.GoBack(); Assert.That(host.CurrentPage.Id, Is.EqualTo("clinic-records"));
                Assert.That(host.TryCycleTab(-1), Is.True); Assert.That(host.CurrentPage.Id, Is.EqualTo("kernel"));
                Assert.That(input.isFocused, Is.False);
            });
        }
        [Test]
        public void KernelContainsTheOperationsLinkWithoutAddingAFifthHeaderMenu()
        {
            var gameRoot = new GameObject("Operation tools host"); gameRoot.SetActive(false);
            try
            {
                Type gameType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Codetective.Gameplay.KernelGameController")).First(t => t != null);
                Component game = gameRoot.AddComponent(gameType); SetField(game, "<Runtime>k__BackingField", runtime); SetField(game, "terminalRoot", root.transform);
                Component ui = gameRoot.GetComponents<Component>().Single(c => c != null && c.GetType().FullName == "Codetective.Gameplay.KernelGameUI");
                SetField(ui, "game", game); SetField(ui, "font", Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")); Invoke(ui, "BuildOperationsPage");
                Assert.That(host.NavigationPages.Count, Is.EqualTo(4));
                host.ShowPage("kernel"); input.SetTextWithoutNotify("save draft");
                host.CurrentPage.GetComponentsInChildren<Button>().Single(b => b.GetComponentInChildren<Text>()?.text == "작전 도구").onClick.Invoke();
                Assert.That(host.CurrentPage.Id, Is.EqualTo("operations")); Assert.That(host.CurrentMenuId, Is.EqualTo("kernel"));
                Assert.That(host.CurrentPage.ShowInNavigation, Is.False);
                host.CurrentPage.GetComponentsInChildren<Button>().Single(b => b.GetComponentInChildren<Text>()?.text == "명령창으로").onClick.Invoke();
                Assert.That(host.CurrentPage.Id, Is.EqualTo("kernel")); Assert.That(input.text, Is.EqualTo("save draft"));
            }
            finally { Object.DestroyImmediate(gameRoot); }
        }
        [Test]
        public void SwitchingPagesReusesTheirViewsAndKeepsTheCommandDraft()
        {
            Assert.That(host.CurrentPage.Id, Is.EqualTo("home"));
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
        public void EnteringStartingAndReenablingTheConsoleNeverSelectsItsInput()
        {
            WithEventSystem(events =>
            {
                host.ShowPage("kernel"); terminal.SetInteractionEnabled(true);
                Assert.That(events.currentSelectedGameObject, Is.Not.SameAs(input.gameObject));
                Assert.That(input.isFocused, Is.False);
                input.SetTextWithoutNotify("save draft"); Invoke(terminal, "Start");
                Assert.That(events.currentSelectedGameObject, Is.Not.SameAs(input.gameObject));
                host.ShowPage("clinic"); host.ShowPage("kernel");
                Assert.That(events.currentSelectedGameObject, Is.Not.SameAs(input.gameObject));
                terminal.SetInteractionEnabled(false); terminal.SetInteractionEnabled(true);
                Assert.That(events.currentSelectedGameObject, Is.Not.SameAs(input.gameObject));
                Assert.That(input.text, Is.EqualTo("save draft"));
            });
        }
        [Test]
        public void KeyboardPageCyclingWrapsVisibleTabsWithoutFocusingTheConsole()
        {
            WithEventSystem(events =>
            {
                Assert.That(host.TryCycleTab(1), Is.False); // Disabled monitor.
                terminal.SetInteractionEnabled(true);
                string[] order = root.GetComponentsInChildren<MonitorPage>(true).Where(p => p.ShowInNavigation).Select(p => p.Id).ToArray();
                host.ShowPage(order.Last()); Assert.That(host.TryCycleTab(1), Is.True);
                Assert.That(host.CurrentPage.Id, Is.EqualTo(order.First()));
                Assert.That(host.TryCycleTab(-1), Is.True); Assert.That(host.CurrentPage.Id, Is.EqualTo(order.Last()));
                Assert.That(input.isFocused, Is.False);
                Assert.That(events.currentSelectedGameObject, Is.Not.SameAs(input.gameObject));
            });
        }
        [Test]
        public void SelectedCommandAndNoteInputsKeepTabForEditing()
        {
            WithEventSystem(events =>
            {
                terminal.SetInteractionEnabled(true); host.ShowPage("kernel");
                events.SetSelectedGameObject(input.gameObject);
                Assert.That(host.TryCycleTab(1), Is.False); Assert.That(host.CurrentPage.Id, Is.EqualTo("kernel"));
                Assert.That(events.currentSelectedGameObject, Is.SameAs(input.gameObject));
                host.ShowPage("clinic-records"); InputField note = host.CurrentPage.GetComponentInChildren<InputField>(true);
                events.SetSelectedGameObject(note.gameObject);
                Assert.That(host.TryCycleTab(-1), Is.False); Assert.That(host.CurrentPage.Id, Is.EqualTo("clinic-records"));
                Assert.That(events.currentSelectedGameObject, Is.SameAs(note.gameObject));
            });
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
            host.ShowPage("clinic");
            ClinicMonitorPage clinic = host.CurrentPage.GetComponent<ClinicMonitorPage>();
            Assert.That(Field<Text>(clinic, "body").text, Does.Not.Contain(runtime.Consultation[0].sentence));
            runtime.RecordClue(0);
            Assert.That(runtime.Facts, Does.Contain("basement_noise"));
            host.ShowPage("kernel");
            Assert.That(terminal.Runtime, Is.SameAs(runtime));
            Assert.That(runtime.Execute("record = archive.inspect()").Success, Is.True);
            host.ShowPage("clinic-records");
            ClinicMonitorPage records = host.CurrentPage.GetComponent<ClinicMonitorPage>();
            Field<MonitorNoteInput>(records, "noteInput").text = "내가 직접 작성한 상담 기록";
            Invoke(records, "SaveNote");
            Assert.That(Field<Text>(records, "body").text, Does.Contain("내가 직접 작성한 상담 기록"));
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
            host.GoBack(); Assert.That(host.CurrentPage.Id, Is.EqualTo("home"));
            Assert.That(canvas.GetComponentsInChildren<Canvas>(true).Length, Is.EqualTo(1));
        }
        [Test]
        public void InquiryApprovalNavigatesToItsDateAndBackToTheOriginalMessage()
        {
            runtime.RegisterMail(new StoryMail { id = "booking", mailbox = Mailbox.Clinic, subject = "예약 문의", reservation = new ReservationRequest { day = 2,
                appointment = new PatientAppointment { id = "visit", startMinute = 660, endMinute = 720 } }, choices = new[] {
                new MailChoice { id = "yes", label = "예약 승인", result = MailChoiceResult.Approved, approvesReservation = true },
                new MailChoice { id = "no", label = "예약 거절", result = MailChoiceResult.Declined } } });
            terminal.SetInteractionEnabled(true); host.ShowPage("mail"); MailMonitorPage mail = host.CurrentPage.GetComponent<MailMonitorPage>();
            mail.ShowMessage("booking"); Invoke(mail, "Choose", "yes"); Invoke(mail, "OpenSchedule");
            Assert.That(host.CurrentPage.Id, Is.EqualTo("schedule")); Assert.That(host.SelectedScheduleDay, Is.EqualTo(2));
            ScheduleMonitorPage schedule = host.CurrentPage.GetComponent<ScheduleMonitorPage>();
            Assert.That(Field<Text>(schedule, "detail").text, Does.Contain("11:00")); Assert.That(Field<Text>(schedule, "detail").text, Does.Contain("메일 승인"));
            Invoke(schedule, "OpenSourceMail"); Assert.That(host.CurrentPage.Id, Is.EqualTo("mail"));
            Assert.That(Field<Text>(mail, "detail").text, Does.Contain("답변: 예약 승인")); Assert.That(runtime.ScheduleForDay(2).Count, Is.EqualTo(1));
        }
        [Test]
        public void AHiddenOrInactiveMonitorCannotSubmitAMailChoice()
        {
            runtime.RegisterMail(new StoryMail { id = "choice", choices = new[] { new MailChoice { id = "answer", label = "답변" } } });
            MailMonitorPage mail = root.GetComponentInChildren<MailMonitorPage>(true); mail.ShowMessage("choice");
            Assert.That(runtime.IsMailRead("choice"), Is.False); runtime.ReadMail("choice"); Invoke(mail, "Choose", "answer");
            Assert.That(runtime.SelectedMailChoice("choice"), Is.Null.Or.Empty);
            host.ShowPage("kernel"); terminal.SetInteractionEnabled(true); Invoke(mail, "Choose", "answer");
            Assert.That(runtime.SelectedMailChoice("choice"), Is.Null.Or.Empty);
            host.ShowPage("mail"); Invoke(mail, "Choose", "answer"); Assert.That(runtime.SelectedMailChoice("choice"), Is.EqualTo("answer"));
        }
        private static void WithEventSystem(Action<EventSystem> check)
        {
            EventSystem previous = EventSystem.current; var root = new GameObject("Page navigation events", typeof(EventSystem));
            try { EventSystem.current = root.GetComponent<EventSystem>(); check(EventSystem.current); }
            finally { Object.DestroyImmediate(root); EventSystem.current = previous; }
        }
        private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        private static void SetField(object owner, string name, object value) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
        private static void Invoke(object owner, string name, params object[] arguments) => owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(owner, arguments);
    }
}
#endif
