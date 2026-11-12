using System;
using System.Linq;
using Codetective.Operations;
using NUnit.Framework;

namespace Codetective.Tests.Operations
{
    public sealed class InstanceMemberTests
    {
        private static KernelRuntime World()
        {
            var r = new KernelRuntime();
            r.CreateInstance("Person", "alice", "Alice"); r.CreateInstance("Person", "bob", "Bob");
            r.CreateInstance("Item", "key", "열쇠");
            return r;
        }
        private static VariableDefinition Note(string text = "hello") => new VariableDefinition { Code = "note", Kind = DataKind.Text,
            Writable = true, DefaultValue = Datum.Text(text), Names = new[] { new LocalizedCommandName(CommandLocale.Korean, "메모") } };
        private static OperationDefinition Ask(string text = "answer") => new OperationDefinition { Code = "ask", ReturnKind = DataKind.Text,
            Result = Datum.Text(text), Names = new[] { new LocalizedCommandName(CommandLocale.Korean, "물어보기") } };

        [Test]
        public void InstancesCanGainAndLoseMembersWithoutChangingTheirSchemaOrSiblings()
        {
            KernelRuntime r = World(); Entity a = r.Entity("alice"), b = r.Entity("bob");
            int revision = r.Revision;
            a.AddVariable(Note()); a.AddFunction(new OperationDefinition { Code = "read", ReturnKind = DataKind.Text, ReturnField = "note" });
            Assert.That(r.Revision, Is.GreaterThan(revision));
            Assert.That(r.Execute("alias = alice").Success, Is.True);
            Assert.That(r.Execute("alias.note = \"shared\"").Success, Is.True);
            Assert.That(r.Execute("alice.read()").Value.text, Is.EqualTo("shared"));
            Assert.That(r.Execute("bob.note").Success, Is.False);
            Assert.That(b.Find("read"), Is.Null);
            Assert.That(a.Schema, Is.SameAs(b.Schema));
            Assert.That(a.Schema.FindVariable("note"), Is.Null);
            const string source = "protocol inspect(Person target) { return target.re }";
            int caret = source.IndexOf("target.re", StringComparison.Ordinal) + "target.re".Length;
            Assert.That(r.CompleteProtocol(source, caret, "alice").Single().Replacement, Does.Contain("target.read()"));
            Assert.That(r.CompleteProtocol(source, caret), Is.Empty);
            Assert.That(a.RemoveVariable("메모"), Is.True);
            Assert.That(r.Complete("alice.re"), Is.Empty); // Its backing field has gone away.
            Assert.That(r.Execute("alice.read()").Success, Is.False);
            Assert.That(a.RemoveFunction("read"), Is.True);
            a.AddVariable(Note("fresh"));
            Assert.That(r.Execute("alias.note").Value.text, Is.EqualTo("fresh"));
        }

        [Test]
        public void InventoryReferencesAreTypedAndRemainOwnedByEachPerson()
        {
            KernelRuntime r = World();
            var holding = new VariableDefinition { Code = "holding", Kind = DataKind.Item, SchemaId = "Item", Writable = true };
            r.Entity("alice").AddVariable(holding); r.Entity("bob").AddVariable(holding);
            r.Entity("alice").AddFunction(new OperationDefinition { Code = "heldItem", ReturnKind = DataKind.Item, ReturnSchemaId = "Item", ReturnField = "holding" });
            Assert.That(r.Execute("alice.heldItem()").Success, Is.True);
            Assert.That(r.Execute("alice.heldItem()").Value.kind, Is.EqualTo(DataKind.Empty));
            Assert.That(r.Execute("alice.holding = key").Success, Is.True);
            Assert.That(r.Execute("alice.holding.name").Value.text, Is.EqualTo("열쇠"));
            Assert.That(r.Execute("bob.holding").Value.kind, Is.EqualTo(DataKind.Empty));
            Assert.That(r.Execute("bob.holding = alice").Success, Is.False);
            r.Entity("alice").RemoveVariable("holding");
            Assert.That(r.Entity("key"), Is.Not.Null);
            Assert.That(r.Execute("alice.holding").Success, Is.False);
        }

