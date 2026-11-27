using System;
using System.Reflection;
using Codetective.Operations;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif
using Object = UnityEngine.Object;

namespace Codetective.Terminal.Tests.UI
{
    public sealed class RegressionInput : BaseInput
    {
        public string Composition = "";
        public override string compositionString => Composition;
        public override IMECompositionMode imeCompositionMode { get; set; }
        public override Vector2 compositionCursorPos { get; set; }
    }

    public sealed class RegressionInputModule : BaseInputModule
    {
        public override bool IsModuleSupported() => true;
        public override bool ShouldActivateModule() => isActiveAndEnabled;
        public override void Process() { }
    }

    public sealed class InputUiRegressionTests
    {
        private GameObject root;
        private EventSystem events;
        private EventSystem previousEvents;
        private RegressionInput backend;
        private TerminalInputField field;
        private Text placeholder;
#if ENABLE_INPUT_SYSTEM
        private Keyboard keyboard;
        private InputSettings.UpdateMode previousUpdateMode;
#endif

        [SetUp]
        public void SetUp()
        {
#if ENABLE_INPUT_SYSTEM
            previousUpdateMode = InputSystem.settings.updateMode;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            keyboard = InputSystem.AddDevice<Keyboard>();
#endif
            previousEvents = EventSystem.current;
            root = new GameObject("Input regression", typeof(RectTransform), typeof(Canvas));
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var eventObject = new GameObject("Events", typeof(EventSystem));
            eventObject.transform.SetParent(root.transform);
            events = eventObject.GetComponent<EventSystem>();
            backend = eventObject.AddComponent<RegressionInput>();
            eventObject.AddComponent<RegressionInputModule>().inputOverride = backend;
            EventSystem.current = events;
            events.UpdateModules();
            Invoke(typeof(EventSystem), events, "Update");

            var input = new GameObject("Input", typeof(RectTransform), typeof(Image));
            input.transform.SetParent(root.transform, false);
            input.SetActive(false);
            field = input.AddComponent<TerminalInputField>();
            ((RectTransform)input.transform).sizeDelta = new Vector2(600, 50);
            field.textComponent = Label(input.transform, "Text");
            field.textComponent.alignment = TextAnchor.MiddleLeft;
            field.textComponent.horizontalOverflow = HorizontalWrapMode.Overflow;
            placeholder = Label(input.transform, "Placeholder");
            placeholder.text = "명령 입력 · 도움말 / 암호 이름 = 대상";
            field.placeholder = placeholder;
            input.SetActive(true);
            Focus();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            EventSystem.current = previousEvents;
#if ENABLE_INPUT_SYSTEM
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.updateMode = previousUpdateMode;
#endif
        }

        private void Focus()
        {
            field.FocusAtEnd();
            Invoke(typeof(CompletingInputField), field, "LateUpdate");
            field.ForceLabelUpdate();
            Canvas.ForceUpdateCanvases();
            Assert.That(field.isFocused, Is.True);
        }

