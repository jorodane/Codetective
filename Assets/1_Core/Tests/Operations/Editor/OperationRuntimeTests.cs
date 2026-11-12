using System;
using System.Linq;
using Codetective.Operations;
using NUnit.Framework;

namespace Codetective.Tests.Operations
{
    public sealed class OperationRuntimeTests
    {
        [Test]
        public void PermissionsUnlockFromCluesAndManualCallsCannotBypassTheSameRule()
        {
            var r = ClinicScenario.Create();
            Assert.That(r.Complete("archive.").Any(c => c.Replacement.Contains("inspect")), Is.False);
            Assert.That(r.Execute("doc = archive.inspect();").Success, Is.False);
            Assert.That(r.Data, Is.Empty);
            r.RecordClue(0);
            Assert.That(r.Complete("archive.").Any(c => c.Replacement.Contains("inspect")), Is.True);
            Assert.That(r.Execute("doc = archive.inspect();").Success, Is.True);
            Assert.That(r.Data["doc"].value.kind, Is.EqualTo(DataKind.Document));
            Assert.That(r.Execute("analysis = archive.analyze(doc);").Success, Is.True);
            Assert.That(r.Execute("statement = client.showEvidence(analysis);").Success, Is.True);
            Assert.That(r.Facts, Does.Contain("testimony_crosschecked"));
        }

        [Test]
        public void TypeErrorsAndUnknownCallsDoNotModifyDataOrInvokeActions()
        {
            var r = ClinicScenario.Create();
            r.RecordClue(0);
            r.Execute("doc = archive.inspect();");
            Assert.That(r.Execute("bad = archive.analyze(client);").Success, Is.False);
            Assert.That(r.Execute("System.IO.File.Delete(\"x\");").Success, Is.False);
            Assert.That(r.Execute("a = archive.inspect(); b = archive.inspect();").Success, Is.False);
            Assert.That(r.Data.ContainsKey("bad"), Is.False);
            Assert.That(r.Data.ContainsKey("a"), Is.False);
            Assert.That(r.Facts.Contains("record_analyzed"), Is.False);
        }

        [Test]
        public void NestedProtocolsHaveTypedParametersReturnsAndLocalScope()
        {
            var r = ClinicScenario.Create();
            r.RecordClue(0);
            Assert.That(r.SaveProtocol("protocol inner(Place target) { var doc = target.inspect(); return archive.analyze(doc); }").Success, Is.True);
            Assert.That(r.SaveProtocol("protocol outer(Place target) { result = inner(target); return result; }").Success, Is.True);
            Assert.That(r.Execute("answer = outer(archive);").Success, Is.True);
            Assert.That(r.Data["answer"].value.kind, Is.EqualTo(DataKind.Analysis));
            Assert.That(r.Data.ContainsKey("doc"), Is.False);
            Assert.That(r.Execute("outer(client);").Success, Is.False);
        }

        [Test]
        public void RecursiveProtocolsFailWithABoundedError()
        {
            var r = ClinicScenario.Create();
            r.SaveProtocol("protocol recursive() { recursive(); }");
            var result = r.Execute("recursive();");
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("깊이"));
        }

        [Test]
        public void ThreadsRunOverGameTimeWhileTheCommanderCanDoOtherWork()
        {
            var r = ClinicScenario.Create();
            r.RecordClue(0);
            Assert.That(r.Execute("thread01.run(recon, archive);").Success, Is.True);
            Assert.That(r.Agent("thread01").State, Is.EqualTo(AgentState.Executing));
            Assert.That(r.Execute("save meanwhile").Success, Is.True);
            r.Tick(4);
            Assert.That(r.Agent("thread01").State, Is.EqualTo(AgentState.Executing));
            r.Tick(1);
            Assert.That(r.Agent("thread01").State, Is.EqualTo(AgentState.Ready));
            Assert.That(r.Data["thread01_report"].value.kind, Is.EqualTo(DataKind.Document));
        }

