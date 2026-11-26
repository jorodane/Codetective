using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Codetective.Operations;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    public sealed partial class TerminalController
    {
        private readonly HashSet<string> mergedConversations = new HashSet<string>(StringComparer.Ordinal);
        private long renderedConversationRevision = -1;
        private string renderedConversationLocale;
        private string renderedConversationId;
        private CommandLocale renderedConversationCommandLocale;
        private bool conversationInputLocked;
        private CrtScreen conversationScreen;
        private ObjectReferenceTextView transcriptLinks;
        private const int TranscriptCharacterLimit = 12000;
        private const string ResponseIndent = " │ ";
        private readonly Dictionary<string, string> responseLayouts = new Dictionary<string, string>(StringComparer.Ordinal);
        private float renderedTranscriptWidth = -1f, responseLayoutWidth = -1f;
        private const float AnswerInset = 40f;
        private static readonly Vector2 QuestionPadding = new Vector2(12, 9);
        private RectTransform answerRow, answerChoices, questionPanel;
        private Text answerOptions, answerQuestion;
        private string[] visibleAnswerKeys = Array.Empty<string>();
        private float logBottom, scrollbarBottom, completionY;

        private void ResetConversationPresentation()
        {
            mergedConversations.Clear(); renderedConversationRevision = -1; renderedConversationLocale = null; renderedConversationId = null;
            contactRevision = -1; conversationInputLocked = false; restoreCommandFocus = false; ClearConversationDisturbance();
        }
        private void ClearConversationDisturbance()
        { if (conversationScreen != null) conversationScreen.SetConversationDisturbance(0); }
        private void MergeCompletedConversation()
        {
            KernelRuntime runtime = session.Runtime;
            if (!runtime.TerminalConversationCompleted || !mergedConversations.Add(runtime.TerminalConversationId)) return;
            TerminalTranscript log = runtime.TerminalConversationForConsole();
            if (!string.IsNullOrWhiteSpace(log.Text)) AppendHistoryTranscript(log);
        }
        private TerminalTranscript ConversationTranscript()
        {
            KernelRuntime runtime = session.Runtime;
            TerminalTranscript content = history.Transcript;
            if (runtime.TerminalConversationActive || runtime.TerminalConversationSuspended)
            {
                TerminalTranscript log = runtime.TerminalConversationForConsole();
                if (log.Text.Length > 0) content = content.Text.Length > 0 ? TerminalTranscript.Join("\n\n", new[] { content, log }) : log;
            }
            // Keep the combined uGUI Text mesh within the same budget as ordinary scrollback.
            int start = BoundedTranscriptStart(content.Text);
            return start > 0 ? content.Slice(start, content.Text.Length - start) : content;
        }
        private static int BoundedTranscriptStart(string text)
        {
            int start = Math.Max(0, text.Length - TranscriptCharacterLimit);
            return start > 0 && char.IsLowSurrogate(text[start]) ? start + 1 : start;
        }
        private void DrawTranscript(TerminalTranscript content)
        {
            if (transcriptLinks == null) transcriptLinks = ObjectReferenceTextView.Attach(transcript, (id, label) => TryInsertObjectReference(id, label));
            ObjectReferenceText rendered = FormatTranscript(content);
            int start = BoundedTranscriptStart(rendered.Text);
            if (start > 0) rendered = rendered.Slice(start, rendered.Text.Length - start);
            if (rendered.Text != transcript.text) QueueTranscriptScroll();
            transcriptLinks.Show(rendered, Runtime.CanLinkObject);
            renderedTranscriptWidth = transcript.rectTransform.rect.width;
        }

        private ObjectReferenceText FormatTranscript(TerminalTranscript transcriptContent)
        {
            if (transcriptContent.Responses.Count == 0) return transcriptContent.Content;
            float width = transcript.rectTransform.rect.width;
            if (!Mathf.Approximately(responseLayoutWidth, width) || responseLayouts.Count >= 256)
            { responseLayouts.Clear(); responseLayoutWidth = width; }

            var parts = new List<ObjectReferenceText>();
            int offset = 0;
            foreach ((int start, int length) in transcriptContent.Responses)
            {
                if (start > offset) parts.Add(transcriptContent.Content.Slice(offset, start - offset));
                string response = transcriptContent.Text.Substring(start, length);
                if (!responseLayouts.TryGetValue(response, out string formatted))
                    responseLayouts.Add(response, formatted = WrapResponse(response, width));
                parts.Add(new ObjectReferenceText(formatted));
                offset = start + length;
            }
            if (offset < transcriptContent.Text.Length) parts.Add(transcriptContent.Content.Slice(offset, transcriptContent.Text.Length - offset));
            return ObjectReferenceText.Join("", parts);
        }

        private string WrapResponse(string response, float width)
        {
            string text = response.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
            var settings = transcript.GetGenerationSettings(new Vector2(width, 0));
            TextGenerator layout = transcript.cachedTextGeneratorForLayout;
            float indent = (layout.GetPreferredWidth(ResponseIndent + "X", settings) - layout.GetPreferredWidth("X", settings)) / transcript.pixelsPerUnit;
            settings.generationExtents.x = Mathf.Max(1, width - indent - 2);
            settings.horizontalOverflow = HorizontalWrapMode.Wrap;
            settings.verticalOverflow = VerticalWrapMode.Overflow;
            if (!layout.Populate(text, settings)) return ResponseIndent + text.Replace("\n", "\n" + ResponseIndent);

            var wrapped = new StringBuilder();
            int start = 0;
            foreach (UILineInfo line in layout.lines)
            {
                int next = line.startCharIdx;
                if (next <= start || next >= text.Length) continue;
                wrapped.Append(ResponseIndent).Append(text.Substring(start, next - start).TrimEnd(' ', '\t', '\n')).Append('\n');
                start = next;
            }
            return wrapped.Append(ResponseIndent).Append(text.Substring(start)).ToString();
        }
        private void UpdateConversationPresentation()
        {
            KernelRuntime runtime = session.Runtime;
            bool visible = pageVisible && interactionEnabled && isActiveAndEnabled && Time.timeScale > 0;
            runtime.AdvanceTerminalConversation(Time.deltaTime, visible);
            RefreshConnectionPresentation();
            if (runtime.TerminalConversationActive && renderedConversationId != runtime.TerminalConversationId)
            { renderedConversationId = runtime.TerminalConversationId; QueueTranscriptScroll(toLatest: true); }
            if (conversationInputLocked != CommandInputBlocked)
            {
                if (visible && !CommandInputBlocked && runtime.TerminalConversationActive)
                    restoreCommandFocus = true;
                ApplyInputInteraction(); runtimeRevision = -1;
            }
            if (conversationScreen == null) conversationScreen = GetComponent<CrtScreen>();
            if (conversationScreen != null) conversationScreen.SetConversationDisturbance(visible ? (float)runtime.TerminalDisturbance : 0);
            MergeCompletedConversation();
            if (renderedConversationRevision == runtime.TerminalConversationRevision && renderedConversationLocale == runtime.DisplayLanguage && renderedConversationCommandLocale == runtime.Language) return;
            renderedConversationRevision = runtime.TerminalConversationRevision; renderedConversationLocale = runtime.DisplayLanguage; renderedConversationCommandLocale = runtime.Language;
            RefreshCommandPrompt();
            RefreshAnswerPrompt();
            DrawTranscript(ConversationTranscript());
        }

        private void RefreshCommandPrompt()
        {
            if (input.placeholder is Text placeholder)
                placeholder.text = Runtime.DisplayLanguage == "en" ? "Awaiting input.." : "입력 대기중..";
        }

        private bool CanContinueConversation() => AcceptsInput && isActiveAndEnabled && Time.timeScale > 0f && Runtime.TerminalCanContinueLine;

        public bool TryContinueConversation()
        {
            if (!CanContinueConversation() || input.IsCompositionProtected || input.CompletionInput.Text.Length != 0 ||
                !Runtime.ContinueTerminalLine()) return false;
            QueueTranscriptScroll(toLatest: true);
            RefreshCommandAction(input.CompletionInput);
            // The Enter that advances the dialogue must not also reach the selected button or input.
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != input.gameObject)
                EventSystem.current.SetSelectedGameObject(null);
            return true;
        }

        private void RefreshAnswerPrompt()
        {
            KernelRuntime runtime = session.Runtime;
            bool waiting = pageVisible && runtime.TerminalAwaitingAnswer;
            if (answerRow == null)
            {
                if (!waiting) return;
                BuildAnswerPrompt();
            }
            if (answerRow.gameObject.activeSelf != waiting)
            {
                QueueTranscriptScroll();
                answerRow.gameObject.SetActive(waiting);
                float inset = waiting ? AnswerInset : 0;
                RectTransform viewport = TranscriptViewport;
                viewport.offsetMin = new Vector2(viewport.offsetMin.x, logBottom + inset);
                if (transcriptScroll.verticalScrollbar != null)
                {
                    var scrollbar = (RectTransform)transcriptScroll.verticalScrollbar.transform;
                    scrollbar.offsetMin = new Vector2(scrollbar.offsetMin.x, scrollbarBottom + inset);
                }
                var candidates = (RectTransform)completionPanel.transform;
                candidates.anchoredPosition = new Vector2(candidates.anchoredPosition.x, completionY + inset);
            }
            if (waiting)
            {
                answerOptions.text = runtime.TerminalAnswerLabel;
                RefreshAnswerChoices(runtime.TerminalAnswerKeys.ToArray());
                LayoutQuestion(runtime.TerminalQuestion);
            }
        }

        private void RefreshAnswerChoices(string[] keys)
        {
            if (visibleAnswerKeys.SequenceEqual(keys)) return;
            visibleAnswerKeys = keys;
            MonitorWidgets.ClearRows(answerChoices);
            float maxWidth = answerChoices.rect.width / keys.Length;
            float offset = 0f;
            foreach (string key in keys)
            {
                float width = Mathf.Min(maxWidth, Mathf.Max(48f, 28f + key.Length * 11f));
                Button choice = MonitorWidgets.Button(answerChoices, answerOptions.font, new Vector2(offset, 0),
                    new Vector2(width - 4f, 30f), "[" + key + "]", () => PickAnswer(key));
                Text label = choice.GetComponentInChildren<Text>();
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 12;
                label.resizeTextMaxSize = label.fontSize;
                offset += width;
            }
        }

        private void PickAnswer(string key)
        {
            if (!AcceptsInput || !Runtime.IsTerminalAnswerKey(key) || (input.characterLimit > 0 && key.Length > input.characterLimit)) return;
            input.ReleaseFocus();
            input.ClearDraft();
            input.SetTextWithoutNotify(key);
            input.FocusDraft();
            InputChanged(key);
        }

        private void LayoutQuestion(string text)
        {
            if (answerQuestion.text == text) return;
            answerQuestion.text = text;
            float width = answerRow.rect.width - QuestionPadding.x * 2;
            var settings = answerQuestion.GetGenerationSettings(new Vector2(width, 0));
            float height = answerQuestion.cachedTextGeneratorForLayout.GetPreferredHeight(text, settings) / answerQuestion.pixelsPerUnit;
            // Long authored questions can scroll within the terminal's visible log area.
            float maxHeight = Mathf.Max(44f, TranscriptViewport.rect.height);
            questionPanel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Clamp(Mathf.Ceil(height) + QuestionPadding.y * 2, 44f, maxHeight));
            answerQuestion.rectTransform.anchoredPosition = Vector2.zero;
        }

        private void BuildAnswerPrompt()
        {
            logBottom = TranscriptViewport.offsetMin.y;
            if (transcriptScroll.verticalScrollbar != null)
                scrollbarBottom = ((RectTransform)transcriptScroll.verticalScrollbar.transform).offsetMin.y;
            completionY = ((RectTransform)completionPanel.transform).anchoredPosition.y;
            RectTransform field = (RectTransform)input.transform;
            field.SetAsLastSibling();
            Font font = input.textComponent.font;
            float width = field.rect.width;
            Color answerColor = new Color(0.075f, 0.15f, 0.115f);
            answerRow = MonitorWidgets.Rect("TerminalAnswerOptions", field, new Vector2(0, AnswerInset), new Vector2(width, AnswerInset - 4));
            answerRow.gameObject.AddComponent<Image>().color = answerColor;
            answerRow.gameObject.AddComponent<CrtMeshEffect>();
            answerOptions = MonitorWidgets.Label(answerRow, font, new Vector2(12, -4), new Vector2(88, 28), "", 22);
            answerChoices = MonitorWidgets.Rect("AnswerChoices", answerRow, new Vector2(102, -3), new Vector2(width - 114, 30));

            questionPanel = MonitorWidgets.Rect("TerminalQuestion", answerRow, Vector2.zero, new Vector2(width, 44));
            questionPanel.pivot = Vector2.zero;
            questionPanel.anchoredPosition = Vector2.zero;
            questionPanel.gameObject.AddComponent<Image>().color = Color.Lerp(answerColor, Color.white, 0.085f);
            questionPanel.gameObject.AddComponent<CrtMeshEffect>();
            answerQuestion = MonitorWidgets.ScrollText(questionPanel, font, Vector2.zero, new Vector2(width, 44));
            answerQuestion.fontSize = 22;
            answerQuestion.rectTransform.sizeDelta = Vector2.zero;
            RectTransform viewport = (RectTransform)answerQuestion.transform.parent;
            viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
            viewport.offsetMin = QuestionPadding; viewport.offsetMax = -QuestionPadding;
            viewport.GetComponent<Image>().color = Color.clear;
            answerRow.gameObject.AddComponent<TerminalQuestionHover>().Question = questionPanel.gameObject;
            questionPanel.gameObject.SetActive(false);
            answerRow.gameObject.SetActive(false);
        }
    }

    internal sealed class TerminalQuestionHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public GameObject Question { get; set; }
        public void OnPointerEnter(PointerEventData eventData) => Question.SetActive(true);
        public void OnPointerExit(PointerEventData eventData)
        {
            GameObject next = eventData.pointerCurrentRaycast.gameObject;
            if (next == null || !next.transform.IsChildOf(transform)) Question.SetActive(false);
        }
        private void OnDisable() { if (Question != null) Question.SetActive(false); }
    }
}
