using System;
using System.Linq;
using Codetective.Operations;
using NUnit.Framework;

namespace Codetective.Tests.Operations
{
    public sealed class ClinicScheduleTests
    {
        private static PatientAppointment Visit(string id, string person, int minute) => new PatientAppointment { id = id, profileId = person, patientName = person,
            startMinute = minute, dialogue = new[] { new ConsultationLine { sentence = "첫 진술", keyword = "첫 단서", fact = id + "_first", dataName = id + "First" },
                new ConsultationLine { sentence = "두 번째 진술", keyword = "두 번째 단서", fact = id + "_second", dataName = id + "Second" } } };
        private static KernelRuntime World()
        {
            KernelRuntime r = ClinicScenario.Create(); r.CreateInstance("Person", "second", "두 번째 손님");
            r.RegisterClinicDay(new ClinicDayPlan { day = 1, subtitle = "문이 열리는 시간", appointments = new[] { Visit("morning", "client", 600), Visit("afternoon", "second", 660) } });
            r.RegisterClinicDay(new ClinicDayPlan { day = 0, subtitle = "이어지는 이야기", appointments = new[] { Visit("daily", "client", 600) } });
            r.InitializeClinicDay(); r.InitializeDailyRecord(); return r;
        }
        private static void Advance(KernelRuntime r)
        { AppointmentProgress v = r.CurrentAppointment; r.AdvanceVisitPresentation(v.id, v.phase); }
        private static void Leave(KernelRuntime r) { while (r.IsClinicTransitioning) Advance(r); }
        [Test]
        public void PatientArrivesAtTheirReservationAndDialogueWaitsForTheEntrance()
        {
            KernelRuntime r = World(); Assert.That(r.CurrentConsultation, Is.Null); r.Tick(59);
            Assert.That(r.CurrentAppointment, Is.Null); r.Tick(1);
            Assert.That(r.Minute, Is.EqualTo(600)); Assert.That(r.CurrentAppointment.phase, Is.EqualTo(VisitPhase.Arriving));
            Assert.That(r.CurrentConsultation, Is.Null); r.Tick(20); Assert.That(r.Minute, Is.EqualTo(600));
            Advance(r); Assert.That(r.CurrentConsultation.sentence, Is.EqualTo("첫 진술"));
            Assert.That(KernelRuntime.ProfileOf(r.CurrentConsultation), Is.EqualTo("client"));
            Assert.That(r.GetTrigger("clinic.present.client"), Is.True);
        }
        [Test]
        public void NaturalCompletionLeavesAnEmptyRoomAndWaitingAdvancesOnlyToTheNextReservation()
        {
            KernelRuntime r = World(); r.WaitForNextAppointment(); Advance(r); r.Reply(); r.Reply();
            Assert.That(r.CurrentAppointment.phase, Is.EqualTo(VisitPhase.Closing)); Leave(r);
            Assert.That(r.CurrentAppointment, Is.Null); Assert.That(r.CurrentConsultation, Is.Null); Assert.That(r.CanWaitForNextAppointment, Is.True);
            Assert.That(r.GetTrigger("clinic.present.client"), Is.False); Assert.That(r.GetTrigger("clinic.met.client"), Is.True);
            double suspicion = r.Suspicion; r.WaitForNextAppointment();
            Assert.That(r.Minute, Is.EqualTo(660)); Assert.That(r.Suspicion, Is.EqualTo(suspicion));
            Assert.That(r.CurrentAppointment.profileId, Is.EqualTo("second")); Assert.That(r.CurrentAppointment.phase, Is.EqualTo(VisitPhase.Arriving));
        }
        [Test]
        public void TimeLimitClosesTheOldVisitBeforeTheNextPatientEntersEvenOnALargeTick()
        {
            KernelRuntime r = World(); r.WaitForNextAppointment(); Advance(r); r.Tick(480);
            Assert.That(r.Minute, Is.EqualTo(660)); Assert.That(r.CurrentAppointment.profileId, Is.EqualTo("client"));
            Assert.That(r.CurrentAppointment.phase, Is.EqualTo(VisitPhase.Closing)); Assert.That(r.CurrentAppointment.outcome, Is.EqualTo(VisitOutcome.NextReservation));
            Assert.That(r.VisitPresentationText, Does.Contain("다음 예약 손님"));
            Assert.That(r.CurrentConsultation, Is.Null); Assert.That(r.GetTrigger("clinic.present.second"), Is.False);
            r.Reply(); Advance(r); Assert.That(r.CurrentAppointment.phase, Is.EqualTo(VisitPhase.Departing));
            Advance(r); Assert.That(r.CurrentAppointment.profileId, Is.EqualTo("second")); Assert.That(r.CurrentAppointment.phase, Is.EqualTo(VisitPhase.Arriving));
            Assert.That(r.Minute, Is.EqualTo(660)); Advance(r); Assert.That(r.CurrentAppointment.phase, Is.EqualTo(VisitPhase.Talking));
        }
        [Test]
        public void WaitingIsRejectedWhileTalkingAndThreadWorkContinuesWhileTheRoomIsEmpty()
        {
            KernelRuntime r = World(); r.Facts.Add("basement_noise"); r.Execute("thread01.run(recon, archive)");
            r.WaitForNextAppointment(); Assert.That(r.Agent("thread01").State, Is.EqualTo(AgentState.Ready));
            Assert.That(r.Data.ContainsKey("thread01_report"), Is.True); Advance(r);
            Assert.That(() => r.WaitForNextAppointment(), Throws.TypeOf<CommandException>());
            Assert.That(() => r.WaitUntilClosing(), Throws.TypeOf<CommandException>());
        }
        [Test]
        public void UnheardLinesAndFuturePatientsCannotBeCollectedByGuessingIndices()
        {
            KernelRuntime r = World(); r.WaitForNextAppointment();
            Assert.That(() => r.RecordClue(r.DailyAppointments.First().lineStart), Throws.TypeOf<CommandException>());
            Advance(r); int first = r.ConsultationIndex; r.RecordClue(first);
            Assert.That(() => r.RecordClue(first + 1), Throws.TypeOf<CommandException>());
            Assert.That(() => r.RecordClue(r.DailyAppointments.Last().lineStart), Throws.TypeOf<CommandException>());
            r.Reply(); Assert.That(r.RecordClue(first + 1).kind, Is.EqualTo(DataKind.Clue));
        }
        [TestCase(VisitPhase.Arriving)] [TestCase(VisitPhase.Talking)] [TestCase(VisitPhase.Closing)] [TestCase(VisitPhase.Departing)]
        public void EveryVisitPhaseSurvivesReloadAndOldAnimationCallbacksDoNotAdvanceItTwice(VisitPhase phase)
        {
            KernelRuntime r = World(); r.WaitForNextAppointment();
            if (phase != VisitPhase.Arriving) Advance(r);
            if (phase == VisitPhase.Closing || phase == VisitPhase.Departing) r.FinishConsultation();
            if (phase == VisitPhase.Departing) Advance(r);
            KernelSnapshot saved = r.Capture(); KernelRuntime restored = World(); restored.Restore(saved);
            Assert.That(restored.CurrentAppointment.phase, Is.EqualTo(phase));
            AppointmentProgress before = restored.CurrentAppointment; restored.AdvanceVisitPresentation("stale-id", phase);
            Assert.That(restored.CurrentAppointment.phase, Is.EqualTo(phase));
            restored.AdvanceVisitPresentation(before.id, before.phase);
            VisitPhase after = restored.CurrentAppointment?.phase ?? VisitPhase.Finished;
            restored.AdvanceVisitPresentation(before.id, before.phase); Assert.That(restored.CurrentAppointment?.phase ?? VisitPhase.Finished, Is.EqualTo(after));
            Assert.That(r.CurrentAppointment.phase, Is.EqualTo(phase));
        }
        [Test]
        public void ClosingTimeWaitsForDepartureBeforeTheReportOrNextDay()
        {
            KernelRuntime r = World(); r.WaitForNextAppointment(); Advance(r); r.FinishConsultation(); Leave(r);
            r.WaitForNextAppointment(); Advance(r); r.Tick(420);
            Assert.That(r.IsDayEnded, Is.True); Assert.That(r.CurrentAppointment.outcome, Is.EqualTo(VisitOutcome.ClosingTime));
            Assert.That(() => r.FinalizeDay(), Throws.TypeOf<CommandException>()); Assert.That(() => r.NextDay(), Throws.TypeOf<CommandException>());
            Leave(r); Assert.That(r.FinalizeDay().visits.Last().outcome, Is.EqualTo(VisitOutcome.ClosingTime));
            r.NextDay(); Assert.That(r.Day, Is.EqualTo(2)); Assert.That(r.DaySubtitle, Is.EqualTo("이어지는 이야기"));
            Assert.That(r.DayIntroSeen, Is.False); Assert.That(r.DailyAppointments.Single().phase, Is.EqualTo(VisitPhase.Waiting));
        }
        [Test]
        public void ReportCountsTheDayAndUpdatesNotesAddedAfterClosingWithoutCountingPreviousDays()
        {
            KernelRuntime r = World(); r.MarkDayIntroSeen(); r.WaitForNextAppointment(); Advance(r); r.RecordClue(r.ConsultationIndex);
            r.FinishConsultation(); Leave(r); r.WaitForNextAppointment(); Advance(r); r.FinishConsultation(); Leave(r);
            r.AddConsultationNote("client", "오늘 기록"); r.WaitUntilClosing(); DailyRecord report = r.FinalizeDay();
            Assert.That(report.visits.Count(v => v.outcome == VisitOutcome.Completed), Is.EqualTo(2)); Assert.That(report.newData.Length, Is.EqualTo(1));
            Assert.That(report.notesWritten, Is.EqualTo(1)); r.AddConsultationNote("client", "정산 후 기록"); Assert.That(r.FinalizeDay().notesWritten, Is.EqualTo(2));
            KernelRuntime restored = World(); restored.Restore(r.Capture()); Assert.That(restored.DayRecord(1).notesWritten, Is.EqualTo(2));
            Assert.That(restored.DayIntroSeen, Is.True); restored.NextDay(); restored.WaitForNextAppointment(); Advance(restored); restored.FinishConsultation(); Leave(restored);
            restored.WaitUntilClosing(); Assert.That(restored.FinalizeDay().notesWritten, Is.Zero);
        }
        [Test]
        public void AppointmentEffectsAndTestsUseTheSharedStoryStateWithoutChangingTheRealVisit()
        {
            KernelRuntime r = World(); r.Entity("hq").Operations.Add(new Operation { Code = "wait", ReturnKind = DataKind.Text,
                Invoke = c => { c.Runtime.WaitForNextAppointment(); return Datum.Text("waited"); } });
            Assert.That(r.TestProtocol("protocol simulate() { return hq.wait(); }", "simulate()").Success, Is.True);
            Assert.That(r.Minute, Is.EqualTo(540)); Assert.That(r.CurrentAppointment, Is.Null); Assert.That(r.GetTrigger("clinic.met.client"), Is.False);
            var other = new KernelRuntime(); PatientAppointment visit = Visit("visit", "person", 540);
            visit.onCompleted = new StoryEffects { quests = new[] { new QuestProgress { id = "clinic", stage = 10 } } };
            other.RegisterClinicDay(new ClinicDayPlan { appointments = new[] { visit } }); other.InitializeClinicDay(); other.Tick(1); Advance(other); other.FinishConsultation(); Leave(other);
            Assert.That(other.GetQuest("clinic").stage, Is.EqualTo(10));
        }
        [Test]
        public void AnEmptyScheduleAndAssignmentGateDoNotStartPhantomPatients()
        {
            KernelRuntime r = World(); r.StoryStartFact = "assigned"; r.Tick(400);
            Assert.That(r.Minute, Is.EqualTo(540)); Assert.That(r.CurrentAppointment, Is.Null); Assert.That(r.CanWaitForNextAppointment, Is.False);
            r.Facts.Add("assigned"); r.WaitForNextAppointment(); Assert.That(r.CurrentAppointment, Is.Not.Null);
            var empty = new KernelRuntime(); empty.RegisterClinicDay(new ClinicDayPlan { appointments = Array.Empty<PatientAppointment>() }); empty.InitializeClinicDay();
            Assert.That(empty.CurrentConsultation, Is.Null); empty.WaitUntilClosing(); Assert.That(empty.IsDayEnded, Is.True);
        }
        [Test]
        public void DuplicateBookingTimesAreRejectedAndExistingSavesDoNotReplayFinishedLegacyDialogue()
        {
            var invalid = new KernelRuntime(); Assert.That(() => invalid.RegisterClinicDay(new ClinicDayPlan { appointments = new[] { Visit("a", "a", 600), Visit("b", "b", 600) } }), Throws.TypeOf<CommandException>());
            KernelRuntime old = ClinicScenario.Create(); old.Reply(); old.Reply(); old.Tick(90); KernelSnapshot saved = old.Capture(); saved.clinicDay = null;
            KernelRuntime restored = World(); restored.Restore(saved); Assert.That(restored.DailyAppointments.First().outcome, Is.EqualTo(VisitOutcome.Completed));
            Assert.That(restored.CurrentConsultation, Is.Null); Assert.That(restored.CanWaitForNextAppointment, Is.True);
        }
        [Test]
        public void FreedDataIdsAreNotReusedAfterReloadAndNewAllocationsRemainVisibleInTheReport()
        {
            var r = new KernelRuntime(); r.Allocate("old"); r.InitializeDailyRecord(); r.Free("old");
            var restored = new KernelRuntime(); restored.Restore(r.Capture()); restored.Store("newData", Datum.Text("new"));
            restored.Tick(540); Assert.That(restored.FinalizeDay().newData, Is.EqualTo(new[] { "newData" }));
        }
    }
}
