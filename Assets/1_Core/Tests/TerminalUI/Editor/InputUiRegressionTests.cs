using System;
using System.Reflection;
using Codetective.Operations;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
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

        [SetUp]
        public void SetUp()
        {
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
            PumpInput(textFirst ? text : physical, textFirst ? physical : text);
            Assert.That(field.text, Is.EqualTo("save "));
            Assert.That(submissions, Is.Zero);
        }

        [Test]
        public void APolledSpaceAndItsTextEchoDoNotLoseTheNextTypedName()
        {
            field.SetTextWithoutNotify("sa"); field.FocusAtEnd();
            field.CompletionQuery = _ => new InlineCompletion("save ", 5);
            Invoke(typeof(CompletingInputField), field, "Poll", CompletionKey.Space);
            PumpInput(new Event { type = EventType.KeyDown, character = ' ' },
                new Event { type = EventType.KeyDown, character = 'x' });
            Assert.That(field.text, Is.EqualTo("save x"));
        }

        [Test]
        public void SpaceConfirmsLiveKoreanCompletionWithoutAppendingADelimiterEcho()
        {
            Compose("ㅇ"); field.CompletionQuery = _ => new InlineCompletion("암호 ", 3);
            PumpInput(new Event { type = EventType.KeyDown, keyCode = KeyCode.Space },
                new Event { type = EventType.KeyDown, character = ' ' });
            Assert.That(field.text, Is.EqualTo("암호 "));
        }

        [Test]
        public void SpacesWithoutASuggestionRemainAvailableForIndentation()
        {
            field.lineType = InputField.LineType.MultiLineNewline;
            PumpInput(new Event { type = EventType.KeyDown, character = ' ' },
                new Event { type = EventType.KeyDown, character = ' ' });
            Assert.That(field.text, Is.EqualTo("  "));
        }

        [Test]
        public void SubmittingAnOrdinaryCommandKeepsTheInputSelectedAndFocused()
        {
            var runtime = new KernelRuntime(); Controller(runtime, out _);
            Submit("save sample");
            Assert.That(runtime.Data.ContainsKey("sample"), Is.True);
            Assert.That(field.text, Is.Empty);
            Assert.That(events.currentSelectedGameObject, Is.EqualTo(field.gameObject));
            Assert.That(field.isFocused, Is.True);
        }

        [Test]
        public void SubmittingAComposedNameRestoresFocusAndConsumesTheDelayedImeCommit()
        {
            var runtime = new KernelRuntime(); Controller(runtime, out _);
            field.SetTextWithoutNotify("save "); field.FocusAtEnd(); Compose("단");
            field.CompletionQuery = _ => null;
            PumpInput(new Event { type = EventType.KeyDown, keyCode = KeyCode.Return });
            Invoke(typeof(CompletingInputField), field, "LateUpdate");
            PumpInput(new Event { type = EventType.KeyDown, character = '단' });
            Assert.That(runtime.Data.ContainsKey("단"), Is.True);
            Assert.That(field.text, Is.Empty);
            Assert.That(field.isFocused, Is.True);
            Assert.That(events.currentSelectedGameObject, Is.EqualTo(field.gameObject));
        }

        [Test]
        public void ADialogueReplyRestoresTheSubmittedCommandFocusAfterUnlocking()
        {
            KernelRuntime runtime = Dialogue(); var controller = Controller(runtime, out _);
            Submit("help");
            Assert.That(runtime.TerminalInputLocked, Is.True);
            Assert.That(field.isFocused, Is.False);
            runtime.AdvanceTerminalConversation(1000);
            Invoke(typeof(TerminalController), controller, "LateUpdate");
            Invoke(typeof(CompletingInputField), field, "LateUpdate");
            Assert.That(field.interactable, Is.True);
            Assert.That(events.currentSelectedGameObject, Is.EqualTo(field.gameObject));
            Assert.That(field.isFocused, Is.True);
        }

        [Test]
        public void ADialogueAnswerIsSentWithEnterFromTheOrdinaryTerminalField()
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
            Assert.That(placeholder.text, Is.EqualTo("y / n"));
            field.SetTextWithoutNotify("n");
            field.FocusAtEnd();
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
        public void LeavingDuringADialogueReplyCancelsCommandFocusRestoration(string destination)
        {
            KernelRuntime runtime = Dialogue(); var controller = Controller(runtime, out _);
            Submit("help");
            Assert.That(runtime.TerminalInputLocked, Is.True);
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

        private TerminalController Controller(KernelRuntime runtime, out GameObject panel)
        {
            var host = new GameObject("Controller"); host.transform.SetParent(root.transform); host.SetActive(false);
            var controller = host.AddComponent<TerminalController>();
            panel = new GameObject("Candidates"); panel.transform.SetParent(root.transform);
            var buttons = new Button[3]; var labels = new Text[3];
            for (int i = 0; i < 3; i++)
            {
                var button = new GameObject("Candidate", typeof(RectTransform), typeof(Image), typeof(Button));
                button.transform.SetParent(panel.transform); buttons[i] = button.GetComponent<Button>();
                buttons[i].targetGraphic = button.GetComponent<Image>(); labels[i] = Label(button.transform, "Text");
            }
            Set(controller, "input", field); Set(controller, "memoryList", Label(host.transform, "Memory"));
            Set(controller, "memoryHeading", Label(host.transform, "Heading"));
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
