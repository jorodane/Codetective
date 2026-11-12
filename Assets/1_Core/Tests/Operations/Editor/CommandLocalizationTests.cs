using System;
using System.Linq;
using Codetective.Operations;
using Codetective.Terminal;
using NUnit.Framework;

namespace Codetective.Tests.Operations
{
    public sealed class CommandLocalizationTests
    {
        private static KernelRuntime Korean()
        {
            KernelRuntime runtime = ClinicScenario.Create();
            runtime.Language = CommandLocale.Korean;
            return runtime;
        }

        [Test]
        public void KoreanBuiltinsCompleteAndShareLiveMemoryWithEnglishIncludingImePrefixes()
        {
            var session = new TerminalSession(Korean());
            Assert.That(session.Complete("").Take(3).Select(c => c.DisplayText), Is.EqualTo(new[] { "저장", "삭제", "도움말" }));
            Assert.That(session.Complete("ㅈ", true).Any(c => c.ReplacementText == "저장 "), Is.True);
            Assert.That(session.Complete("sa").Single().ReplacementText, Is.EqualTo("save "));
            session.Execute("저장 단서");
            Assert.That(session.Complete("삭제 다", true).Single().ReplacementText, Is.EqualTo("삭제 단서"));
            Assert.That(session.Complete("해제 ").Single().ReplacementText, Is.EqualTo("해제 단서"));
            Assert.That(session.Execute("free 단서").Success, Is.True);
            Assert.That(session.Complete("삭제 "), Is.Empty);
        }

        [Test]
        public void LocaleChangesOnlyTheSuggestedNamesAndBothLanguagesStayExecutable()
        {
            KernelRuntime r = Korean();
            Assert.That(r.Complete("지휘소.").Any(c => c.Replacement == "지휘소.커피요청()"), Is.True);
            Assert.That(r.Complete("hq.").Any(c => c.Replacement == "hq.커피요청()"), Is.True);
            Assert.That(r.Complete("hq.req").Single().Replacement, Is.EqualTo("hq.requestCoffee()"));
            Assert.That(r.Complete("지휘소.ㅋ", true).Single().Replacement, Is.EqualTo("지휘소.커피요청()"));
            Assert.That(r.Execute("지휘소.통신확인();").Success, Is.True);
            r.Language = CommandLocale.English;
            Assert.That(r.Complete("hq.").Any(c => c.Replacement == "hq.requestCoffee()"), Is.True);
            Assert.That(r.Complete("지휘소.커").Single().Replacement, Is.EqualTo("지휘소.커피요청()"));
            Assert.That(r.Execute("지휘소.통신확인();").Success, Is.True);
        }

        [Test]
        public void RootNamesAndTypedArgumentsUseTheSelectedLanguageWithoutDuplicateAliases()
        {
            KernelRuntime r = Korean();
            Assert.That(r.Complete("지").Single().Replacement, Is.EqualTo("지휘소."));
            Assert.That(r.Complete("arc").Single().Replacement, Is.EqualTo("archive."));
            Assert.That(r.Complete("").Count(c => c.Replacement == "지휘소."), Is.EqualTo(1));
            Assert.That(r.Complete("스레드01.프로토콜실행(").Single().Replacement, Does.EndWith("recon"));
            r.RecordClue(0);
            r.Execute("문서 = 자료실.조사();");
            Completion argument = r.Complete("자료실.분석(").Single();
            Assert.That(argument.Label, Is.EqualTo("문서 : 문서"));
            Assert.That(argument.Replacement, Is.EqualTo("자료실.분석(문서"));
        }

        [Test]
        public void LocalizedNamesCannotBypassFactsAuthoritiesOrWorldRestrictions()
        {
            KernelRuntime r = Korean();
            Assert.That(r.Complete("자료실.조"), Is.Empty);
            Assert.That(r.Execute("문서 = 자료실.조사();").Success, Is.False);
            r.RecordClue(0);
            Assert.That(r.Complete("자료실.ㅈ", true).Single().Replacement, Is.EqualTo("자료실.조사()"));
            Assert.That(r.Execute("문서 = 자료실.조사();").Success, Is.True);
            Assert.That(r.Execute("결과 = 자료실.분석(문서);").Success, Is.True);
            Assert.That(r.Execute("상담손님.증거제시(결과);").Success, Is.True);
            r.Authorities.Clear();
            Assert.That(r.Execute("지휘소.커피요청();").Success, Is.False);
            Assert.That(r.Complete("지휘소.커"), Is.Empty);
            r.Tick(540);
            Assert.That(r.Complete("자료실.조"), Is.Empty);
        }

        [Test]
        public void EveryDataTypeHasAParseableKoreanName()
        {
            foreach (DataKind kind in Enum.GetValues(typeof(DataKind)))
            {
                if (kind == DataKind.Empty) continue;
                string name = CommandVocabulary.KindName(kind, CommandLocale.Korean);
                Assert.That(CommandParser.ParseKind(name), Is.EqualTo(kind), name);
            }
        }

