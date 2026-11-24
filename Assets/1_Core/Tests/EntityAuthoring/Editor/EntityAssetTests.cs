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
        public void ScenarioInstallsDayTitlesAndConditionalBookingsFromAssets()
        {
            var scenario = Asset<KernelScenarioDefinition>(); var day = Asset<ClinicDayPlanAsset>();
            day.plan = new ClinicDayPlan { day = 1, subtitle = "첫 상담", appointments = new[] {
                new PatientAppointment { id = "first", startMinute = 600, dialogue = new[] { new ConsultationLine { sentence = "안녕하세요." } } },
                new PatientAppointment { id = "hidden", startMinute = 660, condition = new StoryCondition { triggersOn = new[] { "invited" } } } } };
            scenario.clinicDays = new[] { day }; KernelRuntime runtime = scenario.CreateRuntime();
            Assert.That(runtime.DaySubtitle, Is.EqualTo("첫 상담")); Assert.That(runtime.DailyAppointments.Count(), Is.EqualTo(1));
            runtime.WaitForNextAppointment(); AppointmentProgress visit = runtime.CurrentAppointment; runtime.AdvanceVisitPresentation(visit.id, visit.phase);
            Assert.That(runtime.CurrentConsultation.sentence, Is.EqualTo("안녕하세요."));
            runtime.SetTrigger("invited", true); Assert.That(runtime.DailyAppointments.Count(), Is.EqualTo(1)); // Today's frozen reservations remain stable.
        }

        [Test]
        public void OlderSavesRetainTheirWorldAndReceiveNewScenarioOwnedProfiles()
        {
            KernelSnapshot old = ClinicScenario.Create().Capture();
            var scenario = Asset<KernelScenarioDefinition>(); var type = Asset<EntitySchemaAsset>(); type.schemaId = "Visitor"; type.builtinParent = "Person";
            var person = Asset<EntityInstanceAsset>(); person.schema = type; person.instanceId = "newVisitor"; person.displayName = "새 예약자"; person.exposeRoot = false;
            person.access.story.triggersOn = new[] { "clinic.met.newVisitor" }; scenario.instances = new[] { person };
            KernelRuntime runtime = scenario.CreateRuntime(); runtime.Restore(old);
            Assert.That(runtime.Entity("newVisitor"), Is.Not.Null); Assert.That(runtime.Entity("client"), Is.Not.Null);
            Assert.That(runtime.VisibleProfileIds, Does.Not.Contain("newVisitor")); runtime.SetTrigger("clinic.met.newVisitor", true);
            Assert.That(runtime.VisibleProfileIds, Does.Contain("newVisitor"));
        }

        [Test]
        public void OrdinaryMailWithAnInlinePlaceholderDoesNotPreventScenarioInitialization()
        {
            var scenario = Asset<KernelScenarioDefinition>(); var message = Asset<StoryMailAsset>();
            message.message = new StoryMail { id = "intro", requiresAcceptance = true, acceptFacts = new[] { "assigned" }, reservation = new ReservationRequest { day = 0 } };
            scenario.mail = new[] { message }; scenario.storyStartFact = "assigned";
            KernelRuntime runtime = scenario.CreateRuntime();
            Assert.That(runtime.AwaitingAssignment, Is.True); Assert.That(runtime.HasClinicSchedule, Is.False);
            runtime.ReadMail("intro"); runtime.AcceptMail("intro"); Assert.That(runtime.AwaitingAssignment, Is.False);
        }

        [Test]
        public void AuthoredInquiryChoicesCreateAReservationOnlyAfterApproval()
        {
            var scenario = Asset<KernelScenarioDefinition>(); var message = Asset<StoryMailAsset>();
            message.message = new StoryMail { id = "request", reservation = new ReservationRequest { day = 2,
                appointment = new PatientAppointment { id = "visit", startMinute = 660, endMinute = 720 } }, choices = new[] {
                new MailChoice { id = "yes", label = "승인", result = MailChoiceResult.Approved, approvesReservation = true },
                new MailChoice { id = "no", label = "거절", result = MailChoiceResult.Declined } } };
            scenario.mail = new[] { message }; KernelRuntime runtime = scenario.CreateRuntime();
            Assert.That(runtime.ScheduleForDay(2), Is.Empty); runtime.ReadMail("request"); runtime.ChooseMail("request", "yes");
            Assert.That(runtime.ScheduleForDay(2).Single().id, Is.EqualTo("mail:request"));
            var restored = scenario.CreateRuntime(); restored.Restore(runtime.Capture());
            Assert.That(restored.SelectedMailChoice("request"), Is.EqualTo("yes")); Assert.That(restored.ScheduleForDay(2).Single().startMinute, Is.EqualTo(660));
        }

        [Test]
        public void AddingAnInquiryDoesNotMoveExistingDayPlanDialogueOffsetsInOldSaves()
        {
            var scenario = Asset<KernelScenarioDefinition>(); var plan = Asset<ClinicDayPlanAsset>();
            plan.plan.appointments = new[] { new PatientAppointment { id = "original", startMinute = 540, dialogue = new[] { new ConsultationLine { sentence = "기존 대사" } } } };
            scenario.clinicDays = new[] { plan }; KernelRuntime old = scenario.CreateRuntime(); old.Tick(1);
            AppointmentProgress before = old.CurrentAppointment; old.AdvanceVisitPresentation(before.id, before.phase); KernelSnapshot saved = old.Capture();
            var message = Asset<StoryMailAsset>(); message.message = new StoryMail { id = "added", reservation = new ReservationRequest { day = 2,
                appointment = new PatientAppointment { id = "newVisit", startMinute = 600, dialogue = new[] { new ConsultationLine { sentence = "새 대사" } } } },
                choices = new[] { new MailChoice { id = "approve", label = "승인", result = MailChoiceResult.Approved, approvesReservation = true } } };
            scenario.mail = new[] { message }; KernelRuntime upgraded = scenario.CreateRuntime(); upgraded.Restore(saved);
            Assert.That(upgraded.CurrentConsultation.sentence, Is.EqualTo("기존 대사")); Assert.That(upgraded.CurrentAppointment.lineStart, Is.EqualTo(before.lineStart));
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

        private static EntityDefinition DeepLegacyDefinition(int depth)
        {
            var node = new EntityDefinition { code = "node" + depth, label = "leaf", actions = new[] {
                new ActionDefinition { code = "check", result = Datum.Text("deep result"), story = new StoryCondition {
                    quests = new[] { new QuestRequirement { id = "deep.case", minimumStage = 20, status = QuestStatus.Active } } } } } };
            for (int i = depth - 1; i >= 0; i--) node = new EntityDefinition { code = "node" + i, label = "branch", children = new[] { node } };
            return node;
        }

        [Test]
        public void DeepAdditionalObjectsRetainActionsAndQuestConditions()
        {
            var scenario = Asset<KernelScenarioDefinition>(); scenario.additionalObjects = new[] { DeepLegacyDefinition(12) };
            KernelRuntime runtime = scenario.CreateRuntime();
            string command = string.Join(".", Enumerable.Range(0, 13).Select(i => "node" + i)) + ".check()";
            Assert.That(runtime.Execute(command).Success, Is.False);
            runtime.SetQuestStage("deep.case", 20, QuestStatus.Active);
            Assert.That(runtime.Execute(command).Value.text, Is.EqualTo("deep result"));
        }

        [Test]
        public void CyclicAdditionalObjectDefinitionsAreRejectedBeforeRecursing()
        {
            var root = new EntityDefinition { code = "cycle", label = "cycle" }; root.children = new[] { root };
            Assert.That(() => EntityAssetCompiler.Install(new KernelRuntime(), null, null, new[] { root }),
                Throws.TypeOf<CommandException>().With.Message.Contains("순환"));
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
        public void DeepAdditionalObjectChildrenRoundTripThroughUnitySerialization()
        {
            var scenario = Asset<KernelScenarioDefinition>(); scenario.additionalObjects = new[] { DeepLegacyDefinition(12) };
            var restored = Asset<KernelScenarioDefinition>();
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(scenario), restored);
            KernelRuntime runtime = restored.CreateRuntime(); runtime.SetQuestStage("deep.case", 20, QuestStatus.Active);
            string command = string.Join(".", Enumerable.Range(0, 13).Select(i => "node" + i)) + ".check()";
            Assert.That(runtime.Execute(command).Value.text, Is.EqualTo("deep result"));
        }

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
            Assert.That(runtime.VisibleProfileIds, Does.Not.Contain("regularVisitor"));
            FinishTutorialAndAcceptFirstMission(runtime);
            runtime.AddConsultationNote("client", "직접 작성한 내용"); runtime.SetDrawerOpen("desk.Drawer_1", true);
            KernelRuntime restored = scenario.CreateRuntime(); restored.Restore(JsonUtility.FromJson<KernelSnapshot>(JsonUtility.ToJson(runtime.Capture())));
            Assert.That(restored.AwaitingAssignment, Is.False);
            Assert.That(restored.NotesFor("client").Single().text, Is.EqualTo("직접 작성한 내용"));
            Assert.That(restored.ProfileCard("regularVisitor").DisplayName, Is.EqualTo("한정우"));
            Assert.That(restored.CompletedConsultationsFor("regularVisitor").Single().visit.title, Is.EqualTo("일상 스트레스 상담"));
            Assert.That(restored.IsDrawerOpen("desk.Drawer_1"), Is.True);
            Assert.That(restored.Inbox(Mailbox.Clinic).Count(), Is.EqualTo(2));
        }

        private static void FinishTutorialAndReceiveFirstMission(KernelRuntime runtime)
        {
            runtime.ReadMail("government-intro"); runtime.EnterTerminalPage("kernel");
            for (int i = 0; i < 16 && runtime.TerminalConversationActive; i++)
            {
                TerminalOnboardingTests.ReadBriefing(runtime);
                if (runtime.TerminalConversationActive)
                    Assert.That(runtime.Execute(runtime.TerminalAwaitingAnswer ? "y" : runtime.TerminalExpectedCommand).Success, Is.True);
            }
            Assert.That(runtime.ConversationProgress("kernel-onboarding").phase, Is.EqualTo(TerminalConversationPhase.Completed));
            runtime.Tick(1);
            AppointmentProgress visit = runtime.CurrentAppointment;
            Assert.That(visit.id, Is.EqualTo("1:regular"));
            Assert.That(visit.episodeId, Is.Not.Empty);
            runtime.AdvanceVisitPresentation(visit.id, visit.phase);
            runtime.Reply(); runtime.ReplyToConsultationChoice(0); runtime.Reply();
            runtime.AdvanceVisitPresentation(visit.id, VisitPhase.Closing);
            runtime.AdvanceVisitPresentation(visit.id, VisitPhase.Departing);
            runtime.Tick(4);
            Assert.That(runtime.Inbox(Mailbox.Kernel).Any(m => m.id == "government-first-mission"), Is.False);
            runtime.Tick(1);
        }

        private static void FinishTutorialAndAcceptFirstMission(KernelRuntime runtime)
        {
            FinishTutorialAndReceiveFirstMission(runtime);
            runtime.ReadMail("government-first-mission"); runtime.AcceptMail("government-first-mission");
        }

        [Test]
        public void FirstCaseStartsAfterApprovalAndReleasesMaggiesReportAsReferencesAfterTheTitle()
        {
            var scenario = AssetDatabase.LoadAssetAtPath<KernelScenarioDefinition>("Assets/1_Core/Operations/ClinicScenario.asset");
            KernelRuntime runtime = scenario.CreateRuntime();
            Assert.That(runtime.IsDiscovered("Maggie"), Is.False);
            FinishTutorialAndReceiveFirstMission(runtime);
            StoryMail mail = runtime.ReadMail("government-first-mission");
            Assert.That(mail.body, Does.Contain("현장 경찰").And.Contain("수사 지휘"));
            Assert.That(mail.pageOnAccept, Is.EqualTo("kernel"));
            runtime.EnterTerminalPage("kernel");
            Assert.That(runtime.ConversationProgress("case01-briefing").phase, Is.EqualTo(TerminalConversationPhase.Dormant));
            runtime.AcceptMail(mail.id);
            runtime.EnterTerminalPage("kernel");
            Assert.That(runtime.TerminalIntroductionTitle, Is.EqualTo("Case.1"));
            Assert.That(runtime.TerminalIntroductionSubtitle, Is.EqualTo("Victor Hale 실종 사건"));
            Assert.That(runtime.TerminalInputLocked, Is.True);
            runtime.AdvanceTerminalConversation(1000);
            Assert.That(runtime.TerminalConversationTranscript(), Is.Empty);
            Assert.That(runtime.IsDiscovered("VictorHale"), Is.False);

            KernelRuntime restored = scenario.CreateRuntime();
            restored.Restore(JsonUtility.FromJson<KernelSnapshot>(JsonUtility.ToJson(runtime.Capture())));
            Assert.That(restored.TerminalIntroductionTitle, Is.EqualTo("Case.1"));
            restored.CompleteTerminalIntroduction("case01-briefing");
            restored.AdvanceTerminalConversation(1000);
            Assert.That(restored.TerminalConversationTranscript(), Does.StartWith("Maggie> 안녕하십니까.").And.Contain("실종 사건의 은닉을 시도하는 것으로 보입니다."));
            Assert.That(restored.TerminalInputLocked, Is.False);
            Assert.That(restored.Evidence.Select(entry => entry.Id), Does.Contain("Maggie").And.Contain("VictorHale").And.Contain("VictorHaleDisappearance")
                .And.Contain("HandsOnBooks").And.Contain("MeridianProfileServices").And.Contain("NsfPublicRelationsOffice").And.Contain("GlobalMindFair"));
            Assert.That(restored.Observations.Single(report => report.statementKey == "case01.maggie.report").kind, Is.EqualTo(InformationKind.Testimony));
            Assert.That(restored.Data, Is.Empty);
            Assert.That(restored.Execute("암호 피해자 = VictorHale").Success, Is.True);
            Assert.That(restored.Execute("암호 사건 = VictorHaleDisappearance").Success, Is.True);
            Assert.That(restored.Execute("암호 주최측 = NsfPublicRelationsOffice").Success, Is.True);
            Assert.That(restored.Execute("매기.대화하기()").Success, Is.True);
            restored.Tick(2);
            Assert.That(restored.IsDiscovered("OfficialAttendance"), Is.True);
            restored.EnterTerminalPage("kernel");
            restored.CompleteTerminalIntroduction("case01-briefing");
            Assert.That(restored.Observations.Count(report => report.statementKey == "case01.maggie.report"), Is.EqualTo(1));
        }

        [Test]
        public void ShippedNewsUnlocksPermissionsAndStoryStateRoundTripsThroughUnityJson()
        {
            var scenario = AssetDatabase.LoadAssetAtPath<KernelScenarioDefinition>("Assets/1_Core/Operations/ClinicScenario.asset");
            KernelRuntime runtime = scenario.CreateRuntime();
            Assert.That(runtime.TodayNewspaper.articleIds, Does.Contain("research-funding"));
            Assert.That(runtime.Execute("상담손님.재단에대하여()").Success, Is.False);
            runtime.ReadNewspaper(); Assert.That(runtime.Execute("상담손님.재단에대하여()").Success, Is.False);
            FinishTutorialAndAcceptFirstMission(runtime);
            Assert.That(runtime.Execute("상담손님.재단에대하여()").Success, Is.True);
            Assert.That(runtime.GetQuest("nsf.introduction").stage, Is.EqualTo(10));
            runtime.Entity("client").SetFunctionAccess("askAboutFoundation", PlayerAccess.Internal);
            var restored = scenario.CreateRuntime(); restored.Restore(JsonUtility.FromJson<KernelSnapshot>(JsonUtility.ToJson(runtime.Capture())));
            Assert.That(restored.TodayNewspaper.read, Is.True); Assert.That(restored.GetTrigger("foundation.mentioned"), Is.True);
            Assert.That(restored.GetQuest("nsf.introduction").stage, Is.EqualTo(10));
            Assert.That(restored.Execute("상담손님.재단에대하여()").Success, Is.False);
            restored.Entity("client").SetFunctionAccess("askAboutFoundation", PlayerAccess.Inherit);
            Assert.That(restored.Execute("상담손님.재단에대하여()").Success, Is.True);
            restored.Facts.Add("testimony_crosschecked");
            Assert.That(restored.Inbox(Mailbox.Kernel).Any(mail => mail.id == "kernel-followup"), Is.False);
        }

        [Test]
        public void ShippedSchedulesAndMidVisitStateRoundTripThroughUnityJson()
        {
            var scenario = AssetDatabase.LoadAssetAtPath<KernelScenarioDefinition>("Assets/1_Core/Operations/ClinicScenario.asset");
            KernelRuntime runtime = scenario.CreateRuntime();
            Assert.That(runtime.DaySubtitle, Is.EqualTo("Kernel 프로젝트의 시작"));
            Assert.That(runtime.DailyAppointments.Select(a => (a.startMinute, a.endMinute)),
                Is.EqualTo(new[] { (540, 600), (600, 660), (780, 900), (900, 1080) }));
            FinishTutorialAndAcceptFirstMission(runtime); runtime.MarkDayIntroSeen();
            runtime.WaitForNextAppointment(); AppointmentProgress visit = runtime.CurrentAppointment; runtime.AdvanceVisitPresentation(visit.id, visit.phase);
            Assert.That(runtime.CurrentConsultation.sentence, Does.Contain("퇴근"));
            Assert.That(runtime.CurrentConsultation.fact, Is.Null.Or.Empty);
            runtime.Tick(120);
            Assert.That(runtime.CurrentAppointment.phase, Is.EqualTo(VisitPhase.Closing));
            var restored = scenario.CreateRuntime(); restored.Restore(JsonUtility.FromJson<KernelSnapshot>(JsonUtility.ToJson(runtime.Capture())));
            Assert.That(restored.CurrentAppointment.phase, Is.EqualTo(VisitPhase.Closing)); Assert.That(restored.Minute, Is.EqualTo(660));
            Assert.That(restored.DayIntroSeen, Is.True);
            while (restored.IsClinicTransitioning) { visit = restored.CurrentAppointment; restored.AdvanceVisitPresentation(visit.id, visit.phase); }
            Assert.That(restored.CurrentAppointment, Is.Null);
            restored.WaitForNextAppointment();
            Assert.That(restored.CurrentAppointment.profileId, Is.EqualTo("careerVisitor"));
            Assert.That(restored.VisibleProfileIds, Does.Contain("careerVisitor"));
            Assert.That(restored.VisibleProfileIds, Does.Not.Contain("careVisitor"));
        }
        [Test]
        public void ShippedInquiryReplyAndCalendarRoundTripThroughUnityJson()
        {
            var scenario = AssetDatabase.LoadAssetAtPath<KernelScenarioDefinition>("Assets/1_Core/Operations/ClinicScenario.asset");
            KernelRuntime runtime = scenario.CreateRuntime(); FinishTutorialAndAcceptFirstMission(runtime);
            Assert.That(runtime.ScheduleForDay(2).Count, Is.EqualTo(3)); runtime.ReadMail("clinic-reservation-followup"); runtime.ChooseMail("clinic-reservation-followup", "approve");
            var restored = scenario.CreateRuntime(); restored.Restore(JsonUtility.FromJson<KernelSnapshot>(JsonUtility.ToJson(runtime.Capture())));
            Assert.That(restored.SelectedMailChoice("clinic-reservation-followup"), Is.EqualTo("approve"));
            Assert.That(restored.ScheduleForDay(2).Select(v => v.startMinute), Is.EqualTo(new[] { 540, 600, 660, 780 }));
            Assert.That(restored.GetTrigger("clinic.request.care_followup.approved"), Is.True);
            restored.ChooseMail("clinic-reservation-followup", "approve"); Assert.That(restored.ScheduleForDay(2).Count, Is.EqualTo(4));
        }
        [Test]
        public void UnityLoadedOrdinaryMailAssetsAreNotReservationInquiries()
        {
            foreach (string name in new[] { "GovernmentAssignment", "GovernmentFollowup", "ClinicAppointment" })
            {
                StoryMailAsset asset = AssetDatabase.LoadAssetAtPath<StoryMailAsset>("Assets/1_Core/Story/Mail/" + name + ".asset");
                Assert.That(asset.message.HasReservation, Is.False, name);
                StoryMail roundTrip = JsonUtility.FromJson<StoryMail>(JsonUtility.ToJson(asset.message));
                Assert.That(() => new KernelRuntime().RegisterMail(roundTrip), Throws.Nothing, name);
            }
            StoryMailAsset inquiry = AssetDatabase.LoadAssetAtPath<StoryMailAsset>("Assets/1_Core/Story/Mail/ClinicReservationInquiry.asset");
            Assert.That(inquiry.message.HasReservation, Is.True);
            Assert.That(() => AssetDatabase.LoadAssetAtPath<KernelScenarioDefinition>("Assets/1_Core/Operations/ClinicScenario.asset").CreateRuntime(), Throws.Nothing);
        }
#endif
    }
}
