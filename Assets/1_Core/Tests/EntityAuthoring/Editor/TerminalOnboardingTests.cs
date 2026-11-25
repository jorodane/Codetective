using System.Linq;
using Codetective.Gameplay;
using Codetective.Operations;
using NUnit.Framework;
using UnityEngine;

namespace Codetective.Tests.Operations
{
    public sealed class TerminalOnboardingTests
    {
        private TerminalConversationAsset asset;
        [SetUp] public void Setup() => asset = TerminalOnboarding.Create();
        [TearDown] public void Cleanup()
        { foreach (ContentTextTableAsset table in asset.textTables) UnityEngine.Object.DestroyImmediate(table); UnityEngine.Object.DestroyImmediate(asset); }
        internal static void ReadBriefing(KernelRuntime runtime)
        {
            while (runtime.TerminalLineInProgress)
            {
                runtime.AdvanceTerminalConversation(1000);
                if (runtime.TerminalLineInProgress) Assert.That(runtime.ContinueTerminalLine(), Is.True);
            }
        }
        private KernelRuntime World(CommandLocale commands = CommandLocale.Korean, string display = "ko")
        {
            KernelRuntime r = ClinicScenario.Create(); r.Language = commands; r.DisplayLanguage = display; ContentTextTableAsset.Install(r, asset.textTables);
            r.RegisterMail(new StoryMail { id = "government-intro", mailbox = Mailbox.Kernel }); r.RegisterTerminalConversation(asset.conversation);
            r.ReadMail("government-intro"); r.EnterTerminalPage("kernel"); return r;
        }
        [TestCase(CommandLocale.Korean, "ko", "y")]
        [TestCase(CommandLocale.English, "en", "y")]
        [TestCase(CommandLocale.English, "ko", "n")]
        public void AuthoredTutorialTeachesCommandsAndRecordsTheOathWithoutAdvancingTheWorkday(CommandLocale commands, string display, string answer)
        {
            KernelRuntime r = World(commands, display); int sent = 0; int entities = r.World.Objects.Count;
            string[] expected = commands == CommandLocale.Korean
                ? new[] { "도움말", "연습 = 지휘소", "연습.통신확인()", "삭제 연습" }
                : new[] { "help", "practice = hq", "practice.checkComms()", "free practice" };
            while (r.TerminalConversationActive && sent < 10)
            {
                ReadBriefing(r);
                if (!r.TerminalConversationActive) break;
                if (r.TerminalAwaitingAnswer)
                {
                    Assert.That(r.TerminalConversationTranscript(), Does.Contain(display == "en" ? "Answer> [y][n]" : "대답> [y][n]"));
                    Assert.That(r.TerminalAnswerUsage, Is.EqualTo("y / n"));
                    Assert.That(r.Execute(answer).Success, Is.True);
                    continue;
                }
                string command = r.TerminalExpectedCommand;
                Assert.That(sent, Is.LessThan(expected.Length));
                Assert.That(command, Is.EqualTo(expected[sent]));
                if (r.TerminalExpectedGoal == TerminalGoalKind.BindReference)
                {
                    ObjectReferenceText message = r.TerminalConversationContent();
                    ObjectReferenceSpan target = message.Spans.Single();
                    Assert.That(target.Id, Is.EqualTo("hq"));
                    string prefix = command.Substring(0, command.IndexOf('=') + 2);
                    Assert.That(message.Text, Does.Contain(prefix.TrimEnd()));
                    Assert.That(r.ExecuteLinkedInput(prefix + CommandObjectReference.Write(target.Id),
                        prefix + message.Text.Substring(target.Start, target.Length)).Success, Is.True);
                }
                else
                {
                    Assert.That(r.TerminalConversationTranscript(), Does.Contain(command));
                    Assert.That(r.Execute(command).Success, Is.True, command);
                }
                sent++;
            }
            ReadBriefing(r);
            Assert.That(sent, Is.EqualTo(4)); Assert.That(r.TerminalConversationActive, Is.False);
            Assert.That(r.GetTrigger("terminal.onboarding.complete"), Is.True); Assert.That(r.Data.ContainsKey(r.ConversationProgress("kernel-onboarding").slotName), Is.False);
            Assert.That(r.GetTrigger("kernel.oath.accepted"), Is.EqualTo(answer == "y"));
            Assert.That(r.GetTrigger("kernel.oath.declined"), Is.EqualTo(answer == "n"));
            Assert.That(r.World.Objects.Count, Is.EqualTo(entities)); Assert.That(r.Minute, Is.EqualTo(540));
            Assert.That(r.TerminalConversationTranscript(), display == "en" ? Does.Contain("You found Kernel.") : Does.Contain("Kernel을 찾으셨군요."));
        }

