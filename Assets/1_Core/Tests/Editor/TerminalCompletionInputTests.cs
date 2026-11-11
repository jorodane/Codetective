using System.Linq;
using NUnit.Framework;

namespace Codetective.Terminal.Tests
{
    public sealed class TerminalCompletionInputTests
    {
        [TestCase("s", "save")]
        [TestCase("sa", "save")]
        [TestCase("f", "free")]
        [TestCase("fr", "free")]
        [TestCase("h", "help")]
        public void CommandPrefixesAreLanguageIndependent(string composition, string expected)
        {
            var input = new TerminalCompletionInput("", composition, 0, 0);
            Assert.That(input.IsAtEnd, Is.True);
            Assert.That(new TerminalSession().Complete(input.Text, input.IsComposing).Single().DisplayText,
                Is.EqualTo(expected));
        }

        [Test]
        public void SameLengthKoreanCompositionChangesRemainObservableWithoutMovingTheCaret()
        {
            var session = new TerminalSession();
            session.Execute("save 단서");
            var first = new TerminalCompletionInput("free ", "ㄷ", 5, 5);
            var second = new TerminalCompletionInput("free ", "다", 5, 5);
            var third = new TerminalCompletionInput("free ", "단", 5, 5);
            Assert.That(first.IsComposing, Is.EqualTo(second.IsComposing));
            Assert.That(first.Caret, Is.EqualTo(second.Caret));
            Assert.That(first.Text, Is.Not.EqualTo(second.Text));
            Assert.That(second.Text, Is.Not.EqualTo(third.Text));
            Assert.That(session.Complete(third.Text, third.IsComposing).Single().DisplayText, Is.EqualTo("단서"));
        }

        [TestCase("", "ㄷ")]
        [TestCase("", "다")]
        [TestCase("", "단")]
        [TestCase("단", "ㅅ")]
        [TestCase("단", "서")]
        public void FreeSuggestsAllocatedNamesWhileTheirKoreanPrefixIsComposing(string committedName, string composition)
        {
            var session = new TerminalSession();
            session.Execute("save 단서");
            session.Execute("save 증거");
            string committed = "free " + committedName;
            var input = new TerminalCompletionInput(committed, composition, committed.Length, committed.Length);
            Assert.That(input.IsAtEnd, Is.True);
            Assert.That(session.Complete(input.Text, input.IsComposing).Single().ReplacementText, Is.EqualTo("free 단서"));
            session.Execute("free 단서");
            Assert.That(session.Complete(input.Text, input.IsComposing), Is.Empty);
        }

        [Test]
        public void InsertingCompositionInTheMiddlePreservesTheSuffixAndDoesNotCompleteAtTheEnd()
        {
            var input = new TerminalCompletionInput("free 단서", "증", 5, 5);
            Assert.That(input.Text, Is.EqualTo("free 증단서"));
            Assert.That(input.Caret, Is.EqualTo(6));
            Assert.That(input.IsAtEnd, Is.False);
        }

        [Test]
        public void ASelectionIsNotMistakenForACaretAtTheEnd()
        {
            Assert.That(new TerminalCompletionInput("free 단서", "증", 5, 7).IsAtEnd, Is.False);
        }

        [Test]
        public void CommittingCompositionDoesNotDuplicateTheVisibleText()
        {
            var composing = new TerminalCompletionInput("save 단", "서", 6, 6);
            var committed = new TerminalCompletionInput("save 단서", "", 7, 7);
            Assert.That(composing.Text, Is.EqualTo(committed.Text));
            Assert.That(composing.Caret, Is.EqualTo(committed.Caret));
            Assert.That(composing.IsComposing, Is.True);
            Assert.That(committed.IsComposing, Is.False);
        }

        [Test]
        public void PartialSyllableMatchingIsLimitedToActiveComposition()
        {
            var session = new TerminalSession();
            session.Execute("save 단서");
            Assert.That(session.Complete("free 다"), Is.Empty);
            Assert.That(session.Complete("free 다", true).Single().DisplayText, Is.EqualTo("단서"));
            Assert.That(session.Execute("free 다").Success, Is.False);
            Assert.That(session.Count, Is.EqualTo(1));
        }
    }
}