        [Test]
        public void FailedStepsWaitAndResumeAtTheSameStatementAfterTheClueIsFound()
        {
            var r = ClinicScenario.Create();
            r.Execute("thread01.run(recon, archive);");
            r.Tick(5);
            Assert.That(r.Agent("thread01").State, Is.EqualTo(AgentState.Waiting));
            Assert.That(r.Complete("thread01.").Any(c => c.Replacement.Contains("resume")), Is.True);
            r.RecordClue(0);
            Assert.That(r.Execute("thread01.resume();").Success, Is.True);
            r.Tick(0.1);
            Assert.That(r.Agent("thread01").State, Is.EqualTo(AgentState.Ready));
            Assert.That(r.Agent("thread01").Report.kind, Is.EqualTo(DataKind.Document));
        }

        [Test]
        public void AssignedProtocolsKeepTheirSourceWhenTheLibraryIsEdited()
        {
            var r = ClinicScenario.Create();
            r.RecordClue(0);
            r.Execute("thread01.run(recon, archive);");
            r.SaveProtocol("protocol recon(Place target) { return 99; }");
            r.Tick(5);
            Assert.That(r.Agent("thread01").Report.kind, Is.EqualTo(DataKind.Document));
        }

        [Test]
        public void FreeRemovesOnlyTheBindingAndDoesNotDeleteThePersonOrCaptureOfAnActiveTask()
        {
            var r = ClinicScenario.Create();
            r.RecordClue(0);
            r.Execute("place = archive;");
            r.Execute("thread01.run(recon, place);");
            Assert.That(r.Execute("free place").Success, Is.True);
            r.Tick(5);
            Assert.That(r.Agent("thread01").Report.kind, Is.EqualTo(DataKind.Document));
            Assert.That(r.Execute("archive.inspect();").Success, Is.True);
        }

        [Test]
        public void CoffeeDataAppearsOnlyAfterTheSceneAcknowledgesDelivery()
        {
            var r = ClinicScenario.Create();
            string assigned = null;
            r.SceneActionRequested += (id, action) => { Assert.That(action, Is.EqualTo("coffee")); assigned = id; };
            Assert.That(r.Execute("hq.requestCoffee();").Success, Is.True);
            Assert.That(assigned, Is.Not.Null);
            Assert.That(r.Execute("hq.coffee;").Success, Is.False);
            Assert.That(r.Complete("hq.").Any(c => c.Replacement.Contains("requestCoffee")), Is.False);
            r.CompleteCoffee(assigned);
            Assert.That(r.Execute("cup = hq.coffee;").Success, Is.True);
            Assert.That(r.CoffeeReceived, Is.True);
            Assert.That(r.Agent(assigned).State, Is.EqualTo(AgentState.Ready));
            Assert.That(r.Complete("hq.").Any(c => c.Replacement.Contains("requestSupplies")), Is.True);
        }

        [Test]
        public void ProtocolTestsRunOnACopyAndCannotSpawnRealSceneEffects()
        {
            var r = ClinicScenario.Create();
            int sceneCalls = 0;
            r.SceneActionRequested += (id, action) => sceneCalls++;
            ExecutionResult result = r.TestProtocol("protocol coffeeTest() { hq.requestCoffee(); return hq.coffee; }", "coffeeTest();");
            Assert.That(result.Success, Is.True);
            Assert.That(result.Value.kind, Is.EqualTo(DataKind.Item));
            Assert.That(sceneCalls, Is.Zero);
            Assert.That(r.CoffeeReceived, Is.False);
            Assert.That(r.Facts.Contains("coffee_received"), Is.False);
            Assert.That(r.Protocols.ContainsKey("coffeeTest"), Is.False);
        }

