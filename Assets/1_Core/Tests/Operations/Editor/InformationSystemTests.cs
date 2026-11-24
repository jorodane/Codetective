using System;
using System.Linq;
using Codetective.Operations;
using NUnit.Framework;

namespace Codetective.Tests.Operations
{
    public sealed class InformationSystemTests
    {
        private static KernelRuntime World()
        {
            KernelRuntime r = ClinicScenario.Create();
            r.Entity("hq").Operations.Add(new Operation { Code = "waitReport", Minutes = 4, Invoke = c => Datum.Empty });
            Entity victor = r.CreateInstance("Person", "VictorHale", "Victor Hale", false);
            victor.AddVariable(new VariableDefinition { Code = "attended", Kind = DataKind.Boolean, DefaultValue = Datum.Boolean(false) });
            victor.SetValue("attended", Datum.Boolean(true));
            r.CreateInstance("Keyword", "Attendance", "행사 참석", false);
            r.CreateInstance("Organization", "NSF", "Nous Sphaira Foundation", false);
            foreach (string id in new[] { "Maggie", "Rachel", "Evelyn" }) r.CreateInstance("Person", id, id);
            r.Entity("Maggie").AddFunction(new OperationDefinition { Code = "talk", Overrides = true, DispatchToThread = true, Minutes = 2,
                Discoveries = new[] { "VictorHale", "Attendance", "NSF" }, Reports = new[] { Claim("Maggie", true, "행사장에서 Victor와 직접 대화했어요.") } });
            foreach (string id in new[] { "Rachel", "Evelyn" })
                r.Entity(id).AddFunction(new OperationDefinition { Code = "ask", Overrides = true, DispatchToThread = true, Minutes = 3,
                    Parameters = new[] { new OperationInput { Name = "topic", Kind = DataKind.Keyword } },
                    Responses = new[] { new ActionResponseDefinition { argumentIds = new[] { "Attendance" }, reports = new[] {
                        id == "Rachel" ? Claim(id, true, "직접 명찰을 전달했어요.") : new ObservationDefinition { subjectId = "NSF", keywordId = "Attendance", source = id,
                            statement = "최종 공동선언까지 참석한 대표만 공식 참석자로 기록합니다.", value = Datum.Text("공식 집계 규칙") } } } } });
            return r;
        }
        private static ObservationDefinition Claim(string source, bool value, string statement) => new ObservationDefinition {
            subjectId = "VictorHale", keywordId = "Attendance", field = "attended", value = Datum.Boolean(value), sourceId = source, source = source, statement = statement };

