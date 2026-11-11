using System.Linq;
using NUnit.Framework;

namespace Codetective.Terminal.Tests
{
    public sealed class TerminalCompletionInputTests
    {
        [TestCase("ㅅ", "삭제")]
        [TestCase("사", "삭제")]
        [TestCase("삭", "삭제")]
        [TestCase("ㅈ", "저장")]
        [TestCase("저", "저장")]
        public void FirstSyllableSuggestsCommandsBeforeItIsCommitted(string composition, string expected)
        {
            var input = new TerminalCompletionInput("", composition, 0, 0);
            var session = new TerminalSession();
            Assert.That(input.IsAtEnd, Is.True);
            Assert.That(input.IsComposing, Is.True);
            Assert.That(session.Complete(input.Text, input.IsComposing).Single().DisplayText, Is.EqualTo(expected));
        }

        [TestCase("ㅈ")]
        [TestCase("자")]
        [TestCase("장")]
        public void CommandCompletionIncludesTheFinalComposingSyllable(string composition)
        {
            var input = new TerminalCompletionInput("저", composition, 1, 1);
            Assert.That(input.Text, Is.EqualTo("저" + composition));
            Assert.That(input.Caret, Is.EqualTo(input.Text.Length));
            Assert.That(input.Anchor, Is.EqualTo(input.Text.Length));
            Assert.That(input.IsAtEnd, Is.True);
            Assert.That(new TerminalSession().Complete(input.Text, input.IsComposing).Single().ReplacementText,
                Is.EqualTo("저장 "));
        }

        [Test]
        public void SameLengthCompositionChangesRemainObservableWithoutMovingTheCaret()
        {
            var first = new TerminalCompletionInput("", "ㅅ", 0, 0);
            var second = new TerminalCompletionInput("", "사", 0, 0);
            var third = new TerminalCompletionInput("", "삭", 0, 0);
            Assert.That(first.IsComposing, Is.EqualTo(second.IsComposing));
            Assert.That(first.Caret, Is.EqualTo(second.Caret));
            Assert.That(first.Text, Is.Not.EqualTo(second.Text));
            Assert.That(second.Text, Is.Not.EqualTo(third.Text));
            Assert.That(new TerminalSession().Complete(third.Text, third.IsComposing).Single().DisplayText,
                Is.EqualTo("삭제"));
        }

        [TestCase("", "ㄷ")]
        [TestCase("", "다")]
        [TestCase("", "단")]
        [TestCase("단", "ㅅ")]
        [TestCase("단", "서")]
        public void DeleteSuggestsAllocatedNamesWhileTheirKoreanPrefixIsComposing(string committedName, string composition)
        {
            var session = new TerminalSession();
            session.Execute("저장 단서");
            session.Execute("저장 증거");
            string committed = "삭제 " + committedName;
            var input = new TerminalCompletionInput(committed, composition, committed.Length, committed.Length);
            Assert.That(input.IsAtEnd, Is.True);
            Assert.That(session.Complete(input.Text, input.IsComposing).Single().ReplacementText,
                Is.EqualTo("삭제 단서"));
            session.Execute("삭제 단서");
            Assert.That(session.Complete(input.Text, input.IsComposing), Is.Empty);
        }

        [Test]
        public void InsertingCompositionInTheMiddlePreservesTheSuffixAndDoesNotCompleteAtTheEnd()
        {
            var input = new TerminalCompletionInput("삭제 단서", "증", 3, 3);
            Assert.That(input.Text, Is.EqualTo("삭제 증단서"));
            Assert.That(input.Caret, Is.EqualTo(4));
            Assert.That(input.IsAtEnd, Is.False);
        }

        [Test]
        public void ASelectionIsNotMistakenForACaretAtTheEnd()
        {
            var input = new TerminalCompletionInput("삭제 단서", "증", 3, 5);
            Assert.That(input.IsAtEnd, Is.False);
        }

        [Test]
        public void CommittingCompositionDoesNotDuplicateTheVisibleText()
        {
            var composing = new TerminalCompletionInput("저", "장", 1, 1);
            var committed = new TerminalCompletionInput("저장", "", 2, 2);
            Assert.That(composing.Text, Is.EqualTo(committed.Text));
            Assert.That(composing.Caret, Is.EqualTo(committed.Caret));
            Assert.That(composing.IsComposing, Is.True);
            Assert.That(committed.IsComposing, Is.False);
        }

        [Test]
        public void PartialSyllableMatchingIsLimitedToActiveComposition()
        {
            var session = new TerminalSession();
            session.Execute("저장 단서");
            Assert.That(session.Complete("사"), Is.Empty);
            Assert.That(session.Complete("저자"), Is.Empty);
            Assert.That(session.Complete("삭제 다"), Is.Empty);
            Assert.That(session.Complete("삭제 다", true).Single().DisplayText, Is.EqualTo("단서"));
            Assert.That(session.Execute("삭제 다").Success, Is.False);
            Assert.That(session.Count, Is.EqualTo(1));
        }
    }
}
