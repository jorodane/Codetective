#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Codetective.Operations;
using Codetective.Gameplay;
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
        public void TheOpeningRequestAndConnectedCallShareTheSameContactPanel()
        {
            var scenario = AssetDatabase.LoadAssetAtPath<KernelScenarioDefinition>("Assets/1_Core/Operations/ClinicScenario.asset");
            Assert.That(scenario, Is.Not.Null); KernelRuntime r = scenario.CreateRuntime(); terminal.AttachRuntime(r); terminal.SetInteractionEnabled(true);
            var tab = Field<Dictionary<string, Button>>(host, "tabs")["kernel"];
            Assert.That(tab.GetComponentInChildren<Text>().text, Does.EndWith(" (1)"));
            host.ShowPage("kernel");
            Assert.That(r.IsMailRead("government-intro"), Is.False);
            Assert.That(r.TerminalLineInProgress, Is.False); Assert.That(input.interactable, Is.False);
            Transform page = host.CurrentPage.transform;
            var commandField = (RectTransform)input.transform;
            var contact = (RectTransform)page.Find("TerminalContact");
            var status = contact.Find("ContactStatus").GetComponent<Text>();
            var action = contact.Find("ContactAction").GetComponent<Button>();
            var actionLabel = action.GetComponentInChildren<Text>();
            var frame = (RectTransform)contact.Find("PortraitFrame");
            var divider = (RectTransform)contact.Find("ContactDivider");
            var name = (RectTransform)contact.Find("ContactName");
            float splitWidth = commandField.rect.width;
            Assert.That(contact.gameObject.activeSelf, Is.True);
            Assert.That(contact.GetComponent<Image>(), Is.Null);
            Assert.That(status.text, Is.EqualTo("연결 요청"));
            Assert.That(actionLabel.text, Is.EqualTo("수락"));
            Assert.That(name.GetComponent<Text>().text, Is.EqualTo("현장지원과"));
            Assert.That(frame.rect.width / frame.rect.height, Is.EqualTo(4f / 3f).Within(0.001f));
            Assert.That(frame.anchoredPosition.y, Is.LessThan(((RectTransform)contact.Find("ContactRule")).anchoredPosition.y));
            Assert.That(name.anchoredPosition.y, Is.LessThan(frame.anchoredPosition.y - frame.rect.height));
            Assert.That(divider.anchoredPosition.x, Is.GreaterThan(contact.rect.width));
            Assert.That(contact.anchoredPosition.x + divider.anchoredPosition.x + divider.rect.width, Is.LessThan(commandField.anchoredPosition.x));
            Assert.That(frame.Find("PortraitBacking/UnknownPortrait").gameObject.activeSelf, Is.True);
            Assert.That(page.Find("LogHeading"), Is.Null); Assert.That(page.Find("Help"), Is.Null);

            action.onClick.Invoke();
            Assert.That(r.TerminalLineInProgress, Is.True); Assert.That(input.interactable, Is.True); Assert.That(input.readOnly, Is.False);
            Assert.That(input.isFocused, Is.False);
            Assert.That(status.text, Is.EqualTo("연결됨")); Assert.That(actionLabel.text, Is.EqualTo("연결 끊기"));
            Assert.That(commandField.rect.width, Is.EqualTo(splitWidth).Within(0.1f));
            var send = (RectTransform)input.transform.Find("CommandAction");
            Assert.That(send.anchorMax.x, Is.EqualTo(1f));
            Assert.That(input.textComponent.rectTransform.offsetMax.x, Is.LessThan(send.offsetMin.x));
            Invoke(host, "RefreshHeader"); Assert.That(tab.GetComponentInChildren<Text>().text, Does.Not.EndWith(" (1)"));
            Assert.That(terminal.InteractionEnabled, Is.True); Assert.That(host.TryNavigate("mail"), Is.True);
            host.ShowPage("kernel"); r.AdvanceTerminalConversation(1000);
            Invoke(terminal, "LateUpdate");
            Assert.That(input.interactable, Is.True); Assert.That(input.isFocused, Is.False);
            action.onClick.Invoke();
            Assert.That(contact.gameObject.activeSelf, Is.False);
            Assert.That(commandField.rect.width, Is.GreaterThan(splitWidth));
            Assert.That(contact.rect.width, Is.EqualTo(commandField.rect.width * 0.25f).Within(1f));
            r.AdvanceTerminalRedial(3); Invoke(terminal, "LateUpdate"); Invoke(host, "RefreshHeader");
            Assert.That(tab.GetComponentInChildren<Text>().text, Does.EndWith(" (1)"));
            Assert.That(contact.gameObject.activeSelf, Is.True);
            Assert.That(status.text, Is.EqualTo("연결 요청"));
            action.onClick.Invoke();
            Assert.That(status.text, Is.EqualTo("연결됨"));
            Assert.That(r.ConversationProgress("kernel-onboarding").deviations, Is.Zero);
        }
        [Test]
        public void SerializedCase01SampleRetainsContradictoryReportsThroughUnityJsonSaving()
        {
            var sample = AssetDatabase.LoadAssetAtPath<KernelScenarioDefinition>("Assets/1_Core/Story/InformationSample/Case01InformationSample.asset");
            Assert.That(sample, Is.Not.Null); KernelRuntime world = sample.CreateRuntime();
            Assert.That(world.IsDiscovered("VictorHale"), Is.False);
            Assert.That(world.Execute("매기.대화하기()").Success, Is.True); world.Tick(2);
            Assert.That(world.Execute("피해자 = VictorHale").Success, Is.True);
            Assert.That(world.Execute("참석기록.read()").Success, Is.True); world.Tick(2);
            Assert.That(world.Execute("피해자.attended").Value.number, Is.Zero);
            Assert.That(world.Entity("VictorHale").GetValue("attended").number, Is.EqualTo(1));
            Assert.That(world.Execute("참석여부 = 행사참석").Success, Is.True);
            Assert.That(world.Execute("레이첼.질문하기(참석여부)").Success, Is.True); world.Tick(2);
            Assert.That(world.Execute("에블린.질문하기(참석여부)").Success, Is.True); world.Tick(2);
            KernelRuntime loaded = sample.CreateRuntime(); loaded.Restore(JsonUtility.FromJson<KernelSnapshot>(JsonUtility.ToJson(world.Capture())));
            Assert.That(loaded.Observations.Count(o => o.subjectId == "VictorHale" && o.field == "attended"), Is.EqualTo(3));
            Assert.That(loaded.Observations.Any(o => o.source == "Evelyn" && o.statement.Contains("공식 참석자")), Is.True);
            Assert.That(loaded.LastKnownValue("VictorHale", "attended").number, Is.EqualTo(1));
            Assert.That(loaded.Data.Keys, Is.EquivalentTo(new[] { "피해자", "참석여부" }));
        }
        [Test]
        public void ClosingASystemMenuKeepsTheDayScreenAndItsSuspendedDraft()
        {
            host.ShowPage("kernel"); input.SetTextWithoutNotify("save draft");
            var dayRoot = new GameObject("Day", typeof(RectTransform)); dayRoot.transform.SetParent(root.transform, false);
            var day = dayRoot.AddComponent<TerminalDayView>(); day.Initialize(host, () => { }, () => { }); day.ShowIntro("DAY 1", "업무 시작");
            var menu = SystemMenu(new SystemMenuActions()); menu.Show(false); menu.Hide();
            Assert.That(host.SystemMenuVisible, Is.True); Assert.That(host.CurrentPage.gameObject.activeSelf, Is.False); Assert.That(terminal.PageVisible, Is.False);
            Assert.That(day.gameObject.activeSelf, Is.True); day.Hide();
            Assert.That(host.CurrentPage.Id, Is.EqualTo("kernel")); Assert.That(terminal.PageVisible, Is.True); Assert.That(input.text, Is.EqualTo("save draft"));
        }
        [Test]
        public void ProfileAndEvidencePhotosRespectKnowledgeAndDynamicAccess()
        {
            var catalog = ScriptableObject.CreateInstance<ProfileCatalog>(); var person = ScriptableObject.CreateInstance<CharacterProfileAsset>();
            var tool = ScriptableObject.CreateInstance<ToolProfileAsset>(); var texture = new Texture2D(4, 4); Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.zero);
            try
            {
                person.worldId = "client"; person.photo = sprite; person.availability.triggersOn = new[] { "photo.known" };
                tool.worldId = "toolkit"; tool.photo = sprite; catalog.characters = new[] { person }; catalog.tools = new[] { tool }; host.AttachProfiles(catalog);
                Assert.That(host.PortraitFor("client"), Is.Null); runtime.SetTrigger("photo.known", true); Assert.That(host.PortraitFor("client"), Is.SameAs(sprite));
                Assert.That(host.EvidencePhotoFor("toolkit"), Is.Null); runtime.Facts.Add("coffee_received"); runtime.CollectEvidence(runtime.Reference(runtime.Entity("toolkit")));
                terminal.SetInteractionEnabled(true); host.ShowPage("evidence"); EvidenceMonitorPage page = host.CurrentPage.GetComponent<EvidenceMonitorPage>(); page.SelectEvidence("toolkit");
                Assert.That(Field<Image>(page, "photo").sprite, Is.SameAs(sprite)); runtime.Entity("toolkit").SetEntityAccess(PlayerAccess.Internal); Invoke(page, "Refresh");
                Assert.That(host.EvidencePhotoFor("toolkit"), Is.Null); Assert.That(Field<Image>(page, "photo").enabled, Is.False); Assert.That(Field<Image>(page, "photo").sprite, Is.Null);
            }
            finally { Object.DestroyImmediate(catalog); Object.DestroyImmediate(person); Object.DestroyImmediate(tool); Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture); }
        }

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
            Assert.That(menu.GetComponentsInChildren<Button>().Select(b => b.GetComponentInChildren<Text>().text), Is.EqualTo(new[] { "이어하기", "불러오기", "처음부터", "설정", "끝내기" }));
            Assert.That(menu.GetComponentInParent<Canvas>(), Is.SameAs(canvas.GetComponent<Canvas>()));
            Assert.That(terminal.PageVisible, Is.False); Assert.That(host.TryNavigate("mail"), Is.False); Assert.That(host.TryCycleTab(1), Is.False);
            host.GoBack(); Assert.That(host.CurrentPage.Id, Is.EqualTo("kernel")); Assert.That(host.CanGoBack, Is.EqualTo(back));
            Assert.That(MenuButton(menu, "불러오기").interactable, Is.False);
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
                ReadSaves = () => new[] { new SystemSaveInfo { Id = "latest", Available = true, Day = 3, GameTime = "10:00", Kind = "Auto", Situation = "상담 중", SavedAt = "2026-11-18 10:00:00" },
                    new SystemSaveInfo { Id = "earlier", Available = true, Day = 2, GameTime = "09:00", Kind = "수동 저장", Situation = "상담 대기", SavedAt = "2026-11-17 09:00:00" } } });
            menu.Show(false);
            Assert.That(menu.GetComponentsInChildren<Button>().Select(b => b.GetComponentInChildren<Text>().text),
                Is.EqualTo(new[] { "계속하기", "저장하기", "불러오기", "가이드", "설정", "타이틀 화면" }));
            MenuButton(menu, "저장하기").onClick.Invoke(); Assert.That(saves, Is.EqualTo(1));
            MenuButton(menu, "불러오기").onClick.Invoke(); Assert.That(menu.GetComponentsInChildren<Text>().Any(t => t.text == "DAY 3\n10:00"), Is.True);
            menu.SelectSave("earlier"); MenuButton(menu, "이 기록 불러오기").onClick.Invoke(); Assert.That(loaded, Is.EqualTo("earlier")); Assert.That(menu.HandleEscape(), Is.True);
            MenuButton(menu, "가이드").onClick.Invoke(); Assert.That(menu.GetComponentsInChildren<Text>().Any(t => t.text.Contains("ESC 물건 내려놓기 → 전체 보기 → 메뉴")), Is.True);
            Assert.That(menu.HandleEscape(), Is.True); Assert.That(menu.HandleEscape(), Is.False);
            MenuButton(menu, "계속하기").onClick.Invoke(); Assert.That(resumes, Is.EqualTo(1));
            Button title = MenuButton(menu, "타이틀 화면"); menu.Hide(); title.onClick.Invoke(); Assert.That(titles, Is.Zero);
        }
        [Test]
        public void TitleContinueLoadsTheNewestAvailableSaveAndIgnoresTheSelectedSlot()
        {
            string loaded = null;
            var slots = new[] { new SystemSaveInfo { Id = "unavailable" },
                new SystemSaveInfo { Id = "latest", Available = true }, new SystemSaveInfo { Id = "earlier", Available = true } };
            var menu = SystemMenu(new SystemMenuActions { Load = id => loaded = id, ReadSaves = () => slots });
            menu.Show(true);
            MenuButton(menu, "불러오기").onClick.Invoke();
            menu.SelectSave("earlier");
            Assert.That(menu.HandleEscape(), Is.True);
            Assert.That(loaded, Is.Null);
            MenuButton(menu, "이어하기").onClick.Invoke();
            Assert.That(loaded, Is.EqualTo("latest"));
            Assert.That(menu.GetComponentInChildren<SystemSaveRow>(), Is.Null);

            slots = new[] { new SystemSaveInfo { Id = "newest", Available = true } }.Concat(slots).ToArray();
            menu.RefreshSaveList();
            MenuButton(menu, "이어하기").onClick.Invoke();
            Assert.That(loaded, Is.EqualTo("newest"));
        }
        [Test]
        public void TitleLoadOpensTenScrollableRecordsAndOnlyLoadsTheChosenAvailableId()
        {
            string loaded = null;
            var slots = Enumerable.Range(0, 10).Select(i => new SystemSaveInfo { Id = "save" + i, Available = i != 2,
                Day = 10 - i, GameTime = "09:00", Kind = i % 2 == 0 ? "Auto" : "수동 저장", Situation = "상담 중", SavedAt = "실제 시각 " + i }).ToArray();
            var menu = SystemMenu(new SystemMenuActions { Load = id => loaded = id, ReadSaves = () => slots }); menu.Show(true);
            MenuButton(menu, "불러오기").onClick.Invoke(); Assert.That(loaded, Is.Null);
            ScrollRect scroll = menu.GetComponentInChildren<CrtScrollRect>();
            Assert.That(scroll.vertical, Is.True); Assert.That(scroll.content.rect.height, Is.GreaterThan(scroll.viewport.rect.height));
            Assert.That(scroll.content.GetComponentsInChildren<Button>().Length, Is.EqualTo(10));
            slots = new[] { new SystemSaveInfo { Id = "quick", Available = true, Day = 11, GameTime = "09:00", Kind = "빠른 저장", Situation = "새 기록", SavedAt = "방금" } }.Concat(slots.Take(9)).ToArray();
            menu.RefreshSaveList(); Assert.That(menu.GetComponentsInChildren<Text>().Any(t => t.text.Contains("빠른 저장")), Is.True);
            menu.SelectSave("save8"); menu.SelectSave("save2"); MenuButton(menu, "이 기록 불러오기").onClick.Invoke();
            Assert.That(loaded, Is.EqualTo("save8")); Assert.That(menu.HandleEscape(), Is.True); Assert.That(menu.IsTitle, Is.True);
        }
        [Test]
        public void QuitRequiresConfirmationAndEscapeOrNoReturnsToTheTitle()
        {
            int quits = 0; var menu = SystemMenu(new SystemMenuActions { Quit = () => quits++ }); menu.Show(true);
            MenuButton(menu, "끝내기").onClick.Invoke(); Assert.That(quits, Is.Zero);
            Assert.That(menu.GetComponentsInChildren<Text>().Any(t => t.text == "정말 끝내시겠습니까?"), Is.True);
            MenuButton(menu, "아니요").onClick.Invoke(); Assert.That(quits, Is.Zero);
            MenuButton(menu, "끝내기").onClick.Invoke(); Assert.That(menu.HandleEscape(), Is.True); Assert.That(quits, Is.Zero);
            MenuButton(menu, "끝내기").onClick.Invoke(); MenuButton(menu, "예, 끝내기").onClick.Invoke(); Assert.That(quits, Is.EqualTo(1));
        }
        [Test]
        public void SettingsUseFourTabsAndVolumeIsOneHorizontalSettingWithAVisibleGauge()
        {
            const string volumeKey = "Codetective.MasterVolume";
            bool hadPreference = PlayerPrefs.HasKey(volumeKey); float oldPreference = PlayerPrefs.GetFloat(volumeKey), oldVolume = AudioListener.volume;
            EventSystem previous = EventSystem.current; var events = new GameObject("Settings navigation", typeof(EventSystem));
            try
            {
                EventSystem.current = events.GetComponent<EventSystem>(); AudioListener.volume = 0.5f;
                CommandLocale language = CommandLocale.Korean;
                var menu = SystemMenu(new SystemMenuActions { GetLanguage = () => language, SetLanguage = value => language = value }); menu.Show(true);
                MenuButton(menu, "설정").onClick.Invoke();
                foreach (string tab in new[] { "게임플레이", "비디오", "오디오", "입력" }) Assert.That(MenuButton(menu, tab), Is.Not.Null);
                SystemSettingChoice choice = menu.GetComponentsInChildren<SystemSettingChoice>().Single(c => c.name == "언어");
                choice.OnMove(new AxisEventData(EventSystem.current) { moveDir = MoveDirection.Right }); Assert.That(language, Is.EqualTo(CommandLocale.English));
                MenuButton(menu, "오디오").onClick.Invoke(); CrtSlider slider = menu.GetComponentInChildren<CrtSlider>();
                Assert.That(slider, Is.Not.Null); Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(slider.gameObject));
                Assert.That(menu.GetComponentsInChildren<Text>().Any(t => t.text == "−" || t.text == "+"), Is.False);
                slider.OnMove(new AxisEventData(EventSystem.current) { moveDir = MoveDirection.Left });
                Assert.That(AudioListener.volume, Is.EqualTo(0.4f).Within(0.001f)); Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(slider.gameObject));
                Assert.That(slider.fillRect.anchorMax.x, Is.EqualTo(0.4f).Within(0.001f));
                slider.OnMove(new AxisEventData(EventSystem.current) { moveDir = MoveDirection.Right });
                Assert.That(AudioListener.volume, Is.EqualTo(0.5f).Within(0.001f)); Assert.That(PlayerPrefs.GetFloat(volumeKey), Is.EqualTo(0.5f).Within(0.001f));
                Assert.That(slider.Track, Is.Not.Null); Assert.That(slider.navigation.selectOnUp, Is.SameAs(MenuButton(menu, "오디오")));
                MenuButton(menu, "오디오").Select(); MenuButton(menu, "오디오").OnMove(new AxisEventData(EventSystem.current) { moveDir = MoveDirection.Right });
                Assert.That(menu.GetComponentInChildren<CrtSlider>(), Is.Null); Assert.That(menu.GetComponentsInChildren<Text>().Any(t => t.text == "배치 취소"), Is.True);
            }
            finally
            {
                AudioListener.volume = oldVolume;
                if (hadPreference) PlayerPrefs.SetFloat(volumeKey, oldPreference); else PlayerPrefs.DeleteKey(volumeKey);
                PlayerPrefs.Save(); Object.DestroyImmediate(events); EventSystem.current = previous;
            }
        }
        [Test]
        public void BrowsingLoadAndReturningToTitleDoesNotReadSavesAgain()
        {
            int reads = 0;
            var menu = SystemMenu(new SystemMenuActions { ReadSaves = () =>
            { reads++; return new[] { new SystemSaveInfo { Id = "latest", Available = true, Day = 1, GameTime = "09:00", Situation = "상담 중", SavedAt = "2026-11-20 10:00:00", Kind = "Auto" } }; } });
            menu.Show(true); Assert.That(reads, Is.EqualTo(1));
            for (int i = 0; i < 3; i++)
            { MenuButton(menu, "불러오기").onClick.Invoke(); MenuButton(menu, "돌아가기").onClick.Invoke(); }
            Assert.That(reads, Is.EqualTo(1));
            MenuButton(menu, "불러오기").onClick.Invoke(); menu.RefreshSaveList(); Assert.That(reads, Is.EqualTo(2));
        }
        [Test]
        public void SaveRowsHaveSeparateDateSituationAndTimestampCellsAndSupportDoubleClickAndSubmit()
        {
            string loaded = null;
            var menu = SystemMenu(new SystemMenuActions { Load = id => loaded = id, ReadSaves = () => new[] {
                new SystemSaveInfo { Id = "latest", Available = true, Day = 1, GameTime = "09:00", Kind = "Auto", Situation = "상담 대기", SavedAt = "2026-11-20 10:00:00" },
                new SystemSaveInfo { Id = "older", Available = true, Day = 1, GameTime = "08:00", Kind = "수동 저장", Situation = "이전 상황", SavedAt = "2026-11-19 10:00:00" } } });
            menu.Show(true); MenuButton(menu, "불러오기").onClick.Invoke();
            SystemSaveRow[] rows = menu.GetComponentsInChildren<SystemSaveRow>(); SystemSaveRow row = rows[0];
            Assert.That(row.transform.Find("GameTime").GetComponentInChildren<Text>().text, Is.EqualTo("DAY 1\n09:00"));
            Assert.That(row.transform.Find("Situation").GetComponentInChildren<Text>().text, Is.EqualTo("상담 대기"));
            Assert.That(row.transform.Find("SavedAt").GetComponentInChildren<Text>().text, Is.EqualTo("2026-11-20 10:00:00"));
            Assert.That(row.transform.Find("SavedAt").GetComponent<RectTransform>().rect.height, Is.LessThan(row.transform.Find("Situation").GetComponent<RectTransform>().rect.height));
            row.OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, clickCount = 1 }); Assert.That(loaded, Is.Null);
            row.OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, clickCount = 2 }); Assert.That(loaded, Is.EqualTo("latest"));
            rows[1].OnSubmit(new BaseEventData(EventSystem.current)); Assert.That(loaded, Is.EqualTo("older"));
        }
        [Test]
        public void MenuArrowNavigationIsScopedSkipsDisabledOptionsAndKeepsSaveRowsVisible()
        {
            EventSystem previous = EventSystem.current; var events = new GameObject("Menu navigation", typeof(EventSystem));
            try
            {
                EventSystem.current = events.GetComponent<EventSystem>();
                var slots = Enumerable.Range(0, 10).Select(i => new SystemSaveInfo { Id = "save" + i, Available = i != 1, Day = 10 - i, GameTime = "09:00" }).ToArray();
                var menu = SystemMenu(new SystemMenuActions { Load = _ => { }, ReadSaves = () => slots }); menu.Show(true);
                Button start = MenuButton(menu, "이어하기"); Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(start.gameObject));
                start.OnMove(new AxisEventData(EventSystem.current) { moveDir = MoveDirection.Down });
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(MenuButton(menu, "불러오기").gameObject));
                MenuButton(menu, "불러오기").onClick.Invoke(); SystemSaveRow[] rows = menu.GetComponentsInChildren<SystemSaveRow>();
                Assert.That(rows[0].navigation.selectOnDown, Is.SameAs(rows[2]));
                rows[9].Select(); Assert.That(menu.GetComponentInChildren<ScrollRect>().content.anchoredPosition.y, Is.GreaterThan(0));
                Assert.That(rows[9].navigation.mode, Is.EqualTo(Navigation.Mode.Explicit));
                EventSystem.current.SetSelectedGameObject(null);
                typeof(SystemMenuMonitorView).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(menu, null);
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.Not.Null);
            }
            finally { Object.DestroyImmediate(events); EventSystem.current = previous; }
        }
        [Test]
        public void HistoryEnvelopesPreservePlayableSnapshotsThroughUnityJsonSerialization()
        {
            string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "codetective-unity-saves-" + Guid.NewGuid().ToString("N"));
            try
            {
                var history = new SaveHistory(directory, record => JsonUtility.ToJson(record), json => JsonUtility.FromJson<GameSaveRecord>(json));
                runtime.Store("record", Datum.Ref(DataKind.Document, "case_record", "사건 기록", "인용: \"단서\"\n두 번째 줄"));
                runtime.CollectEvidence(runtime.Data["record"].value, "현장 확보");
                GameSaveSlot older = history.Save(JsonUtility.ToJson(runtime.Capture()), GameSaveKind.Manual, runtime.Day, runtime.Minute, "상담 중");
                runtime.Free("record"); history.Save(JsonUtility.ToJson(runtime.Capture()), GameSaveKind.Quick, runtime.Day, runtime.Minute, "정리 중");
                var restored = ClinicScenario.Create(); restored.Restore(JsonUtility.FromJson<KernelSnapshot>(history.Read(older.Id).snapshot));
                Assert.That(restored.Data["record"].value.id, Is.EqualTo("case_record")); Assert.That(restored.Evidence.Single().Description, Does.Contain("\"단서\"\n두 번째 줄"));
                Assert.That(history.Slots.Count, Is.EqualTo(2)); Assert.That(history.Slots[0].Record.kind, Is.EqualTo(GameSaveKind.Quick));
            }
            finally { if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true); }
        }
        [Test]
        public void ScheduleShowsAMergedAppointmentAndDateNavigationWithoutAdvancingTime()
        {
            runtime.RegisterClinicDay(new ClinicDayPlan { day = 1, appointments = new[] { new PatientAppointment { id = "visit", profileId = "client",
                patientName = "상담자", title = "예약 제목", startMinute = 540, endMinute = 660 } } });
            terminal.SetInteractionEnabled(true); host.ShowPage("schedule");
            ScheduleMonitorPage schedule = host.CurrentPage.GetComponent<ScheduleMonitorPage>();
            RectTransform table = Field<RectTransform>(schedule, "table");
            RectTransform visits = Field<RectTransform>(schedule, "appointmentCells");
            var appointment = (RectTransform)visits.Find("1:visit");
            RectTransform[] hours = table.GetComponentsInChildren<RectTransform>().Where(cell => cell.parent == table && cell.name == "Time").ToArray();
            RectTransform empty = Field<RectTransform>(schedule, "emptyCells");
            Assert.That(visits.childCount, Is.EqualTo(1));
            Assert.That(appointment.sizeDelta.y, Is.EqualTo(hours[0].sizeDelta.y - hours[1].anchoredPosition.y));
            Assert.That(appointment.GetComponentsInChildren<Text>().Select(label => label.text), Is.EquivalentTo(new[] { "상담자", "예약 제목" }));
            Assert.That(appointment.Find("Title").GetComponent<Text>().text, Is.EqualTo("예약 제목"));
            Assert.That(table.GetComponentsInChildren<Text>().Select(label => label.text), Does.Contain("이름").And.Contain("상담 제목"));
            Assert.That(empty.childCount, Is.EqualTo(hours.Length - 2));
            Assert.That(hours.Select(cell => cell.GetComponentInChildren<Text>().text), Is.EqualTo(Enumerable.Range(9, 9).Select(hour => $"{hour:00}:00")));
            RectTransform[] emptySlots = empty.GetComponentsInChildren<RectTransform>().Where(slot => slot.parent == empty).ToArray();
            Assert.That(emptySlots.Single(slot => slot.anchoredPosition.y == hours[2].anchoredPosition.y).GetComponentInChildren<Text>().text,
                Is.EqualTo("예약 가능"));
            Assert.That(emptySlots.Single(slot => slot.anchoredPosition.y == hours[3].anchoredPosition.y).GetComponentInChildren<Text>().text,
                Is.EqualTo("점심시간"));
            Assert.That(hours[1].GetComponent<Button>().interactable, Is.True);
            Assert.That(hours[3].GetComponent<Button>().interactable, Is.False);
            Button previous = Field<Button>(schedule, "previousDay"), next = Field<Button>(schedule, "nextDay");
            Text date = Field<Text>(schedule, "dateLabel");
            Assert.That(previous.GetComponentInChildren<Text>().text, Is.EqualTo("<"));
            Assert.That(next.GetComponentInChildren<Text>().text, Is.EqualTo(">"));
            Assert.That(previous.interactable, Is.False);
            Assert.That(date.text, Is.EqualTo("1994.01.01 (토)"));
            next.onClick.Invoke(); Assert.That(date.text, Is.EqualTo("1994.01.02 (일)"));
            date.GetComponentInParent<Button>().onClick.Invoke();
            RectTransform calendar = Field<RectTransform>(schedule, "calendar");
            Assert.That(calendar.gameObject.activeSelf, Is.True); Assert.That(table.gameObject.activeSelf, Is.False);
            Assert.That(Field<RectTransform>(schedule, "appointmentDetails").gameObject.activeSelf, Is.False);
            Button today = calendar.GetComponentsInChildren<Button>().Single(button => button.name == "1");
            Button selected = calendar.GetComponentsInChildren<Button>().Single(button => button.name == "2");
            Assert.That(today.GetComponentInChildren<Text>().text, Is.EqualTo("1"));
            Assert.That(selected.GetComponentInChildren<Text>().text, Is.EqualTo("2"));
            Assert.That(today.image.color.g, Is.GreaterThan(selected.image.color.g));
            Assert.That(today.GetComponent<Outline>(), Is.Null);
            Assert.That(selected.GetComponent<Outline>(), Is.Not.Null);
            Button previousMonth = Field<Button>(schedule, "previousMonth"), nextMonth = Field<Button>(schedule, "nextMonth");
            Assert.That(previousMonth.GetComponentInChildren<Text>().text, Is.EqualTo("<<"));
            Assert.That(nextMonth.GetComponentInChildren<Text>().text, Is.EqualTo(">>"));
            nextMonth.onClick.Invoke();
            calendar.GetComponentsInChildren<Button>().Single(button => button.name == "3").onClick.Invoke();
            Assert.That(date.text, Is.EqualTo("1994.02.03 (목)"));
            Assert.That(table.gameObject.activeSelf, Is.True); Assert.That(calendar.gameObject.activeSelf, Is.False);
            Assert.That(Field<Text>(schedule, "appointmentText").text, Is.EqualTo("예약이 없습니다."));
            Assert.That(runtime.Day, Is.EqualTo(1)); Assert.That(runtime.Minute, Is.EqualTo(540));
        }

        [Test]
        public void TimetableFitsOlderMinuteBasedAppointmentsToWholeCellsWithoutOverlap()
        {
            runtime.RegisterClinicDay(new ClinicDayPlan { appointments = new[] {
                new PatientAppointment { id = "first", startMinute = 540 },
                new PatientAppointment { id = "second", startMinute = 600, title = "상담 설명" },
                new PatientAppointment { id = "third", startMinute = 660 },
                new PatientAppointment { id = "fourth", startMinute = 780 }
            } });
            runtime.InitializeClinicDay();
            KernelSnapshot snapshot = runtime.Capture();
            AppointmentProgress[] oldVisits = snapshot.clinicDay.appointments;
            oldVisits[0].endMinute = 560;
            oldVisits[1].startMinute = 580; oldVisits[1].title = null;
            oldVisits[2].endMinute = 675;
            oldVisits[3].startMinute = 675; oldVisits[3].endMinute = 720;
            runtime.Restore(snapshot);
            terminal.SetInteractionEnabled(true); host.ShowPage("schedule");
            ScheduleMonitorPage schedule = host.CurrentPage.GetComponent<ScheduleMonitorPage>();
            RectTransform table = Field<RectTransform>(schedule, "table");
            RectTransform[] hours = table.GetComponentsInChildren<RectTransform>().Where(cell => cell.parent == table && cell.name == "Time").ToArray();
            RectTransform visits = Field<RectTransform>(schedule, "appointmentCells");
            var first = (RectTransform)visits.Find("1:first");
            var second = (RectTransform)visits.Find("1:second");
            var third = (RectTransform)visits.Find("1:third");
            var fourth = (RectTransform)visits.Find("1:fourth");
            Assert.That(new[] { first.anchoredPosition.y, second.anchoredPosition.y, third.anchoredPosition.y, fourth.anchoredPosition.y },
                Is.EqualTo(new[] { hours[0].anchoredPosition.y, hours[1].anchoredPosition.y, hours[2].anchoredPosition.y, hours[2].anchoredPosition.y }));
            Assert.That(visits.GetComponentsInChildren<RectTransform>().Where(cell => cell.parent == visits).Select(cell => cell.sizeDelta.y),
                Is.All.EqualTo(hours[0].sizeDelta.y));
            Assert.That(third.anchoredPosition.x + third.sizeDelta.x, Is.LessThan(fourth.anchoredPosition.x));
            Assert.That(second.Find("Title").GetComponent<Text>().text, Is.EqualTo("상담 설명"));
        }

        [Test]
        public void SelectingAReservationUpdatesTheRightHandDetailsAndLeavesTheTimetableVisible()
        {
            runtime.RegisterClinicDay(new ClinicDayPlan { appointments = new[] {
                new PatientAppointment { id = "morning", profileId = "client", title = "오전" },
                new PatientAppointment { id = "later", profileId = "client", title = "두 번째 상담", description = "예약할 때 전달한 자세한 이야기", startMinute = 600 } } });
            runtime.Tick(1);
            while (runtime.IsClinicTransitioning)
            {
                AppointmentProgress visit = runtime.CurrentAppointment;
                runtime.AdvanceVisitPresentation(visit.id, visit.phase);
            }
            runtime.AddConsultationNote("client", "오전에 남긴 기록");
            terminal.SetInteractionEnabled(true); host.ShowPage("schedule");
            ScheduleMonitorPage schedule = host.CurrentPage.GetComponent<ScheduleMonitorPage>();
            RectTransform visits = Field<RectTransform>(schedule, "appointmentCells");
            RectTransform table = Field<RectTransform>(schedule, "table");
            Button morning = visits.Find("1:morning").GetComponent<Button>();
            Button later = visits.Find("1:later").GetComponent<Button>();
            Text contents = Field<Text>(schedule, "appointmentText");
            RectTransform details = Field<RectTransform>(schedule, "appointmentDetails");
            Assert.That(details.anchoredPosition.x, Is.GreaterThan(table.anchoredPosition.x + table.sizeDelta.x));
            Assert.That(contents.text, Does.Contain("오전에 남긴 기록").And.Not.Contain("두 번째 상담"));
            Assert.That(later.GetComponentsInChildren<Text>().Select(label => label.text), Does.Not.Contain("예약할 때 전달한 자세한 이야기"));
            Color selectedColor = morning.image.color;
            contents.rectTransform.anchoredPosition = new Vector2(0, 48);
            later.onClick.Invoke();
            Assert.That(contents.text, Does.Contain("예약할 때 전달한 자세한 이야기").And.Not.Contain("오전에 남긴 기록"));
            Assert.That(contents.rectTransform.anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(later.image.color, Is.EqualTo(selectedColor));
            Assert.That(morning.image.color, Is.Not.EqualTo(selectedColor));
            Field<Button[]>(schedule, "timeCells")[0].onClick.Invoke();
            Assert.That(contents.text, Does.Contain("오전에 남긴 기록").And.Not.Contain("두 번째 상담"));
            Assert.That(table.gameObject.activeSelf, Is.True);
            Assert.That(details.gameObject.activeSelf, Is.True);
            Assert.That(Field<Text>(schedule, "dateLabel").text, Does.Contain("1994.01.01"));
        }

        [Test]
        public void KernelEvidenceBrowserKeepsTheCommandDraftAndReconnectsFreedReferences()
        {
            terminal.SetInteractionEnabled(true); runtime.Store("record", Datum.Ref(DataKind.Document, "case_record", "사건 기록", "조사에서 얻은 상세 기록")); runtime.Free("record");
            runtime.CollectEvidence(runtime.Reference(runtime.Entity("case_record")), "현장 확보");
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
            Entity key = runtime.CreateInstance("Item", "security_key", "보안 열쇠", false);
            key.SetValue("description", Datum.Text("문을 여는 도구")); runtime.CollectEvidence(runtime.Reference(key), "현장 확보");
            runtime.CollectEvidence(Datum.Ref(DataKind.Document, "security_record", "문서", "security 연구 기록"), "현장 확보");
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
        public void ClinicPersonRowsSwitchProfilesAndKeepEachPersonsDraft()
        {
            terminal.SetInteractionEnabled(true);
            runtime.CreateInstance("Person", "unseen", "미확인 인물", false);
            host.ShowPage("clinic");
            ClinicMonitorPage clinic = host.CurrentPage.GetComponent<ClinicMonitorPage>();
            var choices = Field<Dictionary<string, Button>>(clinic, "profileChoices");
            Assert.That(choices.Keys, Is.EquivalentTo(runtime.VisibleProfileIds));
            Assert.That(choices.ContainsKey("unseen"), Is.False);
            choices["kernel"].onClick.Invoke();
            Assert.That(host.SelectedProfileId, Is.EqualTo("kernel"));
            Assert.That(choices["client"].image.color, Is.Not.EqualTo(choices["kernel"].image.color));
            Assert.That(Field<Text>(clinic, "status").text, Does.Contain("Kernel"));

            host.ShowPage("clinic-records");
            ClinicMonitorPage editor = host.CurrentPage.GetComponent<ClinicMonitorPage>();
            var editorChoices = Field<Dictionary<string, Button>>(editor, "profileChoices");
            MonitorNoteInput journal = Field<MonitorNoteInput>(editor, "noteInput");
            journal.text = "작성 중인 Kernel 기록";
            editorChoices["client"].onClick.Invoke();
            Assert.That(journal.text, Is.Empty);
            editorChoices["kernel"].onClick.Invoke();
            Assert.That(journal.text, Is.EqualTo("작성 중인 Kernel 기록"));
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
            Invoke(terminal, "Submit", "must_not_run = 1");
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
        public void ApprovedMailAppearsOnItsDateWithoutAddingMailControlsToTheTimetable()
        {
            runtime.RegisterMail(new StoryMail { id = "booking", mailbox = Mailbox.Clinic, subject = "예약 문의", body = "가족 돌봄에 대해 상담하고 싶습니다.", reservation = new ReservationRequest { day = 2,
                appointment = new PatientAppointment { id = "visit", patientName = "예약 손님", title = "후속 상담", startMinute = 660 } }, choices = new[] {
                new MailChoice { id = "yes", label = "예약 승인", result = MailChoiceResult.Approved, approvesReservation = true },
                new MailChoice { id = "no", label = "예약 거절", result = MailChoiceResult.Declined } } });
            terminal.SetInteractionEnabled(true); host.ShowPage("mail"); MailMonitorPage mail = host.CurrentPage.GetComponent<MailMonitorPage>();
            mail.ShowMessage("booking"); Invoke(mail, "Choose", "yes");
            Assert.That(Field<Text>(mail, "detail").text, Does.Contain("94.01.02 / 11:00 ~ 12:00"));
            Assert.That(host.CurrentPage.Id, Is.EqualTo("mail"));
            Field<Dictionary<string, Button>>(host, "tabs")["schedule"].onClick.Invoke();
            Assert.That(host.CurrentPage.Id, Is.EqualTo("schedule")); Assert.That(host.SelectedScheduleDay, Is.EqualTo(2));
            ScheduleMonitorPage schedule = host.CurrentPage.GetComponent<ScheduleMonitorPage>();
            Transform appointment = Field<RectTransform>(schedule, "appointmentCells").Find("mail:booking");
            Assert.That(appointment.GetComponentsInChildren<Text>().Select(label => label.text), Does.Contain("예약 손님").And.Contain("후속 상담"));
            Assert.That(schedule.GetComponentsInChildren<Button>().Count(button => button.transform.parent == schedule.transform), Is.EqualTo(3));
            appointment.GetComponent<Button>().onClick.Invoke();
            Assert.That(Field<Text>(schedule, "appointmentText").text, Does.Contain("가족 돌봄에 대해 상담하고 싶습니다."));
            Assert.That(runtime.ScheduleForDay(2).Count, Is.EqualTo(1));
        }
        [Test]
        public void ClickingAnObjectInMailReturnsToTheKernelDraftWithTheReference()
        {
            runtime.RegisterMail(new StoryMail { id = "object-link", mailbox = Mailbox.Kernel, body = "[[hq|Operations HQ]] is ready." });
            terminal.SetInteractionEnabled(true);
            host.ShowPage("kernel"); input.SetTextWithoutNotify("office = ");
            host.ShowPage("mail");
            MailMonitorPage mail = host.CurrentPage.GetComponent<MailMonitorPage>();
            Text detail = Field<Text>(mail, "detail");
            Assert.That(detail.text, Is.EqualTo("Operations HQ is ready."));
            Canvas.ForceUpdateCanvases();
            UICharInfo glyph = detail.cachedTextGenerator.characters[0];
            UILineInfo line = detail.cachedTextGenerator.lines[0];
            Vector2 localPoint = new Vector2(glyph.cursorPos.x + glyph.charWidth * 0.5f, line.topY - line.height * 0.5f) / detail.pixelsPerUnit;
            Vector2 point = RectTransformUtility.WorldToScreenPoint(null, detail.transform.TransformPoint(localPoint + detail.PixelAdjustPoint(Vector2.zero)));
            var links = detail.GetComponent<ObjectReferenceTextView>();
            Assert.That(links.IsRaycastLocationValid(point, null), Is.True);
            links.OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, position = point });

            Assert.That(host.CurrentPage.Id, Is.EqualTo("kernel"));
            Assert.That(input.text, Is.EqualTo("office = Operations HQ"));
            Assert.That(runtime.Data, Is.Empty);
            Invoke(terminal, "Submit", input.text);
            Assert.That(runtime.Data["office"].value.id, Is.EqualTo("hq"));
        }

        [Test]
        public void MailFieldsStayAboveTheScrollableBodyAndUseTheDestinationMailboxByDefault()
        {
            runtime.ConfigureInitialClock(new GameDateTime(1994, 1, 31, 9));
            string longBody = string.Join("\n", Enumerable.Repeat("본문 내용", 60));
            runtime.RegisterMail(new StoryMail { id = "long-mail", mailbox = Mailbox.Kernel,
                sender = "작전지원팀", recipient = "Kernel / 작전실", subject = "현장 보고", body = longBody });
            runtime.RegisterMail(new StoryMail { id = "latest-mail", mailbox = Mailbox.Kernel, day = 2, subject = "후속 보고" });
            runtime.RegisterMail(new StoryMail { id = "clinic-mail", mailbox = Mailbox.Clinic, day = 2, sender = "상담 예약자", subject = "예약 문의", body = "예약하고 싶습니다." });
            runtime.Tick(540); runtime.NextDay();
            terminal.SetInteractionEnabled(true); host.ShowPage("mail");
            MailMonitorPage mail = host.CurrentPage.GetComponent<MailMonitorPage>();
            RectTransform inbox = Field<RectTransform>(mail, "list");
            Transform olderMail = inbox.Find("Mail_long-mail");
            Assert.That(olderMail.Find("ReadCheck"), Is.Null);
            Assert.That(olderMail.Find("Date").GetComponent<Text>().text, Is.EqualTo("94.01.31"));
            Assert.That(inbox.Find("Mail_latest-mail").Find("Date").GetComponent<Text>().text, Is.EqualTo("94.02.01"));
            olderMail.GetComponent<Button>().onClick.Invoke();
            Assert.That(inbox.Find("Mail_long-mail").Find("ReadCheck"), Is.Not.Null);
            Text sender = Field<Text>(mail, "sender"), recipient = Field<Text>(mail, "recipient"),
                title = Field<Text>(mail, "title"), date = Field<Text>(mail, "date");
            Text detail = Field<Text>(mail, "detail"); ScrollRect bodyScroll = Field<ScrollRect>(mail, "bodyScroll");
            Assert.That(mail.GetComponentsInChildren<Button>().Select(b => b.GetComponentInChildren<Text>().text),
                Does.Not.Contain("스케줄"));
            Assert.That(new[] { sender.text, recipient.text, title.text, date.text }, Is.EqualTo(new[] { "작전지원팀", "Kernel / 작전실", "현장 보고", "94.01.31 (월)" }));
            Assert.That(new[] { sender, recipient, title, date }.All(field => !field.transform.IsChildOf(bodyScroll.transform)), Is.True);
            Assert.That(detail.text, Does.Contain("본문 내용").And.Not.Contain("작전지원팀").And.Not.Contain("DAY 1"));
            Canvas.ForceUpdateCanvases();
            bodyScroll.verticalNormalizedPosition = 0;
            Assert.That(detail.rectTransform.anchoredPosition.y, Is.GreaterThan(0));
            Assert.That(sender.text, Is.EqualTo("작전지원팀"));
            mail.ShowMessage("clinic-mail");
            Assert.That(recipient.text, Is.EqualTo("상담소"));
            Assert.That(title.text, Is.EqualTo("예약 문의"));
            Assert.That(date.text, Is.EqualTo("94.02.01 (화)"));
            Assert.That(detail.rectTransform.anchoredPosition.y, Is.Zero.Within(0.01f));
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
        [Test]
        public void ApprovingADispatchNavigatesToKernelAndStartsTheTitleBeforeTheBriefingLine()
        {
            runtime.RegisterMail(new StoryMail { id = "dispatch", mailbox = Mailbox.Kernel, requiresAcceptance = true,
                acceptLabel = "승인", pageOnAccept = "kernel" });
            runtime.RegisterTerminalConversation(new TerminalConversationDefinition { id = "first-case", requiredMailAccepted = "dispatch",
                introductionTitle = "Case.1", introductionSubtitle = "Victor Hale 실종 사건", speaker = "Maggie", useSpeakerPrompt = true,
                steps = new[] { new TerminalConversationStep { id = "report", lines = new[] { new TerminalDialogueLine { text = "실종 신고입니다." } } } } });
            terminal.SetInteractionEnabled(true);
            host.ShowPage("mail");
            var page = host.CurrentPage.GetComponent<MailMonitorPage>();
            Button approval = Field<Button>(page, "accept");
            Assert.That(approval.GetComponentInChildren<Text>().text, Is.EqualTo("승인"));
            approval.onClick.Invoke();
            Assert.That(runtime.IsMailAccepted("dispatch"), Is.True);
            Assert.That(host.CurrentPage.Id, Is.EqualTo("kernel"));
            Assert.That(input.interactable, Is.False);
            Assert.That(runtime.TerminalIntroductionTitle, Is.EqualTo("Case.1"));
            Assert.That(runtime.TerminalConversationTranscript(), Is.Empty);
            runtime.CompleteTerminalIntroduction("first-case");
            runtime.AdvanceTerminalConversation(1000);
            Assert.That(runtime.TerminalConversationTranscript(), Is.EqualTo("Maggie> 실종 신고입니다."));
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
