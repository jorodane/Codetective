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
        private static KernelRuntime World(int secondStart = 660)
        {
            KernelRuntime r = ClinicScenario.Create(); r.CreateInstance("Person", "second", "두 번째 손님");
            r.RegisterClinicDay(new ClinicDayPlan { day = 1, subtitle = "문이 열리는 시간", appointments = new[] { Visit("morning", "client", 600), Visit("afternoon", "second", secondStart) } });
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
            Assert.That(r.Agent("thread01").Report.kind, Is.EqualTo(DataKind.Document)); Advance(r);
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
            KernelRuntime r = World(1020); r.WaitForNextAppointment(); Advance(r); r.FinishConsultation(); Leave(r);
            r.WaitForNextAppointment(); Advance(r); r.Tick(60);
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

        private static KernelRuntime TutorialClinic()
        {
            KernelRuntime r = ClinicScenario.Create();
            r.StoryStartFact = "assigned";
            r.RegisterMail(new StoryMail { id = "welcome", mailbox = Mailbox.Kernel });
            r.RegisterTerminalConversation(new TerminalConversationDefinition { id = "briefing", requiredMailRead = "welcome",
                steps = new[] { new TerminalConversationStep { id = "welcome", lines = new[] { new TerminalDialogueLine { text = "첫 손님이 곧 도착합니다." } } } } });
            r.RegisterConsultationPool("routine", new[] { "시계", "양말", "베개" }.Select(topic => new ConsultationEpisode { id = topic,
                lines = new[] { new ConsultationLine { sentence = topic }, new ConsultationLine { sentence = "어떻게 생각하세요?", responses = new[] { "음.", "글쎄요." } },
                    new ConsultationLine { sentence = "그렇군요." } } }).ToArray());
            var visit = new PatientAppointment { id = "regular", profileId = "regular", patientName = "한정우", title = "일상 스트레스 상담",
                startMinute = 540, endMinute = 600, beforeAssignment = true, afterConversation = "briefing", episodePoolId = "routine" };
            r.RegisterClinicDay(new ClinicDayPlan { appointments = new[] { visit, Visit("case", "client", 660) } });
            PatientAppointment recurring = visit.Copy(); recurring.beforeAssignment = false; recurring.afterConversation = "";
            r.RegisterClinicDay(new ClinicDayPlan { day = 0, appointments = new[] { recurring } });
            r.RegisterMail(new StoryMail { id = "mission", mailbox = Mailbox.Kernel, afterAppointmentId = "1:regular",
                minutesAfterAppointment = 5, requiresAcceptance = true, acceptFacts = new[] { "assigned" } });
            r.InitializeClinicDay(); return r;
        }

        [Test]
        public void APrebookedVisitStartsAfterTheBriefingAndAssignmentArrivesFiveMinutesAfterDeparture()
        {
            KernelRuntime r = TutorialClinic();
            r.Tick(50); r.ReadMail("welcome"); r.Tick(50); r.EnterTerminalPage("kernel"); r.Tick(50);
            Assert.That(r.CurrentAppointment, Is.Null);
            Assert.That(r.Minute, Is.EqualTo(540));
            Assert.That(r.Inbox(Mailbox.Kernel).Select(m => m.id), Is.EqualTo(new[] { "welcome" }));
            r.AdvanceTerminalConversation(1000); r.Tick(1); Advance(r);
            Assert.That(r.CurrentAppointment.id, Is.EqualTo("1:regular"));
            r.Tick(65); Assert.That(r.CurrentAppointment.outcome, Is.EqualTo(VisitOutcome.ReservationEnd));
            Advance(r); r.Tick(30);
            Assert.That(r.Inbox(Mailbox.Kernel).Any(m => m.id == "mission"), Is.False);
            Advance(r);
            Assert.That(r.CompletedConsultationsFor("regular").Single().visit.SessionEnd, Is.EqualTo(600));
            r.Tick(4.5);
            KernelRuntime restored = TutorialClinic(); restored.Restore(r.Capture());
            Assert.That(restored.Inbox(Mailbox.Kernel).Any(m => m.id == "mission"), Is.False);
            restored.Tick(50);
            Assert.That(restored.Minute, Is.EqualTo(605));
            Assert.That(restored.CurrentAppointment, Is.Null);
            restored.ReadMail("mission"); restored.AcceptMail("mission");
            restored.WaitForNextAppointment();
            Assert.That(restored.CurrentAppointment.id, Is.EqualTo("1:case"));
        }

        [Test]
        public void RoutineTopicsVaryBySaveAndBothResponsesResumeAtTheSameLineWithoutClues()
        {
            var topics = new System.Collections.Generic.HashSet<string>();
            for (int seed = 1; seed <= 15; seed++)
            {
                KernelRuntime r = TutorialClinic();
                KernelSnapshot initial = r.Capture(); initial.consultationSeed = seed; r.Restore(initial);
                r.ReadMail("welcome"); r.EnterTerminalPage("kernel"); r.AdvanceTerminalConversation(1000);
                r.Tick(1); Advance(r); topics.Add(r.CurrentConsultation.sentence);
                Assert.That(KernelRuntime.ProfileOf(r.CurrentConsultation), Is.EqualTo("regular"));
                r.Tick(6); r.Reply();
                Assert.That(r.CurrentConsultation.responses.Length, Is.EqualTo(2));
                r.Reply(); Assert.That(r.CurrentConsultation.sentence, Is.EqualTo("어떻게 생각하세요?"));
                Assert.That(() => r.RecordClue(r.ConsultationIndex), Throws.TypeOf<CommandException>());
                KernelSnapshot saved = r.Capture();
                KernelRuntime restored = TutorialClinic(); restored.Restore(saved);
                Assert.That(restored.CurrentAppointment.episodeId, Is.EqualTo(r.CurrentAppointment.episodeId));
                Assert.That(restored.CurrentConsultation.responses, Is.EqualTo(r.CurrentConsultation.responses));
                r.ReplyToConsultationChoice(0); restored.ReplyToConsultationChoice(1);
                Assert.That(restored.CurrentConsultation.sentence, Is.EqualTo(r.CurrentConsultation.sentence));
                restored.Reply(); Leave(restored);
                Assert.That(restored.ProfileJournalText("regular", 0), Does.Contain("09:00 ~ 09:06").And.Contain("일상 스트레스 상담"));
                Assert.That(restored.NotesFor("regular"), Is.Empty);
                Assert.That(restored.AddConsultationNote("regular", "직접 기록한 상담").startMinute, Is.EqualTo(540));
                Assert.That(restored.RecordedClues, Is.Empty);
                Assert.That(restored.Suspicion, Is.Zero);
                Assert.That(restored.ScheduleForDay(2).Single().profileId, Is.EqualTo("regular"));
            }
            Assert.That(topics.Count, Is.GreaterThan(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OlderCalendarsAddTheTutorialOnlyIfTheAssignmentHasNotAlreadyStarted(bool assigned)
        {
            KernelRuntime r = TutorialClinic();
            if (assigned) r.Facts.Add("assigned");
            KernelSnapshot old = r.Capture();
            old.clinicDay.appointments = old.clinicDay.appointments.Where(visit => visit.id != "1:regular").ToArray();
            KernelRuntime restored = TutorialClinic(); restored.Restore(old);
            Assert.That(restored.DailyAppointments.Any(visit => visit.id == "1:regular"), Is.EqualTo(!assigned));
            if (assigned)
            {
                restored.WaitForNextAppointment(); Advance(restored);
                Assert.That(restored.RecordClue(restored.ConsultationIndex).id, Is.EqualTo("case_first"));
            }
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
        public void DayPlansAllowLongAppointmentsButRejectOverlappingHours()
        {
            var r = new KernelRuntime();
            PatientAppointment longVisit = Visit("longVisit", "person", 540);
            longVisit.endMinute = 660;
            Assert.That(() => r.RegisterClinicDay(new ClinicDayPlan { appointments = new[] { longVisit, Visit("overlap", "person", 600) } }),
                Throws.TypeOf<CommandException>());
            r.RegisterClinicDay(new ClinicDayPlan { appointments = new[] { longVisit, Visit("adjacent", "person", 660), Visit("closing", "person", 1020) } });
            Assert.That(r.ScheduleForDay(1).Select(v => (v.startMinute, v.endMinute)), Is.EqualTo(new[] { (540, 660), (660, 720), (1020, 1080) }));
        }

        [Test]
        public void SavedAppointmentsWithOldMinuteBasedSlotsRetainTheirTimesAndJournal()
        {
            KernelRuntime r = World(); KernelSnapshot snapshot = r.Capture();
            snapshot.clinicDay.appointments[0].startMinute = 570;
            snapshot.clinicDay.appointments[0].endMinute = 590;
            r.Restore(snapshot);
            Assert.That(r.ScheduleForDay(1).First().startMinute, Is.EqualTo(570));
            r.WaitForNextAppointment(); Advance(r); r.Tick(30); Leave(r);
            r.AddConsultationNote("client", "기존 예약 일지");
            Assert.That(r.AppointmentJournalText(1, "1:morning"), Does.Contain("09:30 ~ 09:50").And.Contain("기존 예약 일지"));
        }

        [Test]
        public void AppointmentJournalMatchesOnlyTheSelectedCompletedConsultation()
        {
            KernelRuntime r = ClinicScenario.Create();
            r.RegisterClinicDay(new ClinicDayPlan { day = 0, appointments = new[] { Visit("first", "client", 540), Visit("second", "client", 600) } });
            r.Tick(1); Advance(r); r.Tick(5);
            r.AddConsultationNote("client", "첫 상담 기록");
            r.Tick(5); r.FinishConsultation(); Leave(r);
            Assert.That(r.AppointmentJournalText(1, "0:first"), Does.Contain("09:00 ~ 09:10").And.Contain("첫 상담 기록"));
            Assert.That(() => r.AppointmentJournalText(1, "0:second"), Throws.TypeOf<CommandException>());
            r.WaitForNextAppointment(); Advance(r); r.Tick(4); r.FinishConsultation(); Leave(r);
            Assert.That(r.AppointmentJournalText(1, "0:second"), Does.Contain("작성된 상담 일지가 없습니다").And.Not.Contain("첫 상담 기록"));
            r.AddConsultationNote("client", "두 번째 상담 기록");
            r.WaitUntilClosing(); r.NextDay();
            r.WaitForNextAppointment(); Advance(r); r.AddConsultationNote("client", "다음 날 기록");
            KernelSnapshot saved = r.Capture(); r.Restore(saved);
            Assert.That(r.AppointmentJournalText(1, "0:first"), Does.Contain("첫 상담 기록").And.Not.Contain("두 번째 상담 기록").And.Not.Contain("다음 날 기록"));
            Assert.That(r.AppointmentJournalText(1, "0:second"), Does.Contain("두 번째 상담 기록").And.Not.Contain("첫 상담 기록"));
        }

        [Test]
        public void FreedReferenceIdsAreNotReusedAndWorkingAliasesDoNotCountAsNewFindings()
        {
            var r = new KernelRuntime(); r.Allocate("old"); r.InitializeDailyRecord(); r.Free("old");
            var restored = new KernelRuntime(); restored.Restore(r.Capture()); restored.Store("newData", Datum.Text("new"));
            Assert.That(restored.Data["newData"].id, Is.GreaterThan(1));
            restored.Tick(540); Assert.That(restored.FinalizeDay().newData, Is.Empty);
        }
    }
}
