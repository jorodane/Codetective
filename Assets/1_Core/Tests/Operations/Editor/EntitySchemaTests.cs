using System;
using System.Linq;
using Codetective.Operations;
using NUnit.Framework;

namespace Codetective.Tests.Operations
{
    public sealed class EntitySchemaTests
    {
        private static KernelRuntime World()
        {
            var runtime = new KernelRuntime();
            var room = new EntitySchema("Room", "방", DataKind.Place, runtime.Schemas.Require("Place"))
            { Names = new[] { new LocalizedCommandName(CommandLocale.Korean, "방형") } };
            room.Variables.Add(new VariableDefinition { Code = "note", Kind = DataKind.Text, Writable = true,
                DefaultValue = Datum.Text("base"), Names = new[] { new LocalizedCommandName(CommandLocale.Korean, "메모") } });
            room.Variables.Add(new VariableDefinition { Code = "peer", Kind = DataKind.Entity, SchemaId = "Room", Writable = true });
            room.Operations.Add(new Operation { Code = "read", ReturnKind = DataKind.Text, Invoke = c => c.Target.GetValue("note") });
            var archive = new EntitySchema("Study", "서재", DataKind.Place, room);
            runtime.Schemas.Register(archive);
            runtime.CreateInstance("Study", "study", "서재 1");
            runtime.CreateInstance("Room", "room", "방 2");
            return runtime;
        }

        [Test]
        public void InheritanceSharesDefinitionsWhileEachInstanceOwnsItsValues()
        {
            KernelRuntime r = World();
            Assert.That(r.Entities["study"].Schema.IsA(r.Schemas.Require("Entity")), Is.True);
            Assert.That(r.Execute("study.id").Value.text, Is.EqualTo("study"));
            Assert.That(r.Execute("study.read()").Value.text, Is.EqualTo("base"));
            Assert.That(r.Execute("study.note = \"changed\"").Success, Is.True);
            Assert.That(r.Execute("room.note").Value.text, Is.EqualTo("base"));
            EntitySchema parent = r.Schemas.Require("Room");
            parent.FindVariable("note").DefaultValue = Datum.Text("new default");
            parent.Variables.Add(new VariableDefinition { Code = "count", Kind = DataKind.Number, DefaultValue = Datum.Number(4) });
            parent.Operations.Add(new Operation { Code = "countNow", ReturnKind = DataKind.Number, Invoke = c => c.Target.GetValue("count") });
            r.NotifySchemaChanged();
            Assert.That(r.Execute("study.read()").Value.text, Is.EqualTo("changed"));
            Assert.That(r.Execute("room.read()").Value.text, Is.EqualTo("new default"));
            Assert.That(r.Execute("study.countNow()").Value.number, Is.EqualTo(4));
            Assert.That(r.Complete("study.c").Select(c => c.Replacement), Does.Contain("study.countNow()"));
        }

        [Test]
        public void AssigningAndFreeingReferencesPreservesIdentityAndTheWorldObject()
        {
            KernelRuntime r = World();
            int count = r.Entities.Count;
            Assert.That(r.Execute("a = study").Success, Is.True);
            Assert.That(r.Execute("b = a").Success, Is.True);
            Assert.That(r.Execute("a.note = \"shared\"").Success, Is.True);
            Assert.That(r.Execute("b.note").Value.text, Is.EqualTo("shared"));
            Assert.That(r.Data["a"].value.schemaId, Is.EqualTo("Study"));
            Assert.That(r.Execute("free a").Success, Is.True);
            Assert.That(r.Execute("study.note").Value.text, Is.EqualTo("shared"));
            Assert.That(r.Entities.Count, Is.EqualTo(count));
        }

        [TestCase("a = new Room()")]
        [TestCase("a = malloc(4)")]
        [TestCase("a = 생성()")]
        [TestCase("new = study")]
        public void PlayerCommandsCannotConstructInstances(string command)
        {
            KernelRuntime r = World();
            int count = r.Entities.Count;
            Assert.That(r.Execute(command).Success, Is.False);
            Assert.That(r.SaveProtocol("protocol forbidden() { " + command + "; }").Success, Is.False);
            Assert.That(r.Entities.Count, Is.EqualTo(count));
            Assert.That(r.Data, Is.Empty);
        }

