using System;
using System.Collections.Generic;
using System.Linq;

namespace Codetective.Operations
{
    public enum VisitPhase { Waiting, Arriving, Talking, Closing, Departing, Finished }
    public enum VisitOutcome { None, Completed, NextReservation, ClosingTime, Missed, ReservationEnd }

    public static class ClinicHours
    {
        public const int MinutesPerSlot = 60;
        public const int Opening = 9 * MinutesPerSlot;
        public const int LunchStart = 12 * MinutesPerSlot;
        public const int LunchEnd = 13 * MinutesPerSlot;
        public const int Closing = 18 * MinutesPerSlot;

        internal static bool ContainsBooking(int start, int end) => start < end &&
            start % MinutesPerSlot == 0 && end % MinutesPerSlot == 0 &&
            (start >= Opening && end <= LunchStart || start >= LunchEnd && end <= Closing);
    }

    [Serializable] public sealed class PatientAppointment
    {
        public string id;
        public string profileId = "client";
        public string patientName = "상담 손님";
        public string title;
        public int startMinute = ClinicHours.Opening;
        public int endMinute; // 0 uses a single hour.
        internal int BookingEndMinute => endMinute == 0 ? startMinute + ClinicHours.MinutesPerSlot : endMinute;
        public bool beforeAssignment;
        public string afterConversation;
        public string episodePoolId;
        public string greeting = "안녕하세요. 예약하고 왔습니다.";
        public string farewell = "오늘 말씀해 주셔서 감사합니다. 다음 상담에서 이어가겠습니다.";
        public ConsultationLine[] dialogue = Array.Empty<ConsultationLine>();
        public StoryCondition condition = new StoryCondition();
        public StoryEffects onArrival = new StoryEffects();
        public StoryEffects onCompleted = new StoryEffects();
        public StoryEffects onTimeLimit = new StoryEffects();
        public PatientAppointment Copy() => new PatientAppointment { id = id, profileId = profileId, patientName = patientName, title = title, startMinute = startMinute,
            beforeAssignment = beforeAssignment, afterConversation = afterConversation, episodePoolId = episodePoolId,
            endMinute = endMinute, greeting = greeting, farewell = farewell, dialogue = dialogue.Select(l => l.Copy()).ToArray(),
            condition = condition?.Copy() ?? new StoryCondition(), onArrival = onArrival?.Copy() ?? new StoryEffects(),
            onCompleted = onCompleted?.Copy() ?? new StoryEffects(), onTimeLimit = onTimeLimit?.Copy() ?? new StoryEffects() };
    }
    [Serializable] public sealed class ClinicDayPlan
    {
        public int day = 1; // 0 is the fallback schedule for days without an explicit plan.
        public string subtitle = "상담과 작전 사이";
        public PatientAppointment[] appointments = Array.Empty<PatientAppointment>();
        public ClinicDayPlan Copy() => new ClinicDayPlan { day = day, subtitle = subtitle, appointments = appointments.Select(a => a.Copy()).ToArray() };
    }
    [Serializable] public sealed class AppointmentProgress
    {
        public string id, profileId, patientName, title, episodeId;
        public int startMinute, endMinute, lineStart, lineCount, cursor;
        public double startedMinute, finishedMinute;
        public double SessionStart => startedMinute > 0 ? startedMinute : startMinute;
        public double SessionEnd => finishedMinute > 0 ? finishedMinute : SessionStart;
        public VisitPhase phase;
        public VisitOutcome outcome;
        public bool HasJournal => phase == VisitPhase.Finished && outcome != VisitOutcome.Missed;
        public AppointmentProgress Copy() => (AppointmentProgress)MemberwiseClone();
    }
    [Serializable] public sealed class ClinicDayState
    {
        public int day;
        public string subtitle;
        public AppointmentProgress[] appointments;
        public ClinicDayState Copy() => new ClinicDayState { day = day, subtitle = subtitle, appointments = appointments.Select(a => a.Copy()).ToArray() };
    }

