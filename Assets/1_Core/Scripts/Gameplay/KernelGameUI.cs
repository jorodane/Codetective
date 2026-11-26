using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Codetective.Interaction;
using Codetective.Operations;
using Codetective.Terminal;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Codetective.Gameplay
{
    public sealed class KernelGameUI : MonoBehaviour
    {
        private KernelGameController game;
        private InspectionController inspection;
        private Font font;
        private GameObject root;
        private RectTransform panel;
        private Text heading;
        private Text body;
        private Text clock;
        private Text feedback;
        private Text languageLabel;
        private readonly List<GameObject> controls = new List<GameObject>();
        private GraphicRaycaster raycaster;
        private readonly List<RaycastResult> hits = new List<RaycastResult>();
        private string context;
        private string modal;
        private float nextRefresh;
        private InputField protocolSource;
        private InputField arguments;
        private int selectedThread;
        private int libraryIndex;
        private RectTransform protocolSuggestions;
        private readonly List<Button> suggestionButtons = new List<Button>();
        private IReadOnlyList<Completion> suggestions = Array.Empty<Completion>();
        private int completionSuffix;
        private string draft;
        private string draftArguments;
        private RectTransform suspicionFill;
        private Image suspicionImage;
        private RectTransform heldBar, newspaperReader;
        private Text heldLabel;
        private Button closeItem;
        private ItemInspectionView itemInspection;
        private PaperDocument openedDocument;
        private int documentSection;
        private int documentRevision;
        private GameObject nextDayButton;
        private TerminalDayView clinicDay;
        private DayStartPresentation dayStart;
        private CanvasGroup interfaceInteraction;
        private GameObject saveNoticeRoot;
        private Text saveNotice;
        private ProfileArchiveView profileArchive;
        private ConsultationOverlay dialogue;
        private float saveNoticeUntil;
        public bool IsInitialized { get; private set; }
        public bool SummaryVisible => clinicDay != null && clinicDay.SummaryVisible;
        public bool DayStartVisible => dayStart != null && dayStart.IsVisible;
        public bool IsCompositionProtected => (protocolSource is CompletingInputField source && source.isFocused && source.IsCompositionProtected) ||
            (arguments is CompletingInputField args && args.isFocused && args.IsCompositionProtected);

        public void Initialize(KernelGameController game, InspectionController inspection, Font font, Camera view, Transform terminalBody)
        {
            if (IsInitialized) return;
            this.game = game; this.inspection = inspection;
            this.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            draft = game.Runtime.ExampleProtocol;
            draftArguments = game.Runtime.ExampleArguments;
            root = new GameObject("KernelInterface", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 100;
            interfaceInteraction = root.AddComponent<CanvasGroup>();
            var scale = root.GetComponent<CanvasScaler>(); scale.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scale.referenceResolution = new Vector2(1280, 720); scale.matchWidthOrHeight = 0.5f;
            raycaster = root.GetComponent<GraphicRaycaster>();
            RectTransform hud = Box("KernelHUD", root.transform, new Vector2(16, -12), new Vector2(550, 42), new Vector2(0, 1));
            hud.GetComponent<Image>().raycastTarget = false;
            clock = Label(hud, new Vector2(10, -4), new Vector2(270, 34), "", 18);
            Label(hud, new Vector2(286, -4), new Vector2(80, 34), "의심도", 18);
            RectTransform gauge = Box("SuspicionGauge", hud, new Vector2(368, -14), new Vector2(164, 14), new Vector2(0, 1));
            gauge.GetComponent<Image>().raycastTarget = false;
            suspicionFill = Box("Fill", gauge, Vector2.zero, Vector2.zero, Vector2.zero);
            suspicionFill.anchorMax = Vector2.one; suspicionFill.offsetMin = suspicionFill.offsetMax = Vector2.zero;
            suspicionImage = suspicionFill.GetComponent<Image>(); suspicionImage.raycastTarget = false;
            panel = Box("Context", root.transform, new Vector2(-18, -74), new Vector2(570, 620), new Vector2(1, 1));
            heading = Label(panel, new Vector2(20, -16), new Vector2(460, 42), "", 26);
            Button(panel, new Vector2(492, -14), new Vector2(58, 40), "닫기", CloseContext);
            body = ScrollText(panel, new Vector2(20, -72), new Vector2(530, 330));
            feedback = Label(panel, new Vector2(20, -552), new Vector2(530, 54), "", 17);
            panel.gameObject.SetActive(false);
            BuildOperationsPage();
            dialogue = GetComponent<ConsultationOverlay>();
            if (dialogue == null) dialogue = gameObject.AddComponent<ConsultationOverlay>();
            dialogue.Initialize(game, inspection, root.transform, this.font, game.DialoguePresentation, view, terminalBody);
            BuildItemControls();
            RectTransform archiveRoot = OverlayWidgets.Rect("ProfileArchive", root.transform, new Vector2(-18, -86), new Vector2(250, 534), new Vector2(1, 1));
            profileArchive = archiveRoot.gameObject.AddComponent<ProfileArchiveView>();
            profileArchive.Initialize(game.Runtime, this.font, game.SelectProfile, () => game.DisplayedProfileId,
                () => game.CanSelectProfile, inspection.ReturnToOverview);
            Text shortcuts = Label(root.transform, new Vector2(16, 0), new Vector2(1248, 18), "1 터미널 · 2 상담 · 3 프로필 · 4 서랍 · 5 축음기 · Tab/Shift+Tab 페이지 · F5 빠른 저장 (입력 중 제외) · ESC 내려놓기→전체→메뉴", 14);
            shortcuts.rectTransform.anchorMin = shortcuts.rectTransform.anchorMax = shortcuts.rectTransform.pivot = Vector2.zero;
            nextDayButton = Button(root.transform, new Vector2(500, -66), new Vector2(280, 40), "18:00 업무 종료 / 정산 보기", game.ShowDaySummary).gameObject;
            nextDayButton.SetActive(false);
            BuildSaveNotice();
            dayStart = gameObject.AddComponent<DayStartPresentation>(); dayStart.Initialize(this.font, game.Transitions != null ? game.Transitions.Settings : null);
            if (game.Monitor != null)
            {
                var dayRoot = new GameObject("TerminalDay", typeof(RectTransform)); dayRoot.transform.SetParent(game.Monitor.transform, false); dayRoot.layer = 5;
                clinicDay = dayRoot.AddComponent<TerminalDayView>(); clinicDay.Initialize(game.Monitor, game.CloseDaySummary, game.BeginNextDay);
            }
            IsInitialized = true;
            if (context != null) ShowContext(context);
        }

        private void Update()
        {
            if (!IsInitialized) return;
            if (saveNoticeRoot.activeSelf && Time.unscaledTime >= saveNoticeUntil) saveNoticeRoot.SetActive(false);
            bool inMenu = inspection.Mode == InspectionMode.Menu || inspection.PresentingOnTerminal || DayStartVisible || game.IsDateTransitioning;
            interfaceInteraction.alpha = inMenu ? 0 : 1;
            interfaceInteraction.blocksRaycasts = !inMenu;
            interfaceInteraction.interactable = !game.IsDateTransitioning && !game.IsVisitPresenting && !game.IsViewTransitioning && !inMenu;
            if (modal == "document" && openedDocument != null && openedDocument.ContentRevision != documentRevision)
            {
                if (openedDocument.Sections.Count == 0) Back();
                else { documentSection = Mathf.Clamp(documentSection, 0, openedDocument.Sections.Count - 1); BuildDocumentReader(); }
            }
            bool showItem = !inMenu && modal == null && game.Carry.IsExamining;
            itemInspection.Show(showItem ? game.Carry.HeldItem : null);
            bool showArchive = !inMenu && modal == null && context == "profile" && game.Carry.HeldItem == null;
            if (showArchive != profileArchive.IsVisible) { if (showArchive) profileArchive.Show(); else profileArchive.Hide(); }
            if (showArchive) profileArchive.SetTopInset(dialogue.TopInset);
            bool showPlacement = !inMenu && modal == null && game.Carry.IsPlacing && !game.Carry.IsDragging;
            heldBar.gameObject.SetActive(showPlacement);
            if (showPlacement)
            {
                heldLabel.text = game.Carry.PlacementHint;
                closeItem.interactable = game.Carry.CanManipulate;
            }
            nextDayButton.SetActive(game.Runtime.IsDayEnded && !inMenu && modal == null && game.Carry.HeldItem == null);
            if (modal == "protocol" && protocolSource != null && protocolSource.isFocused) RefreshProtocolCompletions();
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.2f;
            KernelRuntime r = game.Runtime;
            clock.text = r.ClockText;
            float suspicion = Mathf.Clamp01((float)r.Suspicion / 100f);
            suspicionFill.anchorMax = new Vector2(suspicion, 1);
            suspicionImage.color = Color.Lerp(new Color(0.25f, 0.72f, 0.43f), new Color(1f, 0.38f, 0.12f), suspicion);
            if (panel.gameObject.activeSelf && modal != "protocol") RefreshBody();
        }

        public bool BlocksPointer(Vector2 point)
        {
            if (raycaster == null || EventSystem.current == null) return false;
            hits.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
            return hits.Count > 0;
        }

        public void ShowContext(string id)
        {
            context = id;
            if (!IsInitialized) return;
            if (id == "profile" && game.Carry.HeldItem == null) profileArchive.Show(); else profileArchive.Hide();
            if (id == "monitor" || id == "counsel" || id == "drawer" || id == "profile" || id == "gramophone") { HidePanel(); return; }
            BuildContext();
        }

        public void HideContext() { context = null; CloseModal(); if (profileArchive != null) profileArchive.Hide(); HidePanel(); }
        private void CloseContext()
        {
            if (Back()) return;
            if ((context == "clock" || context == "portrait") && inspection.CloseInformationView()) HideContext();
            else HidePanel();
        }

        public bool Back()
        {
            if (!IsInitialized) return false;
            if (game.IsDateTransitioning) return true;
            if (SummaryVisible) { game.CloseDaySummary(); return true; }
            if (modal == null) return false;
            if (modal == "protocol" && protocolSource != null && ((ProtocolInputField)protocolSource).IsCompositionProtected) return true;
            CloseModal();
            if (context != null) ShowContext(context);
            else HidePanel();
            return true;
        }

        private void OpenModal(string name)
        {
            if (!IsInitialized) return;
            if (inspection.Mode == InspectionMode.Menu || game.IsDateTransitioning || game.IsVisitPresenting || game.IsViewTransitioning || SummaryVisible) return;
            SaveDraft();
            modal = name;
            inspection.ModalInputBlocked = true;
            if (newspaperReader != null) newspaperReader.gameObject.SetActive(false);
            if (profileArchive != null) profileArchive.Hide();
            ClearControls();
            if (name == "document") { BuildDocumentReader(); panel.gameObject.SetActive(false); return; }
            panel.gameObject.SetActive(true);
            if (name == "protocol") BuildProtocolEditor();
            else
            {
                heading.text = name == "threads" ? "Thread 상태 / 작전 배정" : "확보 데이터";
                body.gameObject.SetActive(true);
                if (name == "threads")
                {
                    for (int i = 0; i < game.Runtime.Threads.Count; i++)
                    {
                        string id = game.Runtime.Threads[i].Id;
                        AddButton(new Vector2(20 + i * 175, -428), new Vector2(165, 40), id + " 재개", () => Run(id + ".resume();"));
                        AddButton(new Vector2(20 + i * 175, -476), new Vector2(165, 40), id + " 복귀", () => Run(id + ".recall();"));
                    }
                }
                RefreshBody();
            }
        }

        private void CloseModal()
        {
            SaveDraft();
            modal = null;
            openedDocument = null;
            if (newspaperReader != null) newspaperReader.gameObject.SetActive(false);
            if (profileArchive != null) profileArchive.Hide();
            if (inspection != null) inspection.ModalInputBlocked = false;
            ClearSelection();
        }

        private void BuildContext()
        {
            ClearControls();
            body.gameObject.SetActive(true);
            panel.gameObject.SetActive(true);
            switch (context)
            {
                case "counsel":
                    HidePanel(); return;
                case "profile":
                    HidePanel(); return;
                case "clock":
                    heading.text = "업무 시간 / 날짜 기록";
                    AddButton(new Vector2(20, -428), new Vector2(245, 44), "현재 기록 저장", game.SaveNow);
                    AddButton(new Vector2(285, -428), new Vector2(245, 44), "업무 정산 (18시 이후)", game.ShowDaySummary);
                    break;
                case "drawer": HidePanel(); return;
                case "gramophone": heading.text = "통신 / Thread 보고"; break;
                case "portrait": heading.text = "Kernel / 현재 작전 정보"; break;
                default: heading.text = "정보 기록"; break;
            }
            RefreshBody();
        }

        private void BuildOperationsPage()
        {
            MonitorPageHost host = game.Monitor; if (host == null) return;
            host.Initialize();
            RectTransform page = OverlayWidgets.Rect("OperationTools", host.transform, new Vector2(24, -140), new Vector2(1140, 590));
            MonitorPage definition = page.gameObject.AddComponent<MonitorPage>(); definition.Configure("operations", "작전 도구", false, "kernel");
            OverlayWidgets.Label(page, font, new Vector2(20, -16), new Vector2(1080, 50), "데이터 · Thread · 프로토콜", 32);
            OverlayWidgets.Button(page, font, new Vector2(840, -16), new Vector2(240, 50), "명령창으로", () => host.ShowPage("kernel"));
            OverlayWidgets.Button(page, font, new Vector2(20, -100), new Vector2(320, 70), "확보 자료", () => host.ShowPage("evidence"));
            OverlayWidgets.Button(page, font, new Vector2(370, -100), new Vector2(320, 70), "Thread 상태 / 배정", () => OpenModal("threads"));
            OverlayWidgets.Button(page, font, new Vector2(720, -100), new Vector2(320, 70), "프로토콜 편집", () => OpenModal("protocol"));
            languageLabel = OverlayWidgets.Button(page, font, new Vector2(20, -210), new Vector2(320, 60), "", ToggleLanguage).GetComponentInChildren<Text>();
            RefreshLanguageLabel();
            foreach (Graphic graphic in page.GetComponentsInChildren<Graphic>(true))
                if (graphic.GetComponent<CrtMeshEffect>() == null) graphic.gameObject.AddComponent<CrtMeshEffect>();
            host.Register(definition);
        }
        public void RefreshCommandLanguage() { if (languageLabel != null) RefreshLanguageLabel(); }
        private void BuildItemControls()
        {
            itemInspection = ItemInspectionView.Create(root.transform, font, game);
            heldBar = Box("HeldObject", root.transform, new Vector2(0, 24), new Vector2(1040, 54), new Vector2(0.5f, 0));
            heldBar.GetComponent<Image>().raycastTarget = false;
            heldLabel = Label(heldBar, new Vector2(12, -12), new Vector2(874, 32), "", 15);
            closeItem = Button(heldBar, new Vector2(896, -8), new Vector2(128, 38), "닫기", game.CloseItemInspection);
            heldBar.gameObject.SetActive(false);
        }
        public void ShowNewspaper() => ShowDocument(game.NewspaperDocument, 0);
        public void ShowDocument(PaperDocument document, int index)
        {
            if (document == null || !document.Item.IsHeld || game.Carry.IsPlacing || document.Sections.Count == 0) return;
            openedDocument = document; documentSection = Mathf.Clamp(index, 0, document.Sections.Count - 1); OpenModal("document");
        }
        private void BuildDocumentReader()
        {
            if (openedDocument == null) return;
            documentRevision = openedDocument.ContentRevision;
            if (newspaperReader != null) { newspaperReader.gameObject.SetActive(false); Destroy(newspaperReader.gameObject); }
            newspaperReader = Box("DocumentReader", root.transform, Vector2.zero, new Vector2(1000, 642), new Vector2(0.5f, 0.5f));
            newspaperReader.GetComponent<Image>().color = new Color(0.85f, 0.82f, 0.69f);
            Text title = Label(newspaperReader, new Vector2(30, -16), new Vector2(824, 40), openedDocument.Title, 22);
            title.color = new Color(0.12f, 0.11f, 0.08f);
            Button(newspaperReader, new Vector2(872, -12), new Vector2(98, 40), "닫기", () => Back());
            RectTransform viewport = Box("DocumentViewport", newspaperReader, new Vector2(30, -72), new Vector2(940, 540), new Vector2(0, 1));
            viewport.GetComponent<Image>().color = new Color(0.85f, 0.82f, 0.69f);
            viewport.gameObject.AddComponent<RectMask2D>();
            ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport; scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 32;
            scroll.content = PaperContentView.Create(viewport, openedDocument.Typeface, openedDocument.Sections[documentSection], 922, true);
            scroll.content.anchoredPosition = new Vector2(4, -4); scroll.verticalNormalizedPosition = 1;
            newspaperReader.gameObject.SetActive(true);
        }
        private void BuildSaveNotice()
        {
            saveNoticeRoot = new GameObject("SaveNotification", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            Canvas canvas = saveNoticeRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 3000;
            CanvasScaler scaler = saveNoticeRoot.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = 0.5f;
            RectTransform box = Box("SaveStatus", saveNoticeRoot.transform, new Vector2(-16, -14), new Vector2(300, 42), new Vector2(1, 1));
            box.GetComponent<Image>().raycastTarget = false;
            saveNotice = Label(box, new Vector2(12, -6), new Vector2(276, 30), "", 20); saveNotice.alignment = TextAnchor.MiddleRight;
            saveNoticeRoot.SetActive(false);
        }
        public void ShowSaveStatus(string text, float seconds)
        {
            if (!IsInitialized || saveNoticeRoot == null) return;
            saveNotice.text = text; saveNoticeUntil = Time.unscaledTime + seconds; saveNoticeRoot.SetActive(true);
        }
        public void ShowDayIntro(string title, string subtitle) { clinicDay?.ShowIntro(title, subtitle); }
        public IEnumerator PlayDayStart(int day, string subtitle, Action textFading) => dayStart.Play(day, subtitle, textFading);
        public IEnumerator PlayTerminalIntroduction(string title, string subtitle) => dayStart.Play(title, subtitle);
        public void HideDay() { clinicDay?.Hide(); }
        public void ShowDaySummary(DailyRecord record) { HideContext(); clinicDay?.ShowSummary(record); }
        public void CloseDaySummary() { clinicDay?.Hide(); }

        private void RefreshBody()
        {
            KernelRuntime r = game.Runtime;
            string view = modal ?? context;
            var text = new StringBuilder();
            if (view == "threads" || view == "gramophone")
            {
                foreach (ThreadAgent agent in r.Threads)
                    text.Append(agent.Id.ToUpperInvariant()).Append("  ").Append(agent.State.ToString().ToUpperInvariant()).Append('\n')
                        .Append("위치: ").Append(agent.Location).Append("\n프로토콜: ").Append(agent.ProtocolName)
                        .Append("\n명령: ").Append(agent.CurrentCommand).Append('\n').Append(agent.Message)
                        .Append("\n마지막 보고: ").Append(agent.Report).Append("\n\n");
            }
            else if (view == "data" || view == "drawer")
            {
                foreach (DataSlot slot in r.Data.Values)
                    text.Append(slot.name).Append(" : ").Append(r.IsPlayerVisible(slot.value) ? r.TypeName(slot.value) + "\n" + r.Format(slot.value) : "[접근 제한]").Append("\n\n");
                if (r.Data.Count == 0) text.Append("확보한 데이터가 없습니다. 상담의 붉은 단서를 기록하거나 명령을 실행하세요.");
            }
            else if (view == "profile")
            {
                if (!r.VisibleProfileIds.Contains(game.SelectedProfileId)) game.SelectedProfileId = r.VisibleProfileIds.FirstOrDefault();
                text.Append(game.SelectedProfileId != null ? r.ProfileSummary(game.SelectedProfileId) : "열람 가능한 프로필이 없습니다.");
            }
            else if (view == "clock")
            {
                text.Append(r.ClockText).Append("\n\n업무 시간: 09:00 ~ 18:00\n18시에 날짜 기록이 자동 저장됩니다.\n진행 중인 Thread 작업도 이어집니다.\n\n")
                    .Append(r.IsDayEnded ? "오늘의 업무가 끝났습니다. 다음 날로 진행할 수 있습니다." : "업무 진행 중")
                    .Append("\n\nKernel 의심도는 상단 게이지에서 확인할 수 있습니다.");
            }
            else text.Append("코드네임: Kernel\n신분: 개인 심리상담소 상담가\n\nThread와 신원은 상호 비공개입니다.\n\n작전 권한\n")
                .Append(string.Join("\n", r.Authorities)).Append("\n\n지휘소 권한\n")
                .Append(string.Join("\n", r.Complete(r.ObjectName("hq") + ".").Select(c => c.Label)));
            body.text = text.ToString();
            if (view == "clock") feedback.text = game.Feedback;
        }

        private void BuildProtocolEditor()
        {
            heading.text = "프로토콜 생성 / 입력·명령·반환";
            body.gameObject.SetActive(false);
            protocolSource = Input(panel, new Vector2(20, -72), new Vector2(530, 280), draft, true);
            controls.Add(protocolSource.gameObject);
            arguments = Input(panel, new Vector2(20, -365), new Vector2(530, 36), draftArguments, false);
            controls.Add(arguments.gameObject);
            for (int i = 0; i < game.Runtime.Threads.Count; i++)
            {
                int index = i;
                AddButton(new Vector2(20 + i * 175, -409), new Vector2(165, 34), game.Runtime.Threads[i].Id, () =>
                {
                    selectedThread = index;
                    feedback.text = "배정 대상: " + game.Runtime.Threads[index].Id;
                });
            }
            AddButton(new Vector2(20, -452), new Vector2(160, 40), "저장", () =>
            {
                ExecutionResult saved = game.Runtime.SaveProtocol(protocolSource.text);
                feedback.text = saved.Message;
                if (saved.Success) game.AutoSave();
            });
            AddButton(new Vector2(195, -452), new Vector2(160, 40), "복사본 테스트", () =>
            {
                try
                {
                    Protocol p = new CommandParser(protocolSource.text).ReadProtocol(protocolSource.text);
                    ExecutionResult result = game.Runtime.TestProtocol(protocolSource.text, p.Name + "(" + arguments.text + ");");
                    feedback.text = (result.Success ? "테스트 성공: " : "테스트 실패: ") + result.Message;
                }
                catch (CommandException e) { feedback.text = e.Message; }
            });
            AddButton(new Vector2(370, -452), new Vector2(160, 40), "Thread 배정", () =>
            {
                ExecutionResult saved = game.Runtime.SaveProtocol(protocolSource.text);
                if (!saved.Success) { feedback.text = saved.Message; return; }
                Protocol p = new CommandParser(protocolSource.text).ReadProtocol(protocolSource.text);
                ThreadAgent agent = game.Runtime.Threads.ElementAtOrDefault(selectedThread);
                if (agent == null || agent.State != AgentState.Ready) { feedback.text = "선택한 Thread가 대기 상태가 아닙니다. 다른 Thread를 선택하세요."; return; }
                Run(agent.Id + ".run(" + p.Name + (string.IsNullOrWhiteSpace(arguments.text) ? "" : ", " + arguments.text) + ");");
            });
            AddButton(new Vector2(20, -504), new Vector2(165, 34), "이전 저장본", () => LoadProtocol(-1));
            AddButton(new Vector2(195, -504), new Vector2(160, 34), "저장본 불러오기", () => LoadProtocol(0));
            AddButton(new Vector2(370, -504), new Vector2(160, 34), "다음 저장본", () => LoadProtocol(1));
            protocolSuggestions = Box("권한 목록", panel, new Vector2(20, -252), new Vector2(530, 98), new Vector2(0, 1));
            controls.Add(protocolSuggestions.gameObject);
            for (int i = 0; i < 3; i++)
            {
                int row = i;
                suggestionButtons.Add(Button(protocolSuggestions, new Vector2(0, -i * 32), new Vector2(530, 31), "", () => AcceptProtocolCompletion(row)));
            }
            protocolSuggestions.gameObject.SetActive(false);
            var completing = (ProtocolInputField)protocolSource;
            completing.CompletionQuery = QueryProtocolCompletion;
            completing.CompletionApplied += RefreshProtocolCompletions;
            completing.CompletionListRequested += RefreshProtocolCompletions;
            feedback.text = "입력 예: " + game.Runtime.ExampleArguments + " / 배정 대상: " + game.Runtime.Threads.ElementAtOrDefault(selectedThread)?.Id +
                "\nTab / Enter / Space 완성 · 다음 Enter 줄바꿈 · 테스트는 세계 복사본에서 수행됩니다.";
        }

        private void LoadProtocol(int direction)
        {
            Protocol[] library = game.Runtime.Protocols.Values.OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
            if (library.Length == 0) { feedback.text = "저장된 프로토콜이 없습니다."; return; }
            libraryIndex = (libraryIndex + direction + library.Length) % library.Length;
            Protocol selected = library[libraryIndex];
            protocolSource.text = draft = selected.Source;
            feedback.text = selected.SignatureFor(game.Runtime.Language) + "\n호출 관계: " + string.Join(", ", selected.CalledProtocols);
            protocolSuggestions.gameObject.SetActive(false);
        }

        private void ToggleLanguage()
        {
            if (inspection.Mode == InspectionMode.Menu) return;
            SaveDraft();
            ClearControls();
            CommandLocale next = game.Runtime.Language == CommandLocale.Korean ? CommandLocale.English : CommandLocale.Korean;
            // Translate the untouched sample only; player-authored source and names stay intact.
            if (draft == game.Runtime.ExampleProtocol) draft = null;
            if (draftArguments == game.Runtime.ExampleArguments) draftArguments = null;
            game.SetCommandLanguage(next);
            if (draft == null) draft = game.Runtime.ExampleProtocol;
            if (draftArguments == null) draftArguments = game.Runtime.ExampleArguments;
            RefreshLanguageLabel();
            if (modal != null) OpenModal(modal);
            else if (context != null) ShowContext(context);
        }

        private void RefreshLanguageLabel() => languageLabel.text = game.Runtime.Language == CommandLocale.Korean ? "명령: 한국어" : "Code: English";

        private void RefreshProtocolCompletions()
        {
            if (protocolSource == null || protocolSuggestions == null) return;
            TerminalCompletionInput current = ((ProtocolInputField)protocolSource).CompletionInput;
            RefreshProtocolCompletions(current);
        }

        private void RefreshProtocolCompletions(TerminalCompletionInput current)
        {
            completionSuffix = current.Text.Length - current.Caret;
            suggestions = current.Anchor == current.Caret ? game.Runtime.CompleteProtocol(current.Text, current.Caret, arguments.text, current.IsComposing) : Array.Empty<Completion>();
            if (suggestions.Any(c => c.Replacement == current.Text)) suggestions = Array.Empty<Completion>();
            protocolSuggestions.gameObject.SetActive(suggestions.Count > 0);
            ((ProtocolInputField)protocolSource).SetInlinePreview(suggestions.Count > 0 ?
                new InlineCompletion(suggestions[0].Replacement, suggestions[0].Replacement.Length - completionSuffix) : (InlineCompletion?)null);
            for (int i = 0; i < suggestionButtons.Count; i++)
            {
                suggestionButtons[i].gameObject.SetActive(i < suggestions.Count);
                if (i < suggestions.Count) suggestionButtons[i].GetComponentInChildren<Text>().text = suggestions[i].Label;
            }
        }

        private InlineCompletion? QueryProtocolCompletion(TerminalCompletionInput current)
        {
            RefreshProtocolCompletions(current);
            if (suggestions.Count == 0) return null;
            string value = suggestions[0].Replacement;
            return new InlineCompletion(value, value.Length - completionSuffix);
        }

        private void AcceptProtocolCompletion(int index)
        {
            if (protocolSource == null || index >= suggestions.Count) return;
            string value = suggestions[index].Replacement;
            ((ProtocolInputField)protocolSource).AcceptCompletion(new InlineCompletion(value, value.Length - completionSuffix));
        }

        private void Run(string command) { ExecutionResult result = game.Runtime.Execute(command); feedback.text = result.Message; }
        private void SaveDraft()
        {
            if (protocolSource != null) draft = ((ProtocolInputField)protocolSource).CompletionInput.Text;
            if (arguments != null) draftArguments = arguments.text;
        }
        private void ClearControls()
        {
            SaveDraft(); ClearSelection();
            foreach (GameObject item in controls) { item.SetActive(false); Destroy(item); }
            controls.Clear(); protocolSource = null; arguments = null;
            suggestionButtons.Clear(); suggestions = Array.Empty<Completion>(); protocolSuggestions = null;
            if (feedback != null) feedback.text = "";
        }
        private void HidePanel() { if (panel != null) { ClearControls(); panel.gameObject.SetActive(false); } }
        private void ClearSelection()
        {
            if (EventSystem.current == null || panel == null) return;
            GameObject selected = EventSystem.current.currentSelectedGameObject;
            if (selected != null && (selected.transform.IsChildOf(panel) ||
                (newspaperReader != null && selected.transform.IsChildOf(newspaperReader)))) EventSystem.current.SetSelectedGameObject(null);
        }

        private Button AddButton(Vector2 position, Vector2 size, string title, Action action)
        {
            Button button = Button(panel, position, size, title, action);
            controls.Add(button.gameObject); return button;
        }

        private RectTransform Box(string name, Transform parent, Vector2 position, Vector2 size, Vector2 anchor)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform), typeof(Image)).transform;
            rect.SetParent(parent, false); rect.gameObject.layer = 5;
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = position; rect.sizeDelta = size;
            rect.GetComponent<Image>().color = new Color(0.025f, 0.055f, 0.04f, 0.97f);
            return rect;
        }

        private Text Label(Transform parent, Vector2 position, Vector2 size, string value, int fontSize)
        {
            var rect = (RectTransform)new GameObject("Text", typeof(RectTransform), typeof(Text)).transform;
            rect.SetParent(parent, false); rect.gameObject.layer = 5;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = position; rect.sizeDelta = size;
            Text text = rect.GetComponent<Text>(); text.font = font; text.fontSize = fontSize;
            text.color = new Color(0.70f, 0.93f, 0.78f); text.supportRichText = false;
            text.raycastTarget = false; text.text = value;
            return text;
        }

        private Button Button(Transform parent, Vector2 position, Vector2 size, string title, Action action)
        {
            RectTransform rect = Box(title, parent, position, size, new Vector2(0, 1));
            Image image = rect.GetComponent<Image>(); image.color = new Color(0.10f, 0.23f, 0.16f);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            button.onClick.AddListener(() => action());
            Text text = Label(rect, new Vector2(6, -4), size - new Vector2(12, 8), title, 17);
            text.alignment = TextAnchor.MiddleCenter;
            return button;
        }

        private InputField Input(Transform parent, Vector2 position, Vector2 size, string value, bool multiline)
        {
            RectTransform rect = Box("CommandSource", parent, position, size, new Vector2(0, 1));
            var field = rect.gameObject.AddComponent<ProtocolInputField>(); field.targetGraphic = rect.GetComponent<Image>();
            Text text = Label(rect, new Vector2(10, -8), size - new Vector2(20, 16), "", 20);
            field.textComponent = text; field.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
            field.characterLimit = multiline ? 16000 : 512; field.text = value;
            return field;
        }

        private Text ScrollText(Transform parent, Vector2 position, Vector2 size)
        {
            RectTransform rect = Box("Records", parent, position, size, new Vector2(0, 1));
            rect.gameObject.AddComponent<RectMask2D>();
            var scroll = rect.gameObject.AddComponent<ScrollRect>();
            Text text = Label(rect, new Vector2(0, 0), new Vector2(size.x - 12, 0), "", 21);
            text.rectTransform.anchorMax = new Vector2(1, 1); text.rectTransform.sizeDelta = new Vector2(-12, 0);
            text.rectTransform.pivot = new Vector2(0, 1);
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = text.rectTransform; scroll.viewport = rect; scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 24;
            return text;
        }

        private void OnDestroy() { IsInitialized = false; if (root != null) Destroy(root); if (clinicDay != null) Destroy(clinicDay.gameObject); if (saveNoticeRoot != null) Destroy(saveNoticeRoot); }
    }
}