        [Test]
        public void DayEndsAt18AutosavesOnceAndResumesAt09TheNextDay()
        {
            var r = ClinicScenario.Create();
            int saves = 0;
            r.DayEnded += () => saves++;
            r.Tick(1000);
            Assert.That(r.Minute, Is.EqualTo(1080));
            Assert.That(r.IsDayEnded, Is.True);
            r.Tick(10);
            Assert.That(saves, Is.EqualTo(1));
            r.NextDay();
            Assert.That(r.Day, Is.EqualTo(2));
            Assert.That(r.Minute, Is.EqualTo(540));
            Assert.That(r.IsDayEnded, Is.False);
        }

        [Test]
        public void LateRepliesIncreaseSuspicionAndCluesAreRecordedOnlyOnce()
        {
            var r = ClinicScenario.Create();
            r.Tick(10);
            Assert.That(r.Suspicion, Is.EqualTo(2));
            r.RecordClue(0); r.RecordClue(0);
            Assert.That(r.RecordedClues.Count, Is.EqualTo(1));
            Assert.That(r.Data.Count, Is.EqualTo(1));
            r.Reply();
            Assert.That(r.ResponseMinutes, Is.Zero);
        }

        [Test]
        public void SaveRestorePreservesDataKnowledgeAndAnInFlightProtocol()
        {
            var r = ClinicScenario.Create();
            r.RecordClue(0);
            r.Execute("thread01.run(recon, archive);");
            r.Tick(2);
            KernelSnapshot save = r.Capture();
            var loaded = ClinicScenario.Create();
            loaded.Restore(save);
            Assert.That(loaded.Agent("thread01").State, Is.EqualTo(AgentState.Executing));
            Assert.That(loaded.RecordedClues, Does.Contain("basement_noise"));
            loaded.Tick(3);
            Assert.That(loaded.Agent("thread01").Report.kind, Is.EqualTo(DataKind.Document));
            Assert.That(loaded.Minute, Is.EqualTo(545));
        }

        [Test]
        public void CompletionFiltersArgumentKindsAndSupportsUnicodeAliases()
        {
            var r = ClinicScenario.Create();
            Assert.That(r.Complete("thread01.run(").All(c => c.Replacement.EndsWith("recon", StringComparison.Ordinal)), Is.True);
            Assert.That(r.Execute("지휘소.통신확인();").Success, Is.True);
            Assert.That(r.Complete("지휘소.").Any(c => c.Replacement.Contains("requestCoffee")), Is.True);
        }

        [Test]
        public void NestedCallsAndReturnCallsKeepTheirDurationsOnAThreadAndAcrossSaveRestore()
        {
            var r = ClinicScenario.Create();
            r.RecordClue(0);
            r.SaveProtocol("protocol collect(Place target) { return target.inspect(); }");
            r.SaveProtocol("protocol investigate(Place target) { return archive.analyze(collect(target)); }");
            r.Execute("thread01.run(investigate, archive);");
            r.Tick(4);
            Assert.That(r.Agent("thread01").State, Is.EqualTo(AgentState.Executing));
            Assert.That(r.Facts, Does.Not.Contain("archive_inspected"));
            var loaded = ClinicScenario.Create();
            loaded.Restore(r.Capture());
            loaded.Tick(1);
            Assert.That(loaded.Facts, Does.Contain("archive_inspected"));
            Assert.That(loaded.Agent("thread01").Location, Is.EqualTo("현장 자료실"));
            loaded.Tick(7);
            Assert.That(loaded.Agent("thread01").State, Is.EqualTo(AgentState.Executing));
            loaded.Tick(1);
            Assert.That(loaded.Agent("thread01").Report.kind, Is.EqualTo(DataKind.Analysis));
            Assert.That(loaded.Agent("thread01").State, Is.EqualTo(AgentState.Ready));
        }

