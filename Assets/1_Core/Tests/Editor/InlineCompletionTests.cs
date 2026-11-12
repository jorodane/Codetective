using System;
using System.Linq;
using Codetective.Operations;
using NUnit.Framework;

namespace Codetective.Terminal.Tests
{
    public sealed class InlineCompletionTests
    {
        private static TerminalCompletionInput Input(string text) => new TerminalCompletionInput(text, "", text.Length, text.Length);

        [TestCase(CompletionKey.Tab)]
        [TestCase(CompletionKey.Enter)]
        [TestCase(CompletionKey.Space)]
        public void EachConfirmingKeyAcceptsThePreviewAndOnlyANewEnterRunsTheCommand(CompletionKey key)
        {
            var session = new TerminalSession();
            session.Execute("save clue");
            TerminalCompletionInput typed = Input("free cl");
            string replacement = session.Complete(typed.Text).Single().ReplacementText;
            var proposal = new InlineCompletion(replacement, replacement.Length);
            Assert.That(InlineCompletion.Resolve(key, typed, proposal, true), Is.EqualTo(CompletionKeyAction.Accept));
            Assert.That(session.Count, Is.EqualTo(1), "Confirming the suggestion does not execute free.");

            var submitted = new TerminalSubmission();
            submitted.Consume(10);
            submitted.Request(10, Input(replacement)); // Physical key and uGUI/IME echo in one frame.
            Assert.That(submitted.TryTake(Input(replacement), out _), Is.False);
            Assert.That(session.Count, Is.EqualTo(1));

            Assert.That(InlineCompletion.Resolve(CompletionKey.Enter, Input(replacement), proposal, true), Is.EqualTo(CompletionKeyAction.Submit));
            submitted.Request(11, Input(replacement));
            Assert.That(submitted.TryTake(Input(replacement), out string line), Is.True);
            Assert.That(session.Execute(line).Success, Is.True);
            Assert.That(session.Count, Is.Zero);
        }

        [Test]
        public void RepeatedPreviewAndWorldClockRefreshesDoNotExpireOrExecuteTheSuggestion()
        {
            KernelRuntime runtime = ClinicScenario.Create();
            var session = new TerminalSession(runtime);
            session.Execute("save clue");
            TerminalCompletionInput typed = Input("free cl");
            for (int i = 0; i < 60; i++)
            {
                runtime.Tick(1);
                string replacement = session.Complete(typed.Text).Single().ReplacementText;
                Assert.That(new InlineCompletion(replacement, replacement.Length).PreviewFrom(0), Is.EqualTo("free clue"));
                Assert.That(typed.Text, Is.EqualTo("free cl"));
                Assert.That(session.Count, Is.EqualTo(1));
            }
        }

        [TestCase(CompletionKey.Tab)]
        [TestCase(CompletionKey.Enter)]
        [TestCase(CompletionKey.Space)]
        public void ComposingKoreanNamesAreCompletedWithoutDuplicatingTheSyllable(CompletionKey key)
        {
            var runtime = new KernelRuntime { Language = CommandLocale.Korean };
            var session = new TerminalSession(runtime);
            session.Execute("저장 단서");
            var composing = new TerminalCompletionInput("삭제 ", "다", 3, 3);
            string replacement = session.Complete(composing.Text, true).Single().ReplacementText;
            var proposal = new InlineCompletion(replacement, replacement.Length);
            Assert.That(proposal.PreviewFrom(0), Is.EqualTo("삭제 단서"));
            Assert.That(InlineCompletion.Resolve(key, composing, proposal, true), Is.EqualTo(CompletionKeyAction.Accept));
            Assert.That(session.Count, Is.EqualTo(1));
            Assert.That(InlineCompletion.Resolve(CompletionKey.Enter, Input(replacement), null, true), Is.EqualTo(CompletionKeyAction.Submit));
            Assert.That(session.Execute(replacement).Success, Is.True);
        }

