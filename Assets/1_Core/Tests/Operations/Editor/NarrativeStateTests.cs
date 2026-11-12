using System.Linq;
using Codetective.Operations;
using NUnit.Framework;

namespace Codetective.Tests.Operations
{
    public sealed class NarrativeStateTests
    {
        private static KernelRuntime World()
        {
            KernelRuntime runtime = ClinicScenario.Create(); runtime.StoryStartFact = "assigned";
            runtime.RegisterMail(new StoryMail { id = "intro", mailbox = Mailbox.Kernel, subject = "정부 임무", readFacts = new[] { "contacted" },
                requiresAcceptance = true, acceptFacts = new[] { "assigned" }, grantAuthorities = new[] { "assignment01" } });
            runtime.RegisterMail(new StoryMail { id = "clinic", mailbox = Mailbox.Clinic, requiredFacts = new[] { "assigned" } });
            runtime.RegisterMail(new StoryMail { id = "followup", mailbox = Mailbox.Kernel, requiredFacts = new[] { "testimony_crosschecked" },
                requiresAcceptance = true, acceptFacts = new[] { "complete" } });
            return runtime;
        }
        [Test]
        public void GovernmentAssignmentStartsTheCaseAndMakesTheClinicInboxAvailable()
        {
            KernelRuntime r = World();
            Assert.That(r.AwaitingAssignment, Is.True); Assert.That(r.CurrentConsultation, Is.Null);
            r.Tick(30); Assert.That(r.Minute, Is.EqualTo(540)); Assert.That(r.Suspicion, Is.Zero);
            Assert.That(r.Inbox(Mailbox.Kernel).Select(m => m.id), Is.EqualTo(new[] { "intro" }));
            Assert.That(r.Inbox(Mailbox.Clinic), Is.Empty);
            Assert.That(() => r.AcceptMail("intro"), Throws.TypeOf<CommandException>());
            r.ReadMail("intro"); Assert.That(r.Facts, Does.Contain("contacted")); Assert.That(r.AwaitingAssignment, Is.True);
            r.AcceptMail("intro");
            Assert.That(r.AwaitingAssignment, Is.False); Assert.That(r.CurrentConsultation, Is.Not.Null);
            Assert.That(r.Authorities, Does.Contain("assignment01"));
            Assert.That(r.Inbox(Mailbox.Clinic).Single().id, Is.EqualTo("clinic"));
            r.Tick(1); Assert.That(r.Minute, Is.EqualTo(541));
        }
        [Test]
        public void LockedMailCannotBeReadOrAcceptedByGuessingItsId()
        {
            KernelRuntime r = World();
            Assert.That(() => r.ReadMail("followup"), Throws.TypeOf<CommandException>());
            Assert.That(() => r.AcceptMail("followup"), Throws.TypeOf<CommandException>());
            Assert.That(r.IsMailRead("followup"), Is.False);
            r.Facts.Add("testimony_crosschecked");
            Assert.That(r.Inbox(Mailbox.Kernel).Any(m => m.id == "followup"), Is.True);
            r.ReadMail("followup"); r.AcceptMail("followup"); Assert.That(r.Facts, Does.Contain("complete"));
        }
        [Test]
        public void ReadAndAcceptanceEffectsAreOneTimeAndReturnCopiesOfMailContent()
        {
            KernelRuntime r = World(); int changes = 0; r.NarrativeChanged += () => changes++;
            StoryMail copy = r.ReadMail("intro"); copy.subject = "changed";
            r.ReadMail("intro"); r.AcceptMail("intro"); r.AcceptMail("intro");
            Assert.That(changes, Is.EqualTo(2)); Assert.That(r.ReadMail("intro").subject, Is.EqualTo("정부 임무"));
            Assert.That(r.UnreadMailCount(Mailbox.Kernel), Is.Zero);
            Assert.That(r.UnreadMailCount(Mailbox.Clinic), Is.EqualTo(1));
        }
        [Test]
        public void DialogueIsNotAutomaticallyTranscribedOrAddedToPatientNotes()
        {
            KernelRuntime r = ClinicScenario.Create(); string sentence = r.CurrentConsultation.sentence;
            Assert.That(r.ProfileSummary("client"), Does.Not.Contain(sentence));
            r.Reply(); Assert.That(r.NotesFor("client"), Is.Empty);
            Assert.That(r.ProfileSummary("client"), Does.Not.Contain(sentence));
            r.RecordClue(0); Assert.That(r.ProfileSummary("client"), Does.Contain(sentence));
            Assert.That(r.NotesFor("client"), Is.Empty);
        }
        [Test]
        public void NotesAreExplicitEditableAndIndependentOfExtractedClueSlots()
        {
            KernelRuntime r = ClinicScenario.Create();
            ConsultationNote a = r.AddConsultationNote("client", "직접 적은 상담 메모");
            r.UpdateConsultationNote(a.id, "수정한 메모");
            Assert.That(r.NotesFor("client").Single().text, Is.EqualTo("수정한 메모"));
            Assert.That(r.Data, Is.Empty); Assert.That(r.RecordedClues, Is.Empty);
            Assert.That(() => r.AddConsultationNote("unknown", "memo"), Throws.TypeOf<CommandException>());
            Assert.That(() => r.AddConsultationNote("client", " "), Throws.TypeOf<CommandException>());
            Assert.That(r.RemoveConsultationNote(a.id), Is.True);
            Assert.That(r.AddConsultationNote("client", "새 메모").id, Is.GreaterThan(a.id));
        }
        [Test]
        public void MailProgressNotesAndIndividualDrawerStatesSurviveSaving()
        {
            KernelRuntime r = World(); r.ReadMail("intro"); r.AcceptMail("intro");
            r.AddConsultationNote("client", "메모"); r.SetDrawerOpen("desk.1", true); r.SetDrawerOpen("desk.2", false);
            KernelRuntime restored = World(); restored.Restore(r.Capture());
            Assert.That(restored.IsMailAccepted("intro"), Is.True); Assert.That(restored.IsMailRead("intro"), Is.True);
            Assert.That(restored.NotesFor("client").Single().text, Is.EqualTo("메모"));
            Assert.That(restored.IsDrawerOpen("desk.1"), Is.True); Assert.That(restored.IsDrawerOpen("desk.2"), Is.False);
            restored.SetDrawerOpen("desk.1", false); Assert.That(r.IsDrawerOpen("desk.1"), Is.True);
        }
        [Test]
        public void OlderDailySavesPreserveTheirExistingCaseInsteadOfRestartingTheIntroduction()
        {
            KernelRuntime old = ClinicScenario.Create(); old.RecordClue(0); old.Reply();
            KernelSnapshot saved = old.Capture(); saved.mailProgress = null; saved.consultationNotes = null; saved.drawers = null;
            KernelRuntime restored = World(); restored.Restore(saved);
            Assert.That(restored.AwaitingAssignment, Is.False); Assert.That(restored.ConsultationIndex, Is.EqualTo(1));
            Assert.That(restored.IsMailAccepted("intro"), Is.True); Assert.That(restored.Facts, Does.Contain("basement_noise"));
        }
        [Test]
        public void ReadingTheIntroductionWithoutAcceptingItRemainsPendingAfterReload()
        {
            KernelRuntime r = World(); r.ReadMail("intro");
            KernelRuntime restored = World(); restored.Restore(r.Capture());
            Assert.That(restored.AwaitingAssignment, Is.True); Assert.That(restored.IsMailRead("intro"), Is.True);
            Assert.That(restored.IsMailAccepted("intro"), Is.False);
        }
        [Test]
        public void PrototypeTestsCannotMutateRealDrawerOrMailState()
        {
            KernelRuntime r = World(); r.ReadMail("intro"); r.AcceptMail("intro"); r.SetDrawerOpen("desk.1", true);
            r.Entity("hq").Operations.Add(new Operation { Code = "simulateNarrative", ReturnKind = DataKind.Text, Invoke = c =>
            {
                c.Runtime.ReadMail("clinic"); c.Runtime.SetDrawerOpen("desk.1", false); c.Runtime.AddConsultationNote("client", "복사본 메모");
                return Datum.Text("ok");
            } });
            Assert.That(r.TestProtocol("protocol check() { return hq.simulateNarrative(); }", "check()").Success, Is.True);
            Assert.That(r.IsDrawerOpen("desk.1"), Is.True); Assert.That(r.IsMailAccepted("intro"), Is.True);
            Assert.That(r.IsMailRead("clinic"), Is.False); Assert.That(r.NotesFor("client"), Is.Empty);
        }
    }
}
