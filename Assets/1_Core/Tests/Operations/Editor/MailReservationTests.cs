using System;
using System.Linq;
using Codetective.Operations;
using NUnit.Framework;

namespace Codetective.Tests.Operations
{
    public sealed class MailReservationTests
    {
        [TestCase(0)] [TestCase(1)]
        public void NormalSerializedMailDoesNotBecomeAReservationWhenUnityCreatesAnEmptyInlineObject(int placeholderDay)
        {
            var r = new KernelRuntime();
            var message = new StoryMail { id = "legacy", requiresAcceptance = true, acceptFacts = new[] { "assigned" },
                reservation = new ReservationRequest { day = placeholderDay } };
            Assert.That(() => r.RegisterMail(message), Throws.Nothing);
            Assert.That(r.HasClinicSchedule, Is.False); Assert.That(r.PendingReservationCount, Is.Zero);
            r.ReadMail("legacy"); r.AcceptMail("legacy"); Assert.That(r.Facts, Does.Contain("assigned"));
            Assert.That(r.Inbox(Mailbox.Clinic).Single().reservation, Is.Null);
        }
        [Test]
        public void ExplicitlyMarkedMalformedInquiriesStillFailValidation()
        {
            var r = new KernelRuntime();
            Assert.That(() => r.RegisterMail(new StoryMail { id = "missingApproval", isReservationInquiry = true, reservation = new ReservationRequest() }), Throws.TypeOf<CommandException>());
            Assert.That(() => r.RegisterMail(new StoryMail { id = "missingPayload", isReservationInquiry = true }), Throws.TypeOf<CommandException>());
            Assert.That(r.PendingReservationCount, Is.Zero);
        }
        [Test]
        public void LegacyApprovalChoicesIdentifyAnInquiryWithoutTheNewFlag()
        {
            KernelRuntime r = World(); r.ReadMail("inquiry");
            Assert.That(r.Inbox(Mailbox.Clinic).Single().HasReservation, Is.True);
            r.ChooseMail("inquiry", "approve"); Assert.That(r.ScheduleForDay(2).Count, Is.EqualTo(3));
        }
        private static PatientAppointment Appointment(string id, int minute, int end = 0) => new PatientAppointment { id = id, profileId = "client", patientName = "예약자",
            startMinute = minute, endMinute = end, dialogue = new[] { new ConsultationLine { sentence = "예약하고 왔어요.", keyword = "문의", fact = id + "Fact", dataName = id + "Data" } } };
        private static StoryMail Inquiry(string id = "inquiry", int day = 2, int minute = 660, int end = 720) => new StoryMail { id = id, mailbox = Mailbox.Clinic, subject = "예약 문의",
            reservation = new ReservationRequest { day = day, appointment = Appointment(id, minute, end) }, choices = new[] {
                new MailChoice { id = "approve", label = "예약 승인", result = MailChoiceResult.Approved, approvesReservation = true,
                    facts = new[] { "approved" }, effects = new StoryEffects { triggers = new[] { new TriggerValue { id = "approved", value = true } } } },
                new MailChoice { id = "decline", label = "예약 거절", result = MailChoiceResult.Declined,
                    effects = new StoryEffects { triggers = new[] { new TriggerValue { id = "declined", value = true } } } } } };
        private static KernelRuntime World(StoryMail inquiry = null)
        {
            KernelRuntime r = ClinicScenario.Create();
            r.RegisterClinicDay(new ClinicDayPlan { day = 0, appointments = new[] { Appointment("morning", 570), Appointment("afternoon", 780) } });
            r.RegisterMail(inquiry ?? Inquiry()); r.InitializeClinicDay(); r.InitializeDailyRecord(); return r;
        }
        private static void Advance(KernelRuntime r) { var v = r.CurrentAppointment; r.AdvanceVisitPresentation(v.id, v.phase); }
        private static void FinishDay(KernelRuntime r)
        {
            for (int step = 0; !r.IsDayEnded && step < 60; step++)
                if (r.IsClinicTransitioning) Advance(r);
                else if (r.CurrentAppointment != null) r.FinishConsultation();
                else if (r.CanWaitForNextAppointment) r.WaitForNextAppointment();
                else r.WaitUntilClosing();
        }
        [Test]
        public void PendingAndDeclinedRequestsNeverBecomeConfirmedCalendarEntries()
        {
            KernelRuntime r = World(); Assert.That(r.PendingReservationCount, Is.EqualTo(1));
            Assert.That(r.ScheduleForDay(2).Count, Is.EqualTo(2)); r.ReadMail("inquiry"); r.ChooseMail("inquiry", "decline");
            Assert.That(r.ScheduleForDay(2).Count, Is.EqualTo(2)); Assert.That(r.IsMailDeclined("inquiry"), Is.True);
            Assert.That(r.PendingReservationCount, Is.Zero); Assert.That(r.GetTrigger("declined"), Is.True);
            Assert.That(r.IsMailAccepted("inquiry"), Is.False);
        }
        [Test]
        public void ReadingAndAValidChoiceAreRequiredAndTheOldAcceptanceApiCannotBypassThem()
        {
            KernelRuntime r = World(); Assert.That(() => r.ChooseMail("inquiry", "approve"), Throws.TypeOf<CommandException>());
            r.ReadMail("inquiry"); Assert.That(() => r.AcceptMail("inquiry"), Throws.TypeOf<CommandException>());
            Assert.That(() => r.ChooseMail("inquiry", "unknown"), Throws.TypeOf<CommandException>());
            Assert.That(r.SelectedMailChoice("inquiry"), Is.Null.Or.Empty); Assert.That(r.ScheduleForDay(2).Count, Is.EqualTo(2));
        }
        [Test]
        public void ApprovalAddsOnlyTheRequestedDayAndRevisitingTheReplyNeverDuplicatesEffects()
        {
            KernelRuntime r = World(); r.ReadMail("inquiry"); r.ChooseMail("inquiry", "approve");
            Assert.That(r.IsMailAccepted("inquiry"), Is.True); Assert.That(r.SelectedMailChoiceLabel("inquiry"), Is.EqualTo("예약 승인"));
            Assert.That(r.ScheduleForDay(1).Count, Is.EqualTo(2));
            Assert.That(r.ScheduleForDay(2).Select(v => v.startMinute), Is.EqualTo(new[] { 570, 660, 780 }));
            r.SetTrigger("approved", false); r.ChooseMail("inquiry", "approve");
            Assert.That(r.GetTrigger("approved"), Is.False); Assert.That(r.ScheduleForDay(2).Count, Is.EqualTo(3));
            Assert.That(() => r.ChooseMail("inquiry", "decline"), Throws.TypeOf<CommandException>());
        }
        [Test]
        public void ApprovedFuturePatientAppearsInTheRealDayFlowAtTheConfirmedTime()
        {
            KernelRuntime r = World(); r.ReadMail("inquiry"); r.ChooseMail("inquiry", "approve"); FinishDay(r); r.NextDay();
            r.WaitForNextAppointment(); Advance(r); r.FinishConsultation(); while (r.IsClinicTransitioning) Advance(r);
            r.WaitForNextAppointment(); Assert.That(r.Minute, Is.EqualTo(660)); Assert.That(r.CurrentAppointment.id, Is.EqualTo("mail:inquiry"));
            Advance(r); Assert.That(r.CurrentConsultation.sentence, Is.EqualTo("예약하고 왔어요."));
        }
        [Test]
        public void SameDayApprovalPreservesAnOngoingConversationAndMovesItsNextDeadline()
        {
            KernelRuntime r = World(Inquiry(day: 1)); r.WaitForNextAppointment(); Advance(r);
            int cursor = r.ConsultationIndex; r.Tick(10); r.ReadMail("inquiry"); r.ChooseMail("inquiry", "approve");
            Assert.That(r.CurrentAppointment.id, Is.EqualTo("0:morning")); Assert.That(r.CurrentAppointment.phase, Is.EqualTo(VisitPhase.Talking));
            Assert.That(r.ConsultationIndex, Is.EqualTo(cursor)); Assert.That(r.CurrentAppointment.endMinute, Is.EqualTo(660));
            r.Tick(400); Assert.That(r.Minute, Is.EqualTo(660)); Assert.That(r.CurrentAppointment.outcome, Is.EqualTo(VisitOutcome.NextReservation));
            while (r.IsClinicTransitioning) Advance(r); Assert.That(r.CurrentAppointment.id, Is.EqualTo("mail:inquiry"));
        }
        [Test]
        public void ExactTimeCollisionsDoNotApplyReplyStateOrStoryEffects()
        {
            KernelRuntime r = World(Inquiry(minute: 780, end: 840)); r.ReadMail("inquiry");
            Assert.That(r.MailChoiceDenial("inquiry", "approve"), Does.Contain("겹칩니다"));
            Assert.That(() => r.ChooseMail("inquiry", "approve"), Throws.TypeOf<CommandException>());
            Assert.That(r.GetTrigger("approved"), Is.False); Assert.That(r.Facts, Does.Not.Contain("approved"));
            Assert.That(r.SelectedMailChoice("inquiry"), Is.Null.Or.Empty); Assert.That(r.PendingReservationCount, Is.EqualTo(1));
            r.ChooseMail("inquiry", "decline"); Assert.That(r.IsMailDeclined("inquiry"), Is.True);
        }
        [TestCase(630, 700)] [TestCase(540, 630)] [TestCase(630, 0)]
        public void FixedDurationSlotsAreProtectedAgainstPartialOverlap(int start, int end)
        {
            var r = new KernelRuntime(); r.RegisterClinicDay(new ClinicDayPlan { day = 2, appointments = new[] { Appointment("fixed", 600, 660) } });
            r.RegisterMail(Inquiry(minute: start, end: end)); r.ReadMail("inquiry");
            Assert.That(() => r.ChooseMail("inquiry", "approve"), Throws.TypeOf<CommandException>());
            Assert.That(r.ScheduleForDay(2).Count, Is.EqualTo(1));
        }
        [Test]
        public void TwoInquiriesForTheSameTimeCannotBothBeApproved()
        {
            KernelRuntime r = World(); r.RegisterMail(Inquiry("another")); r.ReadMail("inquiry"); r.ReadMail("another");
            r.ChooseMail("inquiry", "approve"); Assert.That(() => r.ChooseMail("another", "approve"), Throws.TypeOf<CommandException>());
            Assert.That(r.ScheduleForDay(2).Count, Is.EqualTo(3)); Assert.That(r.SelectedMailChoice("another"), Is.Null.Or.Empty);
        }
        [Test]
        public void ExpiredInquiriesRemainAnswerableButCannotBeApprovedRetroactively()
        {
            KernelRuntime r = World(Inquiry(day: 1, minute: 600, end: 660)); r.ReadMail("inquiry"); r.WaitForNextAppointment(); Advance(r); r.Tick(30);
            Assert.That(r.Minute, Is.EqualTo(600)); Assert.That(r.MailChoiceDenial("inquiry", "approve"), Does.Contain("지난"));
            Assert.That(() => r.ChooseMail("inquiry", "approve"), Throws.TypeOf<CommandException>()); r.ChooseMail("inquiry", "decline");
            Assert.That(r.IsMailDeclined("inquiry"), Is.True);
        }
        [Test]
        public void ReplyAndBookingSurviveSavingWithoutResettingTheCurrentVisit()
        {
            KernelRuntime r = World(Inquiry(day: 1)); r.WaitForNextAppointment(); Advance(r); r.ReadMail("inquiry"); r.ChooseMail("inquiry", "approve");
            KernelSnapshot saved = r.Capture(); KernelRuntime restored = World(Inquiry(day: 1)); restored.Restore(saved);
            Assert.That(restored.SelectedMailChoice("inquiry"), Is.EqualTo("approve")); Assert.That(restored.CurrentAppointment.phase, Is.EqualTo(VisitPhase.Talking));
            Assert.That(restored.ScheduleForDay(1).Count, Is.EqualTo(3)); saved.confirmedReservations[0].patientName = "changed";
            Assert.That(restored.ScheduleForDay(1).Single(v => v.id == "mail:inquiry").patientName, Is.EqualTo("예약자"));
            restored.ChooseMail("inquiry", "approve"); Assert.That(restored.ScheduleForDay(1).Count, Is.EqualTo(3));
            AppointmentProgress copy = restored.ScheduleForDay(1)[0]; copy.cursor = 999;
            Assert.That(restored.CurrentAppointment.cursor, Is.Zero);
        }
        [Test]
        public void GeneralMailChoicesUseStoryConditionsAndKeepTheirReadHistory()
        {
            var r = new KernelRuntime(); r.SetTrigger("visible", true);
            r.RegisterMail(new StoryMail { id = "question", condition = new StoryCondition { triggersOn = new[] { "visible" } }, choices = new[] {
                new MailChoice { id = "reply", label = "다음 단서를 요청한다", condition = new StoryCondition { triggersOn = new[] { "ready" } },
                    effects = new StoryEffects { triggers = new[] { new TriggerValue { id = "visible", value = false } }, quests = new[] { new QuestProgress { id = "case", stage = 20 } } } } } });
            r.ReadMail("question"); Assert.That(() => r.ChooseMail("question", "reply"), Throws.TypeOf<CommandException>());
            r.SetTrigger("ready", true); r.ChooseMail("question", "reply");
            Assert.That(r.GetQuest("case").stage, Is.EqualTo(20)); Assert.That(r.Inbox(Mailbox.Clinic).Single().id, Is.EqualTo("question"));
            r.ChooseMail("question", "reply"); Assert.That(r.GetTrigger("visible"), Is.False);
        }
        [Test]
        public void SandboxApprovalCannotChangeTheRealMailboxOrCalendar()
        {
            KernelRuntime r = World(); r.ReadMail("inquiry");
            r.Entity("hq").Operations.Add(new Operation { Code = "approve", ReturnKind = DataKind.Text,
                Invoke = c => { c.Runtime.ChooseMail("inquiry", "approve"); return Datum.Text("approved"); } });
            Assert.That(r.TestProtocol("protocol test() { return hq.approve(); }", "test()").Success, Is.True);
            Assert.That(r.SelectedMailChoice("inquiry"), Is.Null.Or.Empty); Assert.That(r.ScheduleForDay(2).Count, Is.EqualTo(2));
        }
        [Test]
        public void AnExplicitAppointmentEndUsesItsOwnClosingMessageBeforeTheNextReservation()
        {
            KernelRuntime r = World(Inquiry(day: 1)); r.ReadMail("inquiry"); r.ChooseMail("inquiry", "approve");
            r.WaitForNextAppointment(); Advance(r); r.FinishConsultation(); while (r.IsClinicTransitioning) Advance(r);
            r.WaitForNextAppointment(); Advance(r); r.Tick(60);
            Assert.That(r.Minute, Is.EqualTo(720)); Assert.That(r.CurrentAppointment.outcome, Is.EqualTo(VisitOutcome.ReservationEnd));
            Assert.That(r.VisitPresentationText, Does.Contain("예약된 상담 시간"));
            while (r.IsClinicTransitioning) Advance(r); Assert.That(r.CurrentAppointment, Is.Null); Assert.That(r.NextAppointment.startMinute, Is.EqualTo(780));
        }
    }
}
