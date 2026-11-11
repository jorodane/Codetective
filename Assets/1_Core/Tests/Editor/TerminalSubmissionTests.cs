using NUnit.Framework;

namespace Codetective.Terminal.Tests
{
    public sealed class TerminalSubmissionTests
    {
        [Test]
        public void OneEnterSubmitsTheVisibleKoreanNameWithoutWaitingForAnotherEnter()
        {
            var submit = new TerminalSubmission();
            var composing = new TerminalCompletionInput("save 단", "서", 6, 6);
            submit.Request(1, composing);
            Assert.That(submit.TryTake(composing, out string line), Is.True);
            Assert.That(line, Is.EqualTo("save 단서"));
            var session = new TerminalSession();
            Assert.That(session.Execute(line).Success, Is.True);
            Assert.That(session.Memory[0].Name, Is.EqualTo("단서"));
        }

        [Test]
        public void PhysicalAndUiEnterEventsInTheSameFrameSubmitOnlyOnce()
        {
            var submit = new TerminalSubmission();
            var composing = new TerminalCompletionInput("save 단", "서", 6, 6);
            var committed = new TerminalCompletionInput("save 단서", "", 7, 7);
            submit.Request(5, composing);
            submit.Request(5, committed);
            Assert.That(submit.TryTake(committed, out string line), Is.True);
            Assert.That(line, Is.EqualTo("save 단서"));
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
    }
}
