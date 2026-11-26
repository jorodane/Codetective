using System;
using System.Linq;
using Codetective.Operations;
using Codetective.Terminal;
using NUnit.Framework;

namespace Codetective.Tests.Operations
{
    public sealed class TerminalConversationTests
    {
        private static TerminalDialogueLine Line(string text, double speed = 1000) => new TerminalDialogueLine { text = text, charactersPerSecond = speed, pauseAfter = 0 };
        private static TerminalConversationDefinition Definition(TerminalGoalKind goal = TerminalGoalKind.Help) => new TerminalConversationDefinition {
            id = "tutorial", requiredMailRead = "intro", skipIfMailAlreadyReadOnExistingSave = true, speaker = "Operator", steps = new[] { new TerminalConversationStep { id = "first", lines = new[] { Line("Welcome {slot}. Enter {command}.") },
                goal = new TerminalCommandGoal { kind = goal, targetId = "hq", operation = "checkComms" } },
                new TerminalConversationStep { id = "end", lines = new[] { Line("Done.") } } },
            forgivingReplies = new[] { Line("Forgiven once: {command}"), Line("Forgiven twice: {command}") }, angryReplies = new[] { Line("Kernel. Follow {command}.", 2) },
            onComplete = new StoryEffects { triggers = new[] { new TriggerValue { id = "tutorial.complete", value = true } } } };
        private static KernelRuntime World(TerminalConversationDefinition definition = null, bool start = true)
        {
            KernelRuntime r = ClinicScenario.Create(); r.RegisterMail(new StoryMail { id = "intro", mailbox = Mailbox.Kernel, subject = "Assignment", requiresAcceptance = true });
            r.RegisterTerminalConversation(definition ?? Definition());
            if (start) { r.ReadMail("intro"); r.EnterTerminalPage("kernel"); }
            return r;
        }
        private static void Listen(KernelRuntime r) => r.AdvanceTerminalConversation(1000);

        [Test]
        public void IncomingConnectionWaitsForMailConfirmationAndRedialsTheSavedLineAfterDisconnecting()
        {
            TerminalConversationDefinition definition = Definition();
            definition.connectionMode = TerminalConnectionMode.Incoming;
            definition.requiredMailRead = "";
            definition.requiredMailAccepted = "intro";
            definition.replacesStartupGuide = true;
            definition.redialSeconds = 3;
            KernelRuntime runtime = World(definition, false);
            runtime.DeferDayMailUntilIntro();
            Assert.That(runtime.TerminalIncomingConnectionId, Is.Null);
            runtime.MarkDayIntroSeen(5);
            Assert.That(runtime.Inbox(Mailbox.Kernel), Is.Empty);
            Assert.That(runtime.TerminalIncomingConnectionId, Is.Null);
            runtime.AdvanceDayMailDelay(5);
            runtime.ReadMail("intro");
            Assert.That(runtime.TerminalIncomingConnectionId, Is.Null);
            runtime.Tick(5);
            Assert.That(runtime.Minute, Is.EqualTo(540));
            runtime.AcceptMail("intro");
            Assert.That(runtime.TerminalIncomingConnectionId, Is.EqualTo("tutorial"));
            runtime.EnterTerminalPage("kernel");
            Assert.That(runtime.TerminalConversationActive, Is.False);
            Assert.That(runtime.AcceptTerminalConnection("wrong"), Is.False);
            Assert.That(runtime.AcceptTerminalConnection("tutorial"), Is.True);
            Assert.That(runtime.TerminalIncomingConnectionId, Is.Null);

            runtime.AdvanceTerminalConversation(0.006);
            string line = runtime.TerminalConversationTranscript();
            Assert.That(runtime.DisconnectTerminalConversation("tutorial"), Is.True);
            Assert.That(runtime.TerminalLineInProgress, Is.False);
            runtime.AdvanceTerminalConversation(100);
            runtime.AdvanceTerminalRedial(1);
            Assert.That(runtime.TerminalIncomingConnectionId, Is.Null);
            KernelRuntime restored = World(definition, false);
            restored.Restore(runtime.Capture());
            Assert.That(restored.TerminalConversationPausesWorld, Is.True);
            Assert.That(restored.AcceptTerminalConnection("tutorial"), Is.False);
            restored.AdvanceTerminalRedial(2);
            Assert.That(restored.AcceptTerminalConnection("tutorial"), Is.True);
            Assert.That(restored.TerminalConversationTranscript(), Is.EqualTo(line));
            Listen(restored);
            Assert.That(restored.Execute("help").Success, Is.True);
            Listen(restored);
            Assert.That(restored.GetTrigger("tutorial.complete"), Is.True);
            Assert.That(restored.TerminalIncomingConnectionId, Is.Null);
        }

