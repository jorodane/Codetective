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
        private const float AnswerInset = 40f;
        private RectTransform answerRow;
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
            string log = runtime.TerminalConversationTranscript(includeAnswerPrompts: false);
            if (!string.IsNullOrWhiteSpace(log)) AppendHistory(log);
        }
        private string ConversationTranscript()
        {
            KernelRuntime runtime = session.Runtime;
            string log = runtime.TerminalConversationActive ? runtime.TerminalConversationTranscript(includeAnswerPrompts: false) : "";
            string text = history.Text + (string.IsNullOrEmpty(log) ? "" : "\n\n" + log);
            // Keep the combined uGUI Text mesh within the same budget as ordinary scrollback.
            if (text.Length > 12000)
            {
                int start = text.Length - 12000;
                if (char.IsLowSurrogate(text[start])) start++;
                text = text.Substring(start);
            }
            return text;
        }
        private void UpdateConversationPresentation()
        {
            KernelRuntime runtime = session.Runtime;
            bool visible = pageVisible && interactionEnabled && isActiveAndEnabled && Time.timeScale > 0;
            runtime.AdvanceTerminalConversation(Time.deltaTime, visible);
            if (runtime.TerminalConversationActive && renderedConversationId != runtime.TerminalConversationId)
            { renderedConversationId = runtime.TerminalConversationId; followTranscript = true; scrollTranscript = true; }
            if (conversationInputLocked != runtime.TerminalInputLocked)
            {
                conversationInputLocked = runtime.TerminalInputLocked;
                ApplyInputInteraction(); runtimeRevision = -1;
            }
            if (conversationScreen == null) conversationScreen = GetComponent<CrtScreen>();
            if (conversationScreen != null) conversationScreen.SetConversationDisturbance(visible ? (float)runtime.TerminalDisturbance : 0);
            MergeCompletedConversation();
            if (renderedConversationRevision == runtime.TerminalConversationRevision && renderedConversationLocale == runtime.DisplayLanguage && renderedConversationCommandLocale == runtime.Language) return;
            renderedConversationRevision = runtime.TerminalConversationRevision; renderedConversationLocale = runtime.DisplayLanguage; renderedConversationCommandLocale = runtime.Language;
            RefreshAnswerPrompt();
            string text = ConversationTranscript();
            if (transcript.text == text) return;
            if (!scrollTranscript)
            {
                followTranscript = TerminalHistory.ShouldFollowLatest(transcriptScroll.content.rect.height, TranscriptViewport.rect.height, transcriptScroll.content.anchoredPosition.y);
                preservedTranscriptPosition = transcriptScroll.content.anchoredPosition;
            }
            transcript.text = text; scrollTranscript = true;
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
                answerQuestion.text = runtime.TerminalQuestion;
            }
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
            answerRow = MonitorWidgets.Rect("TerminalAnswerOptions", field, new Vector2(0, AnswerInset), new Vector2(width, AnswerInset - 4));
            answerRow.gameObject.AddComponent<Image>().color = new Color(0.075f, 0.15f, 0.115f);
            answerRow.gameObject.AddComponent<CrtMeshEffect>();
            answerOptions = MonitorWidgets.Label(answerRow, font, new Vector2(12, -4), new Vector2(width - 24, 28), "", 22);

            const float questionHeight = 168f;
            RectTransform question = MonitorWidgets.Rect("TerminalQuestion", answerRow, new Vector2(0, questionHeight + 4), new Vector2(width, questionHeight));
            Image box = question.gameObject.AddComponent<Image>(); box.color = new Color(0.025f, 0.065f, 0.045f); box.raycastTarget = false;
            question.gameObject.AddComponent<CrtMeshEffect>();
            answerQuestion = MonitorWidgets.Label(question, font, new Vector2(12, -9), new Vector2(width - 24, questionHeight - 18), "", 22);
            answerQuestion.resizeTextForBestFit = true; answerQuestion.resizeTextMinSize = 16; answerQuestion.resizeTextMaxSize = 22;
            answerRow.gameObject.AddComponent<TerminalQuestionHover>().Question = question.gameObject;
            question.gameObject.SetActive(false);
            answerRow.gameObject.SetActive(false);
        }
    }

    internal sealed class TerminalQuestionHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public GameObject Question { get; set; }
        public void OnPointerEnter(PointerEventData eventData) => Question.SetActive(true);
        public void OnPointerExit(PointerEventData eventData) => Question.SetActive(false);
        private void OnDisable() { if (Question != null) Question.SetActive(false); }
    }
}
