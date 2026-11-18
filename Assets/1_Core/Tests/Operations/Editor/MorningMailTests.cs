using System.Linq;
using Codetective.Operations;
using NUnit.Framework;

namespace Codetective.Tests.Operations
{
    public sealed class MorningMailTests
    {
        private static KernelRuntime World()
        {
            KernelRuntime runtime = ClinicScenario.Create();
            runtime.RegisterMail(new StoryMail { id = "intro", mailbox = Mailbox.Kernel, subject = "정부 임무", day = 1,
                readFacts = new[] { "contact_read" }, requiresAcceptance = true, acceptFacts = new[] { "assigned" } });
            runtime.RegisterMail(new StoryMail { id = "clinic", mailbox = Mailbox.Clinic, subject = "예약 문의", day = 1, requiredFacts = new[] { "assigned" } });
            runtime.RegisterMail(new StoryMail { id = "tomorrow", mailbox = Mailbox.Kernel, subject = "둘째 날 임무", day = 2 });
            return runtime;
        }
        [Test]
        public void MorningMailIsReleasedWithTheTitleFadeWithoutReadingOrAcceptingIt()
        {
            KernelRuntime runtime = World(); runtime.DeferDayMailUntilIntro();
            Assert.That(runtime.Inbox(Mailbox.Kernel), Is.Empty); Assert.That(runtime.UnreadMailCount(Mailbox.Kernel), Is.Zero);
            Assert.That(runtime.DeferredDayMail.Select(m => m.id), Is.EqualTo(new[] { "intro" }));
            Assert.That(() => runtime.ReadMail("intro"), Throws.TypeOf<CommandException>());
            runtime.MarkDayIntroSeen();
            Assert.That(runtime.DayMailDeferred, Is.False); Assert.That(runtime.DayIntroSeen, Is.True);
            Assert.That(runtime.Inbox(Mailbox.Kernel).Single().id, Is.EqualTo("intro")); Assert.That(runtime.UnreadMailCount(Mailbox.Kernel), Is.EqualTo(1));
            Assert.That(runtime.IsMailRead("intro"), Is.False); Assert.That(runtime.IsMailAccepted("intro"), Is.False); Assert.That(runtime.Facts.Contains("contact_read"), Is.False);
            Assert.That(() => runtime.AcceptMail("intro"), Throws.TypeOf<CommandException>());
            runtime.MarkDayIntroSeen(); runtime.DeferDayMailUntilIntro(); Assert.That(runtime.DayMailDeferred, Is.False);
        }
        [Test]
        public void PreviewReturnsCopiesAndConditionsStillControlWhichMailArrives()
        {
            KernelRuntime runtime = World(); runtime.ReadMail("intro"); runtime.DeferDayMailUntilIntro();
            Assert.That(runtime.Inbox(Mailbox.Kernel).Single().id, Is.EqualTo("intro")); Assert.That(runtime.DeferredDayMail, Is.Empty);
            runtime.Facts.Add("assigned"); StoryMail preview = runtime.DeferredDayMail.Single(); preview.subject = "changed";
            Assert.That(runtime.DeferredDayMail.Single().subject, Is.EqualTo("예약 문의"));
            Assert.That(runtime.Inbox(Mailbox.Clinic), Is.Empty); runtime.MarkDayIntroSeen();
            Assert.That(runtime.Inbox(Mailbox.Clinic).Single().id, Is.EqualTo("clinic")); Assert.That(runtime.IsMailRead("clinic"), Is.False);
            Assert.That(runtime.Inbox(Mailbox.Kernel).Any(m => m.id == "tomorrow"), Is.False);
        }
        [Test]
        public void SavingBeforeAndAfterDeliveryKeepsTheCorrectIntroAndUnreadState()
        {
            KernelRuntime runtime = World(); runtime.DeferDayMailUntilIntro(); KernelSnapshot snapshot = runtime.Capture();
            KernelRuntime restored = World(); restored.Restore(snapshot); snapshot.dayPresentation.mailDeferred = false;
            Assert.That(restored.DayMailDeferred, Is.True); Assert.That(restored.Inbox(Mailbox.Kernel), Is.Empty);
            restored.MarkDayIntroSeen(); KernelRuntime delivered = World(); delivered.Restore(restored.Capture());
            delivered.DeferDayMailUntilIntro(); Assert.That(delivered.DayIntroSeen, Is.True); Assert.That(delivered.DayMailDeferred, Is.False);
            Assert.That(delivered.UnreadMailCount(Mailbox.Kernel), Is.EqualTo(1)); Assert.That(delivered.IsMailRead("intro"), Is.False);
        }
        [Test]
        public void LegacySavesDoNotHideMailOrReplayAnAlreadyStartedDay()
        {
            KernelRuntime runtime = World(); KernelSnapshot snapshot = runtime.Capture(); snapshot.dayPresentation = null;
            KernelRuntime restored = World(); restored.Restore(snapshot); restored.DeferDayMailUntilIntro();
            Assert.That(restored.DayIntroSeen, Is.True); Assert.That(restored.DayMailDeferred, Is.False);
            Assert.That(restored.Inbox(Mailbox.Kernel).Single().id, Is.EqualTo("intro"));
        }
        [Test]
        public void WaitingForTheNextDayDoesNotHideOlderUnreadMailOrDeliverTomorrowsMailEarly()
        {
            KernelRuntime runtime = World(); runtime.DeferDayMailUntilIntro(); runtime.MarkDayIntroSeen(); runtime.Tick(540); runtime.NextDay(); runtime.DeferDayMailUntilIntro();
            Assert.That(runtime.Inbox(Mailbox.Kernel).Single().id, Is.EqualTo("intro"));
            Assert.That(runtime.DeferredDayMail.Single().id, Is.EqualTo("tomorrow")); runtime.MarkDayIntroSeen();
            Assert.That(runtime.Inbox(Mailbox.Kernel).Select(m => m.id), Is.EqualTo(new[] { "tomorrow", "intro" }));
        }
        [Test]
        public void ProtocolSandboxesKeepTheSameMailDeliveryBoundaryAsTheRealWorld()
        {
            KernelRuntime runtime = World(); runtime.DeferDayMailUntilIntro();
            runtime.Entity("hq").Operations.Add(new Operation { Code = "peekMail", ReturnKind = DataKind.Text,
                Invoke = c => Datum.Text(c.Runtime.ReadMail("intro").subject) });
            Assert.That(runtime.TestProtocol("protocol peek() { return hq.peekMail(); }", "peek()").Success, Is.False);
            Assert.That(runtime.DayMailDeferred, Is.True); Assert.That(runtime.IsMailRead("intro"), Is.False); Assert.That(runtime.Facts.Contains("contact_read"), Is.False);
        }
        [Test]
        public void NewDaySubscribersCannotSeeMorningMailBeforeItsDeliveryCue()
        {
            KernelRuntime runtime = World(); runtime.MarkDayIntroSeen(); runtime.Tick(540);
            bool sawTomorrowsMail = true;
            runtime.DayStarted += _ => sawTomorrowsMail = runtime.Inbox(Mailbox.Kernel).Any(m => m.id == "tomorrow");
            runtime.NextDay(true); Assert.That(sawTomorrowsMail, Is.False); Assert.That(runtime.DayMailDeferred, Is.True);
            runtime.MarkDayIntroSeen(); Assert.That(runtime.Inbox(Mailbox.Kernel).Any(m => m.id == "tomorrow"), Is.True);
        }
    }
}
