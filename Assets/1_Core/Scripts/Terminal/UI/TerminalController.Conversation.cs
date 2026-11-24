using System;
using System.Collections.Generic;
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
        private const float AnswerInset = 40f;
        private static readonly Vector2 QuestionPadding = new Vector2(12, 9);
        private RectTransform answerRow, questionPanel;
        private Text answerOptions, answerQuestion;
        private Vector2 logBottom, scrollbarBottom, completionPosition;

        private void ResetConversationPresentation()
        { mergedConversations.Clear(); renderedConversationRevision = -1; renderedConversationLocale = null; renderedConversationId = null; conversationInputLocked = false; restoreCommandFocus = false; ClearConversationDisturbance(); }
        private void ClearConversationDisturbance()
        { if (conversationScreen != null) conversationScreen.SetConversationDisturbance(0); }
        private void MergeCompletedConversation()
        {
            KernelRuntime runtime = session.Runtime;
            if (runtime.TerminalConversationActive || runtime.TerminalConversationId == null || !mergedConversations.Add(runtime.TerminalConversationId)) return;
            ObjectReferenceText log = runtime.TerminalConversationContent(includeAnswerPrompts: false);
            if (!string.IsNullOrWhiteSpace(log.Text)) AppendHistoryContent(log);
        }
        private ObjectReferenceText ConversationTranscript()
        {
            KernelRuntime runtime = session.Runtime;
            ObjectReferenceText content = runtime.TerminalConversationActive ? ObjectReferenceText.Join("\n\n",
                new[] { history.Content, runtime.TerminalConversationContent(includeAnswerPrompts: false) }) : history.Content;
            // Keep the combined uGUI Text mesh within the same budget as ordinary scrollback.
            if (content.Text.Length > 12000)
            {
                int start = content.Text.Length - 12000;
                if (char.IsLowSurrogate(content.Text[start])) start++;
                content = content.Slice(start, content.Text.Length - start);
            }
            return content;
        }
        private void DrawTranscript(ObjectReferenceText content)
        {
            if (transcriptLinks == null) transcriptLinks = ObjectReferenceTextView.Attach(transcript, (id, label) => TryInsertObjectReference(id, label));
            transcriptLinks.Show(content, Runtime.CanLinkObject);
        }
        private void UpdateConversationPresentation()
        {
            KernelRuntime runtime = session.Runtime;
            bool visible = pageVisible && interactionEnabled && isActiveAndEnabled && Time.timeScale > 0;
            runtime.AdvanceTerminalConversation(Time.deltaTime, visible);
            if (runtime.TerminalConversationActive && renderedConversationId != runtime.TerminalConversationId)
            { renderedConversationId = runtime.TerminalConversationId; QueueTranscriptScroll(toLatest: true); }
            if (conversationInputLocked != runtime.TerminalInputLocked)
            {
                if (visible && !runtime.TerminalInputLocked && runtime.TerminalConversationActive)
                    restoreCommandFocus = true;
                conversationInputLocked = runtime.TerminalInputLocked;
                ApplyInputInteraction(); runtimeRevision = -1;
            }
            if (conversationScreen == null) conversationScreen = GetComponent<CrtScreen>();
            if (conversationScreen != null) conversationScreen.SetConversationDisturbance(visible ? (float)runtime.TerminalDisturbance : 0);
            MergeCompletedConversation();
            if (renderedConversationRevision == runtime.TerminalConversationRevision && renderedConversationLocale == runtime.DisplayLanguage && renderedConversationCommandLocale == runtime.Language) return;
            renderedConversationRevision = runtime.TerminalConversationRevision; renderedConversationLocale = runtime.DisplayLanguage; renderedConversationCommandLocale = runtime.Language;
            RefreshCommandPrompt();
            RefreshAnswerPrompt();
            ObjectReferenceText content = ConversationTranscript();
            if (transcript.text != content.Text) QueueTranscriptScroll();
            DrawTranscript(content);
        }

        private void RefreshCommandPrompt()
        {
            if (!(input.placeholder is Text placeholder)) return;
            bool english = session.Runtime.DisplayLanguage == "en";
            KernelRuntime runtime = session.Runtime;
            placeholder.text = runtime.TerminalLineUsesEnter ? (english ? "Press Enter to continue.." : "Enter를 눌러 넘기기..") :
                runtime.TerminalInputLocked ? (english ? "Receiving.." : "수신중..") :
                (english ? "Awaiting input.." : "입력 대기중..");
        }

        public bool TryContinueConversation()
        {
            if (!interactionEnabled || !pageVisible || !isActiveAndEnabled || Time.timeScale <= 0 ||
                !session.Runtime.ContinueTerminalLine()) return false;
            QueueTranscriptScroll(toLatest: true);
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
                Vector2 inset = waiting ? Vector2.up * AnswerInset : Vector2.zero;
                TranscriptViewport.offsetMin = logBottom + inset;
                if (transcriptScroll.verticalScrollbar != null)
                    ((RectTransform)transcriptScroll.verticalScrollbar.transform).offsetMin = scrollbarBottom + inset;
                ((RectTransform)completionPanel.transform).anchoredPosition = completionPosition + inset;
            }
            if (waiting)
            {
                answerOptions.text = runtime.TerminalAnswerPrompt;
                LayoutQuestion(runtime.TerminalQuestion);
            }
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
            logBottom = TranscriptViewport.offsetMin;
            if (transcriptScroll.verticalScrollbar != null)
                scrollbarBottom = ((RectTransform)transcriptScroll.verticalScrollbar.transform).offsetMin;
            completionPosition = ((RectTransform)completionPanel.transform).anchoredPosition;
            RectTransform field = (RectTransform)input.transform;
            field.SetAsLastSibling();
            Font font = input.textComponent.font;
            float width = field.rect.width;
            Color answerColor = new Color(0.075f, 0.15f, 0.115f);
            answerRow = MonitorWidgets.Rect("TerminalAnswerOptions", field, new Vector2(0, AnswerInset), new Vector2(width, AnswerInset - 4));
            answerRow.gameObject.AddComponent<Image>().color = answerColor;
            answerRow.gameObject.AddComponent<CrtMeshEffect>();
            answerOptions = MonitorWidgets.Label(answerRow, font, new Vector2(12, -4), new Vector2(width - 24, 28), "", 22);

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