        [Test]
        public void AnOldSaveAtTheDeclarationLessonResumesWithTheLinkedAssignment()
        {
            KernelRuntime r = World(CommandLocale.English, "en");
            ReadBriefing(r);
            Assert.That(r.Execute("help").Success, Is.True);
            ReadBriefing(r);
            KernelSnapshot snapshot = r.Capture();
            snapshot.terminalConversations[0].stepId = "allocate";

            KernelRuntime restored = World(CommandLocale.English, "en");
            restored.Restore(snapshot);
            ReadBriefing(restored);
            Assert.That(restored.TerminalExpectedCommand, Is.EqualTo("practice = hq"));
            Assert.That(restored.TerminalConversationTranscript(), Does.Contain("Type practice =, then click headquarters"));
            Assert.That(restored.TerminalConversationContent().Spans.Last().Id, Is.EqualTo("hq"));
            Assert.That(restored.CanUseTerminalKeyword("code"), Is.False);
            Assert.That(restored.CanUseTerminalKeyword("free"), Is.False);
            Assert.That(restored.Execute("hq.checkComms()").Success, Is.False);
            Assert.That(restored.Execute("practice = hq").Success, Is.True);
            Assert.That(restored.ConversationProgress("kernel-onboarding").stepId, Is.EqualTo("invoke"));
        }
        [Test] public void ThirdSuccessfulDetourUsesTheAuthoredAngryReplyAndRecoversToTheSameLesson()
        {
            KernelRuntime r = World(); ReadBriefing(r);
            Assert.That(r.Execute("도움말").Success, Is.True);
            ReadBriefing(r);
            foreach (string name in new[] { "첫번째", "두번째", "세번째" })
            {
                Assert.That(r.ExecuteLinkedInput(name + " = " + CommandObjectReference.Write("hq"), name + " = 지휘소").Success, Is.True);
                if (name != "세번째") ReadBriefing(r);
            }
            r.AdvanceTerminalConversation(0.5);
            Assert.That(r.TerminalDisturbance, Is.GreaterThan(0));
            ReadBriefing(r); Assert.That(r.TerminalConversationTranscript(), Does.Contain("지금은 장난칠 때가 아닙니다."));
            Assert.That(r.ConversationProgress("kernel-onboarding").stepId, Is.EqualTo("reference"));
            Assert.That(r.ExecuteLinkedInput("연습 = " + CommandObjectReference.Write("hq"), "연습 = 지휘소").Success, Is.True);
            Assert.That(r.ConversationProgress("kernel-onboarding").stepId, Is.EqualTo("invoke"));
        }
        [Test] public void ConversationUsesTheScenarioRegistryAndCanBeSavedWithLocalizedPendingLines()
        {
            var scenario = ScriptableObject.CreateInstance<KernelScenarioDefinition>(); var mail = ScriptableObject.CreateInstance<StoryMailAsset>();
            try
            {
                mail.message.id = "government-intro"; mail.message.mailbox = Mailbox.Kernel;
                scenario.mail = new[] { mail }; scenario.terminalConversations = new[] { asset }; KernelRuntime r = scenario.CreateRuntime();
                r.ReadMail("government-intro"); r.EnterTerminalPage("kernel"); r.AdvanceTerminalConversation(0.5);
                KernelRuntime restored = scenario.CreateRuntime(); restored.Restore(r.Capture());
                Assert.That(restored.TerminalConversationTranscript(), Is.EqualTo(r.TerminalConversationTranscript()));
                restored.DisplayLanguage = "en"; restored.AdvanceTerminalConversation(1000);
                Assert.That(restored.TerminalConversationTranscript(), Does.Contain("You found Kernel."));
            }
            finally { UnityEngine.Object.DestroyImmediate(scenario); UnityEngine.Object.DestroyImmediate(mail); }
        }
    }
}