        [Test]
        public void ProtocolCompletionBindsSampleParametersWithoutExecutingWorldActions()
        {
            var r = ClinicScenario.Create();
            r.RecordClue(0);
            const string source = "protocol authored(Place target) {\n    return target.\n}";
            int caret = source.IndexOf("target.\n", StringComparison.Ordinal) + "target.".Length;
            Completion candidate = r.CompleteProtocol(source, caret, "archive").Single(c => c.Label.Contains("inspect"));
            Assert.That(candidate.Replacement, Does.Contain("return target.inspect()\n}"));
            Assert.That(r.Facts, Does.Not.Contain("archive_inspected"));
            Assert.That(r.Data.Count, Is.EqualTo(1));
            Assert.That(r.Protocols.ContainsKey("authored"), Is.False);
        }

        [Test]
        public void AForbiddenWorldStateAndMissingAuthorityApplyToBothSuggestionsAndExecution()
        {
            var r = new KernelRuntime();
            int calls = 0;
            var target = new Entity("safe", "현장", DataKind.Place);
            target.Operations.Add(new Operation { Code = "enter", Access = new AccessRule { Authorities = new[] { "access" }, ForbiddenFacts = new[] { "sealed" } },
                Invoke = c => { calls++; return Datum.Empty; } });
            r.Register(target);
            Assert.That(r.Complete("safe.en"), Is.Empty);
            Assert.That(r.Execute("safe.enter();").Success, Is.False);
            r.Authorities.Add("access");
            Assert.That(r.Complete("safe.en").Single().Replacement, Is.EqualTo("safe.enter()"));
            r.Facts.Add("sealed");
            Assert.That(r.Complete("safe.en"), Is.Empty);
            Assert.That(r.Execute("safe.enter();").Success, Is.False);
            Assert.That(calls, Is.Zero);
        }

        [Test]
        public void InvalidGlobalAssignmentNamesAreRejectedBeforeTheActionRuns()
        {
            var r = ClinicScenario.Create();
            r.RecordClue(0);
            Assert.That(r.Execute("hq = archive.inspect();").Success, Is.False);
            Assert.That(r.Facts, Does.Not.Contain("archive_inspected"));
        }

        [Test]
        public void RecallCancelsPendingWorkAndDisconnectedThreadsCannotReceiveAnAssignment()
        {
            var r = ClinicScenario.Create();
            r.RecordClue(0);
            r.Execute("thread01.run(recon, archive);");
            r.Tick(2);
            Assert.That(r.Execute("thread01.recall();").Success, Is.True);
            r.Tick(20);
            Assert.That(r.Agent("thread01").State, Is.EqualTo(AgentState.Ready));
            Assert.That(r.Agent("thread01").Report.kind, Is.EqualTo(DataKind.Empty));
            r.Disconnect("thread01");
            Assert.That(r.Execute("thread01.run(recon, archive);").Success, Is.False);
            Assert.That(r.Complete("thread01.").Any(c => c.Replacement.Contains("run(")), Is.False);
        }

        [Test]
        public void EmptyStatementsDoNotCrashAndSystemReportsAvoidAProtocolNameCollision()
        {
            var r = ClinicScenario.Create();
            Assert.That(r.Execute(";;;").Success, Is.False);
            r.RecordClue(0);
            r.SaveProtocol("protocol thread01_report() { return 1; }");
            r.Execute("thread01.run(recon, archive);");
            r.Tick(5);
            Assert.That(r.Agent("thread01").State, Is.EqualTo(AgentState.Ready));
            Assert.That(r.Data["thread01_report_2"].value.kind, Is.EqualTo(DataKind.Document));
        }

        [Test]
        public void ChildObjectPermissionsAndAliasesUseTheSameExecutionPath()
        {
            var r = ClinicScenario.Create();
            Assert.That(r.Complete("hq.door.").Any(c => c.Replacement == "hq.door.lock()"), Is.False);
            Assert.That(r.Execute("hq.door.lock();").Success, Is.False);
            r.Facts.Add("enemy_at_clinic");
            Assert.That(r.Complete("hq.door.").Any(c => c.Replacement == "hq.door.lock()"), Is.True);
            Assert.That(r.Execute("지휘소.문.잠근다();").Success, Is.True);
            Assert.That(r.Facts, Does.Contain("lockDoor"));
        }
    }
}
