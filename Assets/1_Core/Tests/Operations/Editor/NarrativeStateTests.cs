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
        public void ProfileCardsUseKnownInstanceFieldsAndRespectDynamicAccess()
        {
            KernelRuntime runtime = ClinicScenario.Create(); Entity person = runtime.Entity("client");
            person.AddVariable(new VariableDefinition { Code = "legalName", Kind = DataKind.Text, DefaultValue = Datum.Text("이서현") });
            person.AddVariable(new VariableDefinition { Code = "gender", Kind = DataKind.Text, Overrides = true, DefaultValue = Datum.Text("여성") });
            person.AddVariable(new VariableDefinition { Code = "age", Kind = DataKind.Number, Overrides = true, DefaultValue = Datum.Number(31) });
            person.AddVariable(new VariableDefinition { Code = "birthDate", Kind = DataKind.Text, DefaultValue = Datum.Text("1962.04.17") });
            person.AddVariable(new VariableDefinition { Code = "family", Kind = DataKind.Text, DefaultValue = Datum.Text("배우자, 자녀 1명"),
                Access = new AccessRule { Story = new StoryCondition { triggersOn = new[] { "family.known" } } } });
            runtime.Discover(person.Id, "초기 작전망", true);
            runtime.Observe(new ObservationDefinition { subjectId = person.Id, field = "family", useCurrentField = true, source = "제한된 초기 자료" });
            PersonProfile profile = runtime.ProfileCard("client");
            Assert.That(profile.Name, Is.EqualTo("이서현")); Assert.That(profile.Gender, Is.EqualTo("여성"));
            Assert.That(profile.Age, Is.EqualTo("31")); Assert.That(profile.Birthday, Is.EqualTo("1962.04.17"));
            Assert.That(profile.Occupation, Is.EqualTo("회사원")); Assert.That(profile.Family, Is.EqualTo("[접근 제한]"));
            runtime.SetTrigger("family.known", true); Assert.That(runtime.ProfileCard("client").Family, Is.EqualTo("배우자, 자녀 1명"));
            person.SetVariableAccess("family", PlayerAccess.Internal); Assert.That(runtime.ProfileCard("client").Family, Is.EqualTo("[접근 제한]"));
            profile.Name = "copy"; Assert.That(runtime.ProfileCard("client").Name, Is.EqualTo("이서현"));
            person.SetEntityAccess(PlayerAccess.Internal);
            Assert.That(() => runtime.ProfileCard("client"), Throws.TypeOf<CommandException>());
            Assert.That(() => runtime.ProfileJournalText("client", 0), Throws.TypeOf<CommandException>());
            Assert.That(() => runtime.ProfileCard("thread01"), Throws.TypeOf<CommandException>());
        }
        [Test]
        public void KernelUsesTheSameFormWithoutReplacingRecoveredIdentityInformation()
        {
            KernelRuntime runtime = ClinicScenario.Create(); PersonProfile kernel = runtime.ProfileCard("kernel");
            Assert.That(kernel.Name, Is.EqualTo("[데이터 말소됨]")); Assert.That(kernel.Gender, Is.EqualTo("[데이터 말소됨]"));
            Assert.That(kernel.Age, Is.EqualTo("[데이터 말소됨]")); Assert.That(kernel.Birthday, Is.EqualTo("[데이터 말소됨]"));
            Assert.That(kernel.Family, Is.EqualTo("[데이터 말소됨]")); Assert.That(kernel.Occupation, Is.EqualTo("심리상담가"));
            runtime.Entity("kernel").SetValue("legalName", Datum.Text("확인한 과거 이름")); ClinicScenario.EnsureKernelProfile(runtime);
            runtime.Observe(new ObservationDefinition { subjectId = "kernel", field = "legalName", useCurrentField = true, source = "복원 기록" });
            Assert.That(runtime.ProfileCard("kernel").Name, Is.EqualTo("확인한 과거 이름"));
        }
        [Test]
        public void JournalPagesStoreSessionTimesAndCanBeEditedWithoutAddingDuplicateEntries()
        {
            KernelRuntime runtime = ClinicScenario.Create();
            ConsultationNote first = runtime.AddConsultationNote("client", "첫 상담", 1, 570, 610);
            ConsultationNote second = runtime.AddConsultationNote("client", "두 번째 상담", 1, 660, 720);
            runtime.UpdateConsultationNote(first.id, "수정한 첫 상담", 1, 575, 615);
            Assert.That(runtime.NotesFor("client").Count(), Is.EqualTo(2));
            Assert.That(runtime.ProfileJournalText("client", 0), Does.Contain("09:35 ~ 10:15").And.Contain("수정한 첫 상담").And.Not.Contain("두 번째 상담"));
            Assert.That(runtime.ProfileJournalText("client", 1), Does.Contain("11:00 ~ 12:00").And.Contain("두 번째 상담"));
            KernelSnapshot snapshot = runtime.Capture(); KernelRuntime restored = ClinicScenario.Create(); restored.Restore(snapshot);
            snapshot.consultationNotes[0].startMinute = 0; second.text = "copy";
            Assert.That(restored.NotesFor("client").First().startMinute, Is.EqualTo(575));
            Assert.That(restored.NotesFor("client").Last().text, Is.EqualTo("두 번째 상담"));
            runtime.AddConsultationNote("kernel", "Kernel 자신에 대한 기록", 1, 540, 550);
            Assert.That(runtime.NotesFor("kernel").Single().text, Is.EqualTo("Kernel 자신에 대한 기록"));
            Assert.That(runtime.NotesFor("client").Count(), Is.EqualTo(2));
        }
        [Test]
        public void LegacyNotesBecomeJournalPagesAtTheirOriginalRecordedTime()
        {
            KernelRuntime runtime = ClinicScenario.Create(); runtime.Tick(15); runtime.AddConsultationNote("client", "이전 메모");
            KernelSnapshot snapshot = runtime.Capture(); snapshot.consultationNotes[0].hasSessionTime = false;
            snapshot.consultationNotes[0].sessionDay = snapshot.consultationNotes[0].startMinute = snapshot.consultationNotes[0].endMinute = 0;
            KernelRuntime restored = ClinicScenario.Create(); restored.Restore(snapshot);
            ConsultationNote note = restored.NotesFor("client").Single();
            Assert.That(note.hasSessionTime, Is.True); Assert.That(note.sessionDay, Is.EqualTo(1));
            Assert.That(note.startMinute, Is.EqualTo(555)); Assert.That(note.endMinute, Is.EqualTo(555));
            Assert.That(note.text, Is.EqualTo("이전 메모"));
            Assert.That(snapshot.consultationNotes[0].hasSessionTime, Is.False);
        }
        [TestCase(2, 600, 660)]
        [TestCase(1, 700, 600)]
        [TestCase(1, -1, 600)]
        [TestCase(1, 600, 1441)]
        public void InvalidJournalSessionTimesDoNotChangeSavedPages(int day, int start, int end)
        {
            KernelRuntime runtime = ClinicScenario.Create(); ConsultationNote note = runtime.AddConsultationNote("client", "보존할 내용", 1, 600, 660);
            Assert.That(() => runtime.AddConsultationNote("client", "새 내용", day, start, end), Throws.TypeOf<CommandException>());
            Assert.That(() => runtime.UpdateConsultationNote(note.id, "바꿀 내용", day, start, end), Throws.TypeOf<CommandException>());
            Assert.That(runtime.NotesFor("client").Single().text, Is.EqualTo("보존할 내용"));
        }
        [TestCase("09:05", 545)]
        [TestCase("9:05", 545)]
        [TestCase("24:00", 1440)]
        public void JournalTimeInputAcceptsClockTimes(string value, int expected)
        { Assert.That(KernelRuntime.ParseConsultationTime(value), Is.EqualTo(expected)); }
        [TestCase("25:00")]
        [TestCase("09:61")]
        [TestCase("-1:05")]
        [TestCase("hello")]
        public void JournalTimeInputRejectsInvalidClockTimes(string value)
        { Assert.That(() => KernelRuntime.ParseConsultationTime(value), Throws.TypeOf<CommandException>()); }
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