        [Test]
        public void NamedProtocolTypesAcceptDescendantsAndFilterCompletionArguments()
        {
            KernelRuntime r = World();
            r.CreateInstance("Item", "item", "물건");
            Assert.That(r.SaveProtocol("protocol title(방형 target) { return target.name; }").Success, Is.True);
            Assert.That(r.Execute("title(study)").Value.text, Is.EqualTo("서재 1"));
            Assert.That(r.Execute("title(item)").Success, Is.False);
            Assert.That(r.Execute("title(3)").Success, Is.False);
            Assert.That(r.Complete("title(").Select(c => c.Replacement), Is.EquivalentTo(new[] { "title(study", "title(room" }));
            Assert.That(r.SaveProtocol("protocol bad(Unknown target) { return target; }").Success, Is.False);
            Assert.That(r.SaveProtocol("protocol person(Person p) { return p; }").Success, Is.True);
        }

        [Test]
        public void TaskTokensDoNotBecomeEntityReferencesWhenTheyContainAThreadId()
        {
            KernelRuntime r = ClinicScenario.Create();
            Assert.That(r.SaveProtocol("protocol person(Person p) { return p.id; }").Success, Is.True);
            Assert.That(r.Execute("person(thread01)").Value.text, Is.EqualTo("thread01"));
            Assert.That(r.Execute("task = hq.requestCoffee()").Success, Is.True);
            Assert.That(r.Execute("person(task)").Success, Is.False);
            Assert.That(r.Execute("task.id").Success, Is.False);
            Assert.That(r.TypeName(r.Data["task"].value), Is.EqualTo("Task"));
        }

        [Test]
        public void TypedProtocolCompletionUsesMetadataWithoutSampleInstancesOrExecution()
        {
            KernelRuntime r = World();
            r.Language = CommandLocale.Korean;
            const string source = "프로토콜 보기(방형 대상) {\n    변수 다음 = 대상.peer;\n    반환 다음.메\n}";
            int caret = source.IndexOf("다음.메", StringComparison.Ordinal) + "다음.메".Length;
            Completion result = r.CompleteProtocol(source, caret).Single();
            Assert.That(result.Replacement, Does.Contain("반환 다음.메모\n}"));
            Assert.That(r.Data, Is.Empty);
            Assert.That(r.Entities.Count, Is.EqualTo(2));
        }

        [Test]
        public void ReferenceFieldsFollowExistingInstancesAndEnforceTheirDeclaredType()
        {
            KernelRuntime r = World();
            Assert.That(r.Execute("study.peer = room").Success, Is.True);
            Assert.That(r.Execute("study.peer.note = \"linked\"").Success, Is.True);
            Assert.That(r.Execute("room.note").Value.text, Is.EqualTo("linked"));
            Assert.That(r.Execute("study.peer.name").Value.text, Is.EqualTo("방 2"));
            Assert.That(r.Execute("study.peer = 2").Success, Is.False);
            Assert.That(r.Execute("study.note = 2").Success, Is.False);
            Assert.That(() => r.Entity("study").SetValue("note", Datum.Number(2)), Throws.TypeOf<CommandException>());
        }

        [Test]
        public void ParentAccessRulesCannotBeWeakenedByOverrides()
        {
            KernelRuntime r = World();
            EntitySchema parent = r.Schemas.Require("Room");
            parent.Access.Authorities = new[] { "rooms" };
            parent.FindVariable("note").Access.Facts = new[] { "note_known" };
            parent.FindOperation("read").Access.Facts = new[] { "read_allowed" };
            EntitySchema child = r.Schemas.Require("Study");
            child.Operations.Add(new Operation { Code = "read", Overrides = true, ReturnKind = DataKind.Text, Invoke = c => Datum.Text("override") });
            r.NotifySchemaChanged();
            Assert.That(r.Complete("study."), Is.Empty);
            r.Authorities.Add("rooms");
            Assert.That(r.Execute("study.note = \"hidden\"").Success, Is.False);
            Assert.That(r.Complete("study.re"), Is.Empty);
            Assert.That(r.Execute("study.read()").Success, Is.False);
            r.Facts.UnionWith(new[] { "note_known", "read_allowed" });
            Assert.That(r.Execute("study.read()").Value.text, Is.EqualTo("override"));
            Assert.That(r.Execute("study.note = \"ok\"").Success, Is.True);
        }

