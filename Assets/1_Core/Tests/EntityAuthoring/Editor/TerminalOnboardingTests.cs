using System;
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
        private KernelRuntime World(CommandLocale commands = CommandLocale.Korean, string display = "ko")
        {
            KernelRuntime r = ClinicScenario.Create(); r.Language = commands; r.DisplayLanguage = display; ContentTextTableAsset.Install(r, asset.textTables);
            r.RegisterMail(new StoryMail { id = "government-intro", mailbox = Mailbox.Kernel }); r.RegisterTerminalConversation(asset.conversation);
            r.ReadMail("government-intro"); r.EnterTerminalPage("kernel"); return r;
        }
        [TestCase(CommandLocale.Korean, "ko")]
        [TestCase(CommandLocale.English, "en")]
        [TestCase(CommandLocale.English, "ko")]
        public void AuthoredTutorialTeachesFiveRealCommandsWithoutAdvancingTheWorkday(CommandLocale commands, string display)
        {
            KernelRuntime r = World(commands, display); int sent = 0; int entities = r.World.Objects.Count;
            while (r.TerminalConversationActive && sent < 10)
            {
                r.AdvanceTerminalConversation(1000);
                if (!r.TerminalConversationActive) break;
                string command = r.TerminalExpectedCommand; Assert.That(r.Execute(command).Success, Is.True, command); sent++;
            }
            r.AdvanceTerminalConversation(1000);
            Assert.That(sent, Is.EqualTo(5)); Assert.That(r.TerminalConversationActive, Is.False);
            Assert.That(r.GetTrigger("terminal.onboarding.complete"), Is.True); Assert.That(r.Data.ContainsKey("practice"), Is.False);
            Assert.That(r.World.Objects.Count, Is.EqualTo(entities)); Assert.That(r.Minute, Is.EqualTo(540));
            Assert.That(r.TerminalConversationTranscript(), display == "en" ? Does.Contain("You found Kernel.") : Does.Contain("Kernel을 찾으셨군요."));
        }
        [Test] public void ThirdSuccessfulDetourUsesTheAuthoredAngryReplyAndRecoversToTheSameLesson()
        {
            KernelRuntime r = World(); r.AdvanceTerminalConversation(1000);
            foreach (string command in new[] { "save first", "free first", "save second" })
            { Assert.That(r.Execute(command).Success, Is.True); if (command != "save second") r.AdvanceTerminalConversation(1000); }
            Assert.That(r.TerminalDisturbance, Is.GreaterThan(0));
            r.AdvanceTerminalConversation(1000); Assert.That(r.TerminalConversationTranscript(), Does.Contain("지금은 장난칠 때가 아닙니다."));
            Assert.That(r.ConversationProgress("kernel-onboarding").stepId, Is.EqualTo("help"));
            r.Execute("help"); Assert.That(r.ConversationProgress("kernel-onboarding").stepId, Is.EqualTo("allocate"));
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
