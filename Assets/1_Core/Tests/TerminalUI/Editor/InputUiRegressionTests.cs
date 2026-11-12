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
            placeholder.text = "명령 입력 · 도움말 / 저장 이름";
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
        public void EmptyArrowBrowsingStaysOpenAcrossActualControllerWorldRefreshes()
        {
            var host = new GameObject("Controller"); host.transform.SetParent(root.transform); host.SetActive(false);
            var controller = host.AddComponent<TerminalController>();
            var panel = new GameObject("Candidates"); panel.transform.SetParent(root.transform);
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
            var runtime = new KernelRuntime(); controller.AttachRuntime(runtime);
            host.SetActive(true);
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