        [Test]
        public void ConsoleTranscriptOmitsTheRepeatedContactAndKeepsSpeakerAndObjectReferences()
        {
            TerminalConversationDefinition definition = Definition();
            TerminalDialogueLine alternate = Line("Contact me later.");
            alternate.speaker = "Secretariat";
            definition.steps[0].lines = new[] { Line("Open [[hq|headquarters]]."), alternate };
            KernelRuntime runtime = World(definition);
            Listen(runtime);
            ExecutionResult result = runtime.Execute("help");
            Listen(runtime);

            TerminalTranscript transcript = runtime.TerminalConversationForConsole();
            Assert.That(transcript.Text, Does.StartWith("Open headquarters.\n\n[Secretariat] Contact me later.")
                .And.Contain("> help\n" + result.Message).And.Not.Contain("[Operator]"));
            Assert.That(runtime.TerminalConversationTranscript(), Does.Contain("[Operator] Open headquarters."));
            Assert.That(transcript.Responses.Single().Start, Is.EqualTo(transcript.Text.IndexOf("> help\n", StringComparison.Ordinal) + "> help\n".Length));
            Assert.That(transcript.Responses.Single().Length, Is.EqualTo(result.Message.Length));
            ObjectReferenceSpan link = transcript.Content.Spans.Single();
            Assert.That(link.Id, Is.EqualTo("hq"));
            Assert.That(transcript.Text.Substring(link.Start, link.Length), Is.EqualTo("headquarters"));
        }

        [Test]
        public void AnOutgoingConversationConnectsOnlyOnRequestAndCanBeResumedWithoutRedial()
        {
            TerminalConversationDefinition definition = Definition();
            definition.connectionMode = TerminalConnectionMode.Outgoing;
            definition.speaker = "[[client|Witness]]";
            definition.contactId = "client";
            KernelRuntime runtime = World(definition, false);
            Assert.That(runtime.TryPlaceTerminalCall("tutorial"), Is.False);
            runtime.ReadMail("intro"); runtime.EnterTerminalPage("kernel");
            Assert.That(runtime.TerminalConversationActive, Is.False);
            Assert.That(runtime.TryPlaceTerminalCall("tutorial"), Is.True);
            TerminalContact contact = runtime.ContactForTerminalConversation("tutorial");
            Assert.That(contact.Name, Is.EqualTo("Witness"));
            Assert.That(contact.ProfileId, Is.EqualTo("client"));
            Assert.That(runtime.DisconnectTerminalConversation("tutorial"), Is.True);
            runtime.AdvanceTerminalRedial(60);
            Assert.That(runtime.TerminalIncomingConnectionId, Is.Null);
            Assert.That(runtime.TerminalReconnectId, Is.EqualTo("tutorial"));
            Assert.That(runtime.ResumeTerminalConnection("tutorial"), Is.True);
            Assert.That(runtime.TerminalConversationActive, Is.True);
        }

