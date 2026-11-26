using System;

namespace Codetective.Operations
{
    public enum TerminalConnectionMode { OnPageOpen, Incoming, Outgoing }

    public readonly struct TerminalContact
    {
        public string Name { get; }
        public string ProfileId { get; }

        internal TerminalContact(string name, string profileId)
        {
            Name = name;
            ProfileId = profileId;
        }
    }

    public sealed partial class KernelRuntime
    {
        public string TerminalIncomingConnectionId
        {
            get
            {
                if (TerminalConversationActive || IsDayEnded || (DayMailDeferred && !DayIntroSeen)) return null;
                foreach (TerminalConversationDefinition definition in terminalConversations.Values)
                {
                    TerminalConversationProgress progress = terminalConversationProgress[definition.id];
                    bool redialDue = progress.disconnected && definition.redialSeconds > 0 && progress.redialRemaining <= 0;
                    bool newCaller = definition.connectionMode == TerminalConnectionMode.Incoming && progress.phase == TerminalConversationPhase.Dormant;
                    if (redialDue || (newCaller && CanOpenNewConversation(definition))) return definition.id;
                }
                return null;
            }
        }

        public string TerminalReconnectId
        {
            get
            {
                if (TerminalConversationActive || IsDayEnded) return null;
                foreach (TerminalConversationDefinition definition in terminalConversations.Values)
                    if (definition.redialSeconds == 0 && terminalConversationProgress[definition.id].disconnected) return definition.id;
                return null;
            }
        }

        public TerminalContact ContactForTerminalConversation(string id)
        {
            if (id == null || !terminalConversations.TryGetValue(id, out TerminalConversationDefinition definition)) return default;
            string name = ObjectReferenceText.PlainText(Texts.Resolve(definition.speakerKey, definition.speaker, DisplayLanguage));
            return new TerminalContact(name, definition.contactId);
        }

        public bool AcceptTerminalConnection(string id)
        {
            if (string.IsNullOrEmpty(id) || TerminalIncomingConnectionId != id) return false;
            ConnectTerminalConversation(id);
            return true;
        }

        public bool ResumeTerminalConnection(string id)
        {
            if (string.IsNullOrEmpty(id) || TerminalIncomingConnectionId != null || TerminalReconnectId != id) return false;
            ConnectTerminalConversation(id);
            return true;
        }

        public bool TryPlaceTerminalCall(string id)
        {
            if (string.IsNullOrEmpty(id) || TerminalConversationActive || TerminalIncomingConnectionId != null || IsDayEnded ||
                !terminalConversations.TryGetValue(id, out TerminalConversationDefinition definition) || definition.connectionMode != TerminalConnectionMode.Outgoing)
                return false;
            TerminalConversationProgress progress = terminalConversationProgress[id];
            if (!progress.disconnected && (progress.phase != TerminalConversationPhase.Dormant || !CanOpenNewConversation(definition))) return false;
            if (progress.disconnected && definition.redialSeconds > 0) return false;
            ConnectTerminalConversation(id);
            return true;
        }

        public bool DisconnectTerminalConversation(string id)
        {
            if (!TerminalConversationActive || Conversation.id != id) return false;
            TerminalConversationProgress progress = Conversation;
            progress.disconnected = true;
            progress.redialRemaining = ConversationDefinition.redialSeconds;
            ConversationChanged(true);
            return true;
        }

        public void AdvanceTerminalRedial(double seconds)
        {
            if (!(seconds > 0) || double.IsInfinity(seconds) || IsDayEnded) return;
            bool ringing = false;
            foreach (TerminalConversationProgress progress in terminalConversationProgress.Values)
            {
                if (!progress.disconnected || progress.redialRemaining <= 0) continue;
                progress.redialRemaining = Math.Max(0, progress.redialRemaining - seconds);
                ringing |= progress.redialRemaining == 0;
            }
            if (ringing) ConversationChanged(true);
        }

        private void ConnectTerminalConversation(string id)
        {
            TerminalConversationProgress progress = terminalConversationProgress[id];
            if (!progress.disconnected) { StartTerminalConversation(id); return; }
            activeTerminalConversation = id;
            progress.disconnected = false;
            progress.redialRemaining = 0;
            ConversationChanged(true);
        }
    }
}
