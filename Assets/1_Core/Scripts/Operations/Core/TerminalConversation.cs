using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Codetective.Operations
{
    public enum TerminalConversationPhase { Dormant, Speaking, AwaitingCommand, Completed }
    public enum TerminalGoalKind { None, Help, Allocate, BindReference, Invoke, Free, StoryCondition }
    public enum TerminalEmotion { Normal, Angry }
    [Serializable] public sealed class TerminalDialogueLine
    {
        public string text = "", textKey = "";
        public string speaker = "", speakerKey = "";
        public double charactersPerSecond = 32, pauseAfter = 0.7, effectSeconds = 0.55;
        public TerminalEmotion emotion;
        public TerminalDialogueLine Copy() => (TerminalDialogueLine)MemberwiseClone();
    }
    [Serializable] public sealed class TerminalCommandGoal
    {
        public TerminalGoalKind kind;
        public string dataName = "{slot}", targetId = "", operation = "";
        public int argumentCount;
        public bool preferVariable = true, acceptExisting = true;
        public StoryCondition condition = new StoryCondition();
        public TerminalCommandGoal Copy() => new TerminalCommandGoal { kind = kind, dataName = dataName, targetId = targetId, operation = operation,
            argumentCount = argumentCount, preferVariable = preferVariable, acceptExisting = acceptExisting, condition = condition?.Copy() ?? new StoryCondition() };
    }
    [Serializable] public sealed class TerminalConversationStep
    {
        public string id;
        public TerminalDialogueLine[] lines = Array.Empty<TerminalDialogueLine>();
        public TerminalCommandGoal goal = new TerminalCommandGoal();
        public TerminalAnswer[] answers = Array.Empty<TerminalAnswer>();
        public StoryEffects onComplete = new StoryEffects();
        public TerminalConversationStep Copy() => new TerminalConversationStep
        {
            id = id,
            lines = lines.Select(line => line.Copy()).ToArray(),
            goal = goal.Copy(),
            answers = answers?.Select(answer => answer.Copy()).ToArray() ?? Array.Empty<TerminalAnswer>(),
            onComplete = onComplete.Copy()
        };
    }
    [Serializable] public sealed class TerminalAnswer
    {
        public string key;
        public TerminalDialogueLine[] reply = Array.Empty<TerminalDialogueLine>();
        public StoryEffects effects = new StoryEffects();
        public TerminalAnswer Copy() => new TerminalAnswer
        {
            key = key,
            reply = reply?.Select(line => line.Copy()).ToArray() ?? Array.Empty<TerminalDialogueLine>(),
            effects = effects?.Copy() ?? new StoryEffects()
        };
    }
    [Serializable] public sealed class TerminalConversationDefinition
    {
        public string id = "", requiredMailRead = "", pageId = "kernel", variableBase = "practice", speaker = "운영자", speakerKey = "";
        public string koreanVariableBase = "";
        public int firstDay = 1, lastDay = int.MaxValue;
        public bool pauseWorld = true;
        public bool replacesStartupGuide;
        public bool unlockCommandsByStep;
        public bool advanceWithEnter;
        public bool skipIfMailAlreadyReadOnExistingSave;
        public StoryCondition condition = new StoryCondition();
        public TerminalConversationStep[] steps = Array.Empty<TerminalConversationStep>();
        public TerminalDialogueLine[] forgivingReplies = Array.Empty<TerminalDialogueLine>(), angryReplies = Array.Empty<TerminalDialogueLine>();
        public int angryAfter = 3;
        public StoryEffects onComplete = new StoryEffects();
        public TerminalConversationDefinition Copy() => new TerminalConversationDefinition { id = id, requiredMailRead = requiredMailRead, pageId = pageId,
            variableBase = variableBase, koreanVariableBase = koreanVariableBase, speaker = speaker, speakerKey = speakerKey, firstDay = firstDay, lastDay = lastDay, pauseWorld = pauseWorld, replacesStartupGuide = replacesStartupGuide,
            unlockCommandsByStep = unlockCommandsByStep, advanceWithEnter = advanceWithEnter, skipIfMailAlreadyReadOnExistingSave = skipIfMailAlreadyReadOnExistingSave,
            condition = condition.Copy(), steps = steps.Select(s => s.Copy()).ToArray(), forgivingReplies = forgivingReplies.Select(l => l.Copy()).ToArray(),
            angryReplies = angryReplies.Select(l => l.Copy()).ToArray(), angryAfter = angryAfter, onComplete = onComplete.Copy() };
        public void Validate()
        {
            if (!string.IsNullOrEmpty(koreanVariableBase) && !CommandParser.IsIdentifier(koreanVariableBase))
                throw new CommandException("한국어 연습용 암호 이름이 유효하지 않습니다.");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(pageId) || !CommandParser.IsIdentifier(variableBase) || firstDay < 1 || lastDay < firstDay || angryAfter < 1 ||
                steps == null || steps.Length == 0 || steps.Length > 64 || steps.Any(s => s == null || string.IsNullOrWhiteSpace(s.id) || s.goal == null || s.lines == null) || steps.Select(s => s.id).Distinct().Count() != steps.Length ||
                forgivingReplies == null || angryReplies == null || condition == null || onComplete == null) throw new CommandException("터미널 대화의 ID, 단계 또는 시작 조건이 유효하지 않습니다.");
            int lineCount = steps.Sum(step => step.lines.Length) + forgivingReplies.Length + angryReplies.Length;
            ValidateLines(steps.SelectMany(step => step.lines).Concat(forgivingReplies).Concat(angryReplies));
            foreach (TerminalConversationStep step in steps)
            {
                TerminalCommandGoal goal = step.goal;
                if (!Enum.IsDefined(typeof(TerminalGoalKind), goal.kind) || goal.argumentCount < -1 || goal.condition == null || step.onComplete == null ||
                    (goal.kind == TerminalGoalKind.Invoke && (string.IsNullOrWhiteSpace(goal.targetId) || !CommandParser.IsIdentifier(goal.operation))) ||
                    (goal.kind == TerminalGoalKind.BindReference && string.IsNullOrWhiteSpace(goal.targetId)) ||
                    (goal.kind == TerminalGoalKind.Allocate || goal.kind == TerminalGoalKind.Free || goal.kind == TerminalGoalKind.BindReference) && !CommandParser.IsIdentifier((goal.dataName ?? "").Replace("{slot}", variableBase)))
                    throw new CommandException("터미널 대화의 지시 명령이 유효하지 않습니다: " + step.id);
                step.onComplete.Validate();
                TerminalAnswer[] answers = step.answers ?? Array.Empty<TerminalAnswer>();
                if (answers.Length == 0) continue;
                if (goal.kind != TerminalGoalKind.None || step.lines.Length == 0 ||
                    answers.Any(answer => answer == null || string.IsNullOrEmpty(answer.key) ||
                        !answer.key.All(character => char.IsLetterOrDigit(character) || character == '_')) ||
                    answers.Select(answer => answer.key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != answers.Length)
                    throw new CommandException("터미널 질문의 대사 또는 답변 키가 유효하지 않습니다: " + step.id);
                foreach (TerminalAnswer answer in answers)
                {
                    answer.effects?.Validate();
                    if (answer.reply == null) continue;
                    ValidateLines(answer.reply);
                    lineCount += answer.reply.Length;
                }
            }
            if (lineCount > 1024) throw new CommandException("터미널 대사가 너무 많습니다.");
            onComplete.Validate();
        }
        private static void ValidateLines(IEnumerable<TerminalDialogueLine> lines)
        {
            foreach (TerminalDialogueLine line in lines)
                if (line == null || line.text == null || line.text.Length > 4096 || !Finite(line.charactersPerSecond) || line.charactersPerSecond <= 0 ||
                    !Finite(line.pauseAfter) || line.pauseAfter < 0 || !Finite(line.effectSeconds) || line.effectSeconds < 0 || !Enum.IsDefined(typeof(TerminalEmotion), line.emotion))
                    throw new CommandException("터미널 대사의 문구/출력 속도가 유효하지 않습니다.");
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
    [Serializable] public sealed class TerminalConversationLog
    {
        public string text = "", textKey = "", speaker = "", speakerKey = "", englishCommand = "", koreanCommand = "";
        public string suffix = "";
        public int visibleElements;
        public bool complete;
        public TerminalConversationLog Copy() => (TerminalConversationLog)MemberwiseClone();
    }
    [Serializable] public sealed class TerminalActionProof
    {
        public string targetId, operation; public int arguments;
        public TerminalActionProof Copy() => (TerminalActionProof)MemberwiseClone();
    }
    [Serializable] public sealed class TerminalConversationProgress
    {
        public string id = "", stepId = "", slotName = "", answerKey = "";
        public TerminalConversationPhase phase;
        public int deviations, pendingIndex, activeLog = -1;
        public bool reply;
        public double elapsed;
        public List<TerminalDialogueLine> pending = new List<TerminalDialogueLine>();
        public List<TerminalConversationLog> log = new List<TerminalConversationLog>();
        public List<TerminalActionProof> performed = new List<TerminalActionProof>();
        public TerminalConversationProgress Copy() => new TerminalConversationProgress { id = id, stepId = stepId, slotName = slotName, answerKey = answerKey, phase = phase, deviations = deviations,
            pendingIndex = pendingIndex, activeLog = activeLog, reply = reply, elapsed = elapsed, pending = pending.Select(l => l.Copy()).ToList(), log = log.Select(l => l.Copy()).ToList(), performed = performed.Select(p => p.Copy()).ToList() };
    }
    internal sealed class TerminalCommandTrace
    {
        public string keyword, argument, assigned;
        public TerminalAnswer answer;
        public readonly List<TerminalActionProof> actions = new List<TerminalActionProof>();
    }

    public sealed partial class KernelRuntime
    {
        private const string AnswerPromptKey = "terminal.dialogue.answer.options";
        private readonly Dictionary<string, TerminalConversationDefinition> terminalConversations = new Dictionary<string, TerminalConversationDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, TerminalConversationProgress> terminalConversationProgress = new Dictionary<string, TerminalConversationProgress>(StringComparer.Ordinal);
        private string activeTerminalConversation;
        private long terminalConversationRevision;
        private TerminalCommandTrace terminalCommandTrace;
        private TerminalConversationProgress Conversation => activeTerminalConversation != null && terminalConversationProgress.TryGetValue(activeTerminalConversation, out var p) && terminalConversations.ContainsKey(p.id) ? p : null;
        private TerminalConversationDefinition ConversationDefinition => Conversation != null ? terminalConversations[Conversation.id] : null;
        private TerminalConversationStep ConversationStep => ConversationDefinition?.steps.FirstOrDefault(s => s.id == Conversation.stepId);
        public string TerminalConversationId => Conversation?.id;
        public long TerminalConversationRevision => terminalConversationRevision;
        public bool TerminalConversationActive => Conversation != null && (Conversation.phase == TerminalConversationPhase.Speaking || Conversation.phase == TerminalConversationPhase.AwaitingCommand);
        public bool TerminalInputLocked => Conversation?.phase == TerminalConversationPhase.Speaking;
        public bool TerminalLineUsesEnter
        {
            get
            {
                if (!TerminalInputLocked || !ConversationDefinition.advanceWithEnter) return false;
                TerminalConversationProgress progress = Conversation;
                if (progress.pendingIndex + 1 < progress.pending.Count) return true;
                TerminalConversationStep step = ConversationStep;
                if (progress.reply || IsUnanswered(step) || GoalRequiresInput(step.goal)) return false;
                TerminalConversationStep[] steps = ConversationDefinition.steps;
                for (int i = Array.IndexOf(steps, step) + 1; i < steps.Length; i++)
                {
                    if (steps[i].lines.Length > 0) return true;
                    if (GoalRequiresInput(steps[i].goal)) break;
                }
                return false;
            }
        }
        public bool TerminalConversationPausesWorld => TerminalConversationActive && ConversationDefinition.pauseWorld || terminalConversations.Values.Any(d =>
            d.pauseWorld && d.replacesStartupGuide && terminalConversationProgress[d.id].phase == TerminalConversationPhase.Dormant && Day >= d.firstDay && Day <= d.lastDay &&
            !string.IsNullOrEmpty(d.requiredMailRead) && IsMailRead(d.requiredMailRead) && d.condition.Matches(this));
        public bool TerminalUsesGuidedStartup => terminalConversations.Values.Any(d => d.replacesStartupGuide);
        public string TerminalExpectedCommand => ConversationStep == null ? "" : ExpectedCommand(ConversationStep.goal, Conversation, Language);
        public TerminalGoalKind TerminalExpectedGoal => Conversation?.phase == TerminalConversationPhase.AwaitingCommand ? ConversationStep.goal.kind : TerminalGoalKind.None;
        public string TerminalDeclarationName => TerminalExpectedGoal == TerminalGoalKind.Allocate || TerminalExpectedGoal == TerminalGoalKind.BindReference ?
            GoalDataName(ConversationStep.goal, Conversation) : "";
        private TerminalAnswer[] WaitingAnswers => Conversation?.phase == TerminalConversationPhase.AwaitingCommand ?
            ConversationStep.answers : Array.Empty<TerminalAnswer>();
        public bool TerminalAwaitingAnswer => WaitingAnswers.Length > 0;
        public IEnumerable<string> TerminalAnswerKeys => WaitingAnswers.Select(answer => answer.key);
        public string TerminalAnswerUsage => string.Join(" / ", TerminalAnswerKeys);
        private string AnswerPromptChoices => " " + string.Concat(TerminalAnswerKeys.Select(key => "[" + key + "]"));
        public string TerminalAnswerPrompt => TerminalAwaitingAnswer ? Text(AnswerPromptKey, "대답>") + AnswerPromptChoices : "";
        public string TerminalQuestion => TerminalAwaitingAnswer ? string.Join("\n\n", ConversationStep.lines.Select(line =>
            Text(line.textKey, line.text).Replace("{slot}", Conversation.slotName).Replace("{command}", TerminalExpectedCommand))) : "";
        public bool IsTerminalAnswerKey(string key) => FindWaitingAnswer(key) != null;

        // A missing restriction means ordinary story permissions apply. A dormant tutorial starts with help alone.
        private IEnumerable<TerminalCommandGoal> TutorialGoals()
        {
            if (IsDayEnded || (!string.IsNullOrEmpty(StoryStartFact) && !AwaitingAssignment)) return null;
            TerminalConversationDefinition tutorial = terminalConversations.Values.FirstOrDefault(definition =>
                definition.unlockCommandsByStep && Day >= definition.firstDay && Day <= definition.lastDay &&
                terminalConversationProgress[definition.id].phase != TerminalConversationPhase.Completed && definition.condition.Matches(this));
            if (tutorial == null) return null;
            string step = terminalConversationProgress[tutorial.id].stepId;
            int current = Array.FindIndex(tutorial.steps, item => item.id == step);
            return tutorial.steps.Take(current + 1).Select(item => item.goal);
        }

        private static bool HasTutorialPermission(IEnumerable<TerminalCommandGoal> goals, TerminalGoalKind kind) =>
            goals == null || goals.Any(goal => goal.kind == kind);

        private static bool TutorialAllowsOperation(IEnumerable<TerminalCommandGoal> goals, Entity target, Operation operation, int arguments) =>
            goals == null || goals.Any(goal => goal.kind == TerminalGoalKind.Invoke && goal.targetId == target.Id &&
                goal.operation == operation.Code && (goal.argumentCount < 0 || goal.argumentCount == arguments));

        public bool CanUseTerminalKeyword(string keyword)
        {
            switch (keyword)
            {
                case "answer": return TerminalAwaitingAnswer;
                case "code": case "save": return HasTutorialPermission(TutorialGoals(), TerminalGoalKind.Allocate);
                case "free": return HasTutorialPermission(TutorialGoals(), TerminalGoalKind.Free);
                default: return true;
            }
        }

        private string TutorialPermissionMessage => Language == CommandLocale.Korean ?
            "아직 이 명령의 권한이 열리지 않았습니다." : "This command is not unlocked yet.";

        private string TutorialHelp()
        {
            var goals = TutorialGoals();
            if (goals == null) return null;
            string help = CommandVocabulary.KeywordName("help", Language);
            IEnumerable<string> commands = goals.Select(goal => ExpectedCommand(goal, Conversation, Language))
                .Where(command => !string.IsNullOrEmpty(command)).Prepend(help);
            if (TerminalAwaitingAnswer) commands = commands.Append(TerminalAnswerPrompt);
            return (Language == CommandLocale.Korean ? "사용 가능한 명령\n" : "Available commands\n") + string.Join("\n", commands.Distinct());
        }

        private void RequireTutorialStatementPermission(Statement statement)
        {
            var goals = TutorialGoals();
            if (goals == null) return;
            Expression value = statement.Value;
            if (statement.AssignPath != null || !string.IsNullOrEmpty(statement.DeclaredType) || value.Path == null)
                throw new CommandException(TutorialPermissionMessage);
            if (!value.IsCall && value.Path.Length == 1 && !protocols.ContainsKey(value.Path[0]) &&
                (HasTutorialPermission(goals, TerminalGoalKind.BindReference) ||
                statement.Assign == null && HasTutorialPermission(goals, TerminalGoalKind.Allocate) && data.ContainsKey(value.Path[0]))) return;
            if (!value.IsCall || statement.Assign != null || value.Path.Length != 2 || !HasTutorialPermission(goals, TerminalGoalKind.Invoke) ||
                value.Arguments.Any(argument => argument.IsCall || argument.Elements != null || argument.Path?.Length > 1))
                throw new CommandException(TutorialPermissionMessage);
            Entity target = ResolveEntity(new[] { value.Path[0] }, null);
            Datum[] arguments = value.Arguments.Select(argument => Evaluate(argument, null, null)).ToArray();
            Operation operation = ResolveOperation(target, value.Path[1], arguments, null);
            if (!TutorialAllowsOperation(goals, target, operation, arguments.Length)) throw new CommandException(TutorialPermissionMessage);
        }

        private TerminalAnswer FindWaitingAnswer(string key) =>
            WaitingAnswers.FirstOrDefault(answer => string.Equals(answer.key, key, StringComparison.OrdinalIgnoreCase));
        public double TerminalDisturbance
        {
            get
            {
                TerminalConversationProgress p = Conversation;
                if (!TerminalInputLocked || p.pendingIndex >= p.pending.Count || p.log[p.activeLog].complete) return 0;
                TerminalDialogueLine line = p.pending[p.pendingIndex];
                return line.emotion != TerminalEmotion.Angry || line.effectSeconds <= 0 ? 0 : Math.Max(0, 1 - p.elapsed / line.effectSeconds);
            }
        }
        public void RegisterTerminalConversation(TerminalConversationDefinition definition)
        {
            if (definition == null) throw new CommandException("터미널 대화가 비어 있습니다.");
            definition.Validate();
            if (terminalConversations.ContainsKey(definition.id)) throw new CommandException("터미널 대화 ID가 중복됩니다: " + definition.id);
            terminalConversations.Add(definition.id, definition.Copy());
            if (!terminalConversationProgress.ContainsKey(definition.id)) terminalConversationProgress.Add(definition.id, new TerminalConversationProgress { id = definition.id });
        }
        public TerminalConversationProgress ConversationProgress(string id) => id != null && terminalConversationProgress.TryGetValue(id, out var state) ? state.Copy() : null;
        public void EnterTerminalPage(string pageId)
        {
            if (TerminalConversationActive || IsDayEnded) return;
            foreach (TerminalConversationDefinition definition in terminalConversations.Values)
                if (definition.pageId == pageId && terminalConversationProgress[definition.id].phase == TerminalConversationPhase.Dormant && Day >= definition.firstDay && Day <= definition.lastDay &&
                    (string.IsNullOrEmpty(definition.requiredMailRead) || IsMailRead(definition.requiredMailRead)) && definition.condition.Matches(this))
                { StartTerminalConversation(definition.id); return; }
        }
        public void StartTerminalConversation(string id)
        {
            if (!terminalConversations.TryGetValue(id ?? "", out var definition)) throw new CommandException("터미널 대화가 없습니다: " + id);
            if (TerminalConversationActive || terminalConversationProgress[id].phase != TerminalConversationPhase.Dormant) return;
            var p = terminalConversationProgress[id]; activeTerminalConversation = id;
            string basis = Language == CommandLocale.Korean && !string.IsNullOrEmpty(definition.koreanVariableBase) ?
                definition.koreanVariableBase : definition.variableBase;
            string candidate = basis;
            int suffix = 2;
            while (true)
            {
                try { CheckName(candidate); if (!data.ContainsKey(candidate)) break; }
                catch (CommandException) { }
                candidate = basis + suffix++;
            }
            p.slotName = candidate; p.stepId = definition.steps[0].id;
            BeginConversationSpeech(ConversationStep.lines, false); ConversationChanged(true);
        }
        private void ConversationChanged(bool save)
        { terminalConversationRevision++; if (save) { Revision++; NarrativeUpdated(); } }
        private string GoalDataName(TerminalCommandGoal goal, TerminalConversationProgress p) => (goal.dataName ?? "").Replace("{slot}", p.slotName);
        private string ExpectedCommand(TerminalCommandGoal goal, TerminalConversationProgress p, CommandLocale locale)
        {
            string name = GoalDataName(goal, p);
            if (goal.kind == TerminalGoalKind.Help) return CommandVocabulary.KeywordName("help", locale);
            if (goal.kind == TerminalGoalKind.Allocate) return CommandVocabulary.KeywordName("code", locale) + " " + name;
            if (goal.kind == TerminalGoalKind.Free) return CommandVocabulary.KeywordName("free", locale) + " " + name;
            Entity target = Entity(goal.targetId); string receiver = target?.NameFor(locale) ?? goal.targetId;
            if (goal.kind == TerminalGoalKind.BindReference) return name + " = " + receiver;
            if (goal.kind == TerminalGoalKind.Invoke)
            {
                if (goal.preferVariable && data.TryGetValue(name, out DataSlot slot) && slot.value.CanReferenceEntity && slot.value.id == goal.targetId) receiver = name;
                Operation operation = target?.EffectiveOperations.FirstOrDefault(o => o.Code == goal.operation && (goal.argumentCount < 0 || o.Parameters.Length == goal.argumentCount));
                return receiver + "." + (operation?.NameFor(locale) ?? goal.operation) + "(" + (goal.argumentCount > 0 ? "..." : "") + ")";
            }
            return "";
        }
        private string ConversationText(TerminalConversationLog entry, TerminalConversationProgress p)
        {
            bool korean = Language == CommandLocale.Korean;
            string command = (korean ? entry.koreanCommand : entry.englishCommand) ?? "";
            // Older saves keep the generated lesson command alongside the dialogue.
            int separator = command.IndexOf(' ');
            string keyword = separator >= 0 ? CommandVocabulary.Keyword(command.Substring(0, separator)) : null;
            if (keyword == "save" || keyword == "code")
                command = command.Contains("=") ? command.Substring(separator + 1) : CommandVocabulary.KeywordName("code", Language) + command.Substring(separator);
            return Text(entry.textKey, entry.text).Replace("{slot}", p.slotName).Replace("{command}", command.TrimEnd(';')) + entry.suffix;
        }
        private int AddConversationLog(TerminalConversationLog entry)
        {
            var p = Conversation;
            if (p.log.Count >= 128)
            {
                int removed = p.activeLog == 0 ? 1 : 0; p.log.RemoveAt(removed);
                if (p.activeLog > removed) p.activeLog--;
            }
            p.log.Add(entry); ConversationChanged(false); return p.log.Count - 1;
        }
        public void AppendTerminalConversationReport(string text)
        { if (TerminalConversationActive) AddConversationLog(new TerminalConversationLog { text = "[보고] " + text, complete = true }); }
        private void BeginConversationSpeech(IEnumerable<TerminalDialogueLine> lines, bool reply)
        {
            var p = Conversation; p.pending = lines.Select(l => l.Copy()).ToList(); p.pendingIndex = 0; p.reply = reply; p.activeLog = -1; p.elapsed = 0;
            if (p.pending.Count == 0) { ReadyForConversationCommand(); return; }
            p.phase = TerminalConversationPhase.Speaking; BeginConversationLine();
        }
        private void BeginConversationLine()
        {
            var p = Conversation; TerminalDialogueLine line = p.pending[p.pendingIndex];
            p.elapsed = 0; p.activeLog = -1;
            bool hasSpeaker = !string.IsNullOrEmpty(line.speaker) || !string.IsNullOrEmpty(line.speakerKey);
            p.activeLog = AddConversationLog(new TerminalConversationLog { text = line.text, textKey = line.textKey,
                speaker = hasSpeaker ? line.speaker : ConversationDefinition.speaker, speakerKey = hasSpeaker ? line.speakerKey : ConversationDefinition.speakerKey,
                englishCommand = ExpectedCommand(ConversationStep.goal, p, CommandLocale.English), koreanCommand = ExpectedCommand(ConversationStep.goal, p, CommandLocale.Korean) });
        }
        public void AdvanceTerminalConversation(double seconds, bool visible = true)
        {
            if (!visible || seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) return;
            if (Conversation?.phase == TerminalConversationPhase.AwaitingCommand && ConversationStep.goal.kind == TerminalGoalKind.StoryCondition && ConversationStep.goal.condition.Matches(this)) CompleteConversationStep();
            int budget = 4096;
            while (TerminalInputLocked && seconds > 0 && budget-- > 0)
            {
                var p = Conversation; var line = p.pending[p.pendingIndex]; var entry = p.log[p.activeLog];
                if (entry.complete)
                {
                    if (TerminalLineUsesEnter) break;
                    FinishTerminalLine();
                    ConversationChanged(false);
                    continue;
                }
                string text = ConversationText(entry, p); int length = StringInfo.ParseCombiningCharacters(text).Length;
                bool waitForEnter = TerminalLineUsesEnter;
                bool handsOffToInput = ConversationDefinition.advanceWithEnter && !waitForEnter &&
                    (p.reply || IsUnanswered(ConversationStep) || GoalRequiresInput(ConversationStep.goal));
                double duration = length / line.charactersPerSecond + (handsOffToInput ? 0 : line.pauseAfter);
                double used = Math.Min(seconds, Math.Max(0, duration - p.elapsed)); p.elapsed += used; seconds -= used;
                int visibleElements = Math.Min(length, (int)Math.Min(int.MaxValue, Math.Floor(p.elapsed * line.charactersPerSecond)));
                if (visibleElements != entry.visibleElements) { entry.visibleElements = visibleElements; ConversationChanged(false); }
                if (p.elapsed + 0.0000001 < duration) break;
                entry.complete = true;
                if (!waitForEnter) FinishTerminalLine();
                ConversationChanged(false);
            }
        }
        public bool ContinueTerminalLine()
        {
            if (!TerminalInputLocked || !ConversationDefinition.advanceWithEnter) return false;
            TerminalConversationLog line = Conversation.log[Conversation.activeLog];
            if (line.complete || !TerminalLineUsesEnter)
            {
                line.complete = true;
                FinishTerminalLine();
            }
            else line.complete = true;
            ConversationChanged(false);
            return true;
        }
        private void FinishTerminalLine()
        {
            TerminalConversationProgress p = Conversation;
            p.activeLog = -1; p.pendingIndex++;
            if (p.pendingIndex < p.pending.Count) BeginConversationLine();
            else ReadyForConversationCommand();
        }
        private bool ConversationGoalSatisfied(TerminalCommandGoal goal)
        {
            var p = Conversation; string name = GoalDataName(goal, p);
            if (goal.kind == TerminalGoalKind.Allocate) return data.ContainsKey(name);
            if (goal.kind == TerminalGoalKind.BindReference) return data.TryGetValue(name, out DataSlot slot) && slot.value.CanReferenceEntity && slot.value.id == goal.targetId;
            if (goal.kind == TerminalGoalKind.Free) return !data.ContainsKey(name);
            if (goal.kind == TerminalGoalKind.Invoke) return p.performed.Any(a => a.targetId == goal.targetId && a.operation == goal.operation && (goal.argumentCount < 0 || a.arguments == goal.argumentCount));
            return goal.kind == TerminalGoalKind.StoryCondition && goal.condition.Matches(this);
        }
        private bool IsUnanswered(TerminalConversationStep step) =>
            step.answers.Length > 0 && string.IsNullOrEmpty(Conversation.answerKey);

        private bool GoalRequiresInput(TerminalCommandGoal goal) =>
            goal.kind != TerminalGoalKind.None && (!goal.acceptExisting || !ConversationGoalSatisfied(goal));

        private void ReadyForConversationCommand()
        {
            var p = Conversation; p.pending.Clear(); p.pendingIndex = 0; p.activeLog = -1; p.elapsed = 0;
            if (p.reply) { p.reply = false; p.phase = TerminalConversationPhase.AwaitingCommand; return; }
            TerminalConversationStep step = ConversationStep;
            if (IsUnanswered(step))
            {
                p.phase = TerminalConversationPhase.AwaitingCommand;
                AddConversationLog(new TerminalConversationLog
                {
                    text = "대답>",
                    textKey = AnswerPromptKey,
                    suffix = AnswerPromptChoices,
                    complete = true
                });
                ConversationChanged(true);
                return;
            }
            if (!GoalRequiresInput(step.goal)) CompleteConversationStep();
            else p.phase = TerminalConversationPhase.AwaitingCommand;
        }
        private void CompleteConversationStep()
        {
            var p = Conversation; var definition = ConversationDefinition; var step = ConversationStep;
            if (!string.IsNullOrEmpty(p.answerKey))
                ApplyStoryEffects(step.answers.FirstOrDefault(answer => answer.key == p.answerKey)?.effects);
            p.answerKey = "";
            ApplyStoryEffects(step.onComplete);
            int next = Array.IndexOf(definition.steps, step) + 1;
            if (next >= definition.steps.Length) { p.phase = TerminalConversationPhase.Completed; p.activeLog = -1; definition.onComplete.Validate(); ApplyStoryEffects(definition.onComplete); }
            else { p.stepId = definition.steps[next].id; BeginConversationSpeech(definition.steps[next].lines, false); }
            ConversationChanged(true);
        }
        private ExecutionResult PrepareTerminalAnswer(string[] words)
        {
            if (!TerminalAwaitingAnswer)
                return ExecutionResult.Error(Text("terminal.dialogue.answer.none", "지금 답할 질문이 없습니다."));
            TerminalAnswer selected = words.Length == 2 ? FindWaitingAnswer(words[1]) : null;
            if (selected == null)
                return ExecutionResult.Error(Text("terminal.dialogue.answer.invalid", "표시된 선택지 중 하나를 입력하세요.") + " " + TerminalAnswerUsage);
            return AcceptTerminalAnswer(selected);
        }

        private ExecutionResult AcceptTerminalAnswer(TerminalAnswer selected)
        {
            terminalCommandTrace.answer = selected;
            return ExecutionResult.Ok(Text("terminal.dialogue.answer.sent", "응답을 전송했습니다."));
        }

        private void ObserveTerminalCommand(TerminalCommandTrace trace, ExecutionResult result)
        {
            var p = Conversation; var goal = ConversationStep.goal; string name = GoalDataName(goal, p);
            if (!result.Success) return; // Typos and failed actions are not disobedience.
            foreach (TerminalActionProof proof in trace.actions)
                if (!p.performed.Any(a => a.targetId == proof.targetId && a.operation == proof.operation && a.arguments == proof.arguments)) p.performed.Add(proof.Copy());
            if (TerminalAwaitingAnswer)
            {
                if (trace.answer != null)
                {
                    p.answerKey = trace.answer.key;
                    BeginConversationSpeech(trace.answer.reply, false);
                    ConversationChanged(true);
                }
                return;
            }
            bool matched = goal.kind == TerminalGoalKind.Help && trace.keyword == "help" || goal.kind == TerminalGoalKind.Allocate && trace.keyword == "save" && trace.argument == name ||
                goal.kind == TerminalGoalKind.Free && trace.keyword == "free" && trace.argument == name || goal.kind == TerminalGoalKind.BindReference && trace.assigned == name && result.Value.CanReferenceEntity && result.Value.id == goal.targetId ||
                goal.kind == TerminalGoalKind.Invoke && trace.actions.Any(a => a.targetId == goal.targetId && a.operation == goal.operation && (goal.argumentCount < 0 || a.arguments == goal.argumentCount)) ||
                goal.kind == TerminalGoalKind.StoryCondition && goal.condition.Matches(this);
            if (matched) { CompleteConversationStep(); return; }
            if (trace.keyword == "help") return; // Asking for help is always permitted.
            p.deviations = Math.Min(int.MaxValue - 1, p.deviations) + 1; var definition = ConversationDefinition;
            bool angry = p.deviations >= definition.angryAfter;
            TerminalDialogueLine[] replies = angry ? definition.angryReplies : definition.forgivingReplies;
            if (replies.Length > 0)
            {
                int index = angry ? p.deviations - definition.angryAfter : p.deviations - 1;
                var reply = replies[Math.Min(index, replies.Length - 1)].Copy(); if (angry) reply.emotion = TerminalEmotion.Angry;
                BeginConversationSpeech(new[] { reply }, true);
            }
            ConversationChanged(true);
        }
        public string TerminalConversationTranscript(bool includeAnswerPrompts = true)
        {
            var p = Conversation; if (p == null) return "";
            var text = new StringBuilder();
            foreach (TerminalConversationLog entry in p.log)
            {
                if (!includeAnswerPrompts && entry.textKey == AnswerPromptKey) continue;
                string line = ConversationText(entry, p);
                if (!entry.complete)
                {
                    int[] elements = StringInfo.ParseCombiningCharacters(line);
                    int count = Math.Min(entry.visibleElements, elements.Length); line = count >= elements.Length ? line : line.Substring(0, elements[count]);
                }
                if (text.Length > 0) text.Append("\n\n");
                if (!string.IsNullOrEmpty(entry.speaker) || !string.IsNullOrEmpty(entry.speakerKey)) text.Append('[').Append(Text(entry.speakerKey, entry.speaker)).Append("] ");
                text.Append(line); if (!entry.complete) text.Append('▌');
            }
            return text.ToString();
        }
        private void RestoreTerminalConversations(KernelSnapshot snapshot)
        {
            var restored = new Dictionary<string, TerminalConversationProgress>(StringComparer.Ordinal);
            foreach (TerminalConversationProgress item in snapshot.terminalConversations ?? Array.Empty<TerminalConversationProgress>())
            {
                if (item == null || string.IsNullOrWhiteSpace(item.id) || restored.ContainsKey(item.id) || !Enum.IsDefined(typeof(TerminalConversationPhase), item.phase) || item.deviations < 0 ||
                    item.pending == null || item.pending.Count > 1024 || item.pending.Any(l => l == null || l.text == null || l.text.Length > 4096 || l.charactersPerSecond <= 0 || double.IsNaN(l.charactersPerSecond) || double.IsInfinity(l.charactersPerSecond) ||
                        l.pauseAfter < 0 || double.IsNaN(l.pauseAfter) || double.IsInfinity(l.pauseAfter) || l.effectSeconds < 0 || double.IsNaN(l.effectSeconds) || double.IsInfinity(l.effectSeconds) || !Enum.IsDefined(typeof(TerminalEmotion), l.emotion)) ||
                    item.log == null || item.performed == null || item.performed.Any(a => a == null || a.targetId == null || a.operation == null || a.arguments < 0) || item.log.Count > 128 || item.pendingIndex < 0 || item.pendingIndex > item.pending.Count ||
                    item.elapsed < 0 || double.IsNaN(item.elapsed) || double.IsInfinity(item.elapsed) || item.log.Any(l => l == null || l.text == null || l.visibleElements < 0) ||
                    item.phase == TerminalConversationPhase.Speaking && (item.activeLog < 0 || item.activeLog >= item.log.Count || item.pendingIndex >= item.pending.Count))
                    throw new CommandException("저장된 터미널 대화 상태가 유효하지 않습니다.");
                var state = item.Copy();
                if (terminalConversations.TryGetValue(state.id, out var definition) && state.phase != TerminalConversationPhase.Dormant && !definition.steps.Any(s => s.id == state.stepId)) state.phase = TerminalConversationPhase.Completed;
                restored.Add(state.id, state);
            }
            foreach (var definition in terminalConversations.Values)
                if (!restored.ContainsKey(definition.id)) restored.Add(definition.id, new TerminalConversationProgress { id = definition.id,
                    phase = definition.skipIfMailAlreadyReadOnExistingSave && !string.IsNullOrEmpty(definition.requiredMailRead) && IsMailRead(definition.requiredMailRead) ? TerminalConversationPhase.Completed : TerminalConversationPhase.Dormant });
            if (restored.Values.Count(p => terminalConversations.ContainsKey(p.id) && (p.phase == TerminalConversationPhase.Speaking || p.phase == TerminalConversationPhase.AwaitingCommand)) > 1)
                throw new CommandException("여러 터미널 대화를 동시에 복원할 수 없습니다.");
            terminalConversationProgress.Clear(); foreach (var item in restored) terminalConversationProgress.Add(item.Key, item.Value);
            activeTerminalConversation = snapshot.activeTerminalConversation;
            TerminalConversationProgress active = restored.Values.FirstOrDefault(p => terminalConversations.ContainsKey(p.id) && (p.phase == TerminalConversationPhase.Speaking || p.phase == TerminalConversationPhase.AwaitingCommand));
            if (active != null) activeTerminalConversation = active.id;
            else if (Conversation == null) activeTerminalConversation = null;
            ConversationChanged(false);
        }
    }
}
