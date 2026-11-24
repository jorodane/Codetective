using Codetective.Operations;
using NUnit.Framework;

namespace Codetective.Tests.Operations
{
    public sealed class MailArrivalTests
    {
        private static KernelRuntime World()
        {
            KernelRuntime runtime = ClinicScenario.Create();
            runtime.RegisterMail(new StoryMail { id = "intro", subject = "임무 안내", mailbox = Mailbox.Kernel });
            runtime.RegisterMail(new StoryMail { id = "inquiry", subject = "예약 문의", mailbox = Mailbox.Clinic, requiredFacts = new[] { "assigned" } });
            runtime.RegisterMail(new StoryMail { id = "followup", mailbox = Mailbox.Kernel, condition = new StoryCondition { triggersOn = new[] { "case_progress" } } });
            runtime.RegisterMail(new StoryMail { id = "tomorrow", mailbox = Mailbox.Kernel, day = 2 });
            return runtime;
        }
        [Test]
        public void ExistingUnreadMailDoesNotBeepAgainWhenLoadingASave()
        {
            KernelRuntime runtime = World(); runtime.Facts.Add("assigned");
            var tracker = new MailArrivalTracker(); tracker.Initialize(runtime); tracker.Observe(runtime);
            Assert.That(tracker.PendingCount, Is.Zero); Assert.That(tracker.TakeSignal(), Is.False);
            Assert.That(runtime.UnreadMailCount(Mailbox.Kernel), Is.EqualTo(1)); Assert.That(runtime.UnreadMailCount(Mailbox.Clinic), Is.EqualTo(1));
        }
        [Test]
        public void MorningMailAlertsOnlyAfterTheDayTitleDeliveryCue()
        {
            KernelRuntime runtime = World(); runtime.DeferDayMailUntilIntro();
            var tracker = new MailArrivalTracker(); tracker.Initialize(runtime); tracker.Observe(runtime);
            Assert.That(tracker.PendingCount, Is.Zero); Assert.That(tracker.TakeSignal(), Is.False);
            runtime.MarkDayIntroSeen(); tracker.Observe(runtime);
            Assert.That(tracker.PendingCount, Is.EqualTo(1)); Assert.That(tracker.TakeSignal(), Is.True);
            tracker.Observe(runtime); Assert.That(tracker.TakeSignal(), Is.False);
            Assert.That(runtime.IsMailRead("intro"), Is.False); Assert.That(runtime.IsMailAccepted("intro"), Is.False);
        }
        [Test]
        public void NewIdsAreDetectedEvenIfTheTotalUnreadCountStaysTheSame()
        {
            KernelRuntime runtime = World(); var tracker = new MailArrivalTracker(); tracker.Initialize(runtime);
            runtime.ReadMail("intro"); runtime.Facts.Add("assigned"); tracker.Observe(runtime);
            Assert.That(runtime.UnreadMailCount(Mailbox.Kernel) + runtime.UnreadMailCount(Mailbox.Clinic), Is.EqualTo(1));
            Assert.That(tracker.PendingCount, Is.EqualTo(1)); Assert.That(tracker.TakeSignal(), Is.True);
        }
        [Test]
        public void StoryGatesAndBothMailboxesCanArriveInOneBeepWithoutReadingThem()
        {
            KernelRuntime runtime = World(); var tracker = new MailArrivalTracker(); tracker.Initialize(runtime);
            runtime.Facts.Add("assigned"); runtime.SetTrigger("case_progress", true); tracker.Observe(runtime);
            Assert.That(tracker.PendingCount, Is.EqualTo(2)); Assert.That(tracker.TakeSignal(), Is.True); Assert.That(tracker.TakeSignal(), Is.False);
            Assert.That(runtime.IsMailRead("inquiry"), Is.False); Assert.That(runtime.IsMailRead("followup"), Is.False);
            tracker.Acknowledge(); tracker.Observe(runtime);
            Assert.That(tracker.PendingCount, Is.Zero); Assert.That(runtime.UnreadMailCount(Mailbox.Clinic), Is.EqualTo(1));
        }
        [Test]
        public void TheEnvelopeTargetsTheLatestArrivalThatIsStillUnreadAcrossMailboxes()
        {
            KernelRuntime runtime = World(); var tracker = new MailArrivalTracker(); tracker.Initialize(runtime);
            runtime.SetTrigger("case_progress", true); tracker.Observe(runtime);
            Assert.That(tracker.LatestPendingId, Is.EqualTo("followup"));
            runtime.Facts.Add("assigned"); tracker.Observe(runtime);
            Assert.That(tracker.LatestPendingId, Is.EqualTo("inquiry"));
            runtime.ReadMail("inquiry"); tracker.Observe(runtime);
            Assert.That(tracker.LatestPendingId, Is.EqualTo("followup"));
            tracker.Acknowledge("followup"); tracker.Observe(runtime);
            Assert.That(tracker.LatestPendingId, Is.Null);
            Assert.That(runtime.IsMailRead("followup"), Is.False);
        }
        [Test]
        public void AcknowledgedMailDoesNotAlertAgainWhenItsGateOrCameraChanges()
        {
            KernelRuntime runtime = World(); var tracker = new MailArrivalTracker(); tracker.Initialize(runtime);
            runtime.SetTrigger("case_progress", true); tracker.Observe(runtime); tracker.Acknowledge();
            runtime.SetTrigger("case_progress", false); tracker.Observe(runtime);
            runtime.SetTrigger("case_progress", true); tracker.Observe(runtime);
            Assert.That(tracker.PendingCount, Is.Zero); Assert.That(tracker.TakeSignal(), Is.False);
            runtime.RegisterMail(new StoryMail { id = "late", mailbox = Mailbox.Clinic }); tracker.Observe(runtime);
            Assert.That(tracker.PendingCount, Is.EqualTo(1)); Assert.That(tracker.TakeSignal(), Is.True);
        }
        [Test]
        public void PendingArrivalsWaitForPresentationButReadingThemCancelsTheAlert()
        {
            KernelRuntime runtime = World(); var tracker = new MailArrivalTracker(); tracker.Initialize(runtime);
            runtime.Facts.Add("assigned"); tracker.Observe(runtime); tracker.Observe(runtime);
            Assert.That(tracker.PendingCount, Is.EqualTo(1)); Assert.That(tracker.TakeSignal(), Is.True);
            runtime.SetTrigger("case_progress", true); tracker.Observe(runtime);
            runtime.ReadMail("inquiry"); runtime.ReadMail("followup"); tracker.Observe(runtime);
            Assert.That(tracker.PendingCount, Is.Zero); Assert.That(tracker.TakeSignal(), Is.False);
        }
        [Test]
        public void NextDayAndRestoredGamesKeepTheSameMailDeliveryBoundary()
        {
            KernelRuntime runtime = World(); runtime.MarkDayIntroSeen();
            var tracker = new MailArrivalTracker(); tracker.Initialize(runtime);
            runtime.Tick(540); runtime.NextDay(true); tracker.Observe(runtime); Assert.That(tracker.PendingCount, Is.Zero);
            runtime.MarkDayIntroSeen(); tracker.Observe(runtime); Assert.That(tracker.PendingCount, Is.EqualTo(1)); Assert.That(tracker.TakeSignal(), Is.True);
            KernelRuntime restored = World(); restored.Restore(runtime.Capture()); tracker.Initialize(restored); tracker.Observe(restored);
            Assert.That(tracker.PendingCount, Is.Zero); Assert.That(tracker.TakeSignal(), Is.False); Assert.That(restored.IsMailRead("tomorrow"), Is.False);
        }
    }
}