        [Test]
        public void MemberDefinitionsAndDefaultsAreCopiedOnAttachment()
        {
            KernelRuntime r = World(); VariableDefinition note = Note(); OperationDefinition ask = Ask();
            r.Entity("alice").AddVariable(note); r.Entity("bob").AddVariable(note);
            r.Entity("alice").AddFunction(ask); r.Entity("bob").AddFunction(ask);
            note.DefaultValue.text = "changed source"; note.Names[0].name = "다른이름"; ask.Result.text = "changed source";
            r.Entity("alice").GetVariableDefinition("note").DefaultValue.text = "only Alice";
            Assert.That(r.Execute("alice.note").Value.text, Is.EqualTo("only Alice"));
            Assert.That(r.Execute("bob.note").Value.text, Is.EqualTo("hello"));
            Assert.That(r.Execute("alice.ask()").Value.text, Is.EqualTo("answer"));
            Assert.That(r.Execute("bob.메모").Success, Is.True);
        }

        [Test]
        public void FunctionsCanChangeConversationAvailabilityForOnlyTheirTarget()
        {
            KernelRuntime r = World();
            var intro = Ask("첫 대화");
            intro.MemberChanges = new[] { new MemberToggle { Name = "ask", Enabled = false }, new MemberToggle { Name = "followup", Enabled = true } };
            foreach (Entity person in new[] { r.Entity("alice"), r.Entity("bob") })
            {
                person.AddFunction(intro); person.AddFunction(new OperationDefinition { Code = "followup", ReturnKind = DataKind.Text, Result = Datum.Text("후속 대화") });
                person.SetFunctionEnabled("followup", false);
            }
            Assert.That(r.Complete("alice.f"), Is.Empty);
            Assert.That(r.Execute("alice.ask()").Value.text, Is.EqualTo("첫 대화"));
            Assert.That(r.Complete("alice.a"), Is.Empty);
            Assert.That(r.Execute("alice.ask()").Success, Is.False);
            Assert.That(r.Execute("alice.followup()").Value.text, Is.EqualTo("후속 대화"));
            Assert.That(r.Execute("bob.followup()").Success, Is.False);
            Assert.That(r.Complete("bob.a").Single().Replacement, Is.EqualTo("bob.ask()"));
        }

        [Test]
        public void DisabledFieldsRetainTheirValueAndInheritedMembersCanBeDisabledPerInstance()
        {
            KernelRuntime r = World(); Entity a = r.Entity("alice");
            a.AddVariable(Note()); r.Execute("alice.note = \"kept\"");
            a.SetVariableEnabled("메모", false); a.SetFunctionEnabled("describe", false);
            Assert.That(r.Execute("alice.note").Success, Is.False);
            Assert.That(r.Complete("alice.n").Select(c => c.Replacement), Does.Not.Contain("alice.note"));
            Assert.That(r.Execute("alice.describe()").Success, Is.False);
            Assert.That(r.Execute("bob.describe()").Success, Is.True);
            a.SetVariableEnabled("note", true); a.SetFunctionEnabled("describe", true);
            Assert.That(r.Execute("alice.note").Value.text, Is.EqualTo("kept"));
        }

        [Test]
        public void ReplacingLocalFunctionsAndFieldsUpdatesCompletionAndPreservesCompatibleValues()
        {
            KernelRuntime r = World(); Entity a = r.Entity("alice"); a.AddVariable(Note()); a.AddFunction(Ask());
            r.Execute("alice.note = \"kept\""); a.AddVariable(Note("new default"), true);
            a.AddFunction(new OperationDefinition { Code = "ask", ReturnKind = DataKind.Text,
                Parameters = new[] { new OperationInput { Name = "subject", Kind = DataKind.Text } }, Result = Datum.Text("new answer") }, true);
            Assert.That(r.Complete("alice.ask").Single().Replacement, Is.EqualTo("alice.ask("));
            Assert.That(r.Execute("alice.ask()").Success, Is.False);
            Assert.That(r.Execute("alice.ask(\"topic\")").Value.text, Is.EqualTo("new answer"));
            Assert.That(r.Execute("alice.note").Value.text, Is.EqualTo("kept"));
            Assert.That(() => a.AddVariable(new VariableDefinition { Code = "note", Kind = DataKind.Number }, true), Throws.TypeOf<CommandException>());
            Assert.That(r.Execute("alice.note").Value.text, Is.EqualTo("kept"));
        }

