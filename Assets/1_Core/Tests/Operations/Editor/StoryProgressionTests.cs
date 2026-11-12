using System;
using System.Linq;
using Codetective.Operations;
using NUnit.Framework;

namespace Codetective.Tests.Operations
{
    public sealed class StoryProgressionTests
    {
        private static StoryCondition Gate() => new StoryCondition { triggersOn = new[] { "known" }, triggersOff = new[] { "alarm" },
            quests = new[] { new QuestRequirement { id = "case", minimumStage = 10, maximumStage = 30, status = QuestStatus.Active } } };
        private static KernelRuntime World()
        {
            var r = new KernelRuntime(); Entity p = r.CreateInstance("Person", "person", "인물");
            p.AddVariable(new VariableDefinition { Code = "note", Label = "메모", Kind = DataKind.Text, Writable = true,
                DefaultValue = Datum.Text("기밀"), Access = new AccessRule { Story = Gate() } });
            p.AddFunction(new OperationDefinition { Code = "ask", ReturnKind = DataKind.Text, ReturnField = "note", Access = new AccessRule { Story = Gate() } });
            return r;
        }
        private static void Open(KernelRuntime r) { r.SetTrigger("known", true); r.SetQuestStage("case", 10); }
        [Test]
        public void ConditionsUseReversibleTriggersStageRangesAndQuestStatus()
        {
            KernelRuntime r = World(); Assert.That(r.GetTrigger("unknown"), Is.False);
            Assert.That(r.GetQuest("case").status, Is.EqualTo(QuestStatus.NotStarted));
            Open(r); Assert.That(Gate().Matches(r), Is.True);
            r.SetTrigger("alarm", true); Assert.That(Gate().Matches(r), Is.False);
            r.SetTrigger("alarm", false); r.SetQuestStage("case", 30); Assert.That(Gate().Matches(r), Is.True);
            r.SetQuestStage("case", 31); Assert.That(Gate().Matches(r), Is.False);
            r.SetQuestStage("case", 10, QuestStatus.Completed); Assert.That(Gate().Matches(r), Is.False);
            r.SetQuestStage("case", 10); r.SetTrigger("known", false); Assert.That(Gate().Matches(r), Is.False);
        }
        [Test]
        public void LockedMembersAreAbsentFromPermissionsAndCannotBeReadWrittenOrCalledByName()
        {
            KernelRuntime r = World();
            Assert.That(r.Complete("person.no"), Is.Empty); Assert.That(r.Complete("person.as"), Is.Empty);
            Assert.That(r.Execute("person.note").Success, Is.False); Assert.That(r.Execute("person.ask()").Success, Is.False);
            r.Entity("person").AddFunction(new OperationDefinition { Code = "sideEffect", ReturnKind = DataKind.Text, Result = Datum.Text("changed"),
                StoryEffects = new StoryEffects { triggers = new[] { new TriggerValue { id = "sideEffect", value = true } } } });
            Assert.That(r.Execute("person.note = person.sideEffect()").Success, Is.False); Assert.That(r.GetTrigger("sideEffect"), Is.False);
            Open(r); Assert.That(r.Complete("person.no").Count, Is.EqualTo(1));
            Assert.That(r.Execute("person.ask()").Value.text, Is.EqualTo("기밀"));
            Assert.That(r.Execute("person.note = \"공개 메모\"").Success, Is.True);
            r.SetTrigger("known", false); Assert.That(r.Execute("person.ask()").Success, Is.False);
            Assert.That(r.Entity("person").GetValue("note").text, Is.EqualTo("공개 메모"));
        }
        [Test]
        public void InternalMembersStillExistForWorldLogicAndInstanceOverridesCanExposeThem()
        {
            var r = new KernelRuntime(); Entity p = r.CreateInstance("Person", "person", "인물");
            p.AddVariable(new VariableDefinition { Code = "privateNote", Kind = DataKind.Text, DefaultValue = Datum.Text("hidden"),
                Access = new AccessRule { PlayerAccess = PlayerAccess.Internal } });
            p.AddFunction(new OperationDefinition { Code = "privateTalk", ReturnKind = DataKind.Text, Result = Datum.Text("hello"),
                Access = new AccessRule { PlayerAccess = PlayerAccess.Internal } });
            Assert.That(p.GetValue("privateNote").text, Is.EqualTo("hidden")); Assert.That(p.Find("privateTalk"), Is.Not.Null);
            Assert.That(r.Complete("person.private"), Is.Empty);
            p.SetVariableAccess("privateNote", PlayerAccess.Public); p.SetFunctionAccess("privateTalk", PlayerAccess.Public);
            Assert.That(r.Execute("person.privateNote").Value.text, Is.EqualTo("hidden")); Assert.That(r.Execute("person.privateTalk()").Success, Is.True);
            p.SetVariableAccess("privateNote", PlayerAccess.Inherit); p.SetFunctionAccess("privateTalk", PlayerAccess.Internal);
            Assert.That(r.Execute("person.privateNote").Success, Is.False); Assert.That(r.Execute("person.privateTalk()").Success, Is.False);
        }
        [Test]
        public void InstanceExposureOverrideDoesNotBypassTheStoryCondition()
        {
            KernelRuntime r = World(); r.Entity("person").SetFunctionAccess("ask", PlayerAccess.Public);
            Assert.That(r.Execute("person.ask()").Success, Is.False); Open(r);
            Assert.That(r.Execute("person.ask()").Success, Is.True);
            r.Entity("person").SetFunctionAccess("ask", PlayerAccess.Internal);
            Assert.That(r.Complete("person.as"), Is.Empty); Assert.That(r.Execute("person.ask()").Success, Is.False);
        }
        [Test]
        public void InheritedMembersUseTheInstanceAccessAndAllAncestralConditions()
        {
            var r = new KernelRuntime(); var parent = new EntitySchema("Parent", "부모", DataKind.Person, r.Schemas.Require("Person"));
            parent.Variables.Add(new VariableDefinition { Code = "secret", Kind = DataKind.Text, DefaultValue = Datum.Text("value"),
                Access = new AccessRule { PlayerAccess = PlayerAccess.Internal, Story = new StoryCondition { triggersOn = new[] { "parent" } } } });
            r.Schemas.Register(parent); var child = new EntitySchema("Child", "자식", DataKind.Person, parent); r.Schemas.Register(child);
            Entity a = r.CreateInstance("Child", "a", "A"), b = r.CreateInstance("Child", "b", "B");
            a.SetVariableAccess("secret", PlayerAccess.Public);
            Assert.That(r.Execute("a.secret").Success, Is.False); r.SetTrigger("parent", true);
            Assert.That(r.Execute("a.secret").Success, Is.True); Assert.That(r.Execute("b.secret").Success, Is.False);
        }
        [Test]
        public void HidingAnEntityRevokesExistingAliasesReferencesArgumentsAndItsProfile()
        {
            var r = new KernelRuntime(); Entity person = r.CreateInstance("Person", "person", "Hidden Person");
            Entity holder = r.CreateInstance("Entity", "holder", "Holder");
            holder.AddVariable(new VariableDefinition { Code = "friend", Kind = DataKind.Person }); holder.SetValue("friend", r.Reference(person));
            r.Execute("alias = person"); r.SaveProtocol("protocol identify(Person p) { return p.id; }");
            person.SetEntityAccess(PlayerAccess.Internal);
            foreach (string code in new[] { "person", "alias", "alias.id", "holder.friend", "holder.friend.id", "identify(alias)" })
                Assert.That(r.Execute(code).Success, Is.False, code);
            Assert.That(r.Complete("ali"), Is.Empty); Assert.That(r.Complete("alias."), Is.Empty);
            Assert.That(r.VisibleProfileIds, Does.Not.Contain("person")); Assert.That(() => r.ProfileSummary("person"), Throws.TypeOf<CommandException>());
            person.SetEntityAccess(PlayerAccess.Public); Assert.That(r.Execute("alias.id").Value.text, Is.EqualTo("person"));
        }
        [Test]
        public void ProfilesRespectMemberAccessAndNeverShowThreadIdentities()
        {
            KernelRuntime r = World(); Assert.That(r.ProfileSummary("person"), Does.Not.Contain("기밀"));
            Open(r); Assert.That(r.ProfileSummary("person"), Does.Contain("기밀"));
            r.Entity("person").SetVariableAccess("note", PlayerAccess.Internal); Assert.That(r.ProfileSummary("person"), Does.Not.Contain("기밀"));
            KernelRuntime clinic = ClinicScenario.Create(); Assert.That(clinic.VisibleProfileIds, Does.Contain("kernel"));
            Assert.That(clinic.VisibleProfileIds.Any(id => id.StartsWith("thread", StringComparison.Ordinal)), Is.False);
            Assert.That(clinic.ProfileSummary("kernel"), Does.Contain("[데이터 말소됨]"));
            Assert.That(clinic.ProfileSummary("client"), Does.Not.Contain("현장지원과 전용 기록"));
        }
        [Test]
        public void StoryAndExposureRoundTripAndSnapshotsDoNotShareMutableQuestValues()
        {
            KernelRuntime r = World(); Open(r); r.SetTrigger("alarm", true);
            r.Entity("person").SetVariableAccess("note", PlayerAccess.Internal);
            KernelSnapshot snapshot = r.Capture();
            var restored = new KernelRuntime(); restored.Restore(snapshot);
            Assert.That(restored.GetTrigger("known"), Is.True); Assert.That(restored.GetQuest("case").stage, Is.EqualTo(10));
            restored.SetTrigger("alarm", false); Assert.That(restored.Execute("person.note").Success, Is.False);
            restored.Entity("person").SetVariableAccess("note", PlayerAccess.Public); Assert.That(restored.Execute("person.note").Success, Is.True);
            snapshot.quests[0].stage = 999; QuestProgress copy = restored.GetQuest("case"); copy.stage = 555;
            Assert.That(restored.GetQuest("case").stage, Is.EqualTo(10)); Assert.That(r.GetTrigger("alarm"), Is.True);
        }
        [Test]
        public void AuthoredOperationsAndMailUseTheSameStateAndSandboxTestsCannotChangeIt()
        {
            KernelRuntime r = World(); r.RegisterMail(new StoryMail { id = "intro", requiresAcceptance = true,
                onAccept = new StoryEffects { triggers = new[] { new TriggerValue { id = "known", value = true } }, quests = new[] { new QuestProgress { id = "case", stage = 10 } } } });
            r.ReadMail("intro"); r.AcceptMail("intro"); Assert.That(r.Execute("person.ask()").Success, Is.True);
            r.Entity("person").AddFunction(new OperationDefinition { Code = "finish", ReturnKind = DataKind.Text, Result = Datum.Text("done"),
                StoryEffects = new StoryEffects { triggers = new[] { new TriggerValue { id = "known", value = false } }, quests = new[] { new QuestProgress { id = "case", stage = 100, status = QuestStatus.Completed } } } });
            Assert.That(r.TestProtocol("protocol test() { return person.finish(); }", "test()").Success, Is.True);
            Assert.That(r.GetTrigger("known"), Is.True); Assert.That(r.GetQuest("case").stage, Is.EqualTo(10));
            Assert.That(r.Execute("person.finish()").Success, Is.True); Assert.That(r.GetQuest("case").status, Is.EqualTo(QuestStatus.Completed));
            Assert.That(r.Execute("person.ask()").Success, Is.False);
        }
        [Test]
        public void InvalidStoryEffectsAreRejectedBeforeApplyingAnyTrigger()
        {
            var r = new KernelRuntime();
            Assert.That(() => r.ApplyStoryEffects(new StoryEffects { triggers = new[] { new TriggerValue { id = "valid", value = true } },
                quests = new[] { new QuestProgress { id = "bad", stage = -1 } } }), Throws.TypeOf<CommandException>());
            Assert.That(r.GetTrigger("valid"), Is.False);
        }
        [Test]
        public void AThreadRechecksStoryAccessBeforeFinishingAQueuedAction()
        {
            KernelRuntime r = ClinicScenario.Create();
            r.Entity("client").AddFunction(new OperationDefinition { Code = "longQuestion", Minutes = 5, ReturnKind = DataKind.Text, Result = Datum.Text("answer"),
                Access = new AccessRule { Story = new StoryCondition { triggersOn = new[] { "permission" } } },
                StoryEffects = new StoryEffects { triggers = new[] { new TriggerValue { id = "asked", value = true } } } });
            r.SetTrigger("permission", true); r.SaveProtocol("protocol question(Person p) { return p.longQuestion(); }");
            Assert.That(r.Execute("thread01.run(question, client)").Success, Is.True);
            r.Tick(1); r.SetTrigger("permission", false); r.Tick(10);
            Assert.That(r.Agent("thread01").State, Is.EqualTo(AgentState.Waiting)); Assert.That(r.GetTrigger("asked"), Is.False);
            r.SetTrigger("permission", true); r.Resume("thread01"); r.Tick(1);
            Assert.That(r.Agent("thread01").State, Is.EqualTo(AgentState.Ready)); Assert.That(r.GetTrigger("asked"), Is.True);
        }
        [Test]
        public void OldMemberDefinitionsWithoutStoryFieldsRemainUsable()
        {
            KernelRuntime r = World(); KernelSnapshot old = r.Capture();
            old.triggers = null; old.quests = null; old.entities[0].playerAccess = null;
            old.entities[0].localFunctions[0].StoryEffects = null; old.entities[0].localFunctions[0].Access.Story = null;
            old.entities[0].localVariables[0].Access.Story = null;
            var restored = new KernelRuntime(); restored.Restore(old);
            Assert.That(restored.Execute("person.ask()").Success, Is.True);
        }
    }
}
