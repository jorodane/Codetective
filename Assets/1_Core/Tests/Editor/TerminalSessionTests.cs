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
            Assert.That(session.Execute("save 단서").Success, Is.True);
            Assert.That(session.Execute("save 용의자").Success, Is.True);
            Assert.That(session.Memory.Select(slot => slot.Name), Is.EqualTo(new[] { "단서", "용의자" }));
            Assert.That(session.Memory[0].Id, Is.Not.EqualTo(session.Memory[1].Id));
            Assert.That(session.Memory[0], Is.Not.SameAs(session.Memory[1]));
        }

        [Test]
        public void DuplicateSavePreservesTheOriginalSlot()
        {
            var session = new TerminalSession();
            session.Execute("save clue");
            var original = session.Memory[0];
            Assert.That(session.Execute("save clue").Success, Is.False);
            Assert.That(session.Count, Is.EqualTo(1));
            Assert.That(session.Memory[0], Is.SameAs(original));
        }

        [Test]
        public void DeleteRemovesOnlyTheRequestedSlotAndAllowsReallocation()
        {
            var session = new TerminalSession();
            session.Execute("save a");
            session.Execute("save b");
            var removed = session.Memory[0];
            var remaining = session.Memory[1];
            Assert.That(session.Execute("free a").Success, Is.True);
            Assert.That(session.Memory.Single(), Is.SameAs(remaining));
            Assert.That(session.Execute("save a").Success, Is.True);
            Assert.That(session.Memory.Last(), Is.Not.SameAs(removed));
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("save")]
        [TestCase("free")]
        [TestCase("save one two")]
        [TestCase("free clue extra")]
        [TestCase("실행 clue")]
        [TestCase("free missing")]
        [TestCase("save\nsecond")]
        [TestCase("save a\nfree a")]
        public void InvalidCommandsDoNotChangeExistingMemory(string line)
        {
            var session = new TerminalSession();
            session.Execute("save clue");
            var original = session.Memory.Single();
            Assert.That(session.Execute(line).Success, Is.False);
            Assert.That(session.Memory.Single(), Is.SameAs(original));
        }

        [Test]
        public void WhitespaceAndKoreanNamesAreSupported()
        {
            var session = new TerminalSession();
            Assert.That(session.Execute("  save\t사건_01  ").Success, Is.True);
            Assert.That(session.Execute("free 사건_01").Success, Is.True);
            Assert.That(session.Count, Is.Zero);
        }

        [Test]
        public void KoreanUnicodeFormsReferToTheSameSlot()
        {
            var session = new TerminalSession();
            session.Execute("save 증거");
            Assert.That(session.Execute("save " + "증거".Normalize(NormalizationForm.FormD)).Success, Is.False);
            Assert.That(session.Complete("free " + "증".Normalize(NormalizationForm.FormD)).Single().DisplayText,
                Is.EqualTo("증거"));
            Assert.That(session.Execute("free " + "증거".Normalize(NormalizationForm.FormD)).Success, Is.True);
        }

        [Test]
        public void CommandCompletionAddsSpaceForTheArgument()
        {
            var session = new TerminalSession();
            Assert.That(session.Complete("").Select(item => item.DisplayText), Is.EqualTo(new[] { "save", "free", "help" }));
            Assert.That(session.Complete("sa").Single().ReplacementText, Is.EqualTo("save "));
            Assert.That(session.Complete("fr").Single().ReplacementText, Is.EqualTo("free "));
            Assert.That(session.Complete("he").Single().ReplacementText, Is.EqualTo("help"));
            Assert.That(session.Complete("없는명령"), Is.Empty);
        }

        [Test]
        public void DeleteCompletionUsesLiveMemoryAndFiltersAndSortsNames()
        {
            var session = new TerminalSession();
            Assert.That(session.Complete("free "), Is.Empty);
            session.Execute("save case_b");
            session.Execute("save case_a");
            session.Execute("save other");
            Assert.That(session.Complete("free case").Select(item => item.ReplacementText),
                Is.EqualTo(new[] { "free case_a", "free case_b" }));
            Assert.That(session.Complete("free ").Count, Is.EqualTo(3));
            session.Execute("free case_a");
            Assert.That(session.Complete("free case").Single().ReplacementText, Is.EqualTo("free case_b"));
        }

        [Test]
        public void CompletionNeverCreatesSlotsOrSuggestsNamesForSave()
        {
            var session = new TerminalSession();
            session.Execute("save clue");
            Assert.That(session.Complete("save "), Is.Empty);
            Assert.That(session.Complete("free clue extra"), Is.Empty);
            Assert.That(session.Complete("free\nclue"), Is.Empty);
            Assert.That(session.Count, Is.EqualTo(1));
        }

        [Test]
        public void NamesAreCaseSensitiveAndSessionsAreIndependent()
        {
            var session = new TerminalSession();
            session.Execute("save Case");
            session.Execute("save case");
            Assert.That(session.Count, Is.EqualTo(2));
            Assert.That(session.Complete("free C").Single().DisplayText, Is.EqualTo("Case"));
            Assert.That(new TerminalSession().Count, Is.Zero);
        }

        [Test]
        public void CompletionDoesNotTruncateLargeMemoryLists()
        {
            var session = new TerminalSession();
            for (int i = 0; i < 40; i++)
                session.Execute("save var_" + i.ToString("D2"));
            Assert.That(session.Complete("free var_").Count, Is.EqualTo(40));
            Assert.That(session.Complete("free var_").Last().ReplacementText, Is.EqualTo("free var_39"));
        }

        [Test]
        public void HelpListsAllCommandsWithoutChangingMemory()
        {
            var session = new TerminalSession();
            session.Execute("save 단서");
            var original = session.Memory.Single();
            CommandResult result = session.Execute("help");
            Assert.That(result.Success, Is.True);
            Assert.That(result.Message, Does.Contain("save <name>"));
            Assert.That(result.Message, Does.Contain("free <name>"));
            Assert.That(result.Message, Does.Contain("help"));
            Assert.That(session.Memory.Single(), Is.SameAs(original));
            Assert.That(session.Execute("help extra").Success, Is.False);
        }

        [TestCase("저장", "삭제")]
        [TestCase("저장", "free")]
        [TestCase("save", "해제")]
        public void LocalizedCommandsUseTheSameMemoryAsEnglish(string save, string free)
        {
            var session = new TerminalSession();
            Assert.That(session.Execute(save + " 단서").Success, Is.True);
            Assert.That(session.Memory.Single().Name, Is.EqualTo("단서"));
            Assert.That(session.Execute(free + " 단서").Success, Is.True);
            Assert.That(session.Count, Is.Zero);
        }
    }
}