        [Test]
        public void ReadonlyAssignmentsAreRejectedBeforeTheirRightHandSideRunsOnEitherExecutor()
        {
            KernelRuntime r = ClinicScenario.Create();
            r.RecordClue(0);
            Assert.That(r.Execute("hq.id = archive.inspect()").Success, Is.False);
            Assert.That(r.SaveProtocol("protocol blocked() { hq.id = archive.inspect(); }").Success, Is.True);
            Assert.That(r.Execute("thread02.run(blocked)").Success, Is.True);
            r.Tick(6);
            Assert.That(r.Agent("thread02").State, Is.EqualTo(AgentState.Waiting));
            Assert.That(r.Facts, Does.Not.Contain("archive_inspected"));
        }

        [Test]
        public void FieldAssignmentsKeepNestedCallTimingAndSurviveSavingInFlight()
        {
            KernelRuntime r = ClinicScenario.Create();
            var schema = new EntitySchema("Notebook", "메모장", DataKind.Item, r.Schemas.Require("Item"));
            schema.Variables.Add(new VariableDefinition { Code = "record", Kind = DataKind.Document, Writable = true });
            r.Schemas.Register(schema); r.CreateInstance("Notebook", "book", "메모장");
            r.RecordClue(0);
            Assert.That(r.SaveProtocol("protocol work() { book.record = archive.inspect(); return book.record; }").Success, Is.True);
            Assert.That(r.Execute("thread03.run(work)").Success, Is.True);
            r.Tick(2);
            var restored = ClinicScenario.Create();
            // Asset definitions are installed before loading a daily save.
            var savedSchema = new EntitySchema("Notebook", "메모장", DataKind.Item, restored.Schemas.Require("Item"));
            savedSchema.Variables.Add(new VariableDefinition { Code = "record", Kind = DataKind.Document, Writable = true });
            restored.Schemas.Register(savedSchema);
            restored.Restore(r.Capture());
            restored.Tick(2);
            Assert.That(restored.Entity("book").GetValue("record").kind, Is.EqualTo(DataKind.Empty));
            restored.Tick(1);
            Assert.That(restored.Agent("thread03").State, Is.EqualTo(AgentState.Ready));
            Assert.That(restored.Agent("thread03").Report.schemaId, Is.EqualTo("Document"));
            Assert.That(restored.Entity("book").GetValue("record").id, Is.EqualTo("archive_record"));
        }

        [Test]
        public void SnapshotRestoresIndependentValuesLinksAndReferenceAliases()
        {
            KernelRuntime r = World();
            r.Execute("study.peer = room"); r.Execute("a = study"); r.Execute("study.note = \"saved\"");
            var restored = new KernelRuntime(r.Schemas);
            restored.Restore(r.Capture());
            Assert.That(restored.Execute("a.peer.note = \"other\"").Success, Is.True);
            Assert.That(restored.Execute("room.note").Value.text, Is.EqualTo("other"));
            Assert.That(restored.Execute("study.note").Value.text, Is.EqualTo("saved"));
            Assert.That(r.Execute("room.note").Value.text, Is.EqualTo("base"));
        }

        [Test]
        public void ProtocolTestDoesNotChangeRealInstanceFieldsOrKnowledge()
        {
            KernelRuntime r = World();
            ExecutionResult result = r.TestProtocol("protocol edit(Room target) { target.note = \"test\"; return target.read(); }", "edit(study)");
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(result.Value.text, Is.EqualTo("test"));
            Assert.That(r.Execute("study.note").Value.text, Is.EqualTo("base"));
            Assert.That(r.Protocols, Is.Empty);
        }

