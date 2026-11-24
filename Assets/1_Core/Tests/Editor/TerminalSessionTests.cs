using System.Linq;
using System.Text;
using Codetective.Operations;
using NUnit.Framework;

namespace Codetective.Terminal.Tests
{
    public sealed class TerminalSessionTests
    {
        [Test]
        public void AssignmentsCreateSeparateNamedSlots()
        {
            var session = new TerminalSession();
            Assert.That(session.Execute("단서 = 1").Success, Is.True);
            Assert.That(session.Execute("용의자 = 1").Success, Is.True);
            Assert.That(session.Memory.Select(slot => slot.Name), Is.EqualTo(new[] { "단서", "용의자" }));
            Assert.That(session.Memory[0].Id, Is.Not.EqualTo(session.Memory[1].Id));
            Assert.That(session.Memory[0], Is.Not.SameAs(session.Memory[1]));
        }

        [Test]
        public void ReassignmentUpdatesTheOriginalSlot()
        {
            var session = new TerminalSession();
            session.Execute("clue = 1");
            var original = session.Memory[0];
            Assert.That(session.Execute("clue = 2").Success, Is.True);
            Assert.That(session.Count, Is.EqualTo(1));
            Assert.That(session.Memory[0], Is.SameAs(original));
            Assert.That(session.Memory[0].Value.number, Is.EqualTo(2));
        }

        [Test]
        public void DeleteRemovesOnlyTheRequestedSlotAndAllowsReallocation()
        {
            var session = new TerminalSession();
            session.Execute("a = 1");
            session.Execute("b = 1");
            var removed = session.Memory[0];
            var remaining = session.Memory[1];
            Assert.That(session.Execute("free a").Success, Is.True);
            Assert.That(session.Memory.Single(), Is.SameAs(remaining));
            Assert.That(session.Execute("a = 1").Success, Is.True);
            Assert.That(session.Memory.Last(), Is.Not.SameAs(removed));
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("save")]
        [TestCase("free")]
        [TestCase("save one two")]
        [TestCase("save clue")]
        [TestCase("암호 연습")]
        [TestCase("code clue = 2")]
        [TestCase("free clue extra")]
        [TestCase("실행 clue")]
        [TestCase("free missing")]
        [TestCase("save\nsecond")]
        [TestCase("save a\nfree a")]
        public void InvalidCommandsDoNotChangeExistingMemory(string line)
        {
            var session = new TerminalSession();
            session.Execute("clue = 1");
            var original = session.Memory.Single();
            Assert.That(session.Execute(line).Success, Is.False);
            Assert.That(session.Memory.Single(), Is.SameAs(original));
        }

        [Test]
        public void WhitespaceAndKoreanNamesAreSupported()
        {
            var session = new TerminalSession();
            Assert.That(session.Execute("  사건_01\t= 1  ").Success, Is.True);
            Assert.That(session.Execute("free 사건_01").Success, Is.True);
            Assert.That(session.Count, Is.Zero);
        }

        [Test]
        public void KoreanUnicodeFormsReferToTheSameSlot()
        {
            var session = new TerminalSession();
            session.Execute("증거 = 1");
            Assert.That(session.Execute("증거".Normalize(NormalizationForm.FormD) + " = 2").Success, Is.True);
            Assert.That(session.Memory.Single().Value.number, Is.EqualTo(2));
            Assert.That(session.Complete("free " + "증".Normalize(NormalizationForm.FormD)).Single().DisplayText,
                Is.EqualTo("증거"));
            Assert.That(session.Execute("free " + "증거".Normalize(NormalizationForm.FormD)).Success, Is.True);
        }

        [Test]
        public void CommandCompletionAddsSpaceForTheArgument()
        {
            var session = new TerminalSession();
            Assert.That(session.Complete("").Select(item => item.DisplayText), Is.EqualTo(new[] { "free", "help" }));
            Assert.That(session.Complete("co"), Is.Empty);
            Assert.That(session.Complete("fr").Single().ReplacementText, Is.EqualTo("free "));
            Assert.That(session.Complete("he").Single().ReplacementText, Is.EqualTo("help"));
            Assert.That(session.Complete("없는명령"), Is.Empty);
        }

        [Test]
        public void DeleteCompletionUsesLiveMemoryAndFiltersAndSortsNames()
        {
            var session = new TerminalSession();
            Assert.That(session.Complete("free "), Is.Empty);
            session.Execute("case_b = 1");
            session.Execute("case_a = 1");
            session.Execute("other = 1");
            Assert.That(session.Complete("free case").Select(item => item.ReplacementText),
                Is.EqualTo(new[] { "free case_a", "free case_b" }));
            Assert.That(session.Complete("free ").Count, Is.EqualTo(3));
            session.Execute("free case_a");
            Assert.That(session.Complete("free case").Single().ReplacementText, Is.EqualTo("free case_b"));
        }