        [Test]
        public void RegisteredInputRemainsProtectedAndReleasesAfterSelectionIsLost()
        {
            typeof(EventSystem).GetField("m_CurrentSelected", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(events, null);
            Assert.That(field.isFocused, Is.True);
            Assert.That(TextInputFocus.IsActive, Is.True);
            TextInputFocus.ReleaseAll();
            Assert.That(field.isFocused, Is.False);
            Assert.That(TextInputFocus.IsActive, Is.False);
        }

        [Test]
        public void EmptyArrowBrowsingStaysOpenAcrossActualControllerWorldRefreshes()
        {
            var runtime = new KernelRuntime();
            var controller = Controller(runtime, out GameObject panel);
            Invoke(typeof(TerminalController), controller, "MoveCompletion", 1);
            Assert.That(panel.activeSelf, Is.True);
            for (int i = 0; i < 5; i++)
            {
                runtime.Tick(1);
                Invoke(typeof(TerminalController), controller, "LateUpdate");
                Assert.That(panel.activeSelf, Is.True, "The default refresh must remember explicit empty-prompt browsing.");
                Invoke(typeof(TerminalController), controller, "MoveCompletion", 1);
            }
        }

        [TestCase(KeyCode.Space, ' ', false)]
        [TestCase(KeyCode.Space, ' ', true)]
        [TestCase(KeyCode.Tab, '\t', false)]
        [TestCase(KeyCode.Return, '\r', false)]
        public void PhysicalAndTextConfirmationEventsAcceptOnlyOnce(KeyCode key, char character, bool textFirst)
        {
            field.SetTextWithoutNotify("sa"); field.FocusAtEnd();
            field.CompletionQuery = _ => new InlineCompletion("save ", 5);
            int submissions = 0; field.LineSubmitted += _ => submissions++;
            var physical = new Event { type = EventType.KeyDown, keyCode = key };
            var text = new Event { type = EventType.KeyDown, character = character };
            if (key == KeyCode.Space) PumpSpace(textFirst ? text : physical, textFirst ? physical : text);
            else PumpInput(textFirst ? text : physical, textFirst ? physical : text);
            Assert.That(field.text, Is.EqualTo("save "));
            Assert.That(submissions, Is.Zero);
        }

        [Test]
        public void SpaceCompletionAndItsTextEchoDoNotLoseTheNextTypedName()
        {
            field.SetTextWithoutNotify("sa"); field.FocusAtEnd();
            field.CompletionQuery = _ => new InlineCompletion("save ", 5);
            PumpSpace(new Event { type = EventType.KeyDown, keyCode = KeyCode.Space },
                new Event { type = EventType.KeyDown, character = ' ' },
                new Event { type = EventType.KeyDown, character = 'x' });
            Assert.That(field.text, Is.EqualTo("save x"));
        }

#if ENABLE_INPUT_SYSTEM
        [TestCase(false)]
        [TestCase(true)]
        public void LateKeyAndCharacterEventsCannotInsertAnExtraSpace(bool useCompletion)
        {
            field.SetTextWithoutNotify("암"); field.FocusAtEnd();
            if (useCompletion) field.CompletionQuery = current => current.Text == "암" ? new InlineCompletion("암호  ", 4) : (InlineCompletion?)null;
            PumpSpace(); // The physical press can arrive without an IMGUI event.
            Assert.That(field.text, Is.EqualTo(useCompletion ? "암호 " : "암 "));
            typeof(CompletingInputField).GetField("handledFrame", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(field, -1);
            PumpInput(new Event { type = EventType.KeyDown, keyCode = KeyCode.Space },
                new Event { type = EventType.KeyDown, character = ' ' },
                new Event { type = EventType.KeyDown, keyCode = KeyCode.A, character = ' ' },
                new Event { type = EventType.KeyDown, character = 'x' });
            Assert.That(field.text, Is.EqualTo(useCompletion ? "암호 x" : "암 x"));
        }
#endif

        [Test]
        public void SpaceConfirmsLiveKoreanCompletionWithoutAppendingADelimiterEcho()
        {
            Compose("ㅇ"); field.CompletionQuery = _ => new InlineCompletion("암호 ", 3);
            PumpSpace(new Event { type = EventType.KeyDown, keyCode = KeyCode.Space },
                new Event { type = EventType.KeyDown, character = ' ' });
            Assert.That(field.text, Is.EqualTo("암호 "));
        }

        [Test]
        public void SpacesWithoutASuggestionRemainAvailableForIndentation()
        {
            field.lineType = InputField.LineType.MultiLineNewline;
            for (int i = 0; i < 2; i++) PumpSpace(new Event { type = EventType.KeyDown, character = ' ' });
            Assert.That(field.text, Is.EqualTo("  "));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void OrdinarySpaceIsTypedOnceWhetherTheBackendEmitsACharacterOrOnlyAKeyCode(bool hasText, bool textFirst)
        {
            field.SetTextWithoutNotify("암호"); field.FocusAtEnd();
            var physical = new Event { type = EventType.KeyDown, keyCode = KeyCode.Space };
            var text = new Event { type = EventType.KeyDown, character = ' ' };
            if (hasText) PumpSpace(textFirst ? text : physical, textFirst ? physical : text);
            else PumpSpace(physical);
            Assert.That(field.text, Is.EqualTo("암호 "));
        }

        [Test]
        public void SpaceCommitsHangulAndAddsOnlyOneSpaceWhenThereIsNoSuggestion()
        {
            Compose("호");
            PumpSpace(new Event { type = EventType.KeyDown, keyCode = KeyCode.Space },
                new Event { type = EventType.KeyDown, character = '호' }, new Event { type = EventType.KeyDown, character = ' ' });
            Assert.That(field.text, Is.EqualTo("호 "));
        }

        [Test]
        public void PressingEnterToFocusDoesNotSubmitTheExistingDraft()
        {
            var runtime = new KernelRuntime();
            TerminalController controller = Controller(runtime, out _);
            field.SetTextWithoutNotify("help");
            events.SetSelectedGameObject(null);
            Assert.That(controller.TryFocusCommandInput(), Is.True);
            Invoke(typeof(CompletingInputField), field, "LateUpdate");
            PumpInput(new Event { type = EventType.KeyDown, keyCode = KeyCode.Return });
            Assert.That(field.isFocused, Is.True);
            Assert.That(field.text, Is.EqualTo("help"));
        }

        [Test]
        public void ClickingBackIntoADraftPlacesACaretWithoutSelectingAllText()
        {
            field.SetTextWithoutNotify("연습 = 지휘소");
            events.SetSelectedGameObject(null);
            RectTransform text = field.textComponent.rectTransform;
            Vector2 point = RectTransformUtility.WorldToScreenPoint(null, text.TransformPoint(new Vector3(text.rect.xMin + 40f, text.rect.center.y)));
            field.OnPointerDown(new PointerEventData(events) { button = PointerEventData.InputButton.Left, position = point, pressPosition = point });
            Invoke(typeof(CompletingInputField), field, "LateUpdate");
            Assert.That(field.isFocused, Is.True);
            Assert.That(field.selectionAnchorPosition, Is.EqualTo(field.selectionFocusPosition));
            Assert.That(field.text, Is.EqualTo("연습 = 지휘소"));
        }

        [TestCase(KeyCode.Backspace)]
        [TestCase(KeyCode.Delete)]
        public void AReferenceUsesTheSavedCaretAndOneDeletionRemovesTheWholeLabel(KeyCode key)
        {
            field.SetTextWithoutNotify("result = .checkComms()");
            const int start = 9;
            field.selectionAnchorPosition = field.selectionFocusPosition = start;
            events.SetSelectedGameObject(null);
            Assert.That(field.InsertObjectReference("hq", "Operations HQ"), Is.True);
            Assert.That(field.text, Is.EqualTo("result = Operations HQ.checkComms()"));
            field.FocusDraft(); Invoke(typeof(CompletingInputField), field, "LateUpdate");
            if (key == KeyCode.Delete) field.selectionAnchorPosition = field.selectionFocusPosition = start;
            PumpInput(new Event { type = EventType.KeyDown, keyCode = key });
            Assert.That(field.text, Is.EqualTo("result = .checkComms()"));
            Assert.That(field.EncodeReferences(field.text), Is.EqualTo(field.text));
        }

        [Test]
        public void SubmittingAnOrdinaryCommandKeepsTheInputSelectedAndFocused()
        {
            var runtime = new KernelRuntime(); Controller(runtime, out _);
            Submit("sample = 1");
            Assert.That(runtime.Data.ContainsKey("sample"), Is.True);
            Assert.That(field.text, Is.Empty);
            Assert.That(events.currentSelectedGameObject, Is.EqualTo(field.gameObject));
            Assert.That(field.isFocused, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void KernelLogShowsCommandResultsWithoutMailOrClinicNotifications(bool duringDialogue)
        {
            var runtime = new KernelRuntime();
            TerminalController controller = Controller(runtime, out _);
            if (duringDialogue)
            {
                runtime.RegisterTerminalConversation(new TerminalConversationDefinition { id = "briefing", steps = new[] {
                    new TerminalConversationStep { id = "first", lines = new[] { new TerminalDialogueLine { text = "Briefing.", charactersPerSecond = 1 } } }
                } });
                runtime.EnterTerminalPage("kernel");
            }
            runtime.RegisterMail(new StoryMail { id = "mail", subject = "예약 문의", mailbox = Mailbox.Clinic });
            runtime.ReadMail("mail");
            runtime.Report("11:00 / 상담 손님 입실");
            Submit("note = 42");
            Invoke(typeof(TerminalController), controller, "LateUpdate");

            string log = controller.GetComponentInChildren<ScrollRect>().content.GetComponent<Text>().text;
            Assert.That(log, Does.Contain("> note = 42\n │ 42").And.Not.Contain("예약 문의").And.Not.Contain("손님 입실"));
        }

        [Test]
        public void WrappedCommandResponsesKeepTheirIndentOnEveryVisibleLine()
        {
            var controller = Controller(new KernelRuntime(), out _);
            Text log = controller.GetComponentInChildren<ScrollRect>().content.GetComponent<Text>();
            log.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 240);
            log.verticalOverflow = VerticalWrapMode.Overflow;
            log.horizontalOverflow = HorizontalWrapMode.Wrap;
            string longResponse = "The terminal keeps this lengthy response aligned beneath the command.\nA second paragraph follows.";
            var entry = TerminalTranscript.Join("\n", new[] { new TerminalTranscript("> help"), TerminalTranscript.Response(longResponse) });

            Invoke(typeof(TerminalController), controller, "DrawTranscript", entry);
            Canvas.ForceUpdateCanvases();
            string[] visibleLines = log.text.Split('\n');
            Assert.That(visibleLines.Length, Is.GreaterThan(3));
            Assert.That(visibleLines[0], Is.EqualTo("> help"));
            for (int i = 1; i < visibleLines.Length; i++) Assert.That(visibleLines[i], Does.StartWith(" │ "));
            Assert.That(log.cachedTextGenerator.lines.Count, Is.EqualTo(visibleLines.Length));
        }

        [Test]
        public void CommandExampleUsesATextAlignedFrameWithoutAddingAHitTarget()
        {
            var controller = Controller(new KernelRuntime(), out _);
            Text log = controller.GetComponentInChildren<ScrollRect>().content.GetComponent<Text>();
            log.verticalOverflow = VerticalWrapMode.Overflow;
            var speech = TerminalTranscript.Spoken(new ObjectReferenceText("Enter help."), new[] { (6, 4) });

            Invoke(typeof(TerminalController), controller, "DrawTranscript", speech);
            Canvas.ForceUpdateCanvases();
            Assert.That(log.text, Is.EqualTo("Enter \u00A0help\u00A0."));
            Graphic frames = log.transform.Find("CommandExampleFrames").GetComponent<Graphic>();
            Assert.That(frames.raycastTarget, Is.False);
            Assert.That(frames.GetComponent<CrtMeshEffect>(), Is.Not.Null);
            using (var border = new VertexHelper())
            {
                Invoke(frames.GetType(), frames, "OnPopulateMesh", border);
                Assert.That(border.currentVertCount, Is.EqualTo(16));
            }
            Invoke(typeof(TerminalController), controller, "DrawTranscript", new TerminalTranscript("> help"));
            using (var border = new VertexHelper())
            {
                Invoke(frames.GetType(), frames, "OnPopulateMesh", border);
                Assert.That(border.currentVertCount, Is.Zero);
            }
        }

        [Test]
        public void SubmittingAComposedNameRestoresFocusAndConsumesTheDelayedImeCommit()
        {
            var runtime = new KernelRuntime(); Controller(runtime, out _);
            runtime.Store("단", Datum.Number(1));
            field.SetTextWithoutNotify("sample = "); field.FocusAtEnd(); Compose("단");
            field.CompletionQuery = _ => null;
            PumpInput(new Event { type = EventType.KeyDown, keyCode = KeyCode.Return });
            Invoke(typeof(CompletingInputField), field, "LateUpdate");
            PumpInput(new Event { type = EventType.KeyDown, character = '단' });
            Assert.That(runtime.Data["sample"].value.number, Is.EqualTo(1));
            Assert.That(field.text, Is.Empty);
            Assert.That(field.isFocused, Is.True);
            Assert.That(events.currentSelectedGameObject, Is.EqualTo(field.gameObject));
        }

        [Test]
        public void ADialogueReplyKeepsTheSubmittedCommandFieldFocused()
        {
            KernelRuntime runtime = Dialogue(); var controller = Controller(runtime, out _);
            Submit("help");
            Assert.That(runtime.TerminalLineInProgress, Is.True);
            Assert.That(field.isFocused, Is.True);
            Assert.That(field.readOnly, Is.False);
            runtime.AdvanceTerminalConversation(1000);
            Invoke(typeof(TerminalController), controller, "LateUpdate");
            Invoke(typeof(CompletingInputField), field, "LateUpdate");
            Assert.That(field.interactable, Is.True);
            Assert.That(events.currentSelectedGameObject, Is.EqualTo(field.gameObject));
            Assert.That(field.isFocused, Is.True);
        }

        [TestCase("")]
        [TestCase("note = 1")]
        [TestCase(" ")]
        public void EnterOnlyAdvancesDialogueWhenTheCommandDraftIsEmpty(string draft)
        {
            var runtime = new KernelRuntime();
            runtime.RegisterTerminalConversation(new TerminalConversationDefinition
            {
                id = "briefing", advanceWithEnter = true, steps = new[] { new TerminalConversationStep
                {
                    id = "lines", lines = new[] { new TerminalDialogueLine { text = "First briefing." }, new TerminalDialogueLine { text = "Second briefing." } }
                } }
            });
            runtime.EnterTerminalPage("kernel");
            TerminalController controller = Controller(runtime, out _);
            field.SetTextWithoutNotify(draft);
            field.FocusAtEnd();
            // A candidate offered at the start of the line must not take the dialogue's Enter.
            field.CompletionQuery = current => current.Text.Length == 0 ? new InlineCompletion("help", 4) : (InlineCompletion?)null;
            if (draft.Length > 0) Assert.That(controller.TryContinueConversation(), Is.False);

            PumpInput(new Event { type = EventType.KeyDown, keyCode = KeyCode.Return });

            Assert.That(runtime.TerminalConversationTranscript().Contains("First briefing."), Is.EqualTo(draft.Length == 0));
            Assert.That(runtime.Data.ContainsKey("note"), Is.EqualTo(draft == "note = 1"));
            Assert.That(field.text, Is.EqualTo(draft == " " ? " " : ""));
            Assert.That(runtime.TerminalLineInProgress, Is.True);
        }

        [Test]
        public void RevealingDialogueWithEnterScrollsToTheLatestLine()
        {
            var runtime = new KernelRuntime();
            string dialogue = new string('A', 500) + "\nThis is the last line.";
            runtime.RegisterTerminalConversation(new TerminalConversationDefinition
            {
                id = "scroll", advanceWithEnter = true, steps = new[] { new TerminalConversationStep
                {
                    id = "instructions", lines = new[] { new TerminalDialogueLine { text = dialogue, charactersPerSecond = 1 } }
                } }
            });
            runtime.EnterTerminalPage("kernel");
            TerminalController controller = Controller(runtime, out _);
            ScrollRect scroll = controller.GetComponentInChildren<ScrollRect>();
            ((RectTransform)scroll.transform).sizeDelta = new Vector2(600, 100);
            scroll.content.anchorMin = scroll.content.anchorMax = scroll.content.pivot = new Vector2(0, 1);
            scroll.content.sizeDelta = new Vector2(580, 400);
            Invoke(typeof(TerminalController), controller, "LateUpdate");
            scroll.verticalNormalizedPosition = 1;
            Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(1).Within(0.001f));

            Assert.That(controller.TryContinueConversation(), Is.True);
            Invoke(typeof(TerminalController), controller, "LateUpdate");
            Assert.That(scroll.content.GetComponent<Text>().text, Does.Contain("This is the last line."));
            Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(0).Within(0.001f));
        }

        [Test]
        public void CommandButtonAdvancesAnEmptyDialogueAndSendsTheComposedDraft()
        {
            var runtime = new KernelRuntime { Language = CommandLocale.Korean };
            runtime.RegisterTerminalConversation(new TerminalConversationDefinition
            {
                id = "command-button",
                advanceWithEnter = true,
                steps = new[] { new TerminalConversationStep
                {
                    id = "help",
                    lines = new[] { new TerminalDialogueLine { text = "연결되었습니다." }, new TerminalDialogueLine { text = "도움말을 입력하십시오." } },
                    goal = new TerminalCommandGoal { kind = TerminalGoalKind.Help }
                } },
                onComplete = new StoryEffects { triggers = new[] { new TriggerValue { id = "command-button.complete", value = true } } }
            });
            TerminalController controller = Controller(runtime, out _);
            Invoke(typeof(TerminalController), controller, "LateUpdate");
            var action = field.transform.Find("CommandAction").GetComponent<Button>();
            var label = action.GetComponentInChildren<Text>();
            Assert.That(label.text, Is.EqualTo("Send")); Assert.That(action.interactable, Is.False);

            runtime.EnterTerminalPage("kernel"); runtime.AdvanceTerminalConversation(1000);
            Invoke(typeof(TerminalController), controller, "LateUpdate");
            Assert.That(placeholder.text, Is.EqualTo("입력 대기중.."));
            Assert.That(label.text, Is.EqualTo("Next")); Assert.That(action.interactable, Is.True);
            field.text = "도움말";
            Assert.That(label.text, Is.EqualTo("Send")); Assert.That(action.interactable, Is.True);
            field.text = "";
            action.onClick.Invoke();
            Assert.That(runtime.ConversationProgress("command-button").pendingIndex, Is.EqualTo(1));
            runtime.AdvanceTerminalConversation(1000);
            Invoke(typeof(TerminalController), controller, "LateUpdate");
            Assert.That(label.text, Is.EqualTo("Send")); Assert.That(action.interactable, Is.False);
            Assert.That(runtime.TerminalExpectedGoal, Is.EqualTo(TerminalGoalKind.Help));

            field.text = "도움"; Focus(); Compose("말");
            Invoke(typeof(TerminalController), controller, "LateUpdate");
            Assert.That(field.CompletionInput.Text, Is.EqualTo("도움말")); Assert.That(action.interactable, Is.True);
            action.onClick.Invoke();
            Assert.That(runtime.GetTrigger("command-button.complete"), Is.True);
            Assert.That(field.text, Is.Empty);
            Assert.That(action.interactable, Is.False);
            Invoke(typeof(CompletingInputField), field, "LateUpdate");
            Assert.That(field.isFocused, Is.True);
        }

        [Test]
        public void ClickingAnAnswerFillsTheOrdinaryTerminalFieldAndEnterSendsIt()
        {
            var runtime = new KernelRuntime { Language = CommandLocale.Korean };
            runtime.RegisterTerminalConversation(new TerminalConversationDefinition
            {
                id = "question",
                steps = new[] { new TerminalConversationStep
                {
                    id = "oath",
                    lines = new[] { new TerminalDialogueLine { text = "맹세할 수 있습니까?" } },
                    answers = new[] {
                        new TerminalAnswer { key = "y" },
                        new TerminalAnswer { key = "n",
                            effects = new StoryEffects { triggers = new[] { new TriggerValue { id = "declined", value = true } } } }
                    }
                } }
            });
            runtime.EnterTerminalPage("kernel");
            runtime.AdvanceTerminalConversation(1000);
            TerminalController controller = Controller(runtime, out GameObject completionPanel);
            Invoke(typeof(TerminalController), controller, "LateUpdate");
            Assert.That(placeholder.text, Is.EqualTo("입력 대기중.."));
            Transform options = field.transform.Find("TerminalAnswerOptions");
            Assert.That(options.GetComponentInChildren<Text>().text, Is.EqualTo("대답>"));
            Assert.That(runtime.Execute("도움말").Success, Is.True);
            Invoke(typeof(TerminalController), controller, "LateUpdate");
            Assert.That(options.gameObject.activeSelf, Is.True);
            GameObject tooltip = options.Find("TerminalQuestion").gameObject;
            Assert.That(tooltip.activeSelf, Is.False);
            var pointer = new PointerEventData(events);
            ExecuteEvents.Execute(options.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
            Assert.That(tooltip.GetComponentInChildren<Text>().text, Is.EqualTo("맹세할 수 있습니까?"));
            Assert.That(tooltip.GetComponent<Image>().raycastTarget, Is.True);
            Assert.That(tooltip.GetComponent<Image>().color.grayscale, Is.GreaterThan(options.GetComponent<Image>().color.grayscale));
            var panel = (RectTransform)tooltip.transform;
            float shortHeight = panel.rect.height;
            Assert.That(shortHeight, Is.LessThan(80));
            Invoke(typeof(TerminalController), controller, "LayoutQuestion", "첫째 줄\n둘째 줄\n셋째 줄");
            Assert.That(panel.rect.height, Is.GreaterThan(shortHeight));

            pointer.pointerCurrentRaycast = new RaycastResult { gameObject = tooltip };
            ExecuteEvents.Execute(options.gameObject, pointer, ExecuteEvents.pointerExitHandler);
            Assert.That(tooltip.activeSelf, Is.True);
            pointer.pointerCurrentRaycast = new RaycastResult { gameObject = field.gameObject };
            ExecuteEvents.Execute(options.gameObject, pointer, ExecuteEvents.pointerExitHandler);
            Assert.That(tooltip.activeSelf, Is.False);
            field.SetTextWithoutNotify("previous draft");
            options.Find("AnswerChoices/[n]").GetComponent<Button>().onClick.Invoke();
            Assert.That(field.text, Is.EqualTo("n"));
            Assert.That(runtime.TerminalAwaitingAnswer, Is.True);
            Assert.That(runtime.GetTrigger("declined"), Is.False);
            Assert.That(field.isFocused, Is.True);
            // The next physical Enter occurs after the button click's focus guard.
            typeof(CompletingInputField).GetField("ignoredEnterFrame", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(field, -1);
            PumpInput(new Event { type = EventType.KeyDown, keyCode = KeyCode.Return });
            Assert.That(runtime.GetTrigger("declined"), Is.True);
            Assert.That(runtime.TerminalAwaitingAnswer, Is.False);
            Assert.That(completionPanel.activeSelf, Is.False);
            Assert.That(field.text, Is.Empty);
            Assert.That(field.isFocused, Is.True);
        }

        [TestCase("page")]
        [TestCase("interaction")]
        [TestCase("selection")]
        public void LeavingDuringADialogueReplyDoesNotRefocusTheCommandField(string destination)
        {
            KernelRuntime runtime = Dialogue(); var controller = Controller(runtime, out _);
            Submit("help");
            Assert.That(runtime.TerminalLineInProgress, Is.True);
            if (destination == "page") { controller.SetPageVisible(false); controller.SetPageVisible(true); }
            else if (destination == "interaction") { controller.SetInteractionEnabled(false); controller.SetInteractionEnabled(true); }
            else
            {
                var other = new GameObject("Other control", typeof(RectTransform), typeof(Button)); other.transform.SetParent(root.transform);
                events.SetSelectedGameObject(other);
                Invoke(typeof(TerminalController), controller, "LateUpdate");
                events.SetSelectedGameObject(null);
            }
            runtime.AdvanceTerminalConversation(1000);
            Invoke(typeof(TerminalController), controller, "LateUpdate");
            Invoke(typeof(CompletingInputField), field, "LateUpdate");
            Assert.That(field.interactable, Is.True);
            Assert.That(field.isFocused, Is.False);
            Assert.That(events.currentSelectedGameObject, Is.Null);
        }

        private static KernelRuntime Dialogue()
        {
            var runtime = new KernelRuntime();
            runtime.RegisterTerminalConversation(new TerminalConversationDefinition { id = "focus", steps = new[] {
                new TerminalConversationStep { id = "command", lines = new[] { new TerminalDialogueLine { text = "Enter help." } },
                    goal = new TerminalCommandGoal { kind = TerminalGoalKind.Help } },
                new TerminalConversationStep { id = "reply", lines = new[] { new TerminalDialogueLine { text = "Receiving the next instructions.", charactersPerSecond = 1 } } }
            } });
            runtime.EnterTerminalPage("kernel"); runtime.AdvanceTerminalConversation(1000);
            Assert.That(runtime.TerminalInputLocked, Is.False);
            return runtime;
        }

        private void Submit(string command)
        {
            field.SetTextWithoutNotify(command); field.FocusAtEnd();
            field.CompletionQuery = _ => null;
            PumpInput(new Event { type = EventType.KeyDown, keyCode = KeyCode.Return });
        }

        private void PumpInput(params Event[] keys)
        {
            foreach (Event key in keys) Invoke(typeof(CompletingInputField), field, "ProcessKeyDown", key);
            Invoke(typeof(CompletingInputField), field, "LateUpdate");
        }

        private void PumpSpace(params Event[] keys)
        {
#if ENABLE_INPUT_SYSTEM
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
            InputSystem.Update();
            try { PumpInput(keys); }
            finally
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.Update();
            }
#else
            PumpInput(keys);
#endif
        }

        private TerminalController Controller(KernelRuntime runtime, out GameObject panel)
        {
            var host = new GameObject("Controller"); host.transform.SetParent(root.transform); host.SetActive(false);
            var controller = host.AddComponent<TerminalController>();
            panel = new GameObject("Candidates", typeof(RectTransform)); panel.transform.SetParent(root.transform);
            var buttons = new Button[3]; var labels = new Text[3];
            for (int i = 0; i < 3; i++)
            {
                var button = new GameObject("Candidate", typeof(RectTransform), typeof(Image), typeof(Button));
                button.transform.SetParent(panel.transform); buttons[i] = button.GetComponent<Button>();
                buttons[i].targetGraphic = button.GetComponent<Image>(); labels[i] = Label(button.transform, "Text");
            }
            Set(controller, "input", field);
            Set(controller, "completionPanel", panel); Set(controller, "completionHeading", Label(panel.transform, "Heading"));
            Set(controller, "completionButtons", buttons); Set(controller, "completionLabels", labels);
            var scroll = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect)); scroll.transform.SetParent(host.transform);
            var transcript = Label(scroll.transform, "Transcript");
            scroll.GetComponent<ScrollRect>().content = transcript.rectTransform;
            Set(controller, "transcript", transcript); Set(controller, "transcriptScroll", scroll.GetComponent<ScrollRect>());
            controller.AttachRuntime(runtime);
            host.SetActive(true);
            return controller;
        }

        [Test]
        public void EmptyGhostHidesThePlaceholderAndRestoresItWhenDismissed()
        {
            field.SetInlinePreview(new InlineCompletion("저장 ", 3));
            Invoke(typeof(CompletingInputField), field, "DrawInlinePreview");
            Assert.That(placeholder.enabled, Is.False);
            Assert.That(Ghost().text, Is.EqualTo("저장 "));
            field.ForceLabelUpdate(); // uGUI re-enables the placeholder when actual input is empty.
            Canvas.ForceUpdateCanvases();
            Assert.That(placeholder.gameObject.activeInHierarchy, Is.False);
            field.SetInlinePreview(null);
            Assert.That(placeholder.enabled, Is.True);
            Assert.That(placeholder.gameObject.activeInHierarchy, Is.True);
        }

        [Test]
        public void APartialKoreanSyllableNeverFadesCommittedTextOrDuplicatesItInTheGhost()
        {
            field.SetTextWithoutNotify("삭제 "); field.FocusAtEnd();
            Compose("다");
            field.SetInlinePreview(new InlineCompletion("삭제 단서", 5));
            Invoke(typeof(CompletingInputField), field, "DrawInlinePreview");
            Assert.That(field.textComponent.canvasRenderer.GetAlpha(), Is.EqualTo(1f));
            Assert.That(Ghost().text, Is.EqualTo("서"));
            Assert.That(field.text, Is.EqualTo("삭제 "));
        }

        [Test]
        public void RefocusingDoesNotReappendOldCompositionAndAllTextCanBeSelectedAndDeleted()
        {
            field.SetTextWithoutNotify("저장 "); field.FocusAtEnd();
            Compose("단");
            events.SetSelectedGameObject(null);
            Assert.That(field.text, Is.EqualTo("저장 단"));
            // The simulated backend intentionally keeps its last composition string.
            Focus();
            Assert.That(field.CompletionInput.Text, Is.EqualTo("저장 단"));
            field.selectionAnchorPosition = 0; field.selectionFocusPosition = field.text.Length;
            Assert.That(field.selectionAnchorPosition, Is.Zero);
            field.ProcessEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.Backspace });
            field.ForceLabelUpdate();
            Assert.That(field.text, Is.Empty);
            Assert.That(field.textComponent.text, Is.Empty);
            Assert.That(field.CompletionInput.Text, Is.Empty);
            events.SetSelectedGameObject(null); Focus();
            Assert.That(field.textComponent.text, Is.Empty);
        }