        [Test]
        public void InvalidAttachmentsRollBackWithoutChangingCompletionOrExistingValues()
        {
            KernelRuntime r = World(); Entity a = r.Entity("alice"); a.AddVariable(Note()); a.AddFunction(Ask());
            int revision = r.Revision;
            Assert.That(() => a.AddVariable(Note()), Throws.TypeOf<CommandException>());
            Assert.That(() => a.AddVariable(new VariableDefinition { Code = "other", Aliases = new[] { "메모" } }), Throws.TypeOf<CommandException>());
            Assert.That(() => a.AddVariable(new VariableDefinition { Code = "bad", SchemaId = "Unknown" }), Throws.TypeOf<CommandException>());
            Assert.That(() => a.AddFunction(new OperationDefinition { Code = "note" }), Throws.TypeOf<CommandException>());
            Assert.That(() => a.AddFunction(new OperationDefinition { Code = "ask", ReturnSchemaId = "Unknown" }, true), Throws.TypeOf<CommandException>());
            Assert.That(r.Revision, Is.EqualTo(revision));
            Assert.That(a.LocalVariables.Count(), Is.EqualTo(1)); Assert.That(a.LocalFunctions.Count(), Is.EqualTo(1));
            Assert.That(r.Execute("alice.ask()").Value.text, Is.EqualTo("answer"));
        }

        [Test]
        public void InstanceOverridesKeepInheritedPermissionsAndReadonlyRestrictions()
        {
            KernelRuntime r = World(); Entity a = r.Entity("alice");
            a.Schema.FindOperation("describe").Access.Facts = new[] { "known" };
            a.AddFunction(new OperationDefinition { Code = "describe", Overrides = true, ReturnKind = DataKind.Text, Result = Datum.Text("private") });
            Assert.That(r.Execute("alice.describe()").Success, Is.False);
            Assert.That(r.Complete("alice.describe"), Is.Empty);
            Assert.That(() => a.AddVariable(new VariableDefinition { Code = "id", Overrides = true, Kind = DataKind.Text, Writable = true }), Throws.TypeOf<CommandException>());
            r.Facts.Add("known"); Assert.That(r.Execute("alice.describe()").Value.text, Is.EqualTo("private"));
            a.RemoveFunction("describe"); Assert.That(r.Execute("alice.describe()").Value.text, Does.Contain("Alice"));
        }

        [Test]
        public void SavingRestoresDynamicDefinitionsReferencesFunctionsAndDisabledState()
        {
            KernelRuntime r = World(); Entity a = r.Entity("alice"); a.AddVariable(Note()); a.AddFunction(Ask("saved answer"));
            a.AddVariable(new VariableDefinition { Code = "holding", Kind = DataKind.Entity, SchemaId = "Item", Writable = true });
            r.Execute("alias = alice"); r.Execute("alice.holding = key"); r.Execute("alice.note = \"saved\"");
            a.SetVariableEnabled("note", false); a.SetFunctionEnabled("describe", false);
            var restored = new KernelRuntime(r.Schemas); restored.Restore(r.Capture());
            Assert.That(restored.Execute("alias.ask()").Value.text, Is.EqualTo("saved answer"));
            Assert.That(restored.Execute("alias.holding.name").Value.text, Is.EqualTo("열쇠"));
            Assert.That(restored.Execute("alice.note").Success, Is.False);
            Assert.That(restored.Execute("alice.describe()").Success, Is.False);
            restored.Entity("alice").SetVariableEnabled("note", true);
            Assert.That(restored.Execute("alice.note").Value.text, Is.EqualTo("saved"));
            restored.Entity("alice").RemoveFunction("ask");
            var afterRemoval = new KernelRuntime(r.Schemas); afterRemoval.Restore(restored.Capture());
            Assert.That(afterRemoval.Execute("alice.ask()").Success, Is.False);
        }

