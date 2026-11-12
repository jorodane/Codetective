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
        private RectTransform drawerBar;
        private Button[] drawerButtons;
        private Text mailBadge;
        private RectTransform heldBar, newspaperReader;
        private Text heldLabel, newspaperHeading, newspaperBody;
        private Button useHeld, putDown;
        private GameObject nextDayButton, dateRoot;
        private CanvasGroup dateFade;
        private Text dateLabel;

        public void Initialize(KernelGameController game, InspectionController inspection, Font font)
        {
            this.game = game; this.inspection = inspection; this.font = font;
            draft = game.Runtime.ExampleProtocol;
            draftArguments = game.Runtime.ExampleArguments;
            root = new GameObject("KernelInterface", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 100;
            var scale = root.GetComponent<CanvasScaler>(); scale.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scale.referenceResolution = new Vector2(1280, 720); scale.matchWidthOrHeight = 0.5f;
            raycaster = root.GetComponent<GraphicRaycaster>();
            RectTransform hud = Box("KernelHUD", root.transform, new Vector2(16, -12), new Vector2(950, 42), new Vector2(0, 1));
            clock = Label(hud, new Vector2(10, -4), new Vector2(270, 34), "", 18);
            Button(hud, new Vector2(285, -4), new Vector2(110, 34), "모니터", () => { HideContext(); game.ShowMonitor(); });
            Button(hud, new Vector2(403, -4), new Vector2(110, 34), "데이터", () => OpenModal("data"));
            Button(hud, new Vector2(521, -4), new Vector2(110, 34), "Thread", () => OpenModal("threads"));
            Button(hud, new Vector2(639, -4), new Vector2(140, 34), "프로토콜", () => OpenModal("protocol"));
            languageLabel = Button(hud, new Vector2(787, -4), new Vector2(152, 34), "", ToggleLanguage).GetComponentInChildren<Text>();
            RefreshLanguageLabel();
            panel = Box("Context", root.transform, new Vector2(-18, -74), new Vector2(570, 620), new Vector2(1, 1));
            heading = Label(panel, new Vector2(20, -16), new Vector2(530, 42), "", 26);
            body = ScrollText(panel, new Vector2(20, -72), new Vector2(530, 330));
            feedback = Label(panel, new Vector2(20, -552), new Vector2(530, 54), "", 17);
            panel.gameObject.SetActive(false);
            RectTransform mailNotice = Box("MailNotice", root.transform, new Vector2(-16, -12), new Vector2(220, 42), new Vector2(1, 1));
            mailBadge = Button(mailNotice, new Vector2(6, -4), new Vector2(208, 34), "메일함", () => game.ShowMonitorPage("mail")).GetComponentInChildren<Text>();
            var dialogue = GetComponent<ConsultationOverlay>() ?? gameObject.AddComponent<ConsultationOverlay>();
            dialogue.Initialize(game, inspection, root.transform, font, game.DialoguePresentation);
            BuildDrawerControls();
            BuildItemControls();
            nextDayButton = Button(root.transform, new Vector2(500, -66), new Vector2(280, 40), "18:00 업무 종료 / 다음 날", game.BeginNextDay).gameObject;
            nextDayButton.SetActive(false);
            BuildDateTransition();
        }

        private void Update()
        {
            if (game == null || root == null) return;
            drawerBar.gameObject.SetActive(game.CanOperateDrawers && modal == null);
            HoldableItem held = game.Carry.HeldItem;
            heldBar.gameObject.SetActive(held != null && modal == null && inspection.Mode != InspectionMode.Menu && !game.IsDateTransitioning);
            if (held != null)
            {
                heldLabel.text = held.DisplayName + "  ·  E 사용  ·  우클릭 놓기";
                heldBar.anchoredPosition = new Vector2(0, game.CanOperateDrawers ? 104 : 16);
                useHeld.interactable = !inspection.TextInputFocused && !game.IsViewTransitioning;
                putDown.interactable = !inspection.TextInputFocused;
            }
            nextDayButton.SetActive(game.Runtime.IsDayEnded && !game.IsDateTransitioning && modal == null && inspection.Mode != InspectionMode.Menu);
            if (modal == "protocol" && protocolSource != null && protocolSource.isFocused) RefreshProtocolCompletions();
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.2f;
            KernelRuntime r = game.Runtime;
            clock.text = r.ClockText + "  의심 " + r.Suspicion.ToString("0");
            int unread = r.UnreadMailCount(Mailbox.Kernel) + r.UnreadMailCount(Mailbox.Clinic);
            mailBadge.text = unread > 0 ? "메일함 / 새 메일 " + unread : "메일함";
            for (int i = 0; i < drawerButtons.Length; i++) drawerButtons[i].GetComponentInChildren<Text>().text = (i + 1) + (game.Drawers[i].IsOpen ? " 닫기" : " 열기");
            if (panel.gameObject.activeSelf && modal != "protocol") RefreshBody();
        }

        public bool BlocksPointer(Vector2 point)
        {
            if (raycaster == null || EventSystem.current == null) return false;
            hits.Clear();
            raycaster.Raycast(new PointerEventData(EventSystem.current) { position = point }, hits);
            return hits.Count > 0;
        }

        public void ShowContext(string id)
        {
            context = id;
            if (id == "monitor" || id == "counsel" || id == "drawer") { HidePanel(); return; }
            BuildContext();
        }

        public void HideContext() { context = null; CloseModal(); HidePanel(); }

        public bool Back()
        {
            if (game.IsDateTransitioning) return true;
            if (modal == null) return false;
            if (modal == "protocol" && protocolSource != null && ((ProtocolInputField)protocolSource).IsCompositionProtected) return true;
            CloseModal();
            if (context != null && context != "monitor") BuildContext();
            else HidePanel();
            return true;
        }

        private void OpenModal(string name)
        {
            if (inspection.Mode == InspectionMode.Menu || game.IsDateTransitioning) return;
            SaveDraft();
            modal = name;
            inspection.ModalInputBlocked = true;
            if (newspaperReader != null) newspaperReader.gameObject.SetActive(false);
            ClearControls();
            if (name == "newspaper") { BuildNewspaperReader(); panel.gameObject.SetActive(false); return; }
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
            if (newspaperReader != null) newspaperReader.gameObject.SetActive(false);
            inspection.ModalInputBlocked = false;
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
                    heading.text = "프로필 / 상담 기록";
                    AddButton(new Vector2(20, -416), new Vector2(245, 40), "이전 인물", () => ChangeProfile(-1));
                    AddButton(new Vector2(285, -416), new Vector2(245, 40), "다음 인물", () => ChangeProfile(1));
                    AddButton(new Vector2(20, -468), new Vector2(510, 44), "모니터에서 상담 기록 작성", () => game.ShowMonitorPage("clinic-records"));
                    break;
                case "clock":
                    heading.text = "업무 시간 / 날짜 기록";
                    AddButton(new Vector2(20, -428), new Vector2(245, 44), "현재 기록 저장", game.SaveNow);
                    AddButton(new Vector2(285, -428), new Vector2(245, 44), "다음 날 (18시 이후)", game.BeginNextDay);
                    break;
                case "drawer": HidePanel(); return;
                case "gramophone": heading.text = "통신 / Thread 보고"; break;
                case "portrait": heading.text = "Kernel / 현재 작전 정보"; break;
                default: heading.text = "정보 기록"; break;
            }
            RefreshBody();
        }

        private void BuildDrawerControls()
        {
            drawerBar = Box("DrawerControls", root.transform, new Vector2(0, 16), new Vector2(1120, 76), new Vector2(0.5f, 0));
            Label(drawerBar, new Vector2(12, -5), new Vector2(1096, 24), "서랍을 클릭하거나 아래 버튼으로 열고 닫으세요.", 18);
            drawerButtons = new Button[game.Drawers.Count];
            for (int i = 0; i < drawerButtons.Length; i++)
            {
                int index = i;
                drawerButtons[i] = Button(drawerBar, new Vector2(12 + i * 122, -34), new Vector2(114, 34), "", () => game.ToggleDrawer(index));
            }
            drawerBar.gameObject.SetActive(false);
        }
        private void ChangeProfile(int direction)
        {
            string[] profiles = game.Runtime.VisibleProfileIds.ToArray(); if (profiles.Length == 0) return;
            int index = Math.Max(0, Array.IndexOf(profiles, game.SelectedProfileId));
            game.SelectedProfileId = profiles[(index + direction + profiles.Length) % profiles.Length]; RefreshBody();
        }
        private void BuildItemControls()
        {
            heldBar = Box("HeldObject", root.transform, new Vector2(0, 16), new Vector2(760, 54), new Vector2(0.5f, 0));
            heldLabel = Label(heldBar, new Vector2(12, -12), new Vector2(480, 32), "", 18);
            useHeld = Button(heldBar, new Vector2(504, -8), new Vector2(112, 38), "E 사용", game.UseHeldItem);
            putDown = Button(heldBar, new Vector2(630, -8), new Vector2(112, 38), "놓기", game.PutDownItem);
            heldBar.gameObject.SetActive(false);
        }
        public void ShowNewspaper() => OpenModal("newspaper");
        private void BuildNewspaperReader()
        {
            if (newspaperReader == null)
            {
                newspaperReader = Box("NewspaperReader", root.transform, Vector2.zero, new Vector2(1000, 642), new Vector2(0.5f, 0.5f));
                newspaperReader.GetComponent<Image>().color = new Color(0.85f, 0.82f, 0.69f);
                newspaperHeading = Label(newspaperReader, new Vector2(30, -20), new Vector2(940, 70), "", 30);
                newspaperHeading.color = new Color(0.12f, 0.11f, 0.08f);
                newspaperBody = ScrollText(newspaperReader, new Vector2(30, -108), new Vector2(940, 448));
                newspaperBody.transform.parent.GetComponent<Image>().color = new Color(0.80f, 0.77f, 0.64f);
                newspaperBody.color = new Color(0.12f, 0.11f, 0.08f);
                Button(newspaperReader, new Vector2(30, -584), new Vector2(450, 40), "닫고 계속 들기 / ESC", () => Back());
                Button(newspaperReader, new Vector2(520, -584), new Vector2(450, 40), "책상에 놓기", () => { game.Carry.PutDown(); Back(); });
            }
            newspaperReader.gameObject.SetActive(true);
            newspaperHeading.text = "CODETECTIVE DAILY  /  DAY " + game.Runtime.Day.ToString("D2") + "\n조간 · 사회 / 지역";
            IReadOnlyList<NewsArticle> articles = game.Runtime.NewspaperArticles(game.Runtime.Day);
            newspaperBody.text = articles.Count == 0 ? "오늘의 추가 보도는 없습니다." : string.Join("\n\n────────────────────────\n\n", articles.Select(a => a.headline + "\n\n" + a.body));
            newspaperBody.transform.parent.GetComponent<ScrollRect>().verticalNormalizedPosition = 1;
        }
        private void BuildDateTransition()
        {
            dateRoot = new GameObject("DateTransition", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
            Canvas canvas = dateRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 2000;
            CanvasScaler scaler = dateRoot.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1280, 720);
            RectTransform black = Box("Blackout", dateRoot.transform, Vector2.zero, Vector2.zero, Vector2.zero);
            black.anchorMin = Vector2.zero; black.anchorMax = Vector2.one; black.offsetMin = black.offsetMax = Vector2.zero;
            black.GetComponent<Image>().color = Color.black;
            dateLabel = Label(black, Vector2.zero, new Vector2(900, 180), "", 42);
            dateLabel.rectTransform.anchorMin = dateLabel.rectTransform.anchorMax = dateLabel.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            dateLabel.alignment = TextAnchor.MiddleCenter;
            dateFade = dateRoot.GetComponent<CanvasGroup>(); dateFade.alpha = 0; dateRoot.SetActive(false);
        }
        public IEnumerator FadeDate(float from, float to, float duration, string label)
        {
            dateRoot.SetActive(true); dateLabel.text = label;
            for (float elapsed = 0; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            { dateFade.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration)); yield return null; }
            dateFade.alpha = to; if (to == 0) dateRoot.SetActive(false);
        }

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
                    text.Append(slot.name).Append(" : ").Append(r.IsPlayerVisible(slot.value) ? r.TypeName(slot.value) + "\n" + slot.value : "[접근 제한]").Append("\n\n");
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
                    .Append("\n\nKernel 의심도: ").Append(r.Suspicion.ToString("0"))
                    .Append("\n위치 노출 진행: ").Append((r.Exposure * 100).ToString("0.0")).Append('%');
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
                if (saved.Success) game.SaveNow();
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
            else if (context != null && context != "monitor") BuildContext();
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
            feedback.text = "";
        }
        private void HidePanel() { if (panel != null) { ClearControls(); panel.gameObject.SetActive(false); } }
        private void ClearSelection()
        {
            if (EventSystem.current == null || panel == null) return;
            GameObject selected = EventSystem.current.currentSelectedGameObject;
            if (selected != null && selected.transform.IsChildOf(panel)) EventSystem.current.SetSelectedGameObject(null);
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

        private void OnDestroy() { if (root != null) Destroy(root); if (dateRoot != null) Destroy(dateRoot); }
    }
}
