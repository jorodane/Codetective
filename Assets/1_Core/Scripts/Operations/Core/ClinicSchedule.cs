using System;
using System.Collections.Generic;
using System.Linq;

namespace Codetective.Operations
{
    public enum VisitPhase { Waiting, Arriving, Talking, Closing, Departing, Finished }
    public enum VisitOutcome { None, Completed, NextReservation, ClosingTime, Missed, ReservationEnd }

    [Serializable] public sealed class PatientAppointment
    {
        public string id;
        public string profileId = "client";
        public string patientName = "상담 손님";
        public int startMinute = 540;
        public int endMinute; // 0: next reservation, or 18:00 for the last patient.
        public string greeting = "안녕하세요. 예약하고 왔습니다.";
        public string farewell = "오늘 말씀해 주셔서 감사합니다. 다음 상담에서 이어가겠습니다.";
        public ConsultationLine[] dialogue = Array.Empty<ConsultationLine>();
        public StoryCondition condition = new StoryCondition();
        public StoryEffects onArrival = new StoryEffects();
        public StoryEffects onCompleted = new StoryEffects();
        public StoryEffects onTimeLimit = new StoryEffects();
        public PatientAppointment Copy() => new PatientAppointment { id = id, profileId = profileId, patientName = patientName, startMinute = startMinute,
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
        public string id, profileId, patientName;
        public int startMinute, endMinute, lineStart, lineCount, cursor;
        public VisitPhase phase;
        public VisitOutcome outcome;
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
        public AppointmentProgress NextAppointment => clinicDay?.appointments.FirstOrDefault(a => a.phase == VisitPhase.Waiting)?.Copy();
        public bool IsClinicTransitioning => !AwaitingAssignment && ActiveVisit != null && ActiveVisit.phase != VisitPhase.Talking;
        public bool CanWaitForNextAppointment => HasClinicSchedule && !AwaitingAssignment && !TerminalConversationPausesWorld && !IsDayEnded && ActiveVisit == null && NextAppointment != null;
        public bool CanWaitUntilClosing => HasClinicSchedule && !AwaitingAssignment && !TerminalConversationPausesWorld && !IsDayEnded && ActiveVisit == null && NextAppointment == null;
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

        public void RegisterClinicDay(ClinicDayPlan plan)
        {
            if (plan == null || plan.day < 0 || clinicPlans.ContainsKey(plan.day)) throw new CommandException("상담 일정의 날짜가 유효하지 않거나 중복됩니다.");
            ClinicDayPlan copy = plan.Copy();
            if (copy.appointments.Select(a => a.id).Distinct().Count() != copy.appointments.Length || copy.appointments.Select(a => a.startMinute).Distinct().Count() != copy.appointments.Length)
                throw new CommandException("상담 예약의 ID/시간/대사를 확인하세요. 같은 시간에 두 명을 예약할 수 없습니다.");
            foreach (PatientAppointment a in copy.appointments) ValidateAppointment(a);
            clinicPlans.Add(copy.day, copy);
            foreach (PatientAppointment a in copy.appointments)
                RegisterAppointment(copy.day + ":" + a.id, a);
        }
        private static void ValidateAppointment(PatientAppointment a)
        {
            if (a == null || !CommandParser.IsIdentifier(a.id) || string.IsNullOrWhiteSpace(a.profileId) || a.startMinute < 540 || a.startMinute >= 1080 ||
                (a.endMinute != 0 && (a.endMinute <= a.startMinute || a.endMinute > 1080)) || a.dialogue == null || a.dialogue.Any(l => l == null))
                throw new CommandException("상담 예약의 ID/시간/대사를 확인하세요.");
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
        private ConsultationLine ScheduledConsultation => !AwaitingAssignment && !IsDayEnded && ActiveVisit?.phase == VisitPhase.Talking && ActiveVisit.cursor < ActiveVisit.lineCount ?
            Consultation[ActiveVisit.lineStart + ActiveVisit.cursor] : null;
        private double NextClinicBoundary
        {
            get
            {
                if (!HasClinicSchedule) return 1080;
                return ActiveVisit != null ? ActiveVisit.endMinute : NextAppointment?.startMinute ?? 1080;
            }
        }
        private void SyncClinicClock()
        {
            if (!HasClinicSchedule || AwaitingAssignment) return;
            InitializeClinicDay();
            AppointmentProgress active = ActiveVisit;
            if (active != null)
            {
                if (active.phase == VisitPhase.Talking && Minute >= active.endMinute)
                    CloseVisit(active.endMinute >= 1080 ? VisitOutcome.ClosingTime : NextAppointment?.startMinute <= Minute ? VisitOutcome.NextReservation : VisitOutcome.ReservationEnd);
                return;
            }
            AppointmentProgress next = clinicDay.appointments.FirstOrDefault(a => a.phase == VisitPhase.Waiting && a.startMinute <= Minute);
            if (next == null || IsDayEnded || Minute >= 1080) return;
            next.phase = VisitPhase.Arriving; ResponseMinutes = 0;
            SetTrigger("clinic.met." + next.profileId, true); SetTrigger("clinic.present." + next.profileId, true); ApplyStoryEffects(appointmentDefinitions[next.id].onArrival);
            Report(TimeOfDay(Minute) + " / " + next.patientName + " 님 입실"); NarrativeUpdated();
        }
        public void AdvanceVisitPresentation(string appointmentId, VisitPhase expectedPhase)
        {
            AppointmentProgress active = ActiveVisit;
            if (active == null || active.id != appointmentId || active.phase != expectedPhase) return; // Stale animation callback after a reload/transition.
            if (active.phase == VisitPhase.Arriving)
            {
                active.phase = VisitPhase.Talking; ConsultationIndex = active.lineStart + active.cursor;
                if (active.lineCount == 0) CloseVisit(VisitOutcome.Completed); else heardConsultationLines.Add(ConsultationIndex);
            }
            else if (active.phase == VisitPhase.Closing) active.phase = VisitPhase.Departing;
            else if (active.phase == VisitPhase.Departing)
            {
                active.phase = VisitPhase.Finished;
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
            double until = NextAppointment.startMinute - Minute;
            if (until > 0) Tick(until); else SyncClinicClock();
            NarrativeUpdated();
        }
        public void WaitUntilClosing()
        {
            if (!CanWaitUntilClosing) throw new CommandException("오늘 예약을 모두 마친 뒤 마감까지 기다릴 수 있습니다.");
            Tick(1080 - Minute); NarrativeUpdated();
        }
        private void ReplyToAppointment()
        {
            AppointmentProgress active = ActiveVisit; if (active?.phase != VisitPhase.Talking) return;
            ResponseMinutes = 0; active.cursor++;
            if (active.cursor >= active.lineCount) CloseVisit(VisitOutcome.Completed);
            else { ConsultationIndex = active.lineStart + active.cursor; heardConsultationLines.Add(ConsultationIndex); Revision++; NarrativeUpdated(); }
        }
        private void RestoreClinic(KernelSnapshot snapshot)
        {
            heardConsultationLines.Clear(); heardConsultationLines.UnionWith(snapshot.heardConsultationLines ?? Array.Empty<int>());
            if (!HasClinicSchedule) return;
            if (snapshot.clinicDay != null)
            {
                ClinicDayState restored = snapshot.clinicDay.Copy();
                if (restored.day != Day || restored.appointments.Any(a => !appointmentDefinitions.ContainsKey(a.id) || a.cursor < 0 || a.cursor > a.lineCount ||
                    a.lineStart != appointmentLines[a.id] || a.lineCount != appointmentDefinitions[a.id].dialogue.Length || !Enum.IsDefined(typeof(VisitPhase), a.phase)) ||
                    restored.appointments.Count(a => a.phase != VisitPhase.Waiting && a.phase != VisitPhase.Finished) > 1)
                    throw new CommandException("저장된 상담 일정이 현재 대사와 맞지 않습니다.");
                clinicDay = restored; return;
            }
            clinicDay = null; InitializeClinicDay();
            // Old saves retain finished/ongoing legacy dialogue instead of replaying the first patient.
            foreach (AppointmentProgress visit in clinicDay.appointments.Where(a => a.endMinute <= Minute))
            { visit.phase = VisitPhase.Finished; visit.outcome = VisitOutcome.Missed; }
            AppointmentProgress first = clinicDay.appointments.FirstOrDefault();
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
