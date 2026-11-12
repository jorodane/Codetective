using System.Collections.Generic;
using System.Linq;
using Codetective.Gameplay;
using Codetective.Operations;
using NUnit.Framework;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Codetective.Tests.Operations
{
    public sealed class EntityAssetTests
    {
        private readonly List<ScriptableObject> assets = new List<ScriptableObject>();
        private T Asset<T>() where T : ScriptableObject
        { T asset = ScriptableObject.CreateInstance<T>(); assets.Add(asset); return asset; }
        [TearDown] public void Cleanup()
        { foreach (ScriptableObject asset in assets) Object.DestroyImmediate(asset); assets.Clear(); }

        [Test]
        public void AuthoredMembersAndActionsShareGlobalTriggerAndQuestConditions()
        {
            var type = Asset<EntitySchemaAsset>(); type.schemaId = "Contact"; type.builtinParent = "Person";
            type.fields = new[] { new FieldDefinition { code = "privateFile", defaultValue = Datum.Text("internal"), access = new AccessDefinition { playerAccess = PlayerAccess.Internal } } };
            type.functions = new[] { new ActionDefinition { code = "brief", result = Datum.Text("ok"),
                story = new StoryCondition { triggersOn = new[] { "intro" }, quests = new[] { new QuestRequirement { id = "case", minimumStage = 10 } } },
                storyEffects = new StoryEffects { quests = new[] { new QuestProgress { id = "case", stage = 20 } } } } };
            var person = Asset<EntityInstanceAsset>(); person.schema = type; person.instanceId = "contact";
            var runtime = new KernelRuntime(); EntityAssetCompiler.Install(runtime, null, new[] { person });
            Assert.That(runtime.Execute("contact.privateFile").Success, Is.False);
            Assert.That(runtime.Entity("contact").GetValue("privateFile").text, Is.EqualTo("internal"));
            Assert.That(runtime.Execute("contact.brief()").Success, Is.False);
            runtime.SetTrigger("intro", true); runtime.SetQuestStage("case", 10);
            Assert.That(runtime.Execute("contact.brief()").Success, Is.True);
            Assert.That(runtime.GetQuest("case").stage, Is.EqualTo(20));
            runtime.SetTrigger("intro", false); Assert.That(runtime.Complete("contact.bri"), Is.Empty);
        }

        [Test]
        public void ScenarioRegistersAuthoredNewsAndSelectsItAtMorningDelivery()
        {
            var scenario = Asset<KernelScenarioDefinition>(); var article = Asset<NewspaperArticleAsset>();
            article.article = new NewsArticle { id = "news", headline = "오늘의 소식", firstDay = 1, lastDay = 1,
                onRead = new StoryEffects { triggers = new[] { new TriggerValue { id = "news.known", value = true } } } };
            scenario.newspaper = new[] { article }; KernelRuntime runtime = scenario.CreateRuntime();
            Assert.That(runtime.TodayNewspaper.day, Is.EqualTo(1)); Assert.That(runtime.NewspaperArticles(1).Single().headline, Is.EqualTo("오늘의 소식"));
            runtime.ReadNewspaper(); Assert.That(runtime.GetTrigger("news.known"), Is.True);
        }

        [Test]
        public void AuthoredHierarchyInstantiatesInheritedFieldsAndTargetBoundFunctions()
        {
            var parent = Asset<EntitySchemaAsset>();
            parent.schemaId = "Room"; parent.displayName = "방"; parent.builtinParent = "Place";
            parent.fields = new[] { new FieldDefinition { code = "note", kind = DataKind.Text, writable = true, defaultValue = Datum.Text("default") } };
            parent.functions = new[] { new ActionDefinition { code = "write", parameters = new[] { new InputDefinition { name = "value", kind = DataKind.Text } },
                returnField = "note", fieldWrites = new[] { new FieldWriteDefinition { field = "note", argumentIndex = 0 } } } };
            var child = Asset<EntitySchemaAsset>(); child.schemaId = "Study"; child.parent = parent;
            var a = Asset<EntityInstanceAsset>(); a.instanceId = "a"; a.schema = child;
            var b = Asset<EntityInstanceAsset>(); b.instanceId = "b"; b.schema = child;
            b.initialValues = new[] { new InitialFieldDefinition { field = "note", value = Datum.Text("second") } };
            var runtime = new KernelRuntime();
            EntityAssetCompiler.Install(runtime, null, new[] { a, b });
            Assert.That(runtime.Execute("a.write(\"first\")").Value.text, Is.EqualTo("first"));
            Assert.That(runtime.Execute("b.note").Value.text, Is.EqualTo("second"));
            Assert.That(runtime.Entities["a"].Schema, Is.SameAs(runtime.Entities["b"].Schema));
            Assert.That(runtime.TestProtocol("protocol edit(Room r) { return r.write(\"sandbox\"); }", "edit(a)").Value.text, Is.EqualTo("sandbox"));
            Assert.That(runtime.Execute("a.note").Value.text, Is.EqualTo("first"));
        }

        [Test]
        public void InstanceReferencesSupportCyclesAndAreValidatedAgainstTheSchema()
        {
            var type = Asset<EntitySchemaAsset>(); type.schemaId = "Node";
            type.fields = new[] { new FieldDefinition { code = "next", kind = DataKind.Entity, referenceType = type } };
            var a = Asset<EntityInstanceAsset>(); a.instanceId = "a"; a.schema = type;
            var b = Asset<EntityInstanceAsset>(); b.instanceId = "b"; b.schema = type;
            a.initialValues = new[] { new InitialFieldDefinition { field = "next", reference = b } };
            b.initialValues = new[] { new InitialFieldDefinition { field = "next", reference = a } };
            var runtime = new KernelRuntime(); EntityAssetCompiler.Install(runtime, null, new[] { a });
            Assert.That(runtime.Execute("a.next.next.id").Value.text, Is.EqualTo("a"));
            Assert.That(runtime.Entities.Count, Is.EqualTo(2));
            b.initialValues = new[] { new InitialFieldDefinition { field = "next", value = Datum.Number(1) } };
            Assert.That(() => EntityAssetCompiler.Install(new KernelRuntime(), null, new[] { a }), Throws.TypeOf<CommandException>());
        }

        [Test]
        public void ParentCyclesDuplicateIdentitiesAndInvalidDefaultsAreRejected()
        {
            var a = Asset<EntitySchemaAsset>(); a.schemaId = "A";
            var b = Asset<EntitySchemaAsset>(); b.schemaId = "B"; b.parent = a; a.parent = b;
            Assert.That(() => EntityAssetCompiler.Install(new KernelRuntime(), new[] { a }, null), Throws.TypeOf<CommandException>());
            a.parent = null;
            a.fields = new[] { new FieldDefinition { code = "bad", kind = DataKind.Number, defaultValue = Datum.Text("wrong") } };
            Assert.That(() => EntityAssetCompiler.Install(new KernelRuntime(), new[] { a }, null), Throws.TypeOf<CommandException>());
            a.fields = new FieldDefinition[0];
            var first = Asset<EntityInstanceAsset>(); first.instanceId = "same"; first.schema = a;
            var second = Asset<EntityInstanceAsset>(); second.instanceId = "same"; second.schema = a;
            Assert.That(() => EntityAssetCompiler.Install(new KernelRuntime(), null, new[] { first, second }), Throws.TypeOf<CommandException>());
        }

        [Test]
        public void LegacyAdditionalObjectsCompileIntoSchemaBackedInstances()
        {
            var scenario = Asset<KernelScenarioDefinition>();
            scenario.additionalObjects = new[] { new EntityDefinition { code = "legacy", label = "기존 대상", kind = DataKind.Item,
                actions = new[] { new ActionDefinition { code = "check", result = Datum.Text("ok") } } } };
            KernelRuntime runtime = scenario.CreateRuntime();
            Assert.That(runtime.Execute("legacy.check()").Value.text, Is.EqualTo("ok"));
            Assert.That(runtime.Entity("legacy").Operations, Is.Empty);
            Assert.That(runtime.Entity("legacy").Schema.Parent.Id, Is.EqualTo("Item"));
        }

        [Test]
        public void InstanceAssetsDeclarePersonalFieldsAndConversationFunctionsIndependently()
        {
            var type = Asset<EntitySchemaAsset>(); type.schemaId = "Visitor"; type.builtinParent = "Person";
            var a = Asset<EntityInstanceAsset>(); a.instanceId = "a"; a.schema = type;
            var b = Asset<EntityInstanceAsset>(); b.instanceId = "b"; b.schema = type;
            a.fields = new[] { new FieldDefinition { code = "appointment", kind = DataKind.Text, defaultValue = Datum.Text("09:00") } };
            a.functions = new[] { new ActionDefinition { code = "intro", returnField = "appointment", memberChanges = new[] {
                    new MemberToggle { Name = "intro", Enabled = false }, new MemberToggle { Name = "followup", Enabled = true } } },
                new ActionDefinition { code = "followup", result = Datum.Text("more") } };
            a.disabledFunctions = new[] { "followup" };
            var runtime = new KernelRuntime(); EntityAssetCompiler.Install(runtime, null, new[] { a, b });
            Assert.That(runtime.Execute("a.followup()").Success, Is.False);
            Assert.That(runtime.Execute("a.intro()").Value.text, Is.EqualTo("09:00"));
            Assert.That(runtime.Execute("a.intro()").Success, Is.False);
            Assert.That(runtime.Execute("a.followup()").Value.text, Is.EqualTo("more"));
            Assert.That(runtime.Execute("b.appointment").Success, Is.False);
            Assert.That(runtime.Entity("b").Schema.FindVariable("appointment"), Is.Null);
            var restored = new KernelRuntime(runtime.Schemas); restored.Restore(runtime.Capture());
            Assert.That(restored.Execute("a.followup()").Value.text, Is.EqualTo("more"));
        }

        [Test]
        public void InstanceOnlyMembersCollectTheirReferencedSchemasAndHiddenInstances()
        {
            var visitor = Asset<EntitySchemaAsset>(); visitor.schemaId = "Visitor"; visitor.builtinParent = "Person";
            var itemType = Asset<EntitySchemaAsset>(); itemType.schemaId = "Key"; itemType.builtinParent = "Item";
            var key = Asset<EntityInstanceAsset>(); key.instanceId = "key"; key.schema = itemType; key.exposeRoot = false;
            var person = Asset<EntityInstanceAsset>(); person.instanceId = "visitor"; person.schema = visitor;
            person.fields = new[] { new FieldDefinition { code = "holding", kind = DataKind.Entity, referenceType = itemType } };
            person.initialValues = new[] { new InitialFieldDefinition { field = "holding", reference = key } };
            person.functions = new[] { new ActionDefinition { code = "getKey", returnInstance = key },
                new ActionDefinition { code = "holdKey", returnField = "holding", fieldWrites = new[] { new FieldWriteDefinition { field = "holding", reference = key } } } };
            var runtime = new KernelRuntime(); EntityAssetCompiler.Install(runtime, null, new[] { person });
            Assert.That(runtime.Execute("visitor.holding.id").Value.text, Is.EqualTo("key"));
            Assert.That(runtime.Execute("visitor.getKey()").Value.schemaId, Is.EqualTo("Key"));
            Assert.That(runtime.Execute("visitor.holdKey()").Value.kind, Is.EqualTo(DataKind.Item));
            Assert.That(runtime.Complete("key"), Is.Empty);
        }

#if UNITY_EDITOR
        [Test]
        public void ShippedScenarioAssetsLoadInheritedMembersAndRoundTripThroughUnityJson()
        {
            var scenario = AssetDatabase.LoadAssetAtPath<KernelScenarioDefinition>("Assets/1_Core/Operations/ClinicScenario.asset");
            Assert.That(scenario, Is.Not.Null);
            KernelRuntime runtime = scenario.CreateRuntime();
            Assert.That(runtime.Entity("trainingArchive").Schema.Lineage().Select(s => s.Id),
                Is.EqualTo(new[] { "Entity", "Place", "Room", "ArchiveRoom" }));
            Assert.That(runtime.Execute("대상 = 연습자료실").Success, Is.True);
            Assert.That(runtime.Execute("대상.메모수정(\"확인 완료\")").Value.text, Is.EqualTo("확인 완료"));
            Assert.That(runtime.Execute("예비자료실.메모읽기()").Value.text, Is.EqualTo("기본 메모"));
            Assert.That(runtime.Execute("대상.연결된방.구역").Value.text, Is.EqualTo("B-01"));
            Assert.That(runtime.Execute("연습자료실.검토완료()").Success, Is.False);
            Assert.That(runtime.Execute("연습자료실.검토시작()").Value.text, Is.EqualTo("검토 중"));
            Assert.That(runtime.Execute("예비자료실.확인상태").Success, Is.False);
            string json = JsonUtility.ToJson(runtime.Capture());
            KernelRuntime restored = scenario.CreateRuntime(); restored.Restore(JsonUtility.FromJson<KernelSnapshot>(json));
            Assert.That(restored.Execute("대상.메모읽기()").Value.text, Is.EqualTo("확인 완료"));
            Assert.That(restored.Execute("연습자료실.검토시작()").Success, Is.False);
            Assert.That(restored.Execute("연습자료실.검토완료()").Value.text, Is.EqualTo("검토 완료"));
        }

        [Test]
        public void UnityJsonPreservesInstanceDefinitionsAndConversationTransitions()
        {
            var runtime = new KernelRuntime(); Entity person = runtime.CreateInstance("Person", "person", "인물");
            person.AddVariable(new VariableDefinition { Code = "personalNote", Kind = DataKind.Text, DefaultValue = Datum.Text("개인 메모") });
            person.AddFunction(new OperationDefinition { Code = "talk", ReturnKind = DataKind.Text, ReturnField = "personalNote",
                MemberChanges = new[] { new MemberToggle { Name = "talk", Enabled = false } } });
            var restored = new KernelRuntime(); restored.Restore(JsonUtility.FromJson<KernelSnapshot>(JsonUtility.ToJson(runtime.Capture())));
            Assert.That(restored.Execute("person.talk()").Value.text, Is.EqualTo("개인 메모"));
            var afterTalk = new KernelRuntime(); afterTalk.Restore(JsonUtility.FromJson<KernelSnapshot>(JsonUtility.ToJson(restored.Capture())));
            Assert.That(afterTalk.Execute("person.talk()").Success, Is.False);
            afterTalk.Entity("person").SetFunctionEnabled("talk", true);
            Assert.That(afterTalk.Execute("person.talk()").Value.text, Is.EqualTo("개인 메모"));
        }

        [Test]
        public void ShippedGovernmentMailStartsTheCaseAndNarrativeStateRoundTripsThroughUnityJson()
        {
            var scenario = AssetDatabase.LoadAssetAtPath<KernelScenarioDefinition>("Assets/1_Core/Operations/ClinicScenario.asset");
            KernelRuntime runtime = scenario.CreateRuntime();
            Assert.That(runtime.AwaitingAssignment, Is.True);
            Assert.That(runtime.Inbox(Mailbox.Kernel).Single().id, Is.EqualTo("government-intro"));
            runtime.ReadMail("government-intro"); runtime.AcceptMail("government-intro");
            runtime.AddConsultationNote("client", "직접 작성한 내용"); runtime.SetDrawerOpen("desk.Drawer_1", true);
            KernelRuntime restored = scenario.CreateRuntime(); restored.Restore(JsonUtility.FromJson<KernelSnapshot>(JsonUtility.ToJson(runtime.Capture())));
            Assert.That(restored.AwaitingAssignment, Is.False);
            Assert.That(restored.NotesFor("client").Single().text, Is.EqualTo("직접 작성한 내용"));
            Assert.That(restored.IsDrawerOpen("desk.Drawer_1"), Is.True);
            Assert.That(restored.Inbox(Mailbox.Clinic).Count(), Is.EqualTo(1));
        }

        [Test]
        public void ShippedNewsUnlocksPermissionsAndStoryStateRoundTripsThroughUnityJson()
        {
            var scenario = AssetDatabase.LoadAssetAtPath<KernelScenarioDefinition>("Assets/1_Core/Operations/ClinicScenario.asset");
            KernelRuntime runtime = scenario.CreateRuntime();
            Assert.That(runtime.TodayNewspaper.articleIds, Does.Contain("research-funding"));
            Assert.That(runtime.Execute("상담손님.재단에대하여()").Success, Is.False);
            runtime.ReadNewspaper(); Assert.That(runtime.Execute("상담손님.재단에대하여()").Success, Is.True);
            runtime.ReadMail("government-intro"); runtime.AcceptMail("government-intro");
            Assert.That(runtime.GetQuest("nsf.introduction").stage, Is.EqualTo(10));
            runtime.Entity("client").SetFunctionAccess("askAboutFoundation", PlayerAccess.Internal);
            var restored = scenario.CreateRuntime(); restored.Restore(JsonUtility.FromJson<KernelSnapshot>(JsonUtility.ToJson(runtime.Capture())));
            Assert.That(restored.TodayNewspaper.read, Is.True); Assert.That(restored.GetTrigger("foundation.mentioned"), Is.True);
            Assert.That(restored.GetQuest("nsf.introduction").stage, Is.EqualTo(10));
            Assert.That(restored.Execute("상담손님.재단에대하여()").Success, Is.False);
            restored.Entity("client").SetFunctionAccess("askAboutFoundation", PlayerAccess.Inherit);
            Assert.That(restored.Execute("상담손님.재단에대하여()").Success, Is.True);
            restored.Facts.Add("testimony_crosschecked"); restored.ReadMail("kernel-followup"); restored.AcceptMail("kernel-followup");
            restored.Tick(540); restored.NextDay();
            Assert.That(restored.NewspaperArticles(2).First().id, Is.EqualTo("investigation-followup"));
        }
#endif
    }
}