        [Test]
        public void CompletionNeverCreatesSlotsOrSuggestsDeclarations()
        {
            var session = new TerminalSession();
            session.Execute("clue = 1");
            Assert.That(session.Complete("save "), Is.Empty);
            Assert.That(session.Complete("free clue extra"), Is.Empty);
            Assert.That(session.Complete("free\nclue"), Is.Empty);
            Assert.That(session.Count, Is.EqualTo(1));
        }

        [TestCase(CommandLocale.Korean, "연습", "지휘소", "장소")]
        [TestCase(CommandLocale.English, "practice", "headquarters", "Place")]
        public void LessonAcceptsAnObjectReferenceInTheFirstAssignment(CommandLocale language, string name, string label, string category)
        {
            KernelRuntime runtime = ClinicScenario.Create(); runtime.Language = language;
            runtime.RegisterTerminalConversation(new TerminalConversationDefinition { id = "practice", koreanVariableBase = "연습", unlockCommandsByStep = true,
                steps = new[] {
                    new TerminalConversationStep { id = "bind", goal = new TerminalCommandGoal { kind = TerminalGoalKind.BindReference, targetId = "hq" } }
                } });
            runtime.EnterTerminalPage("kernel");
            var session = new TerminalSession(runtime);
            string prefix = name + " = ";
            Assert.That(session.Complete(prefix, linkObjects: true), Is.Empty);
            Assert.That(session.ExecuteLinkedInput(prefix + runtime.ObjectName("hq"), prefix + label).Success, Is.False);
            Assert.That(session.Count, Is.Zero);
            Assert.That(session.ExecuteLinkedInput(prefix + CommandObjectReference.Write("hq"), prefix + label).Success, Is.True);
            Assert.That(runtime.Data[name].value.id, Is.EqualTo("hq"));
            Assert.That(runtime.TerminalConversationActive, Is.False);
            Assert.That(session.Complete(name.Substring(0, 1)).Single().DisplayText, Is.EqualTo(name + " : " + category));
        }

        [Test]
        public void TutorialPermissionsUnlockInOrderForHelpCompletionAndExecution()
        {
            KernelRuntime runtime = ClinicScenario.Create();
            runtime.Language = CommandLocale.English;
            runtime.RegisterTerminalConversation(new TerminalConversationDefinition
            {
                id = "practice", unlockCommandsByStep = true,
                steps = new[]
                {
                    new TerminalConversationStep { id = "help", goal = new TerminalCommandGoal { kind = TerminalGoalKind.Help } },
                    new TerminalConversationStep { id = "bind", goal = new TerminalCommandGoal { kind = TerminalGoalKind.BindReference, targetId = "hq" } },
                    new TerminalConversationStep { id = "invoke", goal = new TerminalCommandGoal { kind = TerminalGoalKind.Invoke, targetId = "hq", operation = "checkComms", argumentCount = 0 } },
                    new TerminalConversationStep { id = "free", goal = new TerminalCommandGoal { kind = TerminalGoalKind.Free } }
                }
            });
            var session = new TerminalSession(runtime);
            Assert.That(session.Complete("").Select(c => c.DisplayText), Is.EqualTo(new[] { "help" }));
            Assert.That(session.Execute("var early").Success, Is.False);
            Assert.That(session.Execute("code early = hq").Success, Is.False);
            Assert.That(session.Count, Is.Zero);

            runtime.EnterTerminalPage("kernel");
            Assert.That(session.Execute("help").Message, Does.Not.Contain("code").And.Not.Contain("free"));
            Assert.That(session.Complete("co"), Is.Empty);
            Assert.That(session.Complete("practice = h", linkObjects: true), Is.Empty);
            Assert.That(session.Execute("code practice").Success, Is.False);
            Assert.That(session.Complete("hq.").Count, Is.Zero);
            Assert.That(session.Execute("hq.checkComms()").Success, Is.False);
            Assert.That(session.Execute("practice = hq").Success, Is.True);

            Assert.That(session.Complete("practice.").Select(c => c.ReplacementText), Is.EqualTo(new[] { "practice.checkComms()" }));
            Assert.That(session.Execute("hq.requestCoffee()").Success, Is.False);
            Assert.That(session.Execute("free practice").Success, Is.False);
            Assert.That(session.Execute("practice.checkComms()").Success, Is.True);

            Assert.That(session.Complete("free ").Single().ReplacementText, Is.EqualTo("free practice"));
            Assert.That(session.Execute("free practice").Success, Is.True);
            Assert.That(runtime.TerminalConversationActive, Is.False);
            Assert.That(session.Complete("hq.").Select(c => c.ReplacementText), Does.Contain("hq.requestCoffee()"));
        }