        [Test]
        public void InvalidInheritanceAndIncompatibleOverridesFailValidation()
        {
            KernelRuntime r = World();
            EntitySchema schema = r.Schemas.Require("Study");
            schema.Variables.Add(new VariableDefinition { Code = "note", Kind = DataKind.Number, Overrides = true });
            Assert.That(() => r.NotifySchemaChanged(), Throws.TypeOf<CommandException>());
            schema.Variables.Clear();
            schema.Variables.Add(new VariableDefinition { Code = "read", Kind = DataKind.Text });
            Assert.That(() => r.NotifySchemaChanged(), Throws.TypeOf<CommandException>());
            schema.Variables.Clear();
            schema.Operations.Add(new Operation { Code = "note", ReturnKind = DataKind.Text });
            Assert.That(() => r.NotifySchemaChanged(), Throws.TypeOf<CommandException>());
            schema.Operations.Clear();
            schema.Parent.Parent = schema;
            Assert.That(() => r.NotifySchemaChanged(), Throws.TypeOf<CommandException>());
        }

        [Test]
        public void AliasCollisionsReadonlyOverridesAndUnknownFieldTypesFailValidation()
        {
            KernelRuntime r = World(); EntitySchema child = r.Schemas.Require("Study");
            child.Variables.Add(new VariableDefinition { Code = "other", Aliases = new[] { "메모" } });
            Assert.That(() => r.NotifySchemaChanged(), Throws.TypeOf<CommandException>());
            child.Variables.Clear();
            child.Variables.Add(new VariableDefinition { Code = "description", Kind = DataKind.Text, Writable = true, Overrides = true });
            Assert.That(() => r.NotifySchemaChanged(), Throws.TypeOf<CommandException>());
            child.Variables.Clear();
            child.Variables.Add(new VariableDefinition { Code = "unknown", SchemaId = "Missing" });
            Assert.That(() => r.NotifySchemaChanged(), Throws.TypeOf<CommandException>());
            child.Variables.Clear();
            child.Operations.Add(new Operation { Code = "read", Overrides = true, ReturnKind = DataKind.Number });
            Assert.That(() => r.NotifySchemaChanged(), Throws.TypeOf<CommandException>());
        }

        [Test]
        public void BuiltinWorldUsesSchemaFunctionsAndRetainsDistinctThreadIdentities()
        {
            KernelRuntime r = ClinicScenario.Create();
            Assert.That(r.Entities.Values.All(e => e.Schema != null && e.Operations.Count == 0), Is.True);
            Assert.That(r.Entity("thread01").Schema, Is.SameAs(r.Entity("thread03").Schema));
            r.RecordClue(0);
            Assert.That(r.Execute("thread03.run(recon, archive)").Success, Is.True);
            Assert.That(r.Agent("thread01").State, Is.EqualTo(AgentState.Ready));
            Assert.That(r.Agent("thread03").State, Is.EqualTo(AgentState.Executing));
            r.Tick(5);
            Assert.That(r.Agent("thread03").Report.kind, Is.EqualTo(DataKind.Document));
            Assert.That(r.Execute("thread03.id").Value.text, Is.EqualTo("thread03"));
            r.Facts.Add("enemy_at_clinic");
            Assert.That(r.Execute("hq.door.lock()").Success, Is.True);
            Assert.That(r.Execute("hq.door.locked").Value.number, Is.EqualTo(1));
            Assert.That(r.Execute("hq.door.locked = false").Success, Is.False);
        }

        [Test]
        public void PreSchemaSavesKeepScenarioDefaultsAndMigrateRecordReferences()
        {
            KernelRuntime r = ClinicScenario.Create(); r.RecordClue(0);
            KernelSnapshot legacy = r.Capture(); legacy.entities = null;
            foreach (DataSlot slot in legacy.data) slot.value.schemaId = "";
            var restored = ClinicScenario.Create(); restored.Restore(legacy);
            Assert.That(restored.Execute("basementNoise.description").Value.text, Does.Contain("기계음"));
            Assert.That(restored.Execute("hq.door.locked").Value.number, Is.Zero);
        }
    }
}