        [Test]
        public void WorldExistsBeforeDiscoveryAndGuessingAnIdCannotReadOrAliasIt()
        {
            KernelRuntime r = World();
            Assert.That(r.Entity("VictorHale").GetValue("attended").number, Is.EqualTo(1));
            Assert.That(r.Execute("피해자 = VictorHale").Success, Is.False);
            Assert.That(r.Complete("Victor"), Is.Empty); Assert.That(r.Data, Is.Empty);
            Assert.That(r.IsDiscovered("VictorHale"), Is.False);
        }
        [Test]
        public void Case01KeepsConflictingClaimsAndTruthWithoutAutomaticallyCreatingAliases()
        {
            KernelRuntime r = World(); Assert.That(r.Execute("Maggie.대화하기()").Success, Is.True);
            Assert.That(r.IsDiscovered("VictorHale"), Is.False); r.Tick(2);
            Assert.That(r.IsDiscovered("VictorHale"), Is.True); Assert.That(r.Data, Is.Empty);
            Assert.That(r.Execute("피해자 = VictorHale").Success, Is.True);
            Assert.That(r.Execute("참석여부 = Attendance").Success, Is.True);
            r.Observe(Claim("공식 행사 기록", false, "Victor Hale / 개인 사정 불참"));
            Assert.That(r.Execute("피해자.attended").Value.number, Is.Zero);
            Assert.That(r.Entity("VictorHale").GetValue("attended").number, Is.EqualTo(1));
            Assert.That(r.Execute("Rachel.질문하기(참석여부)").Success, Is.True); r.Tick(3);
            Assert.That(r.Execute("Evelyn.질문하기(참석여부)").Success, Is.True); r.Tick(3);
            ObservationRecord[] claims = r.Observations.Where(o => o.subjectId == "VictorHale" && o.field == "attended").ToArray();
            Assert.That(claims.Select(c => c.source), Is.EqualTo(new[] { "Maggie", "공식 행사 기록", "Rachel" }));
            Assert.That(claims.Select(c => c.number).Distinct().Count(), Is.EqualTo(3));
            Assert.That(r.Observations.Any(o => o.source == "Evelyn" && o.statement.Contains("공식 참석자")), Is.True);
            Assert.That(r.Entity("VictorHale").GetValue("attended").number, Is.EqualTo(1));
        }
        [Test]
        public void AliasesStayLiveWhileKnowledgeStaysAtTheLastObservation()
        {
            KernelRuntime r = World(); r.Discover("VictorHale");
            r.Observe(Claim("증인", false, "불참했다고 들었습니다.")); r.Execute("인물 피해자 = VictorHale");
            r.Entity("VictorHale").SetValue("attended", Datum.Boolean(true));
            Assert.That(r.Execute("피해자.attended").Value.number, Is.Zero);
            r.Observe(new ObservationDefinition { subjectId = "VictorHale", field = "attended", useCurrentField = true, source = "Thread 확인" });
            Assert.That(r.Execute("피해자.attended").Value.number, Is.EqualTo(1));
            Assert.That(r.Execute("피해자 = Rachel").Success, Is.True); Assert.That(r.Data["피해자"].value.id, Is.EqualTo("Rachel"));
            Assert.That(r.Execute("피해자 = NSF").Success, Is.False);
            r.Execute("삭제 피해자"); Assert.That(r.Entity("VictorHale"), Is.Not.Null); Assert.That(r.IsDiscovered("VictorHale"), Is.True);
        }
        [Test]
        public void NewerObservationWinsEvenIfAnOlderReportArrivesLater()
        {
            KernelRuntime r = World(); r.Tick(10); r.Discover("VictorHale");
            r.Observe(Claim("신규 조사", true, "참석 확인"), 1, 550);
            r.Observe(Claim("늦게 수신한 기록", false, "불참 표기"), 1, 545);
            Assert.That(r.LastKnownValue("VictorHale", "attended").number, Is.EqualTo(1));
            Assert.That(r.Observations.Last().receivedMinute, Is.EqualTo(550));
        }
        [Test]
        public void WorldHistoryIsIndependentOfTestimonyAndRetainsPreviousValues()
        {
            KernelRuntime r = World(); int before = r.WorldHistory.Count;
            r.Entity("VictorHale").SetValue("attended", Datum.Boolean(false));
            WorldEventRecord change = r.WorldHistory.Last(); Assert.That(change.before.number, Is.EqualTo(1)); Assert.That(change.after.number, Is.Zero);
            r.Observe(Claim("거짓 진술", true, "참석했습니다."));
            Assert.That(r.WorldHistory.Count, Is.EqualTo(before + 1)); Assert.That(r.Entity("VictorHale").GetValue("attended").number, Is.Zero);
        }
        [Test]
        public void ThreadObservationsAndDiscoveriesSurviveSavingBeforeDelivery()
        {
            KernelRuntime r = World(); r.SaveProtocol("protocol investigate() { Maggie.talk(); hq.waitReport(); }");
            r.Execute("thread01.run(investigate)"); r.Tick(2); KernelSnapshot saved = r.Capture();
            Assert.That(saved.agents.Single(a => a.id == "thread01").observations.Any(o => o.source == "Maggie"), Is.True);
            Assert.That(r.IsDiscovered("VictorHale"), Is.False);
            KernelRuntime restored = World(); restored.Restore(saved); restored.Tick(4);
            Assert.That(restored.IsDiscovered("VictorHale"), Is.True); Assert.That(restored.Data, Is.Empty);
            Assert.That(restored.Observations.Any(o => o.source == "Maggie"), Is.True);
            Assert.That(saved.discoveries.Any(d => d.entityId == "VictorHale"), Is.False);
            ObservationRecord claim = restored.Observations.Single(o => o.source == "Maggie" && o.field == "attended");
            Assert.That(claim.observedMinute, Is.EqualTo(542)); Assert.That(claim.receivedMinute, Is.EqualTo(546));
        }
        [Test]
        public void ProtocolGlobalsAreReadAtExecutionAndParametersCaptureTheirAssignedTarget()
        {
            KernelRuntime r = World(); r.Discover("VictorHale"); r.Execute("target = VictorHale");
            r.SaveProtocol("protocol who() { return target; }"); r.Execute("target = Rachel");
            Assert.That(r.Execute("who()").Value.id, Is.EqualTo("Rachel"));
            r.SaveProtocol("protocol frozen(Person person) { return person; }"); r.Execute("thread01.run(frozen, target)"); r.Execute("target = Maggie"); r.Tick(1);
            Assert.That(r.Agent("thread01").Report.id, Is.EqualTo("Rachel"));
        }
        [Test]
        public void StandardTypesExposeTheDesignedInheritanceAndOverloads()
        {
            KernelRuntime r = World(); r.Schemas.Validate();
            Assert.That(r.Schemas.Require("Tool").IsA(r.Schemas.Require("Physical")), Is.True);
            Assert.That(r.Schemas.Require("Organization").IsA(r.Schemas.Require("Concept")), Is.True);
            Assert.That(r.Schemas.Require("Place").EffectiveOperations.Count(o => o.Code == "enter"), Is.EqualTo(3));
            Assert.That(r.Schemas.Require("Tool").EffectiveOperations.Count(o => o.Code == "use"), Is.EqualTo(3));
            Assert.That(r.Schemas.Require("Person").EffectiveOperations.Count(o => o.Code == "alibi"), Is.EqualTo(2));
        }
        [Test]
        public void OverloadSelectionUsesActualTypesAndEachOverloadKeepsItsOwnPermissions()
        {
            KernelRuntime r = World(); Entity tool = r.CreateInstance("Tool", "scanner", "스캐너");
            tool.AddFunction(new OperationDefinition { Code = "use", Overrides = true, Parameters = new[] { new OperationInput { Name = "targets", Kind = DataKind.Array, SchemaId = "Physical[]" } },
                Reports = new[] { new ObservationDefinition { statement = "여러 대상을 확인했습니다." } }, DispatchToThread = true });
            Assert.That(r.Execute("scanner.use([Maggie, Rachel])").Success, Is.True); r.Tick(1);
            Assert.That(r.Observations.Any(o => o.statement == "여러 대상을 확인했습니다."), Is.True);
            Assert.That(r.Execute("scanner.use([1])").Success, Is.False);
        }
        [Test]
        public void NestedCollectionsValidateTypesAndDoNotShareSnapshotValues()
        {
            KernelRuntime r = World(); r.Discover("VictorHale");
            Assert.That(r.Execute("Dict<Physical,Relationship[]> relations = {VictorHale: []}").Success, Is.True);
            Assert.That(r.Execute("Person[] people = [Maggie, Rachel]").Success, Is.True);
            Assert.That(r.Execute("people = [NSF]").Success, Is.False);
            KernelSnapshot snapshot = r.Capture(); KernelRuntime copy = World(); copy.Restore(snapshot);
            snapshot.data.Single(d => d.name == "people").value.collection.nodes[1].id = "NSF";
            Assert.That(copy.Data["people"].value.Elements[0].id, Is.EqualTo("Maggie"));
            Assert.That(copy.Data["relations"].value.Elements[1].kind, Is.EqualTo(DataKind.Array));
        }
        [Test]
        public void NewAndMallocRemainForbiddenAndDuplicateDictionaryKeysFail()
        {
            KernelRuntime r = World(); Assert.That(r.Execute("x = new Person()").Success, Is.False);
            Assert.That(r.Execute("malloc(4)").Success, Is.False); Assert.That(r.Execute("x = {Maggie: 1, Maggie: 2}").Success, Is.False);
            Assert.That(r.Data, Is.Empty);
        }
        [Test]
        public void ReferenceRegistrationCannotInspectTruthAndChainedReadsUseTheObservedTarget()
        {
            KernelRuntime r = World(); Entity victor = r.Entity("VictorHale");
            Entity first = r.CreateInstance("Place", "first", "첫 장소"), second = r.CreateInstance("Place", "second", "다음 장소");
            victor.AddVariable(new VariableDefinition { Code = "location", Kind = DataKind.Place, SchemaId = "Place" }); victor.SetValue("location", r.Reference(first));
            r.Discover(victor.Id); r.Execute("person = VictorHale");
            Assert.That(r.Execute("person.attended").Success, Is.False);
            r.Observe(new ObservationDefinition { subjectId = victor.Id, field = "location", useCurrentField = true });
            victor.SetValue("location", r.Reference(second));
            Assert.That(r.Execute("person.location.id").Value.text, Is.EqualTo("first"));
            Assert.That(victor.GetValue("location").id, Is.EqualTo("second"));
        }
        [Test]
        public void ToolArrayEffectsValidateEveryTargetBeforeMutatingAnyWorldObject()
        {
            KernelRuntime r = World(); Entity first = r.CreateInstance("Physical", "first", "첫 문"), second = r.CreateInstance("Physical", "second", "다음 문");
            foreach (Entity door in new[] { first, second }) door.AddVariable(new VariableDefinition { Code = "locked", Kind = DataKind.Boolean, DefaultValue = Datum.Boolean(true) });
            Entity tool = r.CreateInstance("Tool", "key", "열쇠"); tool.AddFunction(new OperationDefinition { Code = "use", Overrides = true, DispatchToThread = true, Minutes = 1,
                Parameters = new[] { new OperationInput { Name = "doors", Kind = DataKind.Array, SchemaId = "Physical[]" } },
                FieldWrites = new[] { new OperationFieldWrite { TargetArgumentIndex = 0, Field = "locked", Value = Datum.Boolean(false) } } });
            Assert.That(r.Execute("key.use([first, second])").Success, Is.True); r.Tick(1);
            Assert.That(first.GetValue("locked").number, Is.Zero); Assert.That(second.GetValue("locked").number, Is.Zero);
            first.SetValue("locked", Datum.Boolean(true)); second.RemoveVariable("locked");
            r.Execute("key.use([first, second])"); r.Tick(1);
            Assert.That(r.Agent("thread01").State, Is.EqualTo(AgentState.Waiting)); Assert.That(first.GetValue("locked").number, Is.EqualTo(1));
        }
        [Test]
        public void KnowledgeUnlocksAreUsableByTheInvestigatingThreadButWaitForItsReportToKernel()
        {
            KernelRuntime r = World(); r.Entity("Maggie").AddFunction(new OperationDefinition { Code = "talk", Overrides = true, DispatchToThread = true, Minutes = 2,
                UnlockFacts = new[] { "heard" }, GrantAuthorities = new[] { "followup" }, Reports = new[] { Claim("Maggie", true, "참석 확인") } }, true);
            r.Entity("hq").Operations.Single(o => o.Code == "waitReport").Access.Facts = new[] { "heard" };
            r.SaveProtocol("protocol investigate() { Maggie.talk(); hq.waitReport(); }"); r.Execute("thread01.run(investigate)"); r.Tick(2);
            Assert.That(r.Facts.Contains("heard"), Is.False); Assert.That(r.Authorities.Contains("followup"), Is.False);
            Assert.That(r.Agent("thread01").State, Is.EqualTo(AgentState.Executing));
            r.Tick(4); Assert.That(r.Facts.Contains("heard"), Is.True); Assert.That(r.Authorities.Contains("followup"), Is.True);
        }
        [Test]
        public void AnAlreadyTransmittedProtocolKeepsItsCipherTableAfterAliasesAreRebound()
        {
            KernelRuntime r = World(); r.Execute("target = Maggie");
            r.SaveProtocol("protocol identify() { hq.waitReport(); return target; }"); r.Execute("thread01.run(identify)");
            r.Execute("target = Rachel"); r.Tick(4);
            Assert.That(r.Agent("thread01").Report.id, Is.EqualTo("Maggie")); Assert.That(r.Data["target"].value.id, Is.EqualTo("Rachel"));
        }
        [Test]
        public void LegacyFunctionWritesAndNewlyInheritedProfileFieldsMigrateWithoutRetargetingAnArgument()
        {
            KernelRuntime r = World(); Entity person = r.Entity("Maggie"); person.AddVariable(new VariableDefinition { Code = "note", Kind = DataKind.Text });
            var old = new OperationDefinition { FormatVersion = 0, Code = "set", Parameters = new[] { new OperationInput { Name = "text", Kind = DataKind.Text } },
                Reports = null, Discoveries = null, Incidents = null, Responses = null,
                FieldWrites = new[] { new OperationFieldWrite { Field = "note", ArgumentIndex = 0, TargetArgumentIndex = 0 } } };
            person.AddFunction(old); Assert.That(r.Execute("Maggie.set(\"확인\")").Success, Is.True); Assert.That(person.GetValue("note").text, Is.EqualTo("확인"));
            KernelSnapshot snapshot = r.Capture(); snapshot.discoveries = null;
            EntitySnapshot saved = snapshot.entities.Single(e => e.id == "Maggie");
            saved.localVariables = saved.localVariables.Concat(new[] { new VariableDefinition { Code = "age", Kind = DataKind.Text, DefaultValue = Datum.Text("[데이터 말소됨]") } }).ToArray();
            KernelRuntime restored = World(); restored.Restore(snapshot);
            Assert.That(restored.Entity("Maggie").GetVariableDefinition("age").Kind, Is.EqualTo(DataKind.Number));
            Assert.That(restored.Entity("Maggie").GetValue("age").kind, Is.EqualTo(DataKind.Empty));
        }
        [Test]
        public void AutonomousWorldChangesRespectTimeWithoutUpdatingKernelAndDoNotReplayAfterReload()
        {
            KernelRuntime r = World(); r.Discover("VictorHale", "첫 조사", true);
            var entry = new WorldScheduleEntry { id = "npc_change", day = 1, minute = 545, description = "NPC 독립 행동",
                changes = new[] { new WorldMutation { subjectId = "VictorHale", field = "attended", value = Datum.Boolean(false) } } };
            r.RegisterWorldEvent(entry); r.Tick(10);
            Assert.That(r.Entity("VictorHale").GetValue("attended").number, Is.Zero);
            Assert.That(r.LastKnownValue("VictorHale", "attended").number, Is.EqualTo(1));
            Assert.That(r.WorldHistory.Last(e => e.field == "attended").minute, Is.EqualTo(545));
            KernelRuntime loaded = World(); loaded.RegisterWorldEvent(entry); loaded.Restore(r.Capture());
            int count = loaded.WorldHistory.Count; loaded.Tick(1); Assert.That(loaded.WorldHistory.Count, Is.EqualTo(count));
        }
        [Test]
        public void ScheduledWorldEffectsArePrivateToProtocolSandboxes()
        {
            KernelRuntime r = World(); r.RegisterWorldEvent(new WorldScheduleEntry { id = "change", minute = 541,
                changes = new[] { new WorldMutation { subjectId = "VictorHale", field = "attended", value = Datum.Boolean(false) } } });
            r.Entity("hq").Operations.Add(new Operation { Code = "advance", Invoke = c => { c.Runtime.Tick(2); return Datum.Empty; } });
            Assert.That(r.TestProtocol("protocol advance() { hq.advance(); }", "advance()").Success, Is.True);
            Assert.That(r.Minute, Is.EqualTo(540)); Assert.That(r.Entity("VictorHale").GetValue("attended").number, Is.EqualTo(1));
            r.Tick(2); Assert.That(r.Entity("VictorHale").GetValue("attended").number, Is.Zero);
        }
        [Test]
        public void OverloadsPreferNarrowerTypesAndRejectIncomparableSignatures()
        {
            KernelRuntime r = World(); Entity hq = r.Entity("hq");
            foreach (Parameter parameter in new[] { new Parameter("target", DataKind.Any, "Physical"), new Parameter("target", DataKind.Person) })
                hq.Operations.Add(new Operation { Code = "identify", Parameters = new[] { parameter }, Invoke = c => Datum.Text(parameter.TypeName(CommandLocale.English)) });
            Assert.That(r.Execute("hq.identify(Maggie)").Value.text, Is.EqualTo("Person"));
            foreach (string type in new[] { "Physical[]", "Person[]" })
                hq.Operations.Add(new Operation { Code = "group", Parameters = new[] { DataTypes.Parameter("targets", type) }, Invoke = c => Datum.Text(type) });
            Assert.That(r.Execute("hq.group([Maggie])").Value.text, Is.EqualTo("Person[]"));
            hq.Operations.Add(new Operation { Code = "compare", Parameters = new[] { DataTypes.Parameter("a", "Person"), DataTypes.Parameter("b", "Entity") }, Invoke = c => Datum.Text("first") });
            hq.Operations.Add(new Operation { Code = "compare", Parameters = new[] { DataTypes.Parameter("a", "Physical"), DataTypes.Parameter("b", "Physical") }, Invoke = c => Datum.Text("second") });
            Assert.That(r.Execute("hq.compare(Maggie, Rachel)").Success, Is.False);
        }
        [Test]
        public void TransmittedProtocolsCannotSeeAliasesRegisteredAfterTransmission()
        {
            KernelRuntime r = World(); r.SaveProtocol("protocol delayed() { hq.waitReport(); return later; }");
            r.Execute("thread01.run(delayed)"); r.Execute("later = Maggie"); r.Tick(4);
            Assert.That(r.Agent("thread01").State, Is.EqualTo(AgentState.Waiting));
            Assert.That(r.Agent("thread01").Report.kind, Is.EqualTo(DataKind.Empty));
        }
        [Test]
        public void NestedThreadProtocolArgumentsReadCurrentWorldInsteadOfKernelsStaleReport()
        {
            KernelRuntime r = World(); r.Entity("Maggie").SetValue("occupation", Datum.Text("현재 직업"));
            Assert.That(r.LastKnownValue("Maggie", "occupation").kind, Is.EqualTo(DataKind.Empty));
            r.SaveProtocol("protocol relay(Text value) { return value; }");
            r.SaveProtocol("protocol nested() { result = relay(Maggie.occupation); return result; }");
            r.Execute("thread01.run(nested)"); r.Tick(1);
            Assert.That(r.Agent("thread01").Report.text, Is.EqualTo("현재 직업"));
        }
        [Test]
        public void ScheduledWorldChangesRevalidateAllTargetsBeforeApplyingAnyMutation()
        {
            KernelRuntime r = World(); Entity victor = r.Entity("VictorHale");
            victor.AddVariable(new VariableDefinition { Code = "temporary", Kind = DataKind.Text });
            r.RegisterWorldEvent(new WorldScheduleEntry { id = "change", minute = 541, changes = new[] {
                new WorldMutation { subjectId = victor.Id, field = "attended", value = Datum.Boolean(false) },
                new WorldMutation { subjectId = victor.Id, field = "temporary", value = Datum.Text("변경") } } });
            victor.RemoveVariable("temporary"); Assert.Throws<CommandException>(() => r.Tick(1));
            Assert.That(victor.GetValue("attended").number, Is.EqualTo(1));
        }
        [Test]
        public void StoredCollectionsRespectAccessChangesForEveryNestedReference()
        {
            KernelRuntime r = World(); Assert.That(r.Execute("group = {Maggie: [Rachel]}").Success, Is.True);
            r.Entity("Rachel").SetEntityAccess(PlayerAccess.Internal);
            Assert.That(r.Execute("group").Success, Is.False);
            r.Entity("Rachel").SetEntityAccess(PlayerAccess.Public);
            Assert.That(r.Execute("group").Success, Is.True);
        }
        [Test]
        public void DeliveringAReturnedObjectNeverRefreshesItFromUnreportedWorldState()
        {
            KernelRuntime r = World(); Entity victor = r.Entity("VictorHale"); r.Discover(victor.Id, "초기 작전망", true);
            r.SaveProtocol("protocol inspect() { result = VictorHale.look(); hq.waitReport(); return result; }");
            r.Execute("thread01.run(inspect)"); r.Tick(1); victor.SetValue("attended", Datum.Boolean(false)); r.Tick(4);
            Assert.That(r.Agent("thread01").Report.id, Is.EqualTo(victor.Id));
            Assert.That(r.LastKnownValue(victor.Id, "attended").number, Is.EqualTo(1));
            Assert.That(victor.GetValue("attended").number, Is.Zero);
        }
        [Test]
        public void TargetResponsesCanDistinguishActualCollectionContents()
        {
            KernelRuntime r = World(); Entity person = r.Entity("Maggie");
            person.SetValue("inventory", Datum.List(r.Reference(r.CreateInstance("Item", "key", "열쇠"))));
            person.AddFunction(new OperationDefinition { Code = "check", ReturnKind = DataKind.Text, Responses = new[] {
                new ActionResponseDefinition { world = new[] { new WorldRequirement { field = "inventory", expected = Datum.List() } }, result = Datum.Text("비어 있음") },
                new ActionResponseDefinition { result = Datum.Text("소지품 있음") } } });
            Assert.That(r.Execute("Maggie.check()").Value.text, Is.EqualTo("소지품 있음"));
            person.SetValue("inventory", Datum.List()); Assert.That(r.Execute("Maggie.check()").Value.text, Is.EqualTo("비어 있음"));
        }
    }
}