        [Test]
        public void NoSuggestionPreservesOrdinarySpacesAndConsoleSubmission()
        {
            var input = Input("save custom_name");
            Assert.That(InlineCompletion.Resolve(CompletionKey.Space, input, null, true), Is.EqualTo(CompletionKeyAction.TypeCharacter));
            Assert.That(InlineCompletion.Resolve(CompletionKey.Enter, input, null, true), Is.EqualTo(CompletionKeyAction.Submit));
            Assert.That(InlineCompletion.Resolve(CompletionKey.Tab, input, null, true), Is.EqualTo(CompletionKeyAction.None));
        }

        [Test]
        public void AnAlreadyTypedCandidateDoesNotRequireAnExtraConfirmOrReplaceTheName()
        {
            var session = new TerminalSession();
            session.Execute("save a"); session.Execute("save ab");
            TerminalCompletion[] suggestions = session.Complete("free a").ToArray();
            Assert.That(suggestions.First().ReplacementText, Is.EqualTo("free a"));
            var exact = new InlineCompletion("free a", 6);
            Assert.That(exact.Changes(Input("free a")), Is.False);
            Assert.That(InlineCompletion.Resolve(CompletionKey.Enter, Input("free a"), exact, true), Is.EqualTo(CompletionKeyAction.Submit));
        }

        [Test]
        public void ProtocolCompletionKeepsTheSourceSuffixAndNextEnterCreatesANewline()
        {
            KernelRuntime runtime = ClinicScenario.Create();
            runtime.RecordClue(0);
            const string source = "protocol recon(Place target) {\n    return target.ins\n}";
            int caret = source.IndexOf("target.ins", StringComparison.Ordinal) + "target.ins".Length;
            var input = new TerminalCompletionInput(source, "", caret, caret);
            string replacement = runtime.CompleteProtocol(source, caret, "archive").Single().Replacement;
            int resultCaret = replacement.Length - (source.Length - caret);
            var proposal = new InlineCompletion(replacement, resultCaret);
            Assert.That(InlineCompletion.Resolve(CompletionKey.Enter, input, proposal, false), Is.EqualTo(CompletionKeyAction.Accept));
            Assert.That(proposal.PreviewFrom(source.IndexOf("    return", StringComparison.Ordinal)), Is.EqualTo("    return target.inspect()"));
            Assert.That(replacement.Substring(resultCaret), Is.EqualTo("\n}"));
            var accepted = new TerminalCompletionInput(replacement, "", resultCaret, resultCaret);
            Assert.That(InlineCompletion.Resolve(CompletionKey.Enter, accepted, null, false), Is.EqualTo(CompletionKeyAction.TypeCharacter));
            Assert.That(runtime.Facts, Does.Not.Contain("archive_inspected"));
        }

        [Test]
        public void ScrolledPreviewUsesTheSameVisibleStartAndDoesNotShowTextAfterTheProposedCaret()
        {
            var proposal = new InlineCompletion("free clue", 9);
            Assert.That(proposal.PreviewFrom(5), Is.EqualTo("clue"));
            Assert.That(proposal.PreviewFrom(100), Is.Empty);
            Assert.That(proposal.PreviewFrom(-1), Is.EqualTo("free clue"));
        }

        [Test]
        public void SelectingTextDoesNotAcceptAProposedReplacement()
        {
            var selected = new TerminalCompletionInput("free cl", "", 5, 7);
            Assert.That(new InlineCompletion("free clue", 9).Changes(selected), Is.False);
        }

        [Test]
        public void EmptyProtocolLinesLeaveSpacesAvailableForIndentation()
        {
            var runtime = ClinicScenario.Create();
            const string source = "protocol authored() {\n    \n}";
            int caret = source.IndexOf("    ", StringComparison.Ordinal) + 4;
            Assert.That(runtime.CompleteProtocol(source, caret), Is.Empty);
            Assert.That(InlineCompletion.Resolve(CompletionKey.Space, new TerminalCompletionInput(source, "", caret, caret), null, false),
                Is.EqualTo(CompletionKeyAction.TypeCharacter));
        }
    }
}
