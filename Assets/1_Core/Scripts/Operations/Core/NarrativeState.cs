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
        public string recipient;
        public string subject;
        public string body;
        public int day = 1;
        public string afterConversation;
        public string afterAppointmentId; // Clinic plan day plus appointment ID, e.g. "1:regular".
        public int minutesAfterAppointment;
        public string[] requiredFacts = Array.Empty<string>();
        public string[] readFacts = Array.Empty<string>();
        public bool requiresAcceptance;
        public string acceptLabel = "확인";
        public string pageOnAccept = "";
        public string[] acceptFacts = Array.Empty<string>();
        public string[] grantAuthorities = Array.Empty<string>();
        public StoryCondition condition = new StoryCondition();
        public StoryEffects onRead = new StoryEffects();
        public StoryEffects onAccept = new StoryEffects();
        public MailChoice[] choices = Array.Empty<MailChoice>();
        public bool isReservationInquiry;
        public ReservationRequest reservation;
        // Unity inline serialization can materialize an empty ReservationRequest for ordinary mail.
        // An approval choice also identifies pre-flag inquiry assets and programmatic definitions.
        public bool HasReservation => isReservationInquiry || (choices ?? Array.Empty<MailChoice>()).Any(c => c != null && c.approvesReservation);
        public StoryMail Copy() => new StoryMail { id = id, mailbox = mailbox, sender = sender, recipient = recipient, subject = subject, body = body, day = day,
            afterConversation = afterConversation, afterAppointmentId = afterAppointmentId, minutesAfterAppointment = minutesAfterAppointment,
            requiredFacts = requiredFacts.ToArray(), readFacts = readFacts.ToArray(), requiresAcceptance = requiresAcceptance, acceptLabel = acceptLabel, pageOnAccept = pageOnAccept,
            acceptFacts = acceptFacts.ToArray(), grantAuthorities = grantAuthorities.ToArray(), condition = condition?.Copy() ?? new StoryCondition(),
            onRead = onRead?.Copy() ?? new StoryEffects(), onAccept = onAccept?.Copy() ?? new StoryEffects(),
            choices = (choices ?? Array.Empty<MailChoice>()).Select(c => c.Copy()).ToArray(), isReservationInquiry = HasReservation,
            reservation = HasReservation ? reservation?.Copy() : null };
    }
    [Serializable] public sealed class MailProgress
    {
        public string id; public bool read; public bool accepted;
        public string choiceId, choiceLabel; public MailChoiceResult choiceResult;
        public MailProgress Copy() => (MailProgress)MemberwiseClone();
    }
    [Serializable] public sealed class ConsultationNote
    {
        public int id;
        public string profileId;
        public int day;
        public double minute;
        public bool hasSessionTime;
        public int sessionDay, startMinute, endMinute;
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
        public int UnreadMailCount(Mailbox box) => mail.Values.Count(m => m.mailbox == box && !IsMailRead(m.id) && MailAvailable(m));
        public bool IsMailRead(string id) => mailProgress.TryGetValue(id, out MailProgress state) && state.read;
        public bool IsMailAccepted(string id) => mailProgress.TryGetValue(id, out MailProgress state) && state.accepted;
        public bool DayMailDeferred => dayPresentation.day == Day && dayPresentation.mailDeferred;
        public void DeferDayMailUntilIntro()
        {
            if (IsDayEnded || DayIntroSeen || DayMailDeferred) return;
            dayPresentation = new DayPresentationState { day = Day, mailDeferred = true }; Revision++; NarrativeUpdated();
        }
        public IReadOnlyList<StoryMail> DeferredDayMail => DayMailDeferred ? mail.Values
            .Where(m => m.day == Day && !IsMailRead(m.id) && MailConditionsMet(m))
            .OrderByDescending(m => m.mailbox).ThenBy(m => m.id, StringComparer.Ordinal).Select(m => m.Copy()).ToArray() : Array.Empty<StoryMail>();
        private bool MailPrerequisitesMet(StoryMail message) => TerminalConversationRequirementMet(message.afterConversation) &&
            message.requiredFacts.All(Facts.Contains) && message.condition.Matches(this);
        private bool MailConditionsMet(StoryMail message) => message.day <= Day && (IsMailRead(message.id) ||
            MailPrerequisitesMet(message) && (string.IsNullOrEmpty(message.afterAppointmentId) || AppointmentMailMinute(message) <= Minute + MinuteEpsilon));
        private double? AppointmentMailMinute(StoryMail message)
        {
            AppointmentProgress[] visits = message.day == Day ? clinicDay?.appointments :
                dailyRecords.TryGetValue(message.day, out DailyRecord record) ? record.visits : null;
            AppointmentProgress visit = visits?.FirstOrDefault(a => a.id == message.afterAppointmentId && a.phase == VisitPhase.Finished && a.outcome != VisitOutcome.Missed);
            return visit == null ? (double?)null : (message.day - Day) * 1440d + visit.SessionEnd + message.minutesAfterAppointment;
        }
        private double NextAssignmentMailMinute => mail.Values.Where(m => m.day == Day && m.requiresAcceptance && m.acceptFacts.Contains(StoryStartFact) &&
                !string.IsNullOrEmpty(m.afterAppointmentId) && MailPrerequisitesMet(m))
            .Select(AppointmentMailMinute).Where(minute => minute > Minute + MinuteEpsilon).Select(minute => minute.Value).DefaultIfEmpty(Minute).Min();
        private bool MailAvailable(StoryMail message) => MailConditionsMet(message) && (!DayMailDeferred || message.day != Day || IsMailRead(message.id));
        public void RegisterMail(StoryMail message)
        {
            if (message == null || string.IsNullOrWhiteSpace(message.id) || message.day < 1 || mail.ContainsKey(message.id))
                throw new CommandException("메일 ID/도착일이 유효하지 않거나 중복됩니다.");
            if (!string.IsNullOrEmpty(message.afterConversation) && !terminalConversations.ContainsKey(message.afterConversation))
                throw new CommandException("메일의 선행 터미널 대화가 없습니다: " + message.afterConversation);
            if (message.minutesAfterAppointment < 0 ||
                (string.IsNullOrEmpty(message.afterAppointmentId) ? message.minutesAfterAppointment != 0 : !appointmentDefinitions.ContainsKey(message.afterAppointmentId)))
                throw new CommandException("메일이 도착할 상담 예약과 대기 시간을 확인하세요.");
            message.onRead?.Validate(); message.onAccept?.Validate();
            ValidateMailChoices(message);
            if (message.HasReservation) RegisterReservation(message.id, message.reservation);
            mail.Add(message.id, message.Copy()); Revision++;
        }
        public StoryMail ReadMail(string id)
        {
            StoryMail message = RequireMail(id);
            if (!mailProgress.TryGetValue(id, out MailProgress state)) mailProgress[id] = state = new MailProgress { id = id };
            if (!state.read) { state.read = true; Facts.UnionWith(message.readFacts); ApplyStoryEffects(message.onRead); Report("메일 확인: " + message.subject); NarrativeUpdated(); }
            return message.Copy();
        }
        public void AcceptMail(string id)
        {
            StoryMail message = RequireMail(id);
            if (message.choices.Length > 0) throw new CommandException("메일의 선택지로 답변하세요.");
            if (!message.requiresAcceptance) throw new CommandException("수락할 작업이 없는 메일입니다.");
            if (!IsMailRead(id)) throw new CommandException("메일 내용을 먼저 확인하세요.");
            MailProgress state = mailProgress[id];
            if (state.accepted) return;
            state.accepted = true; Facts.UnionWith(message.acceptFacts); Authorities.UnionWith(message.grantAuthorities);
            ApplyStoryEffects(message.onAccept);
            Report("메일 업무 수락: " + message.subject);
            NarrativeUpdated();
        }
        private StoryMail RequireMail(string id) => id != null && mail.TryGetValue(id, out StoryMail message) && MailAvailable(message) ?
            message : throw new CommandException("아직 도착하지 않았거나 존재하지 않는 메일입니다.");

        public static string ProfileOf(ConsultationLine line) => string.IsNullOrWhiteSpace(line?.profileId) ? "client" : line.profileId;
        private bool ProfileAvailable(Entity entity) => entity.Schema.IsA(Schemas.Require("Person")) && !entity.Schema.IsA(Schemas.Require("Thread")) && IsPlayerVisible(entity);
        public IEnumerable<string> VisibleProfileIds => entities.Values.Where(ProfileAvailable)
            .Select(e => e.Id).Concat(Consultation.Select(ProfileOf).Concat(notes.Select(n => n.profileId))
                .Concat(appointmentDefinitions.Values.Where(a => GetTrigger("clinic.met." + a.profileId)).Select(a => a.profileId))
                .Where(id => Entity(id) == null || ProfileAvailable(Entity(id)))).Distinct(StringComparer.Ordinal);
        public IEnumerable<string> ConsultationProfiles => VisibleProfileIds;
        public IEnumerable<ConsultationNote> NotesFor(string profileId) => notes.Where(n => n.profileId == profileId).Select(n => n.Copy());
        public ConsultationNote AddConsultationNote(string profileId, string text)
        {
            var session = ConsultationNoteTime(profileId);
            return AddConsultationNote(profileId, text, session.day, session.startMinute, session.endMinute);
        }
        public (int day, int startMinute, int endMinute) ConsultationNoteTime(string profileId)
        {
            int now = (int)Minute;
            if (ActiveVisit is AppointmentProgress active && active.profileId == profileId)
                return (Day, Math.Min(now, (int)active.SessionStart), now);
            var latest = CompletedConsultationsFor(profileId).FirstOrDefault(entry => entry.day == Day);
            return latest.visit != null ? (latest.day, (int)latest.visit.SessionStart, (int)latest.visit.SessionEnd) : (Day, now, now);
        }
        public ConsultationNote AddConsultationNote(string profileId, string text, int sessionDay, int startMinute, int endMinute)
        {
            ValidateNote(profileId, text);
            ValidateSessionTime(sessionDay, startMinute, endMinute);
            var note = new ConsultationNote { id = nextNoteId++, profileId = profileId, day = Day, minute = Minute, text = text.Trim(),
                hasSessionTime = true, sessionDay = sessionDay, startMinute = startMinute, endMinute = endMinute };
            notes.Add(note); Revision++; NarrativeUpdated(); return note.Copy();
        }
        public void UpdateConsultationNote(int id, string text)
        {
            ConsultationNote note = notes.FirstOrDefault(n => n.id == id) ?? throw new CommandException("상담 메모를 찾을 수 없습니다.");
            ValidateNote(note.profileId, text); note.text = text.Trim(); Revision++; NarrativeUpdated();
        }
        public void UpdateConsultationNote(int id, string text, int sessionDay, int startMinute, int endMinute)
        {
            ConsultationNote note = notes.FirstOrDefault(n => n.id == id) ?? throw new CommandException("상담 일지를 찾을 수 없습니다.");
            ValidateNote(note.profileId, text); ValidateSessionTime(sessionDay, startMinute, endMinute);
            note.text = text.Trim(); note.hasSessionTime = true; note.sessionDay = sessionDay; note.startMinute = startMinute; note.endMinute = endMinute;
            Revision++; NarrativeUpdated();
        }
        private void ValidateSessionTime(int day, int start, int end)
        {
            if (day < 1 || day > Day || start < 0 || start >= 1440 || end < start || end > 1440)
                throw new CommandException("상담일과 시간을 확인하세요. 시작 시간은 종료 시간보다 늦을 수 없습니다.");
        }
        public bool RemoveConsultationNote(int id) { bool removed = notes.RemoveAll(n => n.id == id) > 0; if (removed) { Revision++; NarrativeUpdated(); } return removed; }
        private void ValidateNote(string profileId, string text)
        {
            if (!ConsultationProfiles.Contains(profileId) || string.IsNullOrWhiteSpace(text) || text.Length > 8000)
                throw new CommandException("상담 대상과 메모 내용을 확인하세요. 메모는 8000자까지 저장합니다.");
        }
        public string ProfileSummary(string profileId, bool includeNotes = true)
        {
            if (!VisibleProfileIds.Contains(profileId)) throw new CommandException("현재 열람할 수 없는 프로필입니다.");
            Entity person = Entity(profileId);
            var text = new StringBuilder(); text.Append(person?.Label ?? profileId).Append("\n\n");
            if (person != null)
                foreach (VariableDefinition field in person.Variables.Where(f => f.Code != "id" && f.Code != "name" && VariableDenial(person, f, null) == null))
                {
                    Datum value = LastKnownValue(person.Id, field.Code);
                    if (value.kind != DataKind.Empty && !(value.kind == DataKind.Text && string.IsNullOrEmpty(value.text)) && IsPlayerVisible(value))
                        text.Append(string.IsNullOrEmpty(field.Label) ? field.NameFor(Language) : field.Label).Append(": ").Append(value).Append('\n');
                }
            text.Append("\n기록한 단서\n");
            bool any = false;
            foreach (ConsultationLine line in Consultation.Where(l => ProfileOf(l) == profileId && RecordedClues.Contains(l.fact)).GroupBy(l => l.fact).Select(g => g.First()))
            { text.Append("● ").Append(line.keyword).Append('\n').Append(ObjectReferenceText.PlainText(line.sentence)).Append("\n\n"); any = true; }
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
                ConsultationNote restored = note.Copy();
                if (!restored.hasSessionTime)
                {
                    if (double.IsNaN(restored.minute) || double.IsInfinity(restored.minute)) throw new CommandException("저장된 상담 시간이 유효하지 않습니다.");
                    restored.hasSessionTime = true; restored.sessionDay = Math.Max(1, restored.day);
                    restored.startMinute = restored.endMinute = Math.Max(0, Math.Min(1439, (int)restored.minute));
                }
                ValidateSessionTime(restored.sessionDay, restored.startMinute, restored.endMinute);
                notes.Add(restored);
            }
            nextNoteId = Math.Max(snapshot.nextConsultationNoteId, notes.Count == 0 ? 1 : notes.Max(n => n.id) + 1);
            mailProgress.Clear();
            foreach (MailProgress state in snapshot.mailProgress ?? Array.Empty<MailProgress>())
            {
                MailProgress restored = state.Copy(); restored.read |= state.accepted || !string.IsNullOrEmpty(state.choiceId); mailProgress.Add(state.id, restored);
            }
            if (snapshot.triggers == null && snapshot.quests == null)
                foreach (StoryMail message in mail.Values)
                {
                    if (IsMailRead(message.id)) ApplyStoryEffects(message.onRead);
                    if (IsMailAccepted(message.id)) ApplyStoryEffects(message.onAccept);
                }
            // Older daily saves predate the assignment gate; preserve their ongoing case.
            if (snapshot.mailProgress == null && !string.IsNullOrEmpty(StoryStartFact))
            {
                Facts.Add(StoryStartFact);
                foreach (StoryMail message in mail.Values.Where(m => m.requiresAcceptance && m.acceptFacts.Contains(StoryStartFact)))
                {
                    mailProgress[message.id] = new MailProgress { id = message.id, read = true, accepted = true };
                    ApplyStoryEffects(message.onAccept);
                }
            }
            drawers.Clear();
            foreach (DrawerState drawer in snapshot.drawers ?? Array.Empty<DrawerState>()) drawers.Add(drawer.id, drawer.open);
        }
    }
}
