using System;
using System.Collections.Generic;
using Codetective.Operations;
using UnityEngine;

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

        private void ResetConversationPresentation()
        { mergedConversations.Clear(); renderedConversationRevision = -1; renderedConversationLocale = null; renderedConversationId = null; conversationInputLocked = false; restoreCommandFocus = false; ClearConversationDisturbance(); }
        private void ClearConversationDisturbance()
        { if (conversationScreen != null) conversationScreen.SetConversationDisturbance(0); }
        private void MergeCompletedConversation()
        {
            KernelRuntime runtime = session.Runtime;
            if (runtime.TerminalConversationActive || runtime.TerminalConversationId == null || !mergedConversations.Add(runtime.TerminalConversationId)) return;
            string log = runtime.TerminalConversationTranscript();
            if (!string.IsNullOrWhiteSpace(log)) AppendHistory(log);
        }
        private string ConversationTranscript()
        {
            KernelRuntime runtime = session.Runtime;
            string log = runtime.TerminalConversationActive ? runtime.TerminalConversationTranscript() : "";
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
            string text = ConversationTranscript();
            if (transcript.text == text) return;
            if (!scrollTranscript)
            {
                followTranscript = TerminalHistory.ShouldFollowLatest(transcriptScroll.content.rect.height, TranscriptViewport.rect.height, transcriptScroll.content.anchoredPosition.y);
                preservedTranscriptPosition = transcriptScroll.content.anchoredPosition;
            }
            transcript.text = text; scrollTranscript = true;
        }
    }
}