    public sealed partial class KernelRuntime
    {
        private readonly Dictionary<int, ClinicDayPlan> clinicPlans = new Dictionary<int, ClinicDayPlan>();
        private readonly Dictionary<string, PatientAppointment> appointmentDefinitions = new Dictionary<string, PatientAppointment>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> appointmentLines = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<int> heardConsultationLines = new HashSet<int>();
        private ClinicDayState clinicDay;
        private int legacyDialogueCount;
        private bool legacyDialogueCaptured;
        public bool HasClinicSchedule => clinicPlans.Count > 0 || reservationRequests.Count > 0;
        public string DaySubtitle => clinicDay?.subtitle ?? "상담과 작전 사이";
        public IEnumerable<AppointmentProgress> DailyAppointments => (clinicDay?.appointments ?? Array.Empty<AppointmentProgress>()).Select(a => a.Copy());
        private AppointmentProgress ActiveVisit => clinicDay?.appointments.FirstOrDefault(a => a.phase != VisitPhase.Waiting && a.phase != VisitPhase.Finished);
        public AppointmentProgress CurrentAppointment => ActiveVisit?.Copy();
        private AppointmentProgress NextVisit => clinicDay?.appointments.FirstOrDefault(a => a.phase == VisitPhase.Waiting && CanStartVisit(appointmentDefinitions[a.id]));
        public AppointmentProgress NextAppointment => NextVisit?.Copy();
        public bool IsClinicTransitioning => ActiveVisit is AppointmentProgress active && active.phase != VisitPhase.Talking;
        public bool CanWaitForNextAppointment => HasClinicSchedule && !TerminalConversationPausesWorld && !IsDayEnded && ActiveVisit == null && NextVisit != null;
        public bool CanWaitUntilClosing => HasClinicSchedule && !AwaitingAssignment && !TerminalConversationPausesWorld && !IsDayEnded && ActiveVisit == null && NextVisit == null;
        public string VisitPresentationText
        {
            get
            {
                AppointmentProgress active = ActiveVisit; if (active == null) return "";
                PatientAppointment definition = appointmentDefinitions[active.id];
                if (active.phase == VisitPhase.Arriving) return definition.greeting;
                if (active.phase == VisitPhase.Departing) return active.patientName + " 님이 퇴실합니다.";
                return active.outcome == VisitOutcome.NextReservation ? "다음 예약 손님이 계셔서 오늘 상담은 여기까지 하겠습니다." :
                    active.outcome == VisitOutcome.ClosingTime ? "오늘 상담 시간이 끝나서 여기까지 하겠습니다. 다음에 이어서 말씀 나눌게요." :
                    active.outcome == VisitOutcome.ReservationEnd ? "예약된 상담 시간이 끝나서 오늘은 여기까지 하겠습니다." : definition.farewell;
            }
        }
        public static string TimeOfDay(double minute) => $"{(int)minute / 60:00}:{(int)minute % 60:00}";

        private bool CanStartVisit(PatientAppointment appointment) => (!AwaitingAssignment || appointment.beforeAssignment) &&
            (string.IsNullOrEmpty(appointment.afterConversation) ||
                terminalConversationProgress.TryGetValue(appointment.afterConversation, out TerminalConversationProgress progress) && progress.phase == TerminalConversationPhase.Completed);

        public IEnumerable<(int day, AppointmentProgress visit)> CompletedConsultationsFor(string profileId)
        {
            var days = dailyRecords.Values.Where(record => record.day < Day)
                .Select(record => (day: record.day, visits: record.visits));
            if (clinicDay != null) days = days.Append((Day, clinicDay.appointments));
            return days.OrderByDescending(day => day.day).SelectMany(day => day.visits
                .Where(visit => visit.profileId == profileId && visit.HasJournal)
                .OrderByDescending(visit => visit.SessionStart).Select(visit => (day.day, visit.Copy())));
        }