        private Text Ghost() => field.textComponent.transform.Find("InlineCompletionPreview").GetComponent<Text>();

        [Test]
        public void StartingKoreanCompositionAfterSelectAllImmediatelyReplacesTheDraft()
        {
            field.SetTextWithoutNotify("이전 "); field.FocusAtEnd(); Compose("단");
            EventModifiers modifier = SystemInfo.operatingSystemFamily == OperatingSystemFamily.MacOSX ? EventModifiers.Command : EventModifiers.Control;
            PumpInput(new Event { type = EventType.KeyDown, keyCode = KeyCode.A, modifiers = modifier });
            Assert.That(field.text, Is.EqualTo("이전 단"));
            Assert.That(Math.Abs(field.selectionAnchorPosition - field.selectionFocusPosition), Is.EqualTo(field.text.Length));

            Compose("ㄴ");
            PumpInput(new Event { type = EventType.KeyDown, character = '단' });
            Assert.That(field.text, Is.Empty);
            Assert.That(field.CompletionInput.Text, Is.EqualTo("ㄴ"));
            Assert.That(field.textComponent.text, Is.EqualTo("ㄴ"));

            Compose("나");
            Invoke(typeof(CompletingInputField), field, "CommitComposition", false);
            Assert.That(field.text, Is.EqualTo("나"));
            Assert.That(field.textComponent.text, Is.EqualTo("나"));
        }