        [Test]
        public void NamesAreCaseSensitiveAndSessionsAreIndependent()
        {
            var session = new TerminalSession();
            session.Execute("Case = 1");
            session.Execute("case = 1");
            Assert.That(session.Count, Is.EqualTo(2));
            Assert.That(session.Complete("free C").Single().DisplayText, Is.EqualTo("Case"));
            Assert.That(new TerminalSession().Count, Is.Zero);
        }

        [Test]
        public void CompletionDoesNotTruncateLargeMemoryLists()
        {
            var session = new TerminalSession();
            for (int i = 0; i < 40; i++)
                session.Execute("var_" + i.ToString("D2") + " = 1");
            Assert.That(session.Complete("free var_").Count, Is.EqualTo(40));
            Assert.That(session.Complete("free var_").Last().ReplacementText, Is.EqualTo("free var_39"));
        }

        [Test]
        public void HelpListsAllCommandsWithoutChangingMemory()
        {
            var session = new TerminalSession();
            session.Execute("단서 = 1");
            var original = session.Memory.Single();
            CommandResult result = session.Execute("help");
            Assert.That(result.Success, Is.True);
            Assert.That(result.Message, Does.Contain("<name> = <target>").And.Not.Contain("empty substitution"));
            Assert.That(result.Message, Does.Contain("free <name>"));
            Assert.That(result.Message, Does.Contain("help"));
            Assert.That(session.Memory.Single(), Is.SameAs(original));
            Assert.That(session.Execute("help extra").Success, Is.False);
        }

        [Test]
        public void TheClickedObjectIdentitySurvivesAnIdenticalLabelAndStillRequiresDiscovery()
        {
            var runtime = ClinicScenario.Create();
            Entity second = runtime.CreateInstance("Place", "case.01:별관", "지휘소", exposeRoot: false);
            ObjectReferenceText text = ObjectReferenceText.Parse("[[hq|지휘소]]와 [[case.01:별관|지휘소]]");
            Assert.That(text.Text, Is.EqualTo("지휘소와 지휘소"));
            Assert.That(text.Spans.Select(span => span.Id), Is.EqualTo(new[] { "hq", second.Id }));
            ObjectReferenceSpan choice = text.Spans[1];
            var draft = new TerminalReferenceDraft();
            draft.Update("연습 =");
            draft.Insert(4, 4, choice.Id, text.Text.Substring(choice.Start, choice.Length));
            Assert.That(draft.Content.Text, Is.EqualTo("연습 = 지휘소"));
            string command = draft.Encode(draft.Content.Text);
            Assert.That(runtime.ExecuteLinkedInput(command, draft.Content.Text).Success, Is.False);
            runtime.Discover(second.Id);
            Assert.That(runtime.ExecuteLinkedInput(command, draft.Content.Text).Value.id, Is.EqualTo(second.Id));
            Assert.That(runtime.Data["연습"].value.id, Is.EqualTo(second.Id));
        }

        [Test]
        public void ThePermissionListKeepsAnInsertedReferenceWhenCompletingItsMember()
        {
            var runtime = ClinicScenario.Create();
            runtime.Language = CommandLocale.Korean;
            var session = new TerminalSession(runtime);
            var draft = new TerminalReferenceDraft();
            draft.Update(".통");
            int caret = draft.Insert(0, 0, "hq", "지휘소");
            Assert.That(caret, Is.EqualTo(3));
            Assert.That(draft.Content.Text, Is.EqualTo("지휘소.통"));
            TerminalCompletion match = session.Complete(draft.Encode(draft.Content.Text), linkObjects: true).Single();
            InlineCompletion completion = draft.DecodeCompletion(match.ReplacementText, runtime.ObjectName);
            Assert.That(completion.Text, Is.EqualTo("지휘소.통신확인()"));
            draft.Set(completion.References);
            Assert.That(session.ExecuteLinkedInput(draft.Encode(draft.Content.Text), draft.Content.Text).Success, Is.True);
            Assert.That(session.ExecuteLinkedInput(draft.Content.Text, draft.Content.Text).Success, Is.False);
            Assert.That(draft.TryDeletion(caret, backwards: true, out ObjectReferenceSpan span), Is.True);
            draft.Update(draft.Content.Text.Remove(span.Start, span.Length), span.Start);
            Assert.That(draft.Content.Text, Is.EqualTo(".통신확인()"));
            Assert.That(draft.Content.Spans, Is.Empty);
        }

        [TestCase("삭제")]
        [TestCase("해제")]
        [TestCase("free")]
        public void LocalizedDeleteUsesTheSameMemoryAsEnglish(string free)
        {
            var session = new TerminalSession();
            Assert.That(session.Execute("단서 = 1").Success, Is.True);
            Assert.That(session.Memory.Single().Name, Is.EqualTo("단서"));
            Assert.That(session.Execute(free + " 단서").Success, Is.True);
            Assert.That(session.Count, Is.Zero);
        }
    }
}