        public void RegisterClinicDay(ClinicDayPlan plan)
        {
            if (plan == null || plan.day < 0 || clinicPlans.ContainsKey(plan.day)) throw new CommandException("상담 일정의 날짜가 유효하지 않거나 중복됩니다.");
            foreach (PatientAppointment appointment in plan.appointments) ValidateAppointment(appointment);
            ClinicDayPlan copy = plan.Copy();
            if (copy.appointments.Select(a => a.id).Distinct().Count() != copy.appointments.Length)
                throw new CommandException("상담 예약의 ID가 중복됩니다.");
            PatientAppointment[] byStart = copy.appointments.OrderBy(a => a.startMinute).ToArray();
            for (int i = 1; i < byStart.Length; i++)
                if (byStart[i].startMinute < byStart[i - 1].BookingEndMinute)
                    throw new CommandException("상담 예약 시간이 겹칩니다.");
            clinicPlans.Add(copy.day, copy);
            foreach (PatientAppointment a in copy.appointments)
                RegisterAppointment(copy.day + ":" + a.id, a);
        }
        private void ValidateAppointment(PatientAppointment a)
        {
            if (a == null || !CommandParser.IsIdentifier(a.id) || string.IsNullOrWhiteSpace(a.profileId) ||
                a.dialogue == null || a.dialogue.Any(l => l == null || !HasValidConsultationResponses(l)))
                throw new CommandException("상담 예약의 ID/시간/대사를 확인하세요.");
            if (!ClinicHours.ContainsBooking(a.startMinute, a.BookingEndMinute))
                throw new CommandException("상담은 09:00~12:00 또는 13:00~18:00 사이에 한 시간 단위로 예약할 수 있습니다.");
            if (!string.IsNullOrEmpty(a.episodePoolId) && (a.dialogue.Length != 0 || !consultationPools.ContainsKey(a.episodePoolId)))
                throw new CommandException("일상 상담 예약에는 등록된 에피소드 모음 하나를 지정하세요.");
            if (!string.IsNullOrEmpty(a.afterConversation) && !terminalConversations.ContainsKey(a.afterConversation))
                throw new CommandException("상담 예약의 선행 터미널 대화가 없습니다: " + a.afterConversation);
            a.onArrival?.Validate(); a.onCompleted?.Validate(); a.onTimeLimit?.Validate();
        }
        private void RegisterAppointment(string id, PatientAppointment appointment)
        {
            if (!legacyDialogueCaptured) { legacyDialogueCount = Consultation.Count; legacyDialogueCaptured = true; }
            PatientAppointment a = appointment.Copy(); appointmentDefinitions.Add(id, a); appointmentLines.Add(id, Consultation.Count);
            foreach (ConsultationLine line in a.dialogue)
            {
                line.profileId = a.profileId; line.speaker = a.patientName; Consultation.Add(line);
            }
        }
        public void InitializeClinicDay()
        {
            if (!HasClinicSchedule || clinicDay?.day == Day) return;
            clinicDay = BuildClinicDay(Day);
            Revision++;
        }
        private ConsultationLine ScheduledConsultation => !IsDayEnded && ActiveVisit is AppointmentProgress active &&
            active.phase == VisitPhase.Talking && active.cursor < active.lineCount ?
            activeEpisodeLines?[active.cursor] ?? Consultation[active.lineStart + active.cursor] : null;
        private double NextClinicBoundary => ActiveVisit?.endMinute ?? NextVisit?.startMinute ?? ClinicHours.Closing;
        private void SyncClinicClock()
        {
            if (!HasClinicSchedule) return;
            InitializeClinicDay();
            AppointmentProgress active = ActiveVisit;
            if (active != null)
            {
                if (active.phase == VisitPhase.Talking && Minute >= active.endMinute)
                    CloseVisit(active.endMinute >= ClinicHours.Closing ? VisitOutcome.ClosingTime : NextVisit?.startMinute <= Minute ? VisitOutcome.NextReservation : VisitOutcome.ReservationEnd);
                return;
            }
            foreach (AppointmentProgress visit in clinicDay.appointments.Where(v => v.phase == VisitPhase.Waiting && v.endMinute <= Minute))
            {
                visit.phase = VisitPhase.Finished;
                visit.outcome = VisitOutcome.Missed;
                Revision++;
            }
            AppointmentProgress next = NextVisit;
            if (next == null || next.startMinute > Minute || IsDayEnded || Minute >= ClinicHours.Closing) return;
            PatientAppointment definition = appointmentDefinitions[next.id];
            SelectVisitEpisode(next, definition);
            next.startedMinute = Minute;
            next.phase = VisitPhase.Arriving; ResponseMinutes = 0;
            SetTrigger("clinic.met." + next.profileId, true); SetTrigger("clinic.present." + next.profileId, true); ApplyStoryEffects(definition.onArrival);
            Report(TimeOfDay(Minute) + " / " + next.patientName + " 님 입실"); NarrativeUpdated();
        }
        public void AdvanceVisitPresentation(string appointmentId, VisitPhase expectedPhase)
        {
            AppointmentProgress active = ActiveVisit;
            if (active == null || active.id != appointmentId || active.phase != expectedPhase) return; // Stale animation callback after a reload/transition.
            if (active.phase == VisitPhase.Arriving)
            {
                active.phase = VisitPhase.Talking;
                if (active.lineCount == 0) CloseVisit(VisitOutcome.Completed); else HearVisitLine(active);
            }
            else if (active.phase == VisitPhase.Closing) active.phase = VisitPhase.Departing;
            else if (active.phase == VisitPhase.Departing)
            {
                active.phase = VisitPhase.Finished; active.finishedMinute = Minute;
                activeEpisodeLines = null;
                SetTrigger("clinic.present." + active.profileId, false);
                ApplyStoryEffects(active.outcome == VisitOutcome.Completed ? appointmentDefinitions[active.id].onCompleted : appointmentDefinitions[active.id].onTimeLimit);
                Report(active.patientName + " 님 상담 종료"); SyncClinicClock();
            }
            Revision++; NarrativeUpdated();
        }
        public void FinishConsultation()
        {
            if (!HasClinicSchedule || ActiveVisit?.phase != VisitPhase.Talking) throw new CommandException("진행 중인 상담이 없습니다.");
            CloseVisit(VisitOutcome.Completed);
        }
        private void CloseVisit(VisitOutcome outcome)
        {
            AppointmentProgress active = ActiveVisit; if (active == null || active.phase != VisitPhase.Talking) return;
            active.phase = VisitPhase.Closing; active.outcome = outcome; ResponseMinutes = 0; Revision++; NarrativeUpdated();
        }
        public void WaitForNextAppointment()
        {
            if (!CanWaitForNextAppointment) throw new CommandException("진행 중인 상담을 마친 뒤 다음 예약을 기다릴 수 있습니다.");
            double until = NextVisit.startMinute - Minute;
            if (until > 0) Tick(until); else SyncClinicClock();
            NarrativeUpdated();
        }
        public void WaitUntilClosing()
        {
            if (!CanWaitUntilClosing) throw new CommandException("오늘 예약을 모두 마친 뒤 마감까지 기다릴 수 있습니다.");
            Tick(ClinicHours.Closing - Minute); NarrativeUpdated();
        }
        private void ReplyToAppointment()
        {
            AppointmentProgress active = ActiveVisit; if (active?.phase != VisitPhase.Talking) return;
            ResponseMinutes = 0; active.cursor++;
            if (active.cursor >= active.lineCount) CloseVisit(VisitOutcome.Completed);
            else { HearVisitLine(active); Revision++; NarrativeUpdated(); }
        }

