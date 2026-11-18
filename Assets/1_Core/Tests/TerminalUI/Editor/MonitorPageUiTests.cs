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

        private SystemMenuMonitorView SystemMenu(SystemMenuActions actions)
        {
            var menu = new GameObject("SystemMenu", typeof(RectTransform)); menu.transform.SetParent(root.transform, false);
            var view = menu.AddComponent<SystemMenuMonitorView>(); view.Initialize(host, actions); terminal.SetInteractionEnabled(true); return view;
        }
        private static Button MenuButton(SystemMenuMonitorView view, string label) => view.GetComponentsInChildren<Button>().Single(b => b.GetComponentInChildren<Text>().text == label);
        [Test]
        public void TitleLocksNavigationAndKeepsTheCommandDraftOutsidePageHistory()
        {
            host.ShowPage("kernel"); input.SetTextWithoutNotify("save 작성중"); bool back = host.CanGoBack; int starts = 0;
            var menu = SystemMenu(new SystemMenuActions { NewGame = () => starts++, ReadSaves = () => Array.Empty<SystemSaveInfo>() });
            menu.Show(true);
            Assert.That(menu.GetComponentsInChildren<Button>().Select(b => b.GetComponentInChildren<Text>().text), Is.EqualTo(new[] { "이어하기", "처음부터", "설정", "끝내기" }));
            Assert.That(menu.GetComponentInParent<Canvas>(), Is.SameAs(canvas.GetComponent<Canvas>()));
            Assert.That(terminal.PageVisible, Is.False); Assert.That(host.TryNavigate("mail"), Is.False); Assert.That(host.TryCycleTab(1), Is.False);
            host.GoBack(); Assert.That(host.CurrentPage.Id, Is.EqualTo("kernel")); Assert.That(host.CanGoBack, Is.EqualTo(back));
            Assert.That(MenuButton(menu, "이어하기").interactable, Is.False); MenuButton(menu, "이어하기").onClick.Invoke(); Assert.That(MenuButton(menu, "처음부터"), Is.Not.Null);
            MenuButton(menu, "처음부터").onClick.Invoke(); Assert.That(starts, Is.EqualTo(1));
            MenuButton(menu, "설정").onClick.Invoke(); Assert.That(menu.HandleEscape(), Is.True); Assert.That(menu.HandleEscape(), Is.True);
            menu.Hide(); Assert.That(host.CurrentPage.Id, Is.EqualTo("kernel")); Assert.That(terminal.PageVisible, Is.True);
            Assert.That(input.text, Is.EqualTo("save 작성중")); Assert.That(input.isFocused, Is.False);
        }
        [Test]
        public void PauseProvidesTheSaveSlotGuideAndSettingsWithoutExposingOtherPages()
        {
            string loaded = null; int saves = 0, resumes = 0, titles = 0;
            var menu = SystemMenu(new SystemMenuActions { Load = id => loaded = id, Save = () => saves++, Resume = () => resumes++, Title = () => titles++,
                ReadSaves = () => new[] { new SystemSaveInfo { Id = "latest", Available = true, Label = "Auto · DAY 3 / 10:00", Description = "상담 중\n저장: 2026-11-18 10:00:00" },
                    new SystemSaveInfo { Id = "earlier", Available = true, Label = "수동 저장 · DAY 2 / 09:00", Description = "상담 대기\n저장: 2026-11-17 09:00:00" } } });
            menu.Show(false);
            Assert.That(menu.GetComponentsInChildren<Button>().Select(b => b.GetComponentInChildren<Text>().text),
                Is.EqualTo(new[] { "계속하기", "저장하기", "불러오기", "가이드", "설정", "타이틀 화면" }));
            MenuButton(menu, "저장하기").onClick.Invoke(); Assert.That(saves, Is.EqualTo(1));
            MenuButton(menu, "불러오기").onClick.Invoke(); Assert.That(menu.GetComponentsInChildren<Text>().Any(t => t.text.Contains("DAY 3 / 10:00")), Is.True);
            menu.SelectSave("earlier"); MenuButton(menu, "이 기록 불러오기").onClick.Invoke(); Assert.That(loaded, Is.EqualTo("earlier")); Assert.That(menu.HandleEscape(), Is.True);
            MenuButton(menu, "가이드").onClick.Invoke(); Assert.That(menu.GetComponentsInChildren<Text>().Any(t => t.text.Contains("Q 전체 보기")), Is.True);
            Assert.That(menu.HandleEscape(), Is.True); Assert.That(menu.HandleEscape(), Is.False);
            MenuButton(menu, "계속하기").onClick.Invoke(); Assert.That(resumes, Is.EqualTo(1));
            Button title = MenuButton(menu, "타이틀 화면"); menu.Hide(); title.onClick.Invoke(); Assert.That(titles, Is.Zero);
        }
        [Test]
        public void ContinueOpensTenScrollableRecordsAndOnlyLoadsTheChosenAvailableId()
        {
            string loaded = null;
            var slots = Enumerable.Range(0, 10).Select(i => new SystemSaveInfo { Id = "save" + i, Available = i != 2,
                Label = (i % 2 == 0 ? "Auto" : "수동 저장") + " · DAY " + (10 - i), Description = "상담 중\n저장: 실제 시각 " + i }).ToArray();
            var menu = SystemMenu(new SystemMenuActions { Load = id => loaded = id, ReadSaves = () => slots }); menu.Show(true);
            MenuButton(menu, "이어하기").onClick.Invoke(); Assert.That(loaded, Is.Null);
            ScrollRect scroll = menu.GetComponentInChildren<CrtScrollRect>();
            Assert.That(scroll.vertical, Is.True); Assert.That(scroll.content.rect.height, Is.GreaterThan(scroll.viewport.rect.height));
            Assert.That(scroll.content.GetComponentsInChildren<Button>().Length, Is.EqualTo(10));
            slots = new[] { new SystemSaveInfo { Id = "quick", Available = true, Label = "빠른 저장", Description = "새 기록\n저장: 방금" } }.Concat(slots.Take(9)).ToArray();
            menu.RefreshSaveList(); Assert.That(menu.GetComponentsInChildren<Text>().Any(t => t.text.Contains("빠른 저장")), Is.True);
            menu.SelectSave("save8"); menu.SelectSave("save2"); MenuButton(menu, "이 기록 불러오기").onClick.Invoke();
            Assert.That(loaded, Is.EqualTo("save8")); Assert.That(menu.HandleEscape(), Is.True); Assert.That(menu.IsTitle, Is.True);
        }
        [Test]
        public void HistoryEnvelopesPreservePlayableSnapshotsThroughUnityJsonSerialization()
        {
            string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "codetective-unity-saves-" + Guid.NewGuid().ToString("N"));
            try
            {
                var history = new SaveHistory(directory, record => JsonUtility.ToJson(record), json => JsonUtility.FromJson<GameSaveRecord>(json));
                runtime.Store("record", Datum.Ref(DataKind.Document, "case_record", "사건 기록", "인용: \"단서\"\n두 번째 줄"));
                GameSaveSlot older = history.Save(JsonUtility.ToJson(runtime.Capture()), GameSaveKind.Manual, runtime.Day, runtime.Minute, "상담 중");
                runtime.Free("record"); history.Save(JsonUtility.ToJson(runtime.Capture()), GameSaveKind.Quick, runtime.Day, runtime.Minute, "정리 중");
                var restored = ClinicScenario.Create(); restored.Restore(JsonUtility.FromJson<KernelSnapshot>(history.Read(older.Id).snapshot));
                Assert.That(restored.Data["record"].value.id, Is.EqualTo("case_record")); Assert.That(restored.Evidence.Single().Description, Does.Contain("\"단서\"\n두 번째 줄"));
                Assert.That(history.Slots.Count, Is.EqualTo(2)); Assert.That(history.Slots[0].Record.kind, Is.EqualTo(GameSaveKind.Quick));
            }
            finally { if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true); }
        }
        [Test]
        public void ScheduleHostsWaitingAndFinishingAndBlocksActionsOutsideTodaysActivePage()
        {
            runtime.RegisterClinicDay(new ClinicDayPlan { day = 1, appointments = new[] { new PatientAppointment { id = "visit", profileId = "client", patientName = "상담자", startMinute = 600,
                dialogue = new[] { new ConsultationLine { sentence = "상담 내용" } } } } }); runtime.InitializeClinicDay();
            bool allowed = true; host.ClinicActionsAllowed = () => allowed;
            host.WaitForAppointment = runtime.WaitForNextAppointment; host.FinishConsultation = runtime.FinishConsultation;
            terminal.SetInteractionEnabled(true); host.ShowPage("schedule");
            ScheduleMonitorPage schedule = host.CurrentPage.GetComponent<ScheduleMonitorPage>();
            Button wait = Field<Button>(schedule, "wait"), finish = Field<Button>(schedule, "finish");
            Assert.That(wait.interactable, Is.True); Assert.That(finish.interactable, Is.False);
            allowed = false; wait.onClick.Invoke(); Assert.That(runtime.CurrentAppointment, Is.Null);
            allowed = true; schedule.ShowDay(2); wait.onClick.Invoke(); Assert.That(runtime.CurrentAppointment, Is.Null);
            schedule.ShowDay(1); host.ShowHome(); wait.onClick.Invoke(); Assert.That(runtime.CurrentAppointment, Is.Null);
            host.ShowPage("schedule"); wait.onClick.Invoke(); Assert.That(runtime.CurrentAppointment.phase, Is.EqualTo(VisitPhase.Arriving));
            var visit = runtime.CurrentAppointment; runtime.AdvanceVisitPresentation(visit.id, visit.phase); Invoke(schedule, "Refresh");
            Assert.That(finish.interactable, Is.True); finish.onClick.Invoke(); Assert.That(runtime.CurrentAppointment.phase, Is.EqualTo(VisitPhase.Closing));
        }

        [Test]
        public void KernelEvidenceBrowserKeepsTheCommandDraftAndReconnectsFreedReferences()
        {
            terminal.SetInteractionEnabled(true); runtime.Store("record", Datum.Ref(DataKind.Document, "case_record", "사건 기록", "조사에서 얻은 상세 기록")); runtime.Free("record");
            host.ShowPage("kernel"); input.SetTextWithoutNotify("save 작성중");
            host.CurrentPage.GetComponentsInChildren<Button>().Single(b => b.GetComponentInChildren<Text>()?.text == "확보 자료").onClick.Invoke();
            Assert.That(host.CurrentPage.Id, Is.EqualTo("evidence")); Assert.That(host.CurrentMenuId, Is.EqualTo("kernel"));
            Assert.That(host.NavigationPages.Count, Is.EqualTo(4)); Assert.That(terminal.PageVisible, Is.False);
            EvidenceMonitorPage page = host.CurrentPage.GetComponent<EvidenceMonitorPage>(); page.SelectEvidence("case_record");
            Assert.That(Field<Text>(page, "detail").text, Does.Contain("사건 기록").And.Contain("조사에서 얻은 상세 기록"));
            Field<MonitorNoteInput>(page, "referenceName").text = "caseFile"; Invoke(page, "BindSelected");
            Assert.That(runtime.Data["caseFile"].value.id, Is.EqualTo("case_record"));
            Assert.That(runtime.Evidence.Count, Is.EqualTo(1)); host.ShowPage("kernel");
            Assert.That(input.text, Is.EqualTo("save 작성중")); Assert.That(input.isFocused, Is.False);
        }
        [Test]
        public void EvidenceFiltersSearchAndAccessChangesRemoveStaleDetails()
        {
            runtime.Store("key", Datum.Ref(DataKind.Item, "security_key", "보안 열쇠", "문을 여는 도구"));
            runtime.Store("record", Datum.Ref(DataKind.Document, "security_record", "문서", "security 연구 기록"));
            terminal.SetInteractionEnabled(true); host.ShowPage("evidence"); EvidenceMonitorPage page = host.CurrentPage.GetComponent<EvidenceMonitorPage>();
            Invoke(page, "ChangeFilter", 1);
            Assert.That(Field<RectTransform>(page, "rows").GetComponentsInChildren<Button>().Length, Is.EqualTo(1));
            Assert.That(Field<Text>(page, "detail").text, Does.Contain("보안 열쇠"));
            Invoke(page, "ChangeFilter", 0); Field<MonitorNoteInput>(page, "search").text = "security";
            Assert.That(Field<RectTransform>(page, "rows").GetComponentsInChildren<Button>().Length, Is.EqualTo(1));
            Assert.That(Field<Text>(page, "detail").text, Does.Contain("security 연구 기록"));
            runtime.Entity("security_record").SetEntityAccess(PlayerAccess.Internal); Invoke(page, "Refresh");
            Assert.That(Field<RectTransform>(page, "rows").GetComponentsInChildren<Button>(), Is.Empty);
            Assert.That(Field<Text>(page, "detail").text, Does.Not.Contain("security 연구 기록"));
            Assert.That(Field<Button>(page, "bind").interactable, Is.False);
            runtime.Entity("security_record").SetEntityAccess(PlayerAccess.Public); Invoke(page, "Refresh");
            Assert.That(Field<Text>(page, "detail").text, Does.Contain("security 연구 기록"));
        }
        [Test]
        public void HiddenOrInactiveEvidencePagesCannotCreateReferences()
        {
            runtime.CollectEvidence(Datum.Ref(DataKind.Document, "case_record", "사건 기록", "설명")); host.ShowPage("evidence");
            EvidenceMonitorPage page = host.CurrentPage.GetComponent<EvidenceMonitorPage>();
            Field<MonitorNoteInput>(page, "referenceName").text = "fromUi"; Invoke(page, "BindSelected"); Assert.That(runtime.Data, Is.Empty);
            terminal.SetInteractionEnabled(true); host.ShowPage("kernel"); Invoke(page, "BindSelected"); Assert.That(runtime.Data, Is.Empty);
            host.ShowPage("evidence"); Invoke(page, "BindSelected"); Assert.That(runtime.Data["fromUi"].value.id, Is.EqualTo("case_record"));
        }
        [Test]
        public void ClientAndKernelUseTheSamePortraitAndSixFieldForm()
        {
            host.ShowPage("clinic"); ClinicMonitorPage clinic = host.CurrentPage.GetComponent<ClinicMonitorPage>();
            ProfileFormView form = host.CurrentPage.GetComponentInChildren<ProfileFormView>();
            RectTransform portrait = (RectTransform)form.transform.Find("Portrait35x45");
            Assert.That(portrait.rect.width / portrait.rect.height, Is.EqualTo(3.5f / 4.5f).Within(0.001f));
            string[] labels = { "이름: ", "성별: ", "나이: ", "생일: ", "직업: ", "가족관계: " };
            foreach (string label in labels)
            {
                Text field = form.GetComponentsInChildren<Text>(true).Single(t => t.text.StartsWith(label, StringComparison.Ordinal));
                Assert.That(field.rectTransform.anchoredPosition.x, Is.GreaterThan(portrait.rect.width));
            }
            host.SelectedProfileId = "kernel"; Invoke(clinic, "Refresh");
            Assert.That(host.CurrentPage.GetComponentInChildren<ProfileFormView>(), Is.SameAs(form));
            string[] values = form.GetComponentsInChildren<Text>(true).Select(t => t.text).ToArray();
            Assert.That(values, Does.Contain("이름: [데이터 말소됨]")); Assert.That(values, Does.Contain("나이: [데이터 말소됨]"));
            Assert.That(values, Does.Contain("직업: 심리상담가"));
        }
        [Test]
        public void PortraitsFollowStoryUnlocksAndHiddenPeopleDoNotExposePhotos()
        {
            var texture = new Texture2D(4, 4); Sprite photo = Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.zero);
            try
            {
                SetField(host, "profilePortraits", new[] { new ProfilePortrait { profileId = "client", photo = photo,
                    availability = new StoryCondition { triggersOn = new[] { "portrait.known" } } }, new ProfilePortrait { profileId = "thread01", photo = photo } });
                Assert.That(host.PortraitFor("client"), Is.Null); Assert.That(host.PortraitFor("thread01"), Is.Null);
                runtime.SetTrigger("portrait.known", true); Assert.That(host.PortraitFor("client"), Is.SameAs(photo));
                host.ShowPage("clinic"); ClinicMonitorPage clinic = host.CurrentPage.GetComponent<ClinicMonitorPage>();
                Assert.That(host.CurrentPage.GetComponentInChildren<ProfileFormView>().GetComponentsInChildren<Image>(true).Any(i => i.sprite == photo), Is.True);
                runtime.Entity("client").SetEntityAccess(PlayerAccess.Internal); Assert.That(host.PortraitFor("client"), Is.Null);
                Invoke(clinic, "Refresh");
                Assert.That(host.CurrentPage.GetComponentInChildren<ProfileFormView>().GetComponentsInChildren<Image>(true).Any(i => i.sprite == photo), Is.False);
            }
            finally { Object.DestroyImmediate(photo); Object.DestroyImmediate(texture); }
        }
        [Test]
        public void JournalPagesRetainSeparateDraftsAndEditTheirOwnSessionTime()
        {
            host.ShowPage("clinic-records"); ClinicMonitorPage records = host.CurrentPage.GetComponent<ClinicMonitorPage>();
            MonitorNoteInput input = Field<MonitorNoteInput>(records, "noteInput");
            input.text = "첫 번째 상담 내용"; Field<MonitorNoteInput>(records, "startInput").text = "09:30";
            Field<MonitorNoteInput>(records, "endInput").text = "10:00"; Invoke(records, "SaveNote");
            Invoke(records, "NewPage"); input.text = "두 번째 상담 내용";
            Field<MonitorNoteInput>(records, "startInput").text = "10:30"; Field<MonitorNoteInput>(records, "endInput").text = "11:00"; Invoke(records, "SaveNote");
            Assert.That(runtime.NotesFor("client").Count(), Is.EqualTo(2));
            Invoke(records, "ChangePage", -1); Assert.That(input.text, Is.EqualTo("첫 번째 상담 내용")); input.text = "수정 중인 첫 페이지";
            runtime.SetTrigger("background.report", true); Invoke(records, "Refresh"); Assert.That(input.text, Is.EqualTo("수정 중인 첫 페이지"));
            Invoke(records, "ChangePage", 1); Assert.That(input.text, Is.EqualTo("두 번째 상담 내용"));
            Invoke(records, "ChangePage", -1); Assert.That(input.text, Is.EqualTo("수정 중인 첫 페이지")); Invoke(records, "SaveNote");
            Assert.That(runtime.NotesFor("client").Count(), Is.EqualTo(2)); Assert.That(runtime.NotesFor("client").First().startMinute, Is.EqualTo(570));
            host.SelectedProfileId = "kernel"; Invoke(records, "Refresh"); Assert.That(input.text, Is.Empty);
            input.text = "Kernel 자신의 기록"; Invoke(records, "SaveNote");
            host.SelectedProfileId = "client"; Invoke(records, "Refresh"); Assert.That(input.text, Is.EqualTo("수정 중인 첫 페이지"));
            Assert.That(runtime.NotesFor("kernel").Single().text, Is.EqualTo("Kernel 자신의 기록"));
        }
        [Test]
        public void UntouchedNewPagesUseTodaysDateWithoutOverwritingAnEditedDraft()
        {
            host.ShowPage("clinic-records"); ClinicMonitorPage records = host.CurrentPage.GetComponent<ClinicMonitorPage>();
            runtime.Tick(540); runtime.NextDay(); Invoke(records, "Refresh");
            Assert.That(Field<MonitorNoteInput>(records, "dayInput").text, Is.EqualTo("2"));
            Field<MonitorNoteInput>(records, "noteInput").text = "작성 중인 상담 내용";
            Field<MonitorNoteInput>(records, "startInput").text = "09:15"; runtime.Tick(15); Invoke(records, "Refresh");
            Assert.That(Field<MonitorNoteInput>(records, "noteInput").text, Is.EqualTo("작성 중인 상담 내용"));
            Assert.That(Field<MonitorNoteInput>(records, "startInput").text, Is.EqualTo("09:15"));
        }
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