        [Test]
        public void ManualDialogueWaitsOnTheCurrentLineAfterTypingAndAfterLoading()
        {
            TerminalConversationDefinition definition = Definition();
            definition.advanceWithEnter = true;
            definition.steps[0].lines = new[] { Line("First message", 2), Line("Second message", 2) };
            KernelRuntime runtime = World(definition);

            Assert.That(runtime.TerminalLineUsesEnter, Is.True);
            Assert.That(runtime.ContinueTerminalLine(), Is.True, "Enter during typing reveals the current line.");
            Assert.That(runtime.TerminalConversationTranscript(), Does.Contain("First message").And.Not.Contain("Second message"));
            runtime.AdvanceTerminalConversation(1000);
            Assert.That(runtime.TerminalLineInProgress, Is.True);
            Assert.That(runtime.Execute("note = 1").Success, Is.True);
            Assert.That(runtime.ConversationProgress("tutorial").deviations, Is.Zero);

            KernelRuntime restored = World(definition, false);
            restored.Restore(runtime.Capture());
            Assert.That(restored.TerminalConversationTranscript(), Is.EqualTo(runtime.TerminalConversationTranscript()));
            Assert.That(restored.ContinueTerminalLine(), Is.True);
            Assert.That(restored.TerminalLineUsesEnter, Is.False);
            restored.AdvanceTerminalConversation(8);
            Assert.That(restored.TerminalConversationTranscript(), Does.Contain("Second message"));
            Assert.That(restored.TerminalLineInProgress, Is.False);
            Assert.That(restored.ContinueTerminalLine(), Is.False);
            Assert.That(restored.Execute("help").Success, Is.True);
        }

        [Test]
        public void ABriefReceptionBetweenLinesSurvivesSavingAndDoesNotBlockCommands()
        {
            TerminalConversationDefinition definition = Definition();
            definition.advanceWithEnter = true;
            definition.steps[0].lines = new[] { Line("First message"), Line("Second message", 10) };
            KernelRuntime runtime = World(definition);
            Listen(runtime);
            Assert.That(runtime.ContinueTerminalLine(), Is.True);
            Assert.That(runtime.TerminalConversationTranscript(), Does.Contain("수신중..").And.Not.Contain("Second message"));
            Assert.That(runtime.Execute("note = 1").Success, Is.True);
            runtime.AdvanceTerminalConversation(0.2);

            KernelRuntime restored = World(definition, false);
            restored.Restore(runtime.Capture());
            restored.DisplayLanguage = "en";
            Assert.That(restored.ContinueTerminalLine(), Is.True);
            restored.AdvanceTerminalConversation(10, visible: false);
            Assert.That(restored.TerminalConversationTranscript(), Does.Contain("Receiving..").And.Not.Contain("Second message"));
            restored.AdvanceTerminalConversation(0.3);
            Assert.That(restored.TerminalConversationTranscript(), Does.Contain("[Operator] ▌").And.Not.Contain("Receiving.."));
            Listen(restored);
            Assert.That(restored.TerminalConversationTranscript(), Does.Contain("Second message").And.Contain("> note = 1"));
        }

        [Test]
        public void SavedReportNotificationsDoNotAppearInTheKernelConversation()
        {
            KernelRuntime runtime = World();
            Assert.That(runtime.Execute("note = \"[보고] 명령으로 얻은 내용\"").Success, Is.True);
            KernelSnapshot snapshot = runtime.Capture();
            snapshot.terminalConversations.Single().log.Add(new TerminalConversationLog { text = "[보고] 메일 확인", complete = true });

            KernelRuntime restored = World(start: false);
            restored.Restore(snapshot);
            Listen(restored);
            Assert.That(restored.TerminalConversationTranscript(), Does.Contain("Welcome").And.Contain("\n\n[보고] 명령으로 얻은 내용").And.Not.Contain("메일 확인"));
        }

