using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Codetective.Terminal.Tests
{
    public sealed class TerminalSessionTests
    {
        [Test]
        public void SavingAllocatesSeparateNamedSlots()
        {
            var session = new TerminalSession();
            Assert.That(session.Execute("저장 단서").Success, Is.True);
            Assert.That(session.Execute("저장 용의자").Success, Is.True);
            Assert.That(session.Memory.Select(slot => slot.Name), Is.EqualTo(new[] { "단서", "용의자" }));
            Assert.That(session.Memory[0].Id, Is.Not.EqualTo(session.Memory[1].Id));
            Assert.That(session.Memory[0], Is.Not.SameAs(session.Memory[1]));
        }

        [Test]
        public void DuplicateSavePreservesTheOriginalSlot()
        {
            var session = new TerminalSession();
            session.Execute("저장 clue");
            var original = session.Memory[0];
            Assert.That(session.Execute("저장 clue").Success, Is.False);
            Assert.That(session.Count, Is.EqualTo(1));
            Assert.That(session.Memory[0], Is.SameAs(original));
        }

        [Test]
        public void DeleteRemovesOnlyTheRequestedSlotAndAllowsReallocation()
        {
            var session = new TerminalSession();
            session.Execute("저장 a");
            session.Execute("저장 b");
            var removed = session.Memory[0];
            var remaining = session.Memory[1];
            Assert.That(session.Execute("삭제 a").Success, Is.True);
            Assert.That(session.Memory.Single(), Is.SameAs(remaining));
            Assert.That(session.Execute("저장 a").Success, Is.True);
            Assert.That(session.Memory.Last(), Is.Not.SameAs(removed));
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("저장")]
        [TestCase("삭제")]
        [TestCase("저장 one two")]
        [TestCase("삭제 clue extra")]
        [TestCase("실행 clue")]
        [TestCase("삭제 missing")]
        [TestCase("저장\nsecond")]
        [TestCase("저장 a\n삭제 a")]
        public void InvalidCommandsDoNotChangeExistingMemory(string line)
        {
            var session = new TerminalSession();
            session.Execute("저장 clue");
            var original = session.Memory.Single();
            Assert.That(session.Execute(line).Success, Is.False);
            Assert.That(session.Memory.Single(), Is.SameAs(original));
        }

        [Test]
        public void WhitespaceAndKoreanNamesAreSupported()
        {
            var session = new TerminalSession();
            Assert.That(session.Execute("  저장\t사건_01  ").Success, Is.True);
            Assert.That(session.Execute("삭제 사건_01").Success, Is.True);
            Assert.That(session.Count, Is.Zero);
        }

        [Test]
        public void KoreanUnicodeFormsReferToTheSameSlot()
        {
            var session = new TerminalSession();
            session.Execute("저장 증거");
            Assert.That(session.Execute("저장 " + "증거".Normalize(NormalizationForm.FormD)).Success, Is.False);
            Assert.That(session.Complete("삭제 " + "증".Normalize(NormalizationForm.FormD)).Single().DisplayText,
                Is.EqualTo("증거"));
            Assert.That(session.Execute("삭제 " + "증거".Normalize(NormalizationForm.FormD)).Success, Is.True);
        }

        [Test]
        public void CommandCompletionAddsSpaceForTheArgument()
        {
            var session = new TerminalSession();
            Assert.That(session.Complete("").Select(item => item.DisplayText), Is.EqualTo(new[] { "저장", "삭제" }));
            Assert.That(session.Complete("저").Single().ReplacementText, Is.EqualTo("저장 "));
            Assert.That(session.Complete("삭").Single().ReplacementText, Is.EqualTo("삭제 "));
            Assert.That(session.Complete("없는명령"), Is.Empty);
        }

        [Test]
        public void DeleteCompletionUsesLiveMemoryAndFiltersAndSortsNames()
        {
            var session = new TerminalSession();
            Assert.That(session.Complete("삭제 "), Is.Empty);
            session.Execute("저장 case_b");
            session.Execute("저장 case_a");
            session.Execute("저장 other");
            Assert.That(session.Complete("삭제 case").Select(item => item.ReplacementText),
                Is.EqualTo(new[] { "삭제 case_a", "삭제 case_b" }));
            Assert.That(session.Complete("삭제 ").Count, Is.EqualTo(3));
            session.Execute("삭제 case_a");
            Assert.That(session.Complete("삭제 case").Single().ReplacementText, Is.EqualTo("삭제 case_b"));
        }

        [Test]
        public void CompletionNeverCreatesSlotsOrSuggestsNamesForSave()
        {
            var session = new TerminalSession();
            session.Execute("저장 clue");
            Assert.That(session.Complete("저장 "), Is.Empty);
            Assert.That(session.Complete("삭제 clue extra"), Is.Empty);
            Assert.That(session.Complete("삭제\nclue"), Is.Empty);
            Assert.That(session.Count, Is.EqualTo(1));
        }

        [Test]
        public void NamesAreCaseSensitiveAndSessionsAreIndependent()
        {
            var session = new TerminalSession();
            session.Execute("저장 Case");
            session.Execute("저장 case");
            Assert.That(session.Count, Is.EqualTo(2));
            Assert.That(session.Complete("삭제 C").Single().DisplayText, Is.EqualTo("Case"));
            Assert.That(new TerminalSession().Count, Is.Zero);
        }

        [Test]
        public void CompletionDoesNotTruncateLargeMemoryLists()
        {
            var session = new TerminalSession();
            for (int i = 0; i < 40; i++)
                session.Execute("저장 var_" + i.ToString("D2"));
            Assert.That(session.Complete("삭제 var_").Count, Is.EqualTo(40));
            Assert.That(session.Complete("삭제 var_").Last().ReplacementText, Is.EqualTo("삭제 var_39"));
        }
    }
}
