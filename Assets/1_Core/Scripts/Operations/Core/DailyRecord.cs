using System;
using System.Collections.Generic;
using System.Linq;

namespace Codetective.Operations
{
    [Serializable] public sealed class DayPresentationState { public int day; public bool introduced, mailDeferred; }
    [Serializable] public sealed class DailyLedger
    {
        public int day;
        public int[] dataIds = Array.Empty<int>();
        public string[] evidenceIds;
        public int[] noteIds = Array.Empty<int>();
        public string[] readMailIds = Array.Empty<string>();
        public string[] authorities = Array.Empty<string>();
        public int completedThreadTasks;
    }
    [Serializable] public sealed class DailyRecord
    {
        public int day;
        public string subtitle;
        public AppointmentProgress[] visits = Array.Empty<AppointmentProgress>();
        public string[] newData = Array.Empty<string>();
        public string[] newAuthorities = Array.Empty<string>();
        public int notesWritten, mailRead, completedThreadTasks;
        public DailyRecord Copy() => new DailyRecord { day = day, subtitle = subtitle, visits = visits.Select(v => v.Copy()).ToArray(),
            newData = newData.ToArray(), newAuthorities = newAuthorities.ToArray(), notesWritten = notesWritten, mailRead = mailRead, completedThreadTasks = completedThreadTasks };
    }
    public sealed partial class KernelRuntime
    {
        private DailyLedger dayLedger;
        private DayPresentationState dayPresentation = new DayPresentationState { day = 1 };
        private readonly Dictionary<int, DailyRecord> dailyRecords = new Dictionary<int, DailyRecord>();
        public bool DayIntroSeen => dayPresentation.day == Day && dayPresentation.introduced;
        public void MarkDayIntroSeen()
        {
            if (DayIntroSeen && !DayMailDeferred) return;
            dayPresentation = new DayPresentationState { day = Day, introduced = true }; Revision++; NarrativeUpdated();
        }
        public void InitializeDailyRecord()
        {
            if (dayLedger?.day == Day) return;
            dayLedger = new DailyLedger { day = Day, dataIds = data.Values.Select(s => s.id).ToArray(), evidenceIds = evidence.Keys.ToArray(), noteIds = notes.Select(n => n.id).ToArray(),
                readMailIds = mailProgress.Values.Where(m => m.read).Select(m => m.id).ToArray(), authorities = Authorities.ToArray() };
        }
        public DailyRecord FinalizeDay()
        {
            if (!IsDayEnded || IsClinicTransitioning) throw new CommandException("상담 퇴실과 업무 마감 후 정산할 수 있습니다.");
            InitializeDailyRecord();
            AppointmentProgress[] visits = DailyAppointments.ToArray();
            foreach (AppointmentProgress visit in visits.Where(v => v.phase == VisitPhase.Waiting)) { visit.phase = VisitPhase.Finished; visit.outcome = VisitOutcome.Missed; }
            var record = new DailyRecord { day = Day, subtitle = DaySubtitle, visits = visits,
                newData = Evidence.Where(e => !(dayLedger.evidenceIds ?? Array.Empty<string>()).Contains(e.Id) && e.Day == Day &&
                    !observations.Any(o => !string.IsNullOrEmpty(o.statement) && o.value.CanReferenceEntity && o.value.id == e.Id)).Select(e => e.Name).ToArray(),
                newAuthorities = Authorities.Except(dayLedger.authorities).ToArray(), notesWritten = notes.Count(n => !dayLedger.noteIds.Contains(n.id)),
                mailRead = mailProgress.Values.Count(m => m.read && !dayLedger.readMailIds.Contains(m.id)), completedThreadTasks = dayLedger.completedThreadTasks };
            dailyRecords[Day] = record; NarrativeUpdated(); return record.Copy();
        }
        public DailyRecord DayRecord(int day) => dailyRecords.TryGetValue(day, out DailyRecord record) ? record.Copy() : null;
        private static DailyLedger CopyLedger(DailyLedger ledger) => ledger == null ? null : new DailyLedger { day = ledger.day, dataIds = ledger.dataIds.ToArray(), evidenceIds = ledger.evidenceIds?.ToArray(),
            noteIds = ledger.noteIds.ToArray(), readMailIds = ledger.readMailIds.ToArray(), authorities = ledger.authorities.ToArray(), completedThreadTasks = ledger.completedThreadTasks };
        private void RestoreDailyRecord(KernelSnapshot snapshot)
        {
            dayLedger = CopyLedger(snapshot.dayLedger); InitializeDailyRecord();
            dayPresentation = snapshot.dayPresentation != null ? new DayPresentationState { day = snapshot.dayPresentation.day, introduced = snapshot.dayPresentation.introduced, mailDeferred = snapshot.dayPresentation.mailDeferred } :
                new DayPresentationState { day = Day, introduced = true }; // Existing saves have already started their day.
            dailyRecords.Clear(); foreach (DailyRecord record in snapshot.dailyRecords ?? Array.Empty<DailyRecord>()) dailyRecords.Add(record.day, record.Copy());
        }
    }
}