        [Test]
        public void AnEarlyTutorialCommandCountsAfterTheAuthoredLinesAreReadEvenAfterLoading()
        {
            TerminalConversationDefinition definition = Definition();
            definition.advanceWithEnter = true;
            definition.steps[0].lines = new[] { Line("First message", 2), Line("Second message", 2) };
            KernelRuntime runtime = World(definition);
            runtime.AdvanceTerminalConversation(1);

            Assert.That(runtime.Execute("help").Success, Is.True);
            Assert.That(runtime.ConversationProgress("tutorial").stepId, Is.EqualTo("first"));
            Assert.That(runtime.TerminalConversationTranscript(), Does.Contain("Fi▌").And.Not.Contain("Second message"));
            KernelRuntime restored = World(definition, false);
            restored.Restore(runtime.Capture());

            Assert.That(restored.ContinueTerminalLine(), Is.True);
            Assert.That(restored.ContinueTerminalLine(), Is.True);
            Listen(restored);
            Assert.That(restored.TerminalConversationTranscript(), Does.Contain("Second message").And.Not.Contain("Done."));
            Assert.That(restored.ContinueTerminalLine(), Is.True);
            Listen(restored);
            Assert.That(restored.TerminalConversationTranscript(), Does.Contain("Done."));
            Assert.That(restored.GetTrigger("tutorial.complete"), Is.True);
        }

        private static TerminalConversationDefinition QuestionDefinition()
        {
            TerminalConversationDefinition definition = Definition();
            definition.steps[0] = new TerminalConversationStep
            {
                id = "question",
                lines = new[] { Line("Will you take the oath?") },
                answers = new[]
                {
                    new TerminalAnswer { key = "y", reply = new[] { Line("Accepted.", 4) },
                        effects = new StoryEffects { triggers = new[] { new TriggerValue { id = "oath.yes", value = true } } } },
                    new TerminalAnswer { key = "n", reply = new[] { Line("Declined.", 4) },
                        effects = new StoryEffects { triggers = new[] { new TriggerValue { id = "oath.no", value = true } } } }
                }
            };
            definition.steps[0].lines[0].speaker = "Secretariat";
            return definition;
        }

        [Test]
        public void ManualDialogueHandsOffToAnswersWithoutAnExtraEnter()
        {
            TerminalConversationDefinition definition = QuestionDefinition();
            definition.advanceWithEnter = true;
            definition.steps[0].lines[0].pauseAfter = 10;
            KernelRuntime runtime = World(definition);

            Assert.That(runtime.TerminalLineInProgress, Is.True);
            Assert.That(runtime.TerminalLineUsesEnter, Is.False);
            runtime.AdvanceTerminalConversation(0.1);
            Assert.That(runtime.TerminalAwaitingAnswer, Is.True);
            Assert.That(runtime.Execute("y").Success, Is.True);

            runtime.AdvanceTerminalConversation(10);
            Assert.That(runtime.TerminalLineUsesEnter, Is.True, "The reply leads into another line of dialogue.");
            Assert.That(runtime.ContinueTerminalLine(), Is.True);
            Listen(runtime);
            Assert.That(runtime.GetTrigger("tutorial.complete"), Is.True);
        }

        [Test]
        public void QuestionsUseTheNormalCommandInputAndNeverChooseAnAnswerFromAnEmptyArgument()
        {
            KernelRuntime r = World(QuestionDefinition());
            var terminal = new TerminalSession(r);
            Assert.That(terminal.Complete("ans"), Is.Empty);
            Assert.That(r.Execute("answer y").Success, Is.False);
            Assert.That(r.Execute("y").Success, Is.False);
            Listen(r);

            Assert.That(r.TerminalConversationTranscript(), Does.Contain("[Secretariat] Will you take the oath?").And.Contain("대답> [y][n]"));
            Assert.That(terminal.Complete("ans").Single().ReplacementText, Is.EqualTo("answer "));
            Assert.That(terminal.Complete("answer "), Is.Empty);
            Assert.That(terminal.Complete("대답 Y"), Is.Empty);
            Assert.That(r.Execute("answer").Success, Is.False);
            Assert.That(r.Execute("answer maybe").Success, Is.False);
            Assert.That(r.Execute("maybe").Success, Is.False);
            Assert.That(r.Execute("answer = 7;").Success, Is.True);
            Assert.That(r.Execute("answer = 9;").Success, Is.True);
            Assert.That(r.Data["answer"].value.number, Is.EqualTo(9));
            Assert.That(r.TerminalAwaitingAnswer, Is.True);
            Assert.That(r.ConversationProgress("tutorial").deviations, Is.Zero);

            Assert.That(r.Execute("n = hq;").Success, Is.True);
            Assert.That(terminal.Complete("n"), Is.Empty, "A reference must not autocomplete over a complete answer.");
            Assert.That(r.Execute("n").Success, Is.True);
            Listen(r);
            Assert.That(r.GetTrigger("oath.no"), Is.True);
            Assert.That(r.Execute("n").Value.id, Is.EqualTo("hq"));
        }

