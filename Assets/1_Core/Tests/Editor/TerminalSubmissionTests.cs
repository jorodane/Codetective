using NUnit.Framework;

namespace Codetective.Terminal.Tests
{
    public sealed class TerminalSubmissionTests
    {
        [Test]
        public void OneEnterSubmitsTheVisibleKoreanNameWithoutWaitingForAnotherEnter()
        {
            var submit = new TerminalSubmission();
            var composing = new TerminalCompletionInput("free 단", "서", 6, 6);
            submit.Request(1, composing);
            Assert.That(submit.TryTake(composing, out string line), Is.True);
            Assert.That(line, Is.EqualTo("free 단서"));
            var session = new TerminalSession();
            session.Execute("단서 = 1");
            Assert.That(session.Execute(line).Success, Is.True);
            Assert.That(session.Memory, Is.Empty);
        }

        [Test]
        public void PhysicalAndUiEnterEventsInTheSameFrameSubmitOnlyOnce()
        {
            var submit = new TerminalSubmission();
            var composing = new TerminalCompletionInput("save 단", "서", 6, 6);
            var committed = new TerminalCompletionInput("save 단서", "", 7, 7);
            submit.Request(5, composing);
            submit.Request(5, committed);
            Assert.That(submit.TryTake(committed, out string line, out bool hadComposition), Is.True);
            Assert.That(line, Is.EqualTo("save 단서"));
            Assert.That(hadComposition, Is.True, "IME cleanup is still needed after the backend commits the syllable.");
            submit.Request(5, committed);
            Assert.That(submit.TryTake(committed, out _), Is.False);
        }

        [Test]
        public void CommitCharactersDeliveredAfterEnterAreIncluded()
        {
            var submit = new TerminalSubmission();
            submit.Request(1, new TerminalCompletionInput("save 단", "", 6, 6));
            Assert.That(submit.TryTake(new TerminalCompletionInput("save 단서", "", 7, 7), out string line), Is.True);
            Assert.That(line, Is.EqualTo("save 단서"));
        }

        [Test]
        public void OrdinarySubmissionDoesNotRequireEndingTheInputSession()
        {
            var submit = new TerminalSubmission();
            var input = new TerminalCompletionInput("help", "", 4, 4);
            submit.Request(1, input);
            Assert.That(submit.TryTake(input, out string line, out bool hadComposition), Is.True);
            Assert.That(line, Is.EqualTo("help"));
            Assert.That(hadComposition, Is.False);
        }

        [Test]
        public void CompositionSnapshotIsNotAppendedTwiceWhenTheOsAlsoCommitsIt()
        {
            var submit = new TerminalSubmission();
            submit.Request(1, new TerminalCompletionInput("save 단", "서", 6, 6));
            Assert.That(submit.TryTake(new TerminalCompletionInput("save 단서", "서", 7, 7), out string line), Is.True);
            Assert.That(line, Is.EqualTo("save 단서"));
        }

        [Test]
        public void LeavingTheTerminalCancelsPendingSubmissionAndEmptyEnterDoesNothing()
        {
            var submit = new TerminalSubmission();
            var input = new TerminalCompletionInput("save clue", "", 9, 9);
            submit.Request(1, input);
            submit.Cancel();
            Assert.That(submit.TryTake(input, out _), Is.False);
            var empty = new TerminalCompletionInput("", "", 0, 0);
            submit.Request(2, empty);
            Assert.That(submit.TryTake(empty, out _), Is.False);
            submit.Request(3, input);
            Assert.That(submit.TryTake(input, out _), Is.True);
        }

        [Test]
        public void DialogueTakesOnlyAnActuallyEmptyDraftAndStillCoalescesEnterEvents()
        {
            var submit = new TerminalSubmission();
            var empty = new TerminalCompletionInput("", "", 0, 0);
            submit.Request(1, empty);
            Assert.That(submit.TryTake(empty, out string line, out _, allowEmpty: true), Is.True);
            Assert.That(line, Is.Empty);
            submit.Request(1, empty);
            Assert.That(submit.TryTake(empty, out _, out _, allowEmpty: true), Is.False);

            var space = new TerminalCompletionInput(" ", "", 1, 1);
            submit.Request(2, space);
            Assert.That(submit.TryTake(space, out _, out _, allowEmpty: true), Is.False);
            submit.Request(3, empty);
            Assert.That(submit.TryTake(new TerminalCompletionInput("도움말", "", 3, 3), out line, out _, allowEmpty: true), Is.True);
            Assert.That(line, Is.EqualTo("도움말"));
        }
    }
}
