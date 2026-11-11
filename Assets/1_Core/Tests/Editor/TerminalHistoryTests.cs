using NUnit.Framework;

namespace Codetective.Terminal.Tests
{
    public sealed class TerminalHistoryTests
    {
        [Test]
        public void ScrollbackKeepsChronologicalEntriesAndReportsTheExpiredPrefix()
        {
            var history = new TerminalHistory(2, 100);
            history.Append("first");
            history.Append("second");
            Assert.That(history.Append("third"), Is.EqualTo("first\n\n"));
            Assert.That(history.Text, Is.EqualTo("second\n\nthird"));
        }

        [Test]
        public void ManyLongCommandsStayBelowTheTextMeshCharacterBudget()
        {
            var history = new TerminalHistory();
            for (int i = 0; i < 1000; i++)
                history.Append("> save name_" + i + "\n" + new string('한', 200));
            Assert.That(history.Count, Is.LessThanOrEqualTo(100));
            Assert.That(history.Text.Length, Is.LessThanOrEqualTo(12000));
            Assert.That(history.Text, Does.Contain("name_999"));
            Assert.That(history.Text, Does.Not.Contain("name_0\n"));
        }

        [Test]
        public void ATooLargeEntryCannotOverflowTheCharacterBudget()
        {
            var history = new TerminalHistory(10, 40);
            history.Append("old");
            history.Append(new string('x', 100));
            Assert.That(history.Text.Length, Is.EqualTo(40));
            Assert.That(history.Count, Is.EqualTo(1));
        }

        [TestCase(100f, 400f, 0f, true)]
        [TestCase(1000f, 400f, 600f, true)]
        [TestCase(1000f, 400f, 590f, true)]
        [TestCase(1000f, 400f, 200f, false)]
        [TestCase(1000f, 400f, 0f, false)]
        public void NewOutputFollowsTheBottomOnlyWhenTheReaderIsAlreadyThere(
            float content, float viewport, float topOffset, bool expected)
        {
            Assert.That(TerminalHistory.ShouldFollowLatest(content, viewport, topOffset), Is.EqualTo(expected));
        }
    }
}