        [TestCase(" Y ", true, "Accepted.")]
        [TestCase("n", false, "Declined.")]
        [TestCase("대답 Y", true, "Accepted.")]
        [TestCase("answer n", false, "Declined.")]
        public void PendingQuestionsAndTheirChosenRepliesResumeFromSaves(string command, bool accepted, string reply)
        {
            TerminalConversationDefinition definition = QuestionDefinition();
            KernelRuntime r = World(definition);
            Listen(r);
            KernelRuntime waiting = World(definition, false);
            waiting.Restore(r.Capture());
            Assert.That(waiting.TerminalAwaitingAnswer, Is.True);
            Assert.That(waiting.TerminalConversationTranscript(), Is.EqualTo(r.TerminalConversationTranscript()));
            Assert.That(waiting.Execute(command).Success, Is.True);
            Assert.That(waiting.TerminalAwaitingAnswer, Is.False);
            Assert.That(waiting.TerminalLineInProgress, Is.True);

            waiting.AdvanceTerminalConversation(0.5);
            KernelRuntime responding = World(definition, false);
            responding.Restore(waiting.Capture());
            Assert.That(responding.TerminalConversationTranscript(), Is.EqualTo(waiting.TerminalConversationTranscript()));
            Assert.That(responding.Execute("answer n").Success, Is.False);
            Listen(responding);
            Assert.That(responding.TerminalConversationTranscript(), Does.Contain(reply));
            Assert.That(responding.GetTrigger("oath.yes"), Is.EqualTo(accepted));
            Assert.That(responding.GetTrigger("oath.no"), Is.EqualTo(!accepted));
            Assert.That(responding.GetTrigger("tutorial.complete"), Is.True);
            Assert.That(responding.Execute("answer y").Success, Is.False);
        }
        [Test] public void ReadingMailAndReturningToKernelStartsTheConversationOnce()
        {
            KernelRuntime r = World(start: false); r.EnterTerminalPage("kernel"); Assert.That(r.TerminalConversationActive, Is.False);
            r.ReadMail("intro"); r.EnterTerminalPage("mail"); Assert.That(r.TerminalConversationActive, Is.False);
            r.EnterTerminalPage("kernel"); Assert.That(r.TerminalLineInProgress, Is.True);
            Listen(r); Assert.That(r.Execute("help").Success, Is.True); Listen(r);
            Assert.That(r.TerminalConversationActive, Is.False); Assert.That(r.GetTrigger("tutorial.complete"), Is.True);
            string log = r.TerminalConversationTranscript(); r.EnterTerminalPage("kernel"); Assert.That(r.TerminalConversationTranscript(), Is.EqualTo(log));
        }
        [Test] public void TypingRevealsWholeTextElementsWhileCommandsRemainAvailable()
        {
            var definition = Definition(); definition.steps[0].lines = new[] { Line("가😀a\u0301", 1) };
            KernelRuntime r = World(definition); r.AdvanceTerminalConversation(1);
            Assert.That(r.TerminalConversationTranscript(), Does.Contain("가▌").And.Not.Contain("😀"));
            Assert.That(r.TerminalInputLocked, Is.False);
            Assert.That(r.Execute("note = 1").Success, Is.True); Assert.That(r.Data.ContainsKey("note"), Is.True);
            r.AdvanceTerminalConversation(1); Assert.That(r.TerminalConversationTranscript(), Does.Contain("가😀▌"));
            r.AdvanceTerminalConversation(1); Assert.That(r.TerminalLineInProgress, Is.False); Assert.That(r.TerminalConversationTranscript(), Does.Contain("가😀a\u0301"));
        }
        [Test] public void HiddenPagesAndPausedTicksDoNotConsumeMessages()
        {
            var definition = Definition(); definition.steps[0].lines = new[] { Line("A long message", 1) };
            KernelRuntime r = World(definition); r.AdvanceTerminalConversation(2); string before = r.TerminalConversationTranscript();
            r.AdvanceTerminalConversation(100, false); r.AdvanceTerminalConversation(0);
            Assert.That(r.TerminalConversationTranscript(), Is.EqualTo(before)); Assert.That(r.TerminalLineInProgress, Is.True);
        }
        [Test] public void OnlySuccessfulOffScriptActionsCountAndFirstTwoAreForgiven()
        {
            KernelRuntime r = World(); Listen(r);
            Assert.That(r.Execute("side = 1").Success, Is.True); Assert.That(r.Data.ContainsKey("side"), Is.True);
            Assert.That(r.TerminalConversationTranscript(), Does.Contain("수신중..")); Assert.That(r.TerminalDisturbance, Is.Zero);
            Listen(r); Assert.That(r.TerminalConversationTranscript(), Does.Contain("Forgiven once"));
            r.Execute("free side"); Assert.That(r.TerminalDisturbance, Is.Zero); Listen(r);
            Assert.That(r.TerminalConversationTranscript(), Does.Contain("Forgiven twice"));
            r.Execute("other = 1"); Assert.That(r.TerminalDisturbance, Is.Zero);
            Assert.That(r.ConversationProgress("tutorial").deviations, Is.EqualTo(3));
            r.AdvanceTerminalConversation(0.45); Assert.That(r.TerminalDisturbance, Is.EqualTo(1));
            r.AdvanceTerminalConversation(0.6); Assert.That(r.TerminalDisturbance, Is.Zero); Assert.That(r.TerminalLineInProgress, Is.True);
            Listen(r); Assert.That(r.TerminalConversationTranscript(), Does.Contain("Kernel. Follow"));
            Assert.That(r.ConversationProgress("tutorial").stepId, Is.EqualTo("first"));
        }
        [Test] public void ErrorsAndHelpRequestsDoNotAngerTheOperator()
        {
            KernelRuntime r = World(Definition(TerminalGoalKind.Invoke)); Listen(r);
            Assert.That(r.Execute("nonsense.notHere()").Success, Is.False);
            Assert.That(r.Execute("hq.checkComms(1)").Success, Is.False);
            Assert.That(r.Execute("help").Success, Is.True);
            Assert.That(r.ConversationProgress("tutorial").deviations, Is.Zero); Assert.That(r.TerminalLineInProgress, Is.False);
        }
        [Test] public void EquivalentAliasesAndReferenceReceiversSatisfyTheActualActionGoal()
        {
            KernelRuntime r = World(Definition(TerminalGoalKind.Invoke)); Listen(r);
            Assert.That(r.Execute("지휘소.통신확인();").Success, Is.True); Listen(r); Assert.That(r.GetTrigger("tutorial.complete"), Is.True);
            KernelRuntime second = World(Definition(TerminalGoalKind.Invoke), false); second.Store("receiver", second.Reference(second.Entity("hq")));
            second.ReadMail("intro"); second.EnterTerminalPage("kernel"); Listen(second);
            Assert.That(second.Execute("receiver.checkComms()").Success, Is.True); Listen(second); Assert.That(second.GetTrigger("tutorial.complete"), Is.True);
        }
        [Test] public void TutorialSlotNamesNeverOverwriteExistingPlayerDataOrProtocols()
        {
            var definition = Definition(TerminalGoalKind.BindReference); KernelRuntime r = World(definition, false);
            r.Store("practice", Datum.Number(9)); r.SaveProtocol("protocol practice2() { return 1; }");
            r.ReadMail("intro"); r.EnterTerminalPage("kernel"); Listen(r);
            Assert.That(r.ConversationProgress("tutorial").slotName, Is.EqualTo("practice3"));
            Assert.That(r.Execute("practice3 = hq").Success, Is.True); Listen(r);
            Assert.That(r.Data["practice"].value.number, Is.EqualTo(9)); Assert.That(r.GetTrigger("tutorial.complete"), Is.True);
        }
        [Test] public void ReferenceAndReleaseGoalsDoNotCreateOrDestroyWorldObjects()
        {
            var d = Definition(TerminalGoalKind.BindReference); d.steps[1].goal = new TerminalCommandGoal { kind = TerminalGoalKind.Free };
            KernelRuntime r = World(d); Listen(r); int count = r.World.Objects.Count;
            Assert.That(r.Execute("practice = 지휘소;").Success, Is.True); Listen(r);
            Assert.That(r.TerminalExpectedCommand, Is.EqualTo("free practice"));
            Assert.That(r.Execute("삭제 practice").Success, Is.True);
            Assert.That(r.GetTrigger("tutorial.complete"), Is.True); Assert.That(r.World.Objects.Count, Is.EqualTo(count)); Assert.That(r.Entity("hq"), Is.Not.Null);
        }
        [Test] public void EarlyValidWorkIsRecognizedWhenItsLessonIsReached()
        {
            var d = Definition(); d.steps[1].goal = new TerminalCommandGoal { kind = TerminalGoalKind.BindReference, targetId = "hq" };
            KernelRuntime r = World(d); Listen(r); r.Execute("practice = hq"); Listen(r); r.Execute("help"); Listen(r);
            Assert.That(r.GetTrigger("tutorial.complete"), Is.True); Assert.That(r.Data.ContainsKey("practice"), Is.True);
        }
        [Test] public void WorldClockFreezesUntilTheConversationCompletes()
        {
            KernelRuntime r = World(); r.Tick(30); Assert.That(r.Minute, Is.EqualTo(540));
            Listen(r); r.Tick(30); Assert.That(r.Minute, Is.EqualTo(540));
            r.Execute("help"); Listen(r); r.Tick(1); Assert.That(r.Minute, Is.EqualTo(541));
        }
        [Test] public void GuidedStartupWaitsForKernelWithoutLettingTheFirstAppointmentClockAdvance()
        {
            var d = Definition(); d.replacesStartupGuide = true; KernelRuntime r = World(d, false);
            r.ReadMail("intro"); Assert.That(r.TerminalConversationActive, Is.False); Assert.That(r.TerminalConversationPausesWorld, Is.True);
            r.Tick(15); Assert.That(r.Minute, Is.EqualTo(540));
            r.EnterTerminalPage("kernel"); Listen(r); r.Execute("help"); Listen(r); r.Tick(1); Assert.That(r.Minute, Is.EqualTo(541));
        }
        [Test] public void SavingMidLinePreservesTextPositionDeviationCountAndPendingReply()
        {
            KernelRuntime r = World(); Listen(r); r.Execute("a = 1"); Listen(r); r.Execute("free a"); Listen(r); r.Execute("a = 1"); r.AdvanceTerminalConversation(0.2);
            string before = r.TerminalConversationTranscript(); KernelSnapshot saved = r.Capture();
            KernelRuntime restored = World(start: false); restored.Restore(saved);
            Assert.That(restored.TerminalConversationTranscript(), Is.EqualTo(before)); Assert.That(restored.TerminalLineInProgress, Is.True);
            Assert.That(restored.ConversationProgress("tutorial").deviations, Is.EqualTo(3));
            Assert.That(restored.TerminalDisturbance, Is.EqualTo(r.TerminalDisturbance).Within(0.00001));
            Listen(restored); restored.Execute("help"); Listen(restored); Assert.That(restored.GetTrigger("tutorial.complete"), Is.True);
        }
        [Test] public void SnapshotDialogueBuffersAreIndependentCopies()
        {
            KernelRuntime r = World(); KernelSnapshot snapshot = r.Capture(); snapshot.terminalConversations[0].pending[0].text = "changed";
            snapshot.terminalConversations[0].log[0].text = "changed"; Listen(r);
            Assert.That(r.TerminalConversationTranscript(), Does.Contain("Welcome").And.Not.Contain("changed"));
        }
        [Test] public void ExistingSavesThatAlreadyReadTheMailDoNotReceiveANewForcedTutorial()
        {
            KernelRuntime r = World(start: false); r.ReadMail("intro"); KernelSnapshot old = r.Capture(); old.terminalConversations = null;
            KernelRuntime loaded = World(start: false); loaded.Restore(old); loaded.EnterTerminalPage("kernel");
            Assert.That(loaded.TerminalConversationActive, Is.False); Assert.That(loaded.TerminalInputLocked, Is.False);
        }
        [Test] public void NewSavesFromBeforeTheFirstMailRemainEligibleAfterLoading()
        {
            KernelRuntime r = World(start: false); KernelSnapshot saved = r.Capture();
            KernelRuntime loaded = World(start: false); loaded.Restore(saved); loaded.ReadMail("intro"); loaded.EnterTerminalPage("kernel");
            Assert.That(loaded.TerminalLineInProgress, Is.True);
        }
        [Test] public void StoryTriggeredGoalsUseTheSameQuestAndTriggerState()
        {
            var d = Definition(TerminalGoalKind.StoryCondition); d.steps[0].goal.condition.triggersOn = new[] { "ready" };
            KernelRuntime r = World(d); Listen(r); Assert.That(r.TerminalConversationActive, Is.True);
            r.SetTrigger("ready", true); r.AdvanceTerminalConversation(100);
            Assert.That(r.GetTrigger("tutorial.complete"), Is.True);
        }
        [Test] public void LongSessionsKeepABoundedJournalWithoutLosingTheActiveLine()
        {
            KernelRuntime r = World(); Listen(r);
            for (int i = 0; i < 150; i++) { Assert.That(r.Execute("1").Success, Is.True); Listen(r); }
            Assert.That(r.ConversationProgress("tutorial").log.Count, Is.LessThanOrEqualTo(128));
            Assert.That(r.ConversationProgress("tutorial").deviations, Is.EqualTo(150));
            r.Execute("help"); Listen(r); Assert.That(r.GetTrigger("tutorial.complete"), Is.True);
        }
        [Test] public void TranslatingDialogueDoesNotTranslateThePlayersReferenceName()
        {
            var d = Definition(); d.steps[0].lines[0].textKey = "welcome";
            KernelRuntime r = World(d, false); r.Language = CommandLocale.Korean; r.DisplayLanguage = "en";
            r.Texts.Install(new[] { new TextTranslation { key = "welcome", language = "en", text = "Use {command} with {slot}." } });
            r.ReadMail("intro"); r.EnterTerminalPage("kernel"); Listen(r);
            Assert.That(r.TerminalConversationTranscript(), Does.Contain("Use 도움말 with practice."));
            Assert.That(r.Data, Is.Empty);
        }
        [Test] public void InvalidDefinitionsAndDuplicateIdsAreRejected()
        {
            var bad = Definition(); bad.steps[0].lines[0].charactersPerSecond = 0;
            Assert.That(() => World(bad), Throws.TypeOf<CommandException>());
            bad = Definition(); bad.steps[1].id = bad.steps[0].id; Assert.That(() => World(bad), Throws.TypeOf<CommandException>());
            KernelRuntime r = World(); Assert.That(() => r.RegisterTerminalConversation(Definition()), Throws.TypeOf<CommandException>());
        }
    }
}