        [Test]
        public void MovingTheCaretAfterComposingCommitsTheSyllableIntoOrdinaryEditableText()
        {
            field.SetTextWithoutNotify("저장 "); field.FocusAtEnd(); Compose("단");
            Invoke(typeof(CompletingInputField), field, "CommitComposition", false);
            field.selectionAnchorPosition = 0; field.selectionFocusPosition = 1;
            field.ProcessEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.Backspace });
            field.ForceLabelUpdate();
            Assert.That(field.text, Is.EqualTo("장 단"));
            Assert.That(field.CompletionInput.Text, Is.EqualTo("장 단"));
            field.selectionAnchorPosition = field.selectionFocusPosition = field.text.Length;
            field.ForceLabelUpdate();
            Assert.That(field.textComponent.text, Is.EqualTo("장 단"));
        }
        private static Text Label(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.fontSize = 24;
            text.rectTransform.sizeDelta = new Vector2(580, 46); return text;
        }
        private void Compose(string value)
        {
            backend.Composition = value;
            var session = (ImeSession)typeof(CompletingInputField).GetField("imeSession", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(field);
            session.CompositionChanged(value);
            field.ForceLabelUpdate();
        }
        private static void Set(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static void Invoke(Type type, object target, string name, params object[] arguments) =>
            type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, arguments);
    }
}
