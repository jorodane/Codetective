using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Codetective.Operations
{
    public sealed partial class KernelRuntime
    {
        private readonly Dictionary<string, Entity> roots = new Dictionary<string, Entity>(StringComparer.Ordinal);
        private readonly Dictionary<string, Entity> entities = new Dictionary<string, Entity>(StringComparer.Ordinal);
        private readonly Dictionary<string, DataSlot> data = new Dictionary<string, DataSlot>(StringComparer.Ordinal);
        private readonly Dictionary<string, Protocol> protocols = new Dictionary<string, Protocol>(StringComparer.Ordinal);
        private int nextDataId = 1;
        private int budget;
        private int depth;
        private const string FrozenCipherScope = "\u0001cipher";
        public int Revision { get; private set; }

        public readonly HashSet<string> Facts = new HashSet<string>(StringComparer.Ordinal);
        public readonly HashSet<string> Authorities = new HashSet<string>(StringComparer.Ordinal);
        public readonly List<ThreadAgent> Threads = new List<ThreadAgent>();
        public readonly List<ConsultationLine> Consultation = new List<ConsultationLine>();
        public readonly HashSet<string> RecordedClues = new HashSet<string>(StringComparer.Ordinal);
        public IReadOnlyDictionary<string, DataSlot> Data => data;
        public IReadOnlyDictionary<string, Protocol> Protocols => protocols;
        public bool DryRun { get; private set; }
        public event Action<string> Message;
        public event Action<string, string> SceneActionRequested;
        public event Action DayEnded;

        public const string Help = "code <name> = <target> : register a substitution\n" +
            "code <name> : register an empty substitution\nfree <name> : discard a substitution\n" +
            "answer <choice> : respond to the current question (or enter just the choice)\nhelp : list commands\n\n" +
            "Assign references to existing objects; new/malloc are unavailable.\n" +
            "object. lists currently available commands.\nname = target\nname = object.command(input)\n" +
            "Examples: hq.requestCoffee()\ndoc = archive.inspect()\nthread01.run(recon, archive)\n" +
            "Protocol editor: protocol recon(Place target) { return target.inspect(); }";

        public void Register(Entity entity, params string[] rootAliases) => Register(entity, true, rootAliases);
        public void Register(Entity entity, bool initiallyKnown, params string[] rootAliases)
        {
            foreach (string alias in entity.AllNames.Concat(rootAliases))
            {
                if (!CommandParser.IsIdentifier(alias) || CommandVocabulary.IsReservedKeyword(alias) || CommandParser.IsAllocationKeyword(alias) ||
                    data.ContainsKey(alias) || protocols.ContainsKey(alias)) throw new CommandException("객체 이름이 유효하지 않거나 예약된 이름입니다: " + alias);
                if (roots.TryGetValue(alias, out Entity other) && other != entity) throw new CommandException("객체 별칭이 중복됩니다: " + alias);
            }
            IndexEntity(entity, new HashSet<Entity>());
            foreach (string alias in entity.AllNames.Concat(rootAliases)) roots[alias] = entity;
            if (initiallyKnown)
            {
                var pending = new Stack<Entity>(); var visited = new HashSet<Entity>(); pending.Push(entity);
                while (pending.Count > 0)
                {
                    Entity initial = pending.Pop(); if (!visited.Add(initial)) continue;
                    Discover(initial.Id, "초기 작전망", true);
                    foreach (Entity child in initial.Children.Values) pending.Push(child);
                }
            }
            Revision++;
        }

        private void IndexEntity(Entity entity, HashSet<Entity> visited)
        {
            if (!visited.Add(entity)) return;
            if (string.IsNullOrWhiteSpace(entity.Id) || entity.Id.Length > 256) throw new CommandException("객체 ID가 유효하지 않습니다: " + entity.Id);
            if (entity.Schema == null) entity.Schema = Schemas.ForKind(entity.Kind);
            Schemas.Register(entity.Schema);
            if (entities.TryGetValue(entity.Id, out Entity other) && other != entity) throw new CommandException("객체 ID가 중복됩니다: " + entity.Id);
            entities[entity.Id] = entity;
            ConfigureInstance(entity);
            foreach (var child in entity.Children)
            {
                foreach (string name in child.Value.AllNames.Concat(new[] { child.Key }))
                    if (entity.FindVariable(name) != null || entity.Find(name) != null) throw new CommandException("하위 개체 이름이 멤버와 충돌합니다: " + name);
                IndexEntity(child.Value, visited);
            }
            entity.ValidateMembers();
        }

        public Entity Entity(string id) => !string.IsNullOrEmpty(id) && entities.TryGetValue(id, out Entity entity) ? entity : null;
        public ThreadAgent Agent(string id) => Threads.FirstOrDefault(t => t.Id == id);
        public void Report(string message) { Revision++; Message?.Invoke(message); }

        public DataSlot Allocate(string name)
        {
            name = Normalize(name);
            CheckName(name);
            if (data.ContainsKey(name)) throw new CommandException($"'{name}' 공간은 이미 할당되어 있습니다.");
            var slot = new DataSlot { id = nextDataId++, name = name };
            data.Add(name, slot);
            Revision++;
            return slot;
        }

        public void Store(string name, Datum value)
            => Store(name, value, "명령 결과");
        private void Store(string name, Datum value, string evidenceSource)
        {
            name = Normalize(name);
            CheckName(name);
            if (!data.TryGetValue(name, out DataSlot slot)) slot = Allocate(name);
            if (!string.IsNullOrEmpty(slot.declaredType))
            {
                Parameter type = DataTypes.Parameter(name, slot.declaredType);
                if (!Accepts(type, value)) throw new CommandException("치환 대상의 자료형이 다릅니다: " + slot.declaredType);
            }
            slot.value = NormalizeReference(value);
            Revision++;
        }

        public bool Free(string name) { bool removed = data.Remove(Normalize(name)); if (removed) Revision++; return removed; }

        private void CheckName(string name)
        {
            if (!CommandParser.IsIdentifier(name)) throw new CommandException("이름은 글자 또는 _로 시작하고 글자·숫자·_만 사용하세요.");
            if (roots.ContainsKey(name) || protocols.ContainsKey(name) || CommandVocabulary.IsReservedKeyword(name) || CommandParser.IsAllocationKeyword(name))
                throw new CommandException("객체·프로토콜·예약 명령과 다른 데이터 이름을 사용하세요.");
        }

        public ExecutionResult Execute(string source)
        {
            if (TerminalInputLocked) return ExecutionResult.Error(Text("terminal.dialogue.receiving", "수신 중입니다. 안내가 끝난 뒤 입력하세요."));
            bool observed = TerminalConversationActive && terminalCommandTrace == null && !string.IsNullOrWhiteSpace(source);
            TerminalCommandTrace previous = terminalCommandTrace;
            if (observed)
            {
                terminalCommandTrace = new TerminalCommandTrace();
                AddConversationLog(new TerminalConversationLog { text = "> " + source, complete = true });
            }
            try
            {
                ExecutionResult result = ExecuteTerminalCommand(source);
                if (observed)
                {
                    AddConversationLog(new TerminalConversationLog { text = (result.Success ? "" : "오류 ") + result.Message, complete = true });
                    ObserveTerminalCommand(terminalCommandTrace, result);
                }
                return result;
            }
            finally { terminalCommandTrace = previous; }
        }
        private ExecutionResult ExecuteTerminalCommand(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return ExecutionResult.Error("");
            if (source.IndexOfAny(new[] { '\r', '\n' }) >= 0) return ExecutionResult.Error("한 번에 한 줄만 입력하세요. 여러 줄은 프로토콜 편집창에서 작성하세요.");
            try
            {
                source = Normalize(source.Trim()).TrimEnd(';').TrimEnd();
                if (source.Length == 0) return ExecutionResult.Error("실행할 명령을 입력하세요.");
                TerminalAnswer shortAnswer = FindWaitingAnswer(source);
                if (shortAnswer != null) return AcceptTerminalAnswer(shortAnswer);
                string[] words = source.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                string keyword = CommandVocabulary.Keyword(words[0]);
                // A declaration with a right-hand side is parsed as an assignment.
                // A bare declaration uses the same operation as the older save command.
                if (keyword == "code" && words.Length == 2 && CommandParser.IsIdentifier(words[1]))
                    keyword = "save";
                if (terminalCommandTrace != null) { terminalCommandTrace.keyword = keyword; terminalCommandTrace.argument = words.Length == 2 ? words[1] : ""; }
                if (keyword == "help") return words.Length == 1 ? ExecutionResult.Ok(HelpForLanguage) : ExecutionResult.Error("사용법: " + words[0]);
                if (keyword == "answer" && (words.Length == 1 ? TerminalAwaitingAnswer :
                    char.IsLetterOrDigit(words[1][0]) || words[1][0] == '_'))
                    return PrepareTerminalAnswer(words);
                if (keyword == "save" || keyword == "free")
                {
                    if (words.Length != 2) return ExecutionResult.Error($"사용법: {words[0]} 이름");
                    if (keyword == "save") { Allocate(words[1]); return ExecutionResult.Ok($"'{words[1]}' 치환문을 등록했습니다."); }
                    return Free(words[1]) ? ExecutionResult.Ok($"'{words[1]}' 치환 규칙을 폐기했습니다.") :
                        ExecutionResult.Error($"'{words[1]}'에 할당된 데이터가 없습니다.");
                }
                budget = 1024; depth = 0;
                Statement statement = new CommandParser(source).ReadSingle();
                if (terminalCommandTrace != null) terminalCommandTrace.assigned = statement.Assign;
                Datum value = ExecuteStatement(statement, null, null);
                Revision++;
                return ExecutionResult.Ok(value.kind == DataKind.Empty ? "명령을 수행했습니다." : Format(value), value);
            }
            catch (CommandException error) { return ExecutionResult.Error(error.Message + " (" + CommandVocabulary.KeywordName("help", Language) + "로 사용법 확인)"); }
        }

        public ExecutionResult SaveProtocol(string source)
        {
            try
            {
                Protocol protocol = new CommandParser(source).ReadProtocol(source);
                ValidateProtocolTypes(protocol);
                if (roots.ContainsKey(protocol.Name) || data.ContainsKey(protocol.Name) || CommandVocabulary.IsReservedKeyword(protocol.Name) || CommandParser.IsAllocationKeyword(protocol.Name))
                    throw new CommandException("다른 객체·데이터·기본 명령과 다른 프로토콜 이름을 사용하세요.");
                protocols[protocol.Name] = protocol;
                Revision++;
                return ExecutionResult.Ok("프로토콜 저장: " + protocol.SignatureFor(Language));
            }
            catch (CommandException error) { return ExecutionResult.Error(error.Message); }
        }

        public ExecutionResult TestProtocol(string source, string call)
        {
            KernelRuntime sandbox = CopyWorld();
            ExecutionResult saved = sandbox.SaveProtocol(source);
            return saved.Success ? sandbox.Execute(call) : saved;
        }

        internal Datum ExecuteStatement(Statement statement, Dictionary<string, Datum> locals, string agent)
        {
            string previous = executingAgent; executingAgent = agent;
            try { return ExecuteStatementCore(statement, locals, agent); }
            finally { executingAgent = previous; }
        }
        private Datum ExecuteStatementCore(Statement statement, Dictionary<string, Datum> locals, string agent)
        {
            if (--budget < 0) throw new CommandException("한 번의 실행 예산(1024단계)을 초과했습니다.");
            if (locals == null && statement.Assign != null) CheckName(statement.Assign);
            if (!string.IsNullOrEmpty(statement.DeclaredType)) Schemas.ValidateType(statement.DeclaredType);
            Entity assignedEntity = null;
            VariableDefinition assignedField = null;
            if (statement.AssignPath != null)
            {
                assignedEntity = ResolveEntity(statement.AssignPath.Take(statement.AssignPath.Length - 1).ToArray(), locals);
                assignedField = CheckVariableWrite(assignedEntity, statement.AssignPath.Last(), locals);
            }
            if (statement.ValidateOnly) return Datum.Empty;
            Datum value = Evaluate(statement.Value, locals, agent);
            if (!string.IsNullOrEmpty(statement.DeclaredType) && !Accepts(DataTypes.Parameter(statement.Assign, statement.DeclaredType), value))
                throw new CommandException("선언한 치환 자료형과 대상이 다릅니다: " + statement.DeclaredType);
            if (assignedField != null)
            {
                if (value.kind != DataKind.Empty && !Schemas.Accepts(assignedField.Kind, assignedField.SchemaId, value, Entity))
                    throw new CommandException("변수에 저장할 자료형이 맞지 않습니다: " + assignedField.Code);
                assignedEntity.SetValue(assignedField.Code, value);
                Observe(new ObservationDefinition { subjectId = assignedEntity.Id, field = assignedField.Code, value = value, source = agent ?? "Kernel 명령" });
                Revision++;
            }
            if (statement.Assign != null)
            {
                if (locals != null) locals[statement.Assign] = value.Copy();
                else
                {
                    Store(statement.Assign, value);
                    if (!string.IsNullOrEmpty(statement.DeclaredType)) data[statement.Assign].declaredType = statement.DeclaredType;
                    if ((value.kind == DataKind.Array || value.kind == DataKind.Dictionary) && !string.IsNullOrEmpty(data[statement.Assign].declaredType))
                        data[statement.Assign].value.schemaId = data[statement.Assign].declaredType;
                }
            }
            return value;
        }

        internal Datum Evaluate(Expression expression, Dictionary<string, Datum> locals, string agent)
        {
            if (expression.Elements != null) return CollectionValue.Create(expression.Dictionary ? DataKind.Dictionary : DataKind.Array, expression.Elements.Select(e => Evaluate(e, locals, agent)));
            if (expression.Literal != null) return expression.Literal.Copy();
            string[] path = expression.Path;
            if (expression.IsCall && path.Length == 1 && protocols.TryGetValue(path[0], out Protocol protocol))
                return RunProtocol(protocol, expression.Arguments.Select(e => Evaluate(e, locals, agent)).ToArray(), agent);
            if (expression.IsCall)
            {
                if (path.Length < 2) throw new CommandException("알 수 없는 프로토콜/명령입니다: " + path[0]);
                Entity target = ResolveEntity(path.Take(path.Length - 1).ToArray(), locals);
                if (IsDayEnded && !DryRun) throw new CommandException("오늘의 업무 시간이 끝났습니다. 다음 날로 진행하세요.");
                Datum[] args = expression.Arguments.Select(e => Evaluate(e, locals, agent)).ToArray();
                Operation operation = ResolveOperation(target, path.Last(), args, locals);
                return ObserveEvidence(Invoke(target, operation, args, agent), locals, agent, true);
            }
            if (path.Length == 1)
            {
                if (locals != null && locals.TryGetValue(path[0], out Datum local)) return PlayerValue(local, locals);
                if ((locals == null || !locals.ContainsKey(FrozenCipherScope)) && data.TryGetValue(path[0], out DataSlot slot)) return PlayerValue(slot.value, locals);
                if (protocols.ContainsKey(path[0])) return Datum.Ref(DataKind.Protocol, path[0], path[0]);
            }
            try
            {
                Entity target = ResolveEntity(path, locals);
                return Reference(target);
            }
            catch (CommandException)
            {
                if (path.Length < 2) throw;
                Entity target = ResolveEntity(path.Take(path.Length - 1).ToArray(), locals);
                VariableDefinition field = target.FindVariable(path.Last());
                if (field != null)
                {
                    CheckVariableAccess(target, field, locals);
                    return ObserveEvidence(ReadPlayerField(target, field), locals, agent);
                }
                Operation property = target.Find(path.Last());
                if (property == null || !property.Property) throw new CommandException("알 수 없는 데이터/속성입니다: " + path.Last());
                CheckOperationAccess(target, property, locals);
                return ObserveEvidence(Invoke(target, property, Array.Empty<Datum>(), agent), locals, agent, true);
            }
        }

        internal Entity ResolveEntity(string[] path, Dictionary<string, Datum> locals)
        {
            if (path.Length == 0) throw new CommandException("대상을 지정하세요.");
            Entity entity = null;
            if (locals != null && locals.TryGetValue(path[0], out Datum local)) entity = ReferenceTarget(local);
            else if ((locals == null || !locals.ContainsKey(FrozenCipherScope)) && data.TryGetValue(path[0], out DataSlot slot)) entity = ReferenceTarget(slot.value);
            else roots.TryGetValue(path[0], out entity);
            if (entity == null && IsDiscovered(path[0])) entity = Entity(path[0]);
            if (entity == null) throw new CommandException("알 수 없거나 비어 있는 대상입니다: " + path[0]);
            CheckEntityAccess(entity, locals);
            for (int i = 1; i < path.Length; i++)
            {
                Entity child = string.IsNullOrEmpty(executingAgent) ? KnownChildren(entity).FirstOrDefault(p => p.Key == path[i] || p.Value.AllNames.Contains(path[i])).Value : entity.Child(path[i]);
                if (child == null && entity.FindVariable(path[i]) is VariableDefinition field)
                {
                    CheckVariableAccess(entity, field, locals);
                    child = ReferenceTarget(ReadPlayerField(entity, field));
                }
                entity = child;
                if (entity == null) throw new CommandException("알 수 없는 대상 경로입니다: " + path[i]);
                CheckEntityAccess(entity, locals);
            }
            return entity;
        }

        private Datum RunProtocol(Protocol protocol, Datum[] arguments, string agent)
        {
            if (++depth > 24) { depth--; throw new CommandException("프로토콜 호출 깊이(24)를 초과했습니다."); }
            try
            {
                Dictionary<string, Datum> locals = Bind(protocol, arguments, string.IsNullOrEmpty(agent) ? null : Agent(agent)?.CipherTable);
                foreach (Statement statement in protocol.Body)
                {
                    Datum value = ExecuteStatement(statement, locals, agent);
                    if (statement.Return) return value;
                }
                return Datum.Empty;
            }
            finally { depth--; }
        }

        internal Dictionary<string, Datum> Bind(Protocol protocol, Datum[] arguments, Dictionary<string, Datum> cipher = null)
        {
            Validate(protocol.Parameters, arguments, false);
            var locals = cipher == null ? new Dictionary<string, Datum>(StringComparer.Ordinal) : cipher.ToDictionary(p => p.Key, p => p.Value.Copy(), StringComparer.Ordinal);
            if (cipher != null) locals[FrozenCipherScope] = Datum.Empty;
            for (int i = 0; i < arguments.Length; i++) locals[protocol.Parameters[i].Name] = arguments[i].Copy();
            return locals;
        }

        private void Validate(Parameter[] parameters, Datum[] arguments, bool variadic)
        {
            if (arguments.Length < parameters.Length || (!variadic && arguments.Length != parameters.Length))
                throw new CommandException("입력 데이터 개수가 맞지 않습니다.");
            for (int i = 0; i < parameters.Length; i++)
                if (!Accepts(parameters[i], arguments[i]))
                    throw new CommandException($"{parameters[i].Name}에는 {parameters[i].TypeName(Language)} 데이터가 필요합니다. 현재: {TypeName(arguments[i])}");
        }

        private void CheckAccess(AccessRule rule, Dictionary<string, Datum> locals = null, Entity target = null)
        {
            string denial = rule.Denial(this, locals?.Values, target, true);
            if (denial != null) throw new CommandException(denial);
        }

        internal double Duration(Statement statement, Dictionary<string, Datum> locals)
        {
            Expression e = statement.Value;
            if (!e.IsCall || e.Path.Length < 2) return 0;
            Entity target = ResolveEntity(e.Path.Take(e.Path.Length - 1).ToArray(), locals);
            Datum[] arguments = e.Arguments.Select(a => Evaluate(a, locals, executingAgent)).ToArray();
            return Math.Max(0, ResolveOperation(target, e.Path.Last(), arguments, locals).Minutes);
        }

        public IReadOnlyList<Completion> Complete(string source, bool composing = false) => Complete(source, composing, null);

        private IReadOnlyList<Completion> Complete(string source, bool composing, Dictionary<string, Datum> locals)
        {
            source = source ?? "";
            int equal = source.LastIndexOf('=');
            string leading = equal >= 0 ? source.Substring(0, equal + 1) : "";
            string input = source.Substring(equal + 1).TrimStart();
            leading += source.Substring(equal + 1, source.Substring(equal + 1).Length - input.Length);
            int open = input.LastIndexOf('(');
            if (open >= 0 && input.LastIndexOf(')') < open)
            {
                int comma = input.LastIndexOf(',');
                int start = Math.Max(open, comma) + 1;
                string prefix = input.Substring(start).TrimStart();
                string before = leading + input.Substring(0, start) + input.Substring(start, input.Substring(start).Length - prefix.Length);
                if (prefix.Contains("."))
                    return Complete(prefix, composing, locals).Select(c => new Completion(c.Label, before + c.Replacement)).ToArray();
                Parameter[] expected = { new Parameter("input", DataKind.Any) };
                try
                {
                    string[] call = input.Substring(0, open).Trim().Split('.');
                    int argumentIndex = input.Substring(open + 1).Count(c => c == ',');
                    if (call.Length == 1 && protocols.TryGetValue(call[0], out Protocol p) && argumentIndex < p.Parameters.Length) expected = new[] { p.Parameters[argumentIndex] };
                    else if (call.Length > 1)
                    {
                        Entity target = ResolveCompletionEntity(call.Take(call.Length - 1).ToArray(), locals);
                        Parameter[] options = target.EffectiveOperations.Where(o => o.Matches(call.Last()) && OperationDenial(target, o, locals) == null && argumentIndex < o.Parameters.Length)
                            .Select(o => o.Parameters[argumentIndex]).ToArray();
                        if (options.Length > 0) expected = options;
                    }
                }
                catch (CommandException) { }
                return AvailableValues(locals, prefix, composing).Where(v => expected.Any(p => Accepts(p, v.Value)) && Prefix(v.Key, prefix, composing))
                    .Select(v => new Completion(v.Key + " : " + TypeName(v.Value), before + v.Key)).ToArray();
            }
            int dot = input.LastIndexOf('.');
            if (dot >= 0)
            {
                string objectPath = input.Substring(0, dot);
                string prefix = input.Substring(dot + 1);
                try
                {
                    Entity entity = ResolveCompletionEntity(objectPath.Split('.'), locals);
                    var output = new List<Completion>();
                    foreach (var child in KnownChildren(entity))
                    {
                        string preferred = CommandVocabulary.Name(child.Key, child.Value.Names, Language);
                        string name = CommandVocabulary.Match(preferred, child.Value.AllNames.Concat(new[] { child.Key }), prefix, composing);
                        if (name != null && EntityDenial(child.Value, locals) == null)
                            output.Add(new Completion(name + " · " + Text(child.Value.LabelKey, child.Value.LabelFor(Language)), leading + objectPath + "." + name + (equal < 0 ? "." : "")));
                    }
                    foreach (VariableDefinition field in entity.Variables)
                    {
                        string name = CommandVocabulary.Match(field.NameFor(Language), field.AllNames, prefix, composing);
                        if (name != null && VariableDenial(entity, field, locals) == null)
                            output.Add(new Completion(name + " : " + (string.IsNullOrEmpty(field.SchemaId) ? KindName(field.Kind) : DataTypes.IsArray(field.SchemaId) || DataTypes.DictionaryTypes(field.SchemaId) != null ? field.SchemaId : Schemas.Require(field.SchemaId).NameFor(Language)) +
                                " · " + Text(field.LabelKey, field.LabelFor(Language)), leading + objectPath + "." + name));
                    }
                    foreach (Operation op in entity.EffectiveOperations)
                    {
                        string name = CommandVocabulary.Match(op.NameFor(Language), op.AllNames, prefix, composing);
                        if (name != null && (!IsDayEnded || DryRun || op.Property) && OperationDenial(entity, op, locals) == null)
                            output.Add(new Completion(op.SignatureFor(Language, name) + " · " + op.LabelFor(Language),
                                leading + objectPath + "." + name + (op.Property ? "" : op.Parameters.Length == 0 && !op.Variadic ? "()" : "(")));
                    }
                    return output;
                }
                catch (CommandException) { return Array.Empty<Completion>(); }
            }
            return AvailableValues(locals, input, composing).Where(v => Prefix(v.Key, input, composing)).Select(v =>
                new Completion(v.Key + " : " + TypeName(v.Value), leading + v.Key + (equal < 0 && ReferenceTarget(v.Value) != null ? "." : ""))).ToArray();
        }

        private IEnumerable<KeyValuePair<string, Datum>> AvailableValues(Dictionary<string, Datum> locals, string prefix, bool composing)
        {
            if (locals != null) foreach (var local in locals) if (!local.Key.StartsWith("\u0001", StringComparison.Ordinal) && PlayerValueVisible(local.Value, locals)) yield return local;
            foreach (var slot in data) if ((locals == null || !locals.ContainsKey(slot.Key)) && PlayerValueVisible(slot.Value.value, locals)) yield return new KeyValuePair<string, Datum>(slot.Key, slot.Value.value);
            foreach (var root in roots.Concat(entities.Where(e => IsDiscovered(e.Key) && !roots.ContainsKey(e.Key) && discoveries[e.Key].source != "초기 작전망" &&
                !data.Values.Any(s => s.value.CanReferenceEntity && s.value.id == e.Key))))
                if (root.Key == root.Value.Id && EntityDenial(root.Value) == null)
                {
                    string name = CommandVocabulary.Match(root.Value.NameFor(Language), root.Value.AllNames.Concat(roots.Where(r => r.Value == root.Value).Select(r => r.Key)), prefix, composing);
                    if (name != null && (locals == null || !locals.ContainsKey(name)))
                        yield return new KeyValuePair<string, Datum>(name, Reference(root.Value));
                }
            foreach (var protocol in protocols)
                yield return new KeyValuePair<string, Datum>(protocol.Key, Datum.Ref(DataKind.Protocol, protocol.Key, protocol.Key));
        }

        internal static bool Prefix(string candidate, string prefix, bool composing) =>
            (composing ? candidate.Normalize(NormalizationForm.FormKD) : candidate)
                .StartsWith(composing ? prefix.Normalize(NormalizationForm.FormKD) : prefix, StringComparison.Ordinal);

        private static string Normalize(string value) => (value ?? "").Normalize(NormalizationForm.FormC);
    }
}