        [Test]
        public void OldSnapshotsKeepAuthoredInstanceMembersAndCapturesDoNotShareMutableDefinitions()
        {
            KernelRuntime r = World(); r.Entity("alice").AddVariable(Note()); r.Entity("alice").AddFunction(Ask());
            KernelSnapshot capture = r.Capture(); EntitySnapshot saved = capture.entities.Single(e => e.id == "alice");
            saved.localVariables[0].DefaultValue.text = "snapshot only"; saved.localFunctions[0].Result.text = "snapshot only";
            Assert.That(r.Execute("alice.ask()").Value.text, Is.EqualTo("answer"));
            Assert.That(r.Execute("alice.note").Value.text, Is.EqualTo("hello"));
            saved.localVariables = null; saved.localFunctions = null; saved.disabledVariables = null; saved.disabledFunctions = null;
            r.Restore(capture);
            Assert.That(r.Execute("alice.ask()").Value.text, Is.EqualTo("answer"));
        }

        [Test]
        public void ProtocolSandboxIsolatesInstanceFunctionsFieldsAndConversationState()
        {
            KernelRuntime r = World(); Entity a = r.Entity("alice"); a.AddVariable(Note());
            OperationDefinition ask = Ask(); ask.MemberChanges = new[] { new MemberToggle { Name = "ask", Enabled = false } }; a.AddFunction(ask);
            ExecutionResult test = r.TestProtocol("protocol change(Person p) { p.note = \"test\"; return p.ask(); }", "change(alice)");
            Assert.That(test.Success, Is.True, test.Message);
            Assert.That(r.Execute("alice.note").Value.text, Is.EqualTo("hello"));
            Assert.That(r.Complete("alice.ask").Single().Replacement, Is.EqualTo("alice.ask()"));
        }

        [Test]
        public void RemovingAPendingFunctionMakesAThreadWaitAndReaddingItAllowsResuming()
        {
            KernelRuntime r = ClinicScenario.Create(); Entity archive = r.Entity("archive");
            var ask = Ask(); ask.Minutes = 5; archive.AddFunction(ask);
            Assert.That(r.SaveProtocol("protocol dynamic(Place target) { return target.ask(); }").Success, Is.True);
            Assert.That(r.Execute("thread02.run(dynamic, archive)").Success, Is.True);
            r.Tick(2); archive.RemoveFunction("ask"); r.Tick(3);
            Assert.That(r.Agent("thread02").State, Is.EqualTo(AgentState.Waiting));
            archive.AddFunction(Ask("available again")); r.Resume("thread02"); r.Tick(1);
            Assert.That(r.Agent("thread02").State, Is.EqualTo(AgentState.Ready));
            Assert.That(r.Agent("thread02").Report.text, Is.EqualTo("available again"));
        }

        [Test]
        public void BadFunctionWritesDoNotPartiallyChangeValuesOrKnowledge()
        {
            KernelRuntime r = World(); Entity a = r.Entity("alice"); a.AddVariable(Note());
            OperationDefinition ask = Ask(); ask.UnlockFacts = new[] { "changed" };
            ask.FieldWrites = new[] { new OperationFieldWrite { Field = "note", Value = Datum.Text("changed") },
                new OperationFieldWrite { Field = "id", Value = Datum.Text("changed") } };
            a.AddFunction(ask);
            Assert.That(r.Execute("alice.ask()").Success, Is.False);
            Assert.That(r.Execute("alice.note").Value.text, Is.EqualTo("hello"));
            Assert.That(r.Facts, Does.Not.Contain("changed"));
        }

        [Test]
        public void PlayerCommandsCannotAddMembersOrAllocateWorldInstances()
        {
            KernelRuntime r = World();
            Assert.That(r.Execute("alice.extra = key").Success, Is.False);
            Assert.That(r.Execute("alice.AddVariable(\"extra\")").Success, Is.False);
            Assert.That(r.Execute("alice.AddFunction(\"talk\")").Success, Is.False);
            Assert.That(r.Execute("new Person()").Success, Is.False);
            Assert.That(r.Entity("alice").LocalVariables, Is.Empty);
            Assert.That(r.Entity("alice").LocalFunctions, Is.Empty);
            Assert.That(r.Entities.Count, Is.EqualTo(3));
        }
    }
}
