using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Codetective.Operations
{
    public enum Mailbox { Clinic, Kernel }
    [Serializable] public sealed class StoryMail
    {
        public string id;
        public Mailbox mailbox;
        public string sender;
        public string subject;
        public string body;
        public int day = 1;
        public string[] requiredFacts = Array.Empty<string>();
        public string[] readFacts = Array.Empty<string>();
        public bool requiresAcceptance;
        public string acceptLabel = "확인";
        public string[] acceptFacts = Array.Empty<string>();
        public string[] grantAuthorities = Array.Empty<string>();
        public StoryMail Copy() => new StoryMail { id = id, mailbox = mailbox, sender = sender, subject = subject, body = body, day = day,
            requiredFacts = requiredFacts.ToArray(), readFacts = readFacts.ToArray(), requiresAcceptance = requiresAcceptance, acceptLabel = acceptLabel,
            acceptFacts = acceptFacts.ToArray(), grantAuthorities = grantAuthorities.ToArray() };
    }
    [Serializable] public sealed class MailProgress { public string id; public bool read; public bool accepted; }
    [Serializable] public sealed class ConsultationNote
    {
        public int id;
        public string profileId;
        public int day;
        public double minute;
        public string text;
        public ConsultationNote Copy() => (ConsultationNote)MemberwiseClone();
    }
    [Serializable] public sealed class DrawerState { public string id; public bool open; }

    public sealed partial class KernelRuntime
    {
        private readonly Dictionary<string, StoryMail> mail = new Dictionary<string, StoryMail>(StringComparer.Ordinal);
        private readonly Dictionary<string, MailProgress> mailProgress = new Dictionary<string, MailProgress>(StringComparer.Ordinal);
        private readonly List<ConsultationNote> notes = new List<ConsultationNote>();
        private readonly Dictionary<string, bool> drawers = new Dictionary<string, bool>(StringComparer.Ordinal);
        private int nextNoteId = 1;
        public string StoryStartFact { get; set; } = "";
        public event Action NarrativeChanged;
        private void NarrativeUpdated() { if (!DryRun) NarrativeChanged?.Invoke(); }
        public bool AwaitingAssignment => !string.IsNullOrEmpty(StoryStartFact) && !Facts.Contains(StoryStartFact);
        public IEnumerable<StoryMail> Inbox(Mailbox box) => mail.Values.Where(m => m.mailbox == box && MailAvailable(m))
            .OrderByDescending(m => m.day).ThenBy(m => m.id, StringComparer.Ordinal).Select(m => m.Copy());
        public int UnreadMailCount(Mailbox box) => Inbox(box).Count(m => !IsMailRead(m.id));
        public bool IsMailRead(string id) => mailProgress.TryGetValue(id, out MailProgress state) && state.read;
        public bool IsMailAccepted(string id) => mailProgress.TryGetValue(id, out MailProgress state) && state.accepted;
        private bool MailAvailable(StoryMail message) => message.day <= Day && message.requiredFacts.All(Facts.Contains);
        public void RegisterMail(StoryMail message)
        {
            if (message == null || string.IsNullOrWhiteSpace(message.id) || message.day < 1 || mail.ContainsKey(message.id))
                throw new CommandException("메일 ID/도착일이 유효하지 않거나 중복됩니다.");
            mail.Add(message.id, message.Copy()); Revision++;
        }
        public StoryMail ReadMail(string id)
        {
            StoryMail message = RequireMail(id);
            if (!mailProgress.TryGetValue(id, out MailProgress state)) mailProgress[id] = state = new MailProgress { id = id };
            if (!state.read) { state.read = true; Facts.UnionWith(message.readFacts); Report("메일 확인: " + message.subject); NarrativeUpdated(); }
            return message.Copy();
        }
        public void AcceptMail(string id)
        {
            StoryMail message = RequireMail(id);
            if (!message.requiresAcceptance) throw new CommandException("수락할 작업이 없는 메일입니다.");
            if (!IsMailRead(id)) throw new CommandException("메일 내용을 먼저 확인하세요.");
            MailProgress state = mailProgress[id];
            if (state.accepted) return;
            state.accepted = true; Facts.UnionWith(message.acceptFacts); Authorities.UnionWith(message.grantAuthorities);
            Report("메일 업무 수락: " + message.subject);
            NarrativeUpdated();
        }
        private StoryMail RequireMail(string id) => id != null && mail.TryGetValue(id, out StoryMail message) && MailAvailable(message) ?
            message : throw new CommandException("아직 도착하지 않았거나 존재하지 않는 메일입니다.");

        public static string ProfileOf(ConsultationLine line) => string.IsNullOrWhiteSpace(line?.profileId) ? "client" : line.profileId;
        public IEnumerable<string> ConsultationProfiles => Consultation.Select(ProfileOf).Concat(notes.Select(n => n.profileId)).Distinct(StringComparer.Ordinal);
        public IEnumerable<ConsultationNote> NotesFor(string profileId) => notes.Where(n => n.profileId == profileId).Select(n => n.Copy());
        public ConsultationNote AddConsultationNote(string profileId, string text)
        {
            ValidateNote(profileId, text);
            var note = new ConsultationNote { id = nextNoteId++, profileId = profileId, day = Day, minute = Minute, text = text.Trim() };
            notes.Add(note); Revision++; NarrativeUpdated(); return note.Copy();
        }
        public void UpdateConsultationNote(int id, string text)
        {
            ConsultationNote note = notes.FirstOrDefault(n => n.id == id) ?? throw new CommandException("상담 메모를 찾을 수 없습니다.");
            ValidateNote(note.profileId, text); note.text = text.Trim(); Revision++; NarrativeUpdated();
        }
        public bool RemoveConsultationNote(int id) { bool removed = notes.RemoveAll(n => n.id == id) > 0; if (removed) { Revision++; NarrativeUpdated(); } return removed; }
        private void ValidateNote(string profileId, string text)
        {
            if (!ConsultationProfiles.Contains(profileId) || string.IsNullOrWhiteSpace(text) || text.Length > 8000)
                throw new CommandException("상담 대상과 메모 내용을 확인하세요. 메모는 8000자까지 저장합니다.");
        }
        public string ProfileSummary(string profileId, bool includeNotes = true)
        {
            var text = new StringBuilder(); text.Append(Entity(profileId)?.Label ?? profileId).Append("\n\n기록한 단서\n");
            bool any = false;
            foreach (ConsultationLine line in Consultation.Where(l => ProfileOf(l) == profileId && RecordedClues.Contains(l.fact)))
            { text.Append("● ").Append(line.keyword).Append('\n').Append(line.sentence).Append("\n\n"); any = true; }
            if (!any) text.Append("아직 기록한 단서가 없습니다.\n\n");
            if (includeNotes)
                foreach (ConsultationNote note in NotesFor(profileId)) text.Append("[메모 ").Append(note.id).Append(" / DAY ").Append(note.day)
                    .Append("]\n").Append(note.text).Append("\n\n");
            return text.ToString();
        }
        public bool IsDrawerOpen(string id) => drawers.TryGetValue(id, out bool open) && open;
        public void SetDrawerOpen(string id, bool open)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new CommandException("서랍 ID가 필요합니다.");
            if (!drawers.TryGetValue(id, out bool previous) || previous != open) { drawers[id] = open; Revision++; NarrativeUpdated(); }
        }
        private void RestoreNarrative(KernelSnapshot snapshot)
        {
            notes.Clear();
            foreach (ConsultationNote note in snapshot.consultationNotes ?? Array.Empty<ConsultationNote>())
            {
                if (note.id < 1 || notes.Any(n => n.id == note.id) || string.IsNullOrWhiteSpace(note.profileId) || note.text == null || note.text.Length > 8000)
                    throw new CommandException("저장된 상담 메모가 유효하지 않습니다.");
                notes.Add(note.Copy());
            }
            nextNoteId = Math.Max(snapshot.nextConsultationNoteId, notes.Count == 0 ? 1 : notes.Max(n => n.id) + 1);
            mailProgress.Clear();
            foreach (MailProgress state in snapshot.mailProgress ?? Array.Empty<MailProgress>())
                mailProgress.Add(state.id, new MailProgress { id = state.id, read = state.read || state.accepted, accepted = state.accepted });
            // Older daily saves predate the assignment gate; preserve their ongoing case.
            if (snapshot.mailProgress == null && !string.IsNullOrEmpty(StoryStartFact))
            {
                Facts.Add(StoryStartFact);
                foreach (StoryMail message in mail.Values.Where(m => m.requiresAcceptance && m.acceptFacts.Contains(StoryStartFact)))
                    mailProgress[message.id] = new MailProgress { id = message.id, read = true, accepted = true };
            }
            drawers.Clear();
            foreach (DrawerState drawer in snapshot.drawers ?? Array.Empty<DrawerState>()) drawers.Add(drawer.id, drawer.open);
        }
    }
}