        private void HearVisitLine(AppointmentProgress visit)
        {
            if (!string.IsNullOrEmpty(visit.episodeId)) { ConsultationIndex = -1; return; }
            ConsultationIndex = visit.lineStart + visit.cursor;
            heardConsultationLines.Add(ConsultationIndex);
        }

        private bool IsSavedVisitValid(AppointmentProgress visit)
        {
            if (!appointmentDefinitions.TryGetValue(visit.id, out PatientAppointment definition) || visit.cursor < 0 || visit.cursor > visit.lineCount ||
                visit.lineStart != appointmentLines[visit.id] || !Enum.IsDefined(typeof(VisitPhase), visit.phase)) return false;
            ConsultationEpisode episode = EpisodeFor(visit);
            if (!string.IsNullOrEmpty(visit.episodeId)) return episode != null && visit.lineCount == episode.lines.Length;
            return visit.lineCount == definition.dialogue.Length && (string.IsNullOrEmpty(definition.episodePoolId) ||
                visit.phase == VisitPhase.Waiting || visit.outcome == VisitOutcome.Missed);
        }

        private void RestoreClinic(KernelSnapshot snapshot)
        {
            heardConsultationLines.Clear(); heardConsultationLines.UnionWith(snapshot.heardConsultationLines ?? Array.Empty<int>());
            activeEpisodeLines = null;
            if (!HasClinicSchedule) return;
            if (snapshot.clinicDay != null)
            {
                ClinicDayState restored = snapshot.clinicDay.Copy();
                if (restored.day != Day || restored.appointments.Any(a => !IsSavedVisitValid(a)) ||
                    restored.appointments.Count(a => a.phase != VisitPhase.Waiting && a.phase != VisitPhase.Finished) > 1)
                    throw new CommandException("저장된 상담 일정이 현재 대사와 맞지 않습니다.");
                // Older onboarding saves had a frozen clock and only the case appointments.
                if (AwaitingAssignment && restored.appointments.All(a => a.phase == VisitPhase.Waiting && !appointmentDefinitions[a.id].beforeAssignment))
                {
                    ClinicDayState updated = BuildClinicDay(Day);
                    if (updated.appointments.Any(a => appointmentDefinitions[a.id].beforeAssignment)) restored = updated;
                }
                clinicDay = restored;
                if (ActiveVisit is AppointmentProgress active && EpisodeFor(active) is ConsultationEpisode episode) BindVisitEpisode(active, episode);
                return;
            }
            clinicDay = null; InitializeClinicDay();
            if (!AwaitingAssignment)
                clinicDay.appointments = clinicDay.appointments.Where(a => !appointmentDefinitions[a.id].beforeAssignment).ToArray();
            // Old saves retain finished/ongoing legacy dialogue instead of replaying the first patient.
            foreach (AppointmentProgress visit in clinicDay.appointments.Where(a => a.endMinute <= Minute))
            { visit.phase = VisitPhase.Finished; visit.outcome = VisitOutcome.Missed; }
            AppointmentProgress first = clinicDay.appointments.FirstOrDefault(a => string.IsNullOrEmpty(appointmentDefinitions[a.id].episodePoolId));
            if (Day == 1 && first != null && first.phase == VisitPhase.Waiting && first.startMinute <= Minute && snapshot.consultationIndex > 0)
            {
                first.cursor = Math.Min(first.lineCount, snapshot.consultationIndex);
                first.phase = snapshot.consultationIndex >= legacyDialogueCount ? VisitPhase.Finished : VisitPhase.Talking;
                first.outcome = first.phase == VisitPhase.Finished ? VisitOutcome.Completed : VisitOutcome.None;
                ConsultationIndex = first.lineStart + Math.Min(first.cursor, Math.Max(0, first.lineCount - 1));
                for (int i = 0; i <= Math.Min(first.cursor, first.lineCount - 1); i++) heardConsultationLines.Add(first.lineStart + i);
            }
        }
    }
}
