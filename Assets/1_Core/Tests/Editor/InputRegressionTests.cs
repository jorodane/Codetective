using NUnit.Framework;

namespace Codetective.Terminal.Tests
{
    public sealed class InputRegressionTests
    {
        [Test]
        public void EmptyPromptBrowsingSurvivesOrdinaryRefreshesAndArrowNavigation()
        {
            var state = new CompletionVisibility();
            var empty = new TerminalCompletionInput("", "", 0, 0);
            Assert.That(state.ShouldShow(empty, true, true), Is.False);
            state.Open();
            for (int i = 0; i < 120; i++) Assert.That(state.ShouldShow(empty, true, true), Is.True);
            Assert.That(state.ShouldShow(empty, false, true), Is.False);
            Assert.That(state.ShouldShow(empty, true, true), Is.True);
            state.Dismiss();
            Assert.That(state.ShouldShow(empty, true, true), Is.False);
        }

        [Test]
        public void TypingAndSubmissionResetExplicitEmptyBrowsing()
        {
            var state = new CompletionVisibility();
            state.Open(); state.Edit();
            Assert.That(state.ShouldShow(new TerminalCompletionInput("sa", "", 2, 2), true, true), Is.True);
            Assert.That(state.ShouldShow(new TerminalCompletionInput("", "", 0, 0), true, true), Is.False);
        }

        [TestCase("삭제 ", "다", "삭제 단서", "서")]
        [TestCase("삭제 단", "ㅅ", "삭제 단서", "")]
        [TestCase("", "ㅈ", "저장 ", "장 ")]
        [TestCase("free cl", "", "free clue", "ue")]
        public void PreviewOnlyDrawsUntypedCharactersAndNeverReplacesTheTypedPrefix(string committed, string composition, string replacement, string expected)
        {
            var current = new TerminalCompletionInput(committed, composition, committed.Length, committed.Length);
            var preview = new InlineCompletion(replacement, replacement.Length);
            Assert.That(preview.SuffixFor(current), Is.EqualTo(expected));
            Assert.That(current.Text, Is.EqualTo(committed + composition));
        }

        [Test]
        public void StaleCompositionDoesNotReappearOrOffsetTheCaretAfterRefocus()
        {
            var session = new ImeSession();
            session.Begin(""); session.CompositionChanged("단");
            Assert.That(session.Read("단"), Is.EqualTo("단"));
            session.End("단", "단");
            Assert.That(session.Read("단"), Is.Empty);
            session.Begin("단");
            var current = new TerminalCompletionInput("저장 단", session.Read("단"), 0, 4);
            Assert.That(current.Text, Is.EqualTo("저장 단"));
            Assert.That(current.Anchor, Is.Zero);
            Assert.That(current.Caret, Is.EqualTo(4));
            Assert.That(session.IgnoreCommitEcho('단'), Is.True);
            Assert.That(session.IgnoreCommitEcho('서'), Is.False);
        }

        [Test]
        public void ANewCompositionCanReuseTheSameSyllableAsThePreviousSession()
        {
            var session = new ImeSession();
            session.Begin(""); session.CompositionChanged("단"); session.End("단", "단");
            session.Begin("단"); session.CompositionChanged("단");
            Assert.That(session.Read("단"), Is.EqualTo("단"));
            Assert.That(session.IgnoreCommitEcho('단'), Is.False);
        }

        [Test]
        public void AnEmptyImeEventOverridesAStaleBackendString()
        {
            var session = new ImeSession();
            session.Begin(""); session.CompositionChanged("한"); session.CompositionChanged("");
            Assert.That(session.Read("한"), Is.Empty);
        }

        [Test]
        public void CommittedCharacterInsertionUsesRawCaretIndicesThenRestoresLiveComposition()
        {
            var session = new ImeSession();
            session.Begin(""); session.CompositionChanged("서");
            session.Suppress();
            Assert.That(session.Read("서"), Is.Empty);
            session.Resume();
            Assert.That(session.Read("서"), Is.EqualTo("서"));
            var current = new TerminalCompletionInput("저장 단", session.Read("서"), 4, 4);
            Assert.That(current.Text, Is.EqualTo("저장 단서"));
        }

        [Test]
        public void MultiCharacterCommitEchoIsDiscardedOnlyOnce()
        {
            var session = new ImeSession();
            session.Begin(""); session.End("가나", "가나"); session.Begin("가나");
            Assert.That(session.IgnoreCommitEcho('가'), Is.True);
            Assert.That(session.IgnoreCommitEcho('나'), Is.True);
            Assert.That(session.IgnoreCommitEcho('가'), Is.False);
        }

        [Test]
        public void ADelayedCommitFollowsFocusToTheNextInputFieldWithoutBeingInsertedThere()
        {
            var first = new ImeSession(); first.Begin(""); first.End("단", "단");
            string lateCommit = first.TakeCommitEcho();
            var second = new ImeSession(); second.Begin("단"); second.ExpectCommitEcho(lateCommit);
            Assert.That(second.Read("단"), Is.Empty);
            Assert.That(second.IgnoreCommitEcho('단'), Is.True);
            second.CompositionChanged("단");
            Assert.That(second.IgnoreCommitEcho('단'), Is.False);
        }
    }
}