        [Test]
        public void KoreanProtocolsSupportLocalsTypedInputsNestedCallsAndReturns()
        {
            KernelRuntime r = Korean();
            r.RecordClue(0);
            Assert.That(r.SaveProtocol("프로토콜 수집(장소 대상) { 변수 문서 = 대상.조사(); 반환 문서; }").Success, Is.True);
            Assert.That(r.SaveProtocol("프로토콜 분석작전(장소 대상) { 변수 문서 = 수집(대상); 반환 자료실.분석(문서); }").Success, Is.True);
            Assert.That(r.Execute("결과 = 분석작전(자료실);").Success, Is.True);
            Assert.That(r.Data["결과"].value.kind, Is.EqualTo(DataKind.Analysis));
            Assert.That(r.Data.ContainsKey("문서"), Is.False);
            Assert.That(r.Execute("분석작전(상담손님);").Success, Is.False);
        }

        [Test]
        public void SharedProtocolSourcesAndStringContentsSurviveLocaleSwitchAndRestore()
        {
            KernelRuntime r = Korean();
            const string source = "프로토콜 공유함수() { 반환 \"지휘소.커피요청(); save return 변수\"; }";
            r.SaveProtocol(source);
            KernelSnapshot saved = r.Capture();
            var english = ClinicScenario.Create();
            english.Restore(saved);
            Assert.That(english.Protocols["공유함수"].Source, Is.EqualTo(source));
            ExecutionResult result = english.Execute("answer = 공유함수();");
            Assert.That(result.Success, Is.True);
            Assert.That(result.Value.text, Is.EqualTo("지휘소.커피요청(); save return 변수"));
            Assert.That(english.Data.ContainsKey("answer"), Is.True);
        }

        [Test]
        public void ARunningKoreanProtocolKeepsItsTimeAndDataAfterSwitchingToEnglish()
        {
            KernelRuntime r = Korean();
            r.RecordClue(0);
            r.SaveProtocol("프로토콜 정찰(장소 대상) { 반환 대상.조사(); }");
            Assert.That(r.Execute("스레드01.프로토콜실행(정찰, 자료실);").Success, Is.True);
            r.Tick(2);
            KernelSnapshot saved = r.Capture();
            var english = ClinicScenario.Create();
            english.Restore(saved);
            Assert.That(english.Agent("thread01").State, Is.EqualTo(AgentState.Executing));
            english.Tick(3);
            Assert.That(english.Agent("thread01").State, Is.EqualTo(AgentState.Ready));
            Assert.That(english.Execute("reportCopy = thread01.report;").Value.kind, Is.EqualTo(DataKind.Document));
            english.Language = CommandLocale.Korean;
            Assert.That(english.Execute("보고복사 = 스레드01.보고;").Value.kind, Is.EqualTo(DataKind.Document));
        }

        [Test]
        public void ProtocolSuggestionsUnderstandKoreanParametersAndOngoingImeComposition()
        {
            KernelRuntime r = Korean();
            r.RecordClue(0);
            const string source = "프로토콜 정찰(장소 대상) {\n    반환 대상.ㅈ\n}";
            int caret = source.IndexOf("ㅈ", StringComparison.Ordinal) + 1;
            Completion completion = r.CompleteProtocol(source, caret, "자료실", true).Single();
            Assert.That(completion.Replacement, Does.Contain("반환 대상.조사()\n}"));
            Assert.That(r.Facts, Does.Not.Contain("archive_inspected"));
        }

        [Test]
        public void ContentDefinedNamesWorkThroughTheSameRegistryAsBuiltInNames()
        {
            var r = new KernelRuntime { Language = CommandLocale.Korean };
            var warehouse = new Entity("warehouse", "창고", DataKind.Place)
            { Names = new[] { new LocalizedCommandName(CommandLocale.Korean, "창고") } };
            warehouse.Operations.Add(new Operation { Code = "inspect", Names = new[] { new LocalizedCommandName(CommandLocale.Korean, "살펴본다") },
                Invoke = c => Datum.Text("found") });
            r.Register(warehouse);
            Assert.That(r.Complete("창고.살").Single().Replacement, Is.EqualTo("창고.살펴본다()"));
            Assert.That(r.Execute("창고.살펴본다();").Value.text, Is.EqualTo("found"));
            r.Language = CommandLocale.English;
            Assert.That(r.Execute("warehouse.inspect();").Value.text, Is.EqualTo("found"));
        }

        [Test]
        public void LocalizedKeywordsAndRootNamesCannotBeShadowedByGlobalData()
        {
            KernelRuntime r = Korean();
            Assert.That(r.Execute("save 지휘소").Success, Is.False);
            Assert.That(r.Execute("저장 도움말").Success, Is.False);
            Assert.That(r.SaveProtocol("프로토콜 도움말() { 반환 1; }").Success, Is.False);
            Assert.That(r.Execute("도움말").Message, Does.Contain("저장 <이름>"));
            r.Language = CommandLocale.English;
            Assert.That(r.Execute("도움말").Message, Does.Contain("save <name>"));
        }
    }
}
