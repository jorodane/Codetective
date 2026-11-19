using System.Linq;
using Codetective.Operations;
using NUnit.Framework;

namespace Codetective.Tests.Operations
{
    public sealed class EvidenceCollectionTests
    {
        private static KernelRuntime NewsWorld()
        {
            KernelRuntime runtime = ClinicScenario.Create();
            runtime.RegisterNews(new NewsArticle { id = "nsf.support-1", headline = "재활센터 소식", body = "후원금 흐름을 확인할 수 있는 기사입니다.", lastDay = 1,
                clues = new[] { new NewspaperClue { id = "funding.path", keyword = "후원금", fact = "funding_known" } } });
            return runtime;
        }
        [Test]
        public void FreeReleasesReferencesWithoutRemovingCollectedEvidence()
        {
            KernelRuntime runtime = ClinicScenario.Create();
            runtime.CollectEvidence(Datum.Ref(DataKind.Document, "case_record", "사건 기록", "핵심 증거의 설명"), "현장 확보");
            runtime.Store("first", Datum.Ref(DataKind.Document, "case_record", "사건 기록", "핵심 증거의 설명"));
            runtime.Store("second", runtime.Data["first"].value); runtime.Free("first"); runtime.Free("second");
            EvidenceEntry entry = runtime.Evidence.Single();
            Assert.That(entry.Name, Is.EqualTo("사건 기록")); Assert.That(entry.Description, Is.EqualTo("핵심 증거의 설명")); Assert.That(entry.References, Is.Empty);
            runtime.ReferenceEvidence(entry.Id, "caseFile"); Assert.That(runtime.Execute("caseFile.description").Value.text, Is.EqualTo("핵심 증거의 설명"));
            Assert.That(runtime.Evidence.Count, Is.EqualTo(1)); Assert.That(runtime.Evidence.Single().References, Is.EqualTo(new[] { "caseFile" }));
            entry.Value.label = "changed"; entry.Description = "changed"; Assert.That(runtime.Evidence.Single().Name, Is.EqualTo("사건 기록"));
        }
        [Test]
        public void UnassignedInvestigationResultsStillEnterTheCollection()
        {
            KernelRuntime runtime = ClinicScenario.Create(); runtime.Facts.Add("basement_noise"); runtime.Tick(5);
            Assert.That(runtime.Execute("archive.inspect()").Success, Is.True); Assert.That(runtime.Data, Is.Empty);
            EvidenceEntry document = runtime.Evidence.Single(); Assert.That(document.Value.kind, Is.EqualTo(DataKind.Document));
            Assert.That(document.Minute, Is.EqualTo(545)); runtime.ReferenceEvidence(document.Id, "document");
            Assert.That(runtime.Execute("archive.analyze(document)").Success, Is.True);
            Assert.That(runtime.Evidence.Select(e => e.Value.kind), Is.EqualTo(new[] { DataKind.Document, DataKind.Analysis }));
        }
        [Test]
        public void CoffeeIsCollectedOnDeliveryAndWorkingSlotsAreNotEvidence()
        {
            KernelRuntime runtime = ClinicScenario.Create(); runtime.Allocate("empty"); runtime.Store("count", Datum.Number(3)); runtime.Store("memo", Datum.Text("temporary"));
            Assert.That(runtime.Execute("hq.requestCoffee()").Success, Is.True); Assert.That(runtime.Evidence, Is.Empty);
            runtime.CompleteCoffee("thread01"); Assert.That(runtime.Evidence.Single().Id, Is.EqualTo("coffee"));
            Assert.That(runtime.Execute("hq.requestSupplies()").Success, Is.True);
            Assert.That(runtime.Evidence.Select(e => e.Id), Is.EqualTo(new[] { "coffee", "toolkit" }));
            runtime.Store("agent", runtime.Reference(runtime.Entity("thread01"))); runtime.Execute("hq.checkComms()");
            Assert.That(runtime.Evidence.Count, Is.EqualTo(2));
        }
        [Test]
        public void EvidenceUsesLiveAccessAndCannotExposeRestrictedDescriptionsThroughNewReferences()
        {
            KernelRuntime runtime = ClinicScenario.Create(); Entity key = runtime.CreateInstance("Item", "key", "보안 열쇠", false);
            key.SetValue("description", Datum.Text("기밀 설명")); runtime.CollectEvidence(runtime.Reference(key));
            key.SetVariableAccess("description", PlayerAccess.Internal);
            Assert.That(runtime.Evidence.Single().Description, Does.Not.Contain("기밀"));
            runtime.ReferenceEvidence("key", "keyRef"); Assert.That(runtime.Data["keyRef"].value.text, Is.Empty);
            Assert.That(runtime.Execute("keyRef.description").Success, Is.False);
            key.SetEntityAccess(PlayerAccess.Internal); Assert.That(runtime.Evidence, Is.Empty);
            Assert.That(() => runtime.ReferenceEvidence("key", "newReference"), Throws.TypeOf<CommandException>());
            key.SetEntityAccess(PlayerAccess.Public); key.SetVariableAccess("description", PlayerAccess.Public); key.SetValue("description", Datum.Text("갱신된 설명"));
            Assert.That(runtime.Evidence.Single().Description, Is.EqualTo("기밀 설명"));
            runtime.Observe(new ObservationDefinition { subjectId = key.Id, field = "description", useCurrentField = true, source = "재조사" });
            Assert.That(runtime.Evidence.Single().Description, Is.EqualTo("갱신된 설명"));
        }
        [Test]
        public void NewspaperArticlesAndExplicitCluesRemainAvailableAfterTheEditionChanges()
        {
            KernelRuntime runtime = NewsWorld(); runtime.ReadNewspaper();
            Assert.That(runtime.Evidence.Single().Value.kind, Is.EqualTo(DataKind.Document)); Assert.That(runtime.Facts.Contains("funding_known"), Is.False);
            runtime.RecordNewspaperClue("nsf.support-1", "funding.path"); runtime.RecordNewspaperClue("nsf.support-1", "funding.path");
            Assert.That(runtime.Evidence.Count, Is.EqualTo(2)); Assert.That(runtime.Facts.Contains("funding_known"), Is.True);
            runtime.Tick(540); runtime.NextDay(); Assert.That(runtime.NewspaperArticles(2), Is.Empty);
            EvidenceEntry clue = runtime.Evidence.Single(e => e.Value.kind == DataKind.Clue);
            Assert.That(clue.Name, Is.EqualTo("후원금")); Assert.That(clue.Day, Is.EqualTo(1));
            runtime.ReferenceEvidence(clue.Id, "newsClue"); Assert.That(runtime.Execute("newsClue.description").Value.text, Does.Contain("후원금 흐름"));
        }
        [Test]
        public void CollectionSnapshotsDoNotShareValuesAndKeepFirstAcquisitionTimes()
        {
            KernelRuntime runtime = ClinicScenario.Create(); runtime.RecordClue(0); runtime.ReferenceEvidence("basement_noise", "basementNoise"); runtime.Tick(5); runtime.Store("copy", runtime.Data["basementNoise"].value);
            KernelSnapshot saved = runtime.Capture(); KernelRuntime restored = ClinicScenario.Create(); restored.Restore(saved);
            saved.evidence[0].value.label = "changed"; saved.evidence[0].day = 99;
            EvidenceEntry original = restored.Evidence.Single(e => e.Id == "basement_noise");
            Assert.That(original.Name, Is.EqualTo("지하실 야간 소음")); Assert.That(original.Day, Is.EqualTo(1));
            Assert.That(original.Minute, Is.EqualTo(540));
            restored.Free("copy"); restored.Free("basementNoise"); Assert.That(restored.Evidence.Count(e => e.Id == "basement_noise"), Is.EqualTo(1));
        }
        [Test]
        public void OlderSavesRecoverStoredAndRecordedCluesWithoutInventingAcquisitionTimes()
        {
            KernelRuntime runtime = NewsWorld(); runtime.RecordClue(0); runtime.Free("basementNoise"); runtime.ReadNewspaper(); runtime.RecordNewspaperClue("nsf.support-1", "funding.path");
            KernelSnapshot saved = runtime.Capture(); saved.evidence = null; saved.nextEvidenceNumber = 0;
            KernelRuntime restored = NewsWorld(); restored.Restore(saved);
            Assert.That(restored.Evidence.Count, Is.EqualTo(3)); Assert.That(restored.Evidence.All(e => !e.HasAcquiredTime), Is.True);
            Assert.That(restored.Evidence.Any(e => e.Name == "지하실 야간 소음"), Is.True); Assert.That(restored.Data, Is.Empty);
        }
        [Test]
        public void DuplicateSnapshotEntriesAreRejected()
        {
            KernelRuntime runtime = ClinicScenario.Create(); runtime.RecordClue(0); KernelSnapshot saved = runtime.Capture();
            saved.evidence = new[] { saved.evidence[0], saved.evidence[0].Copy() };
            Assert.That(() => runtime.Restore(saved), Throws.TypeOf<CommandException>());
        }
        [Test]
        public void DetachedItemsRetainTheirOpaqueIdentifiers()
        {
            KernelRuntime runtime = ClinicScenario.Create(); Entity item = runtime.CreateInstance("Item", "item-01", "휴대 도구", false);
            runtime.CollectEvidence(runtime.Reference(item), "현장에서 확보"); runtime.Store("item", runtime.Reference(item)); runtime.Free("item");
            KernelRuntime restored = ClinicScenario.Create(); restored.Restore(runtime.Capture());
            restored.ReferenceEvidence("item-01", "tool"); Assert.That(restored.Execute("tool").Value.id, Is.EqualTo("item-01"));
        }
        [Test]
        public void ACollectionReferenceCannotOverwriteOtherDataAndSuggestionsAvoidReservedRoots()
        {
            KernelRuntime runtime = ClinicScenario.Create(); Entity key = runtime.CreateInstance("Item", "key", "열쇠", false); runtime.CollectEvidence(runtime.Reference(key));
            runtime.CreateInstance("Item", "evidence1", "기존 이름"); runtime.Store("other", Datum.Number(3));
            Assert.That(runtime.SuggestEvidenceReference("key"), Is.EqualTo("evidence1_2"));
            Assert.That(() => runtime.ReferenceEvidence("key", "other"), Throws.TypeOf<CommandException>());
            Assert.That(runtime.Data["other"].value.number, Is.EqualTo(3)); runtime.Allocate("available"); runtime.ReferenceEvidence("key", "available");
            Assert.That(runtime.SuggestEvidenceReference("key"), Is.EqualTo("available"));
        }
        [Test]
        public void ThreadFindingsOnlyEnterTheCollectionWhenTheyAreReported()
        {
            KernelRuntime runtime = ClinicScenario.Create(); runtime.Facts.Add("basement_noise");
            Assert.That(runtime.SaveProtocol("protocol investigate(Place target) { doc = target.inspect(); result = target.analyze(doc); return result; }").Success, Is.True);
            Assert.That(runtime.Execute("thread01.run(investigate, archive)").Success, Is.True);
            runtime.Tick(6); Assert.That(runtime.Agent("thread01").State, Is.EqualTo(AgentState.Executing)); Assert.That(runtime.Evidence, Is.Empty);
            runtime.Tick(10);
            Assert.That(runtime.Evidence.Select(e => e.Value.kind), Is.EquivalentTo(new[] { DataKind.Document, DataKind.Analysis }));
            Assert.That(runtime.Data, Is.Empty);
        }
        [Test]
        public void ProtocolTestsCompletionAndFailedCommandsCannotGrantRealEvidence()
        {
            KernelRuntime runtime = ClinicScenario.Create(); runtime.Facts.Add("basement_noise"); runtime.Complete("archive.");
            Assert.That(runtime.Execute("archive.inspect(unknown)").Success, Is.False);
            Assert.That(runtime.TestProtocol("protocol gather() { return archive.inspect(); }", "gather()").Success, Is.True);
            Assert.That(runtime.Evidence, Is.Empty); Assert.That(runtime.Entity("archive_record"), Is.Null);
            Assert.That(runtime.Facts.Contains("archive_inspected"), Is.False);
        }
    }
}
