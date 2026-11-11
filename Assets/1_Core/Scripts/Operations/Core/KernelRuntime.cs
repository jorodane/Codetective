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

        public const string Help = "save <name> / free <name> / help\n" +
            "save allocates a data slot; free releases it.\n" +
            "object. lists currently available commands.\nname = object.command(input);\n" +
            "Examples: hq.requestCoffee();\ndoc = archive.inspect();\nthread01.run(recon, archive);\n" +
            "Protocol editor: protocol recon(Place target) { return target.inspect(); }";

        public void Register(Entity entity, params string[] rootAliases)
        {
            IndexEntity(entity, new HashSet<Entity>());
            roots[entity.Id] = entity;
            foreach (string alias in entity.AllNames.Concat(rootAliases))
            {
                if (roots.TryGetValue(alias, out Entity other) && other != entity) throw new CommandException("객체 별칭이 중복됩니다: " + alias);
                roots[alias] = entity;
            }
        }

        private void IndexEntity(Entity entity, HashSet<Entity> visited)
        {
            if (!visited.Add(entity)) return;
            if (entities.TryGetValue(entity.Id, out Entity other) && other != entity) throw new CommandException("객체 ID가 중복됩니다: " + entity.Id);
            entities[entity.Id] = entity;
            foreach (Entity child in entity.Children.Values) IndexEntity(child, visited);
        }

        public Entity Entity(string id) => entities.TryGetValue(id, out Entity entity) ? entity : null;
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
        {
            name = Normalize(name);
            CheckName(name);
            if (!data.TryGetValue(name, out DataSlot slot)) slot = Allocate(name);
            slot.value = (value ?? Datum.Empty).Copy();
            Revision++;
        }

        public bool Free(string name) { bool removed = data.Remove(Normalize(name)); if (removed) Revision++; return removed; }

        private void CheckName(string name)
        {
            if (!CommandParser.IsIdentifier(name)) throw new CommandException("이름은 글자 또는 _로 시작하고 글자·숫자·_만 사용하세요.");
            if (roots.ContainsKey(name) || protocols.ContainsKey(name) || CommandVocabulary.Keyword(name) != null)
                throw new CommandException("객체·프로토콜·예약 명령과 다른 데이터 이름을 사용하세요.");
        }

        public ExecutionResult Execute(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return ExecutionResult.Error("");
            if (source.IndexOfAny(new[] { '\r', '\n' }) >= 0) return ExecutionResult.Error("한 번에 한 줄만 입력하세요. 여러 줄은 프로토콜 편집창에서 작성하세요.");
            try
            {
                source = Normalize(source.Trim()).TrimEnd(';').TrimEnd();
                if (source.Length == 0) return ExecutionResult.Error("실행할 명령을 입력하세요.");
                string[] words = source.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                string keyword = CommandVocabulary.Keyword(words[0]);
                if (keyword == "help") return words.Length == 1 ? ExecutionResult.Ok(HelpForLanguage) : ExecutionResult.Error("사용법: " + words[0]);
                if (keyword == "save" || keyword == "free")
                {
                    if (words.Length != 2) return ExecutionResult.Error($"사용법: {words[0]} 이름");
                    if (keyword == "save") { Allocate(words[1]); return ExecutionResult.Ok($"'{words[1]}' 데이터 공간을 할당했습니다."); }
                    return Free(words[1]) ? ExecutionResult.Ok($"'{words[1]}' 데이터 공간을 해제했습니다.") :
                        ExecutionResult.Error($"'{words[1]}'에 할당된 데이터가 없습니다.");
                }
                budget = 1024; depth = 0;
                Statement statement = new CommandParser(source).ReadSingle();
                Datum value = ExecuteStatement(statement, null, null);
                Revision++;
                return ExecutionResult.Ok(value.kind == DataKind.Empty ? "명령을 수행했습니다." : value.ToString(), value);
            }
            catch (CommandException error) { return ExecutionResult.Error(error.Message + " (" + CommandVocabulary.KeywordName("help", Language) + "로 사용법 확인)"); }
        }

        public ExecutionResult SaveProtocol(string source)
        {
            try
            {
                Protocol protocol = new CommandParser(source).ReadProtocol(source);
                if (roots.ContainsKey(protocol.Name) || data.ContainsKey(protocol.Name) || CommandVocabulary.Keyword(protocol.Name) != null)
                    throw new CommandException("다른 객체·데이터·기본 명령과 다른 프로토콜 이름을 사용하세요.");
                protocols[protocol.Name] = protocol;
                Revision++;
                return ExecutionResult.Ok("프로토콜 저장: " + protocol.SignatureFor(Language));
            }
            catch (CommandException error) { return ExecutionResult.Error(error.Message); }
        }

        public ExecutionResult TestProtocol(string source, string call)
        {
            var sandbox = new KernelRuntime { DryRun = true, Language = Language };
            foreach (var pair in entities) sandbox.entities.Add(pair.Key, pair.Value);
            foreach (var pair in roots) sandbox.roots.Add(pair.Key, pair.Value);
            sandbox.Consultation.AddRange(Consultation);
            sandbox.Restore(Capture());
            ExecutionResult saved = sandbox.SaveProtocol(source);
            return saved.Success ? sandbox.Execute(call) : saved;
        }

        internal Datum ExecuteStatement(Statement statement, Dictionary<string, Datum> locals, string agent)
        {
            if (--budget < 0) throw new CommandException("한 번의 실행 예산(1024단계)을 초과했습니다.");
            if (locals == null && statement.Assign != null) CheckName(statement.Assign);
            Datum value = Evaluate(statement.Value, locals, agent);
            if (statement.Assign != null)
            {
                if (locals != null) locals[statement.Assign] = value.Copy();
                else Store(statement.Assign, value);
            }
            return value;
        }

        internal Datum Evaluate(Expression expression, Dictionary<string, Datum> locals, string agent)
        {
            if (expression.Literal != null) return expression.Literal.Copy();
            string[] path = expression.Path;
            if (expression.IsCall && path.Length == 1 && protocols.TryGetValue(path[0], out Protocol protocol))
                return RunProtocol(protocol, expression.Arguments.Select(e => Evaluate(e, locals, agent)).ToArray(), agent);
            if (expression.IsCall)
            {
                if (path.Length < 2) throw new CommandException("알 수 없는 프로토콜/명령입니다: " + path[0]);
                Entity target = ResolveEntity(path.Take(path.Length - 1).ToArray(), locals);
                Operation operation = target.Find(path.Last());
                if (operation == null || operation.Property) throw new CommandException("존재하지 않는 호출입니다: " + path.Last());
                if (IsDayEnded && !DryRun) throw new CommandException("오늘의 업무 시간이 끝났습니다. 다음 날로 진행하세요.");
                CheckAccess(operation.Access, locals);
                Datum[] args = expression.Arguments.Select(e => Evaluate(e, locals, agent)).ToArray();
                Validate(operation.Parameters, args, operation.Variadic);
                return operation.Invoke(new Invocation { Runtime = this, Target = target, Arguments = args, AgentId = agent }) ?? Datum.Empty;
            }
            if (path.Length == 1)
            {
                if (locals != null && locals.TryGetValue(path[0], out Datum local)) return local.Copy();
                if (data.TryGetValue(path[0], out DataSlot slot)) return slot.value.Copy();
                if (protocols.ContainsKey(path[0])) return Datum.Ref(DataKind.Protocol, path[0], path[0]);
            }
            try
            {
                Entity target = ResolveEntity(path, locals);
                return Datum.Ref(target.Kind, target.Id, target.Label);
            }
            catch (CommandException)
            {
                if (path.Length < 2) throw;
                Entity target = ResolveEntity(path.Take(path.Length - 1).ToArray(), locals);
                Operation property = target.Find(path.Last());
                if (property == null || !property.Property) throw new CommandException("알 수 없는 데이터/속성입니다: " + path.Last());
                CheckAccess(property.Access, locals);
                return property.Invoke(new Invocation { Runtime = this, Target = target, Arguments = Array.Empty<Datum>(), AgentId = agent });
            }
        }

        internal Entity ResolveEntity(string[] path, Dictionary<string, Datum> locals)
        {
            if (path.Length == 0) throw new CommandException("대상을 지정하세요.");
            Entity entity = null;
            if (locals != null && locals.TryGetValue(path[0], out Datum local)) entity = Entity(local.id);
            else if (data.TryGetValue(path[0], out DataSlot slot)) entity = Entity(slot.value.id);
            else roots.TryGetValue(path[0], out entity);
            if (entity == null) throw new CommandException("알 수 없거나 비어 있는 대상입니다: " + path[0]);
            CheckAccess(entity.Access, locals);
            for (int i = 1; i < path.Length; i++)
            {
                entity = entity.Child(path[i]);
                if (entity == null) throw new CommandException("알 수 없는 대상 경로입니다: " + path[i]);
                CheckAccess(entity.Access, locals);
            }
            return entity;
        }

        private Datum RunProtocol(Protocol protocol, Datum[] arguments, string agent)
        {
            if (++depth > 24) { depth--; throw new CommandException("프로토콜 호출 깊이(24)를 초과했습니다."); }
            try
            {
                Dictionary<string, Datum> locals = Bind(protocol, arguments);
                foreach (Statement statement in protocol.Body)
                {
                    Datum value = ExecuteStatement(statement, locals, agent);
                    if (statement.Return) return value;
                }
                return Datum.Empty;
            }
            finally { depth--; }
        }

        internal Dictionary<string, Datum> Bind(Protocol protocol, Datum[] arguments)
        {
            Validate(protocol.Parameters, arguments, false);
            var locals = new Dictionary<string, Datum>(StringComparer.Ordinal);
            for (int i = 0; i < arguments.Length; i++) locals[protocol.Parameters[i].Name] = arguments[i].Copy();
            return locals;
        }

        private void Validate(Parameter[] parameters, Datum[] arguments, bool variadic)
        {
            if (arguments.Length < parameters.Length || (!variadic && arguments.Length != parameters.Length))
                throw new CommandException("입력 데이터 개수가 맞지 않습니다.");
            for (int i = 0; i < parameters.Length; i++)
                if (parameters[i].Kind != DataKind.Any && parameters[i].Kind != arguments[i].kind)
                    throw new CommandException($"{parameters[i].Name}에는 {KindName(parameters[i].Kind)} 데이터가 필요합니다. 현재: {KindName(arguments[i].kind)}");
        }

        private void CheckAccess(AccessRule rule, Dictionary<string, Datum> locals = null)
        {
            string denial = rule.Denial(this, locals?.Values);
            if (denial != null) throw new CommandException(denial);
        }

        internal double Duration(Statement statement, Dictionary<string, Datum> locals)
        {
            Expression e = statement.Value;
            if (!e.IsCall || e.Path.Length < 2) return 0;
            Entity target = ResolveEntity(e.Path.Take(e.Path.Length - 1).ToArray(), locals);
            return Math.Max(0, target.Find(e.Path.Last())?.Minutes ?? 0);
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
                DataKind expected = DataKind.Any;
                try
                {
                    string[] call = input.Substring(0, open).Trim().Split('.');
                    Parameter[] parameters = call.Length == 1 && protocols.TryGetValue(call[0], out Protocol p) ? p.Parameters :
                        call.Length > 1 ? ResolveEntity(call.Take(call.Length - 1).ToArray(), locals).Find(call.Last())?.Parameters : null;
                    int argumentIndex = input.Substring(open + 1).Count(c => c == ',');
                    if (parameters != null && argumentIndex < parameters.Length) expected = parameters[argumentIndex].Kind;
                }
                catch (CommandException) { }
                return AvailableValues(locals, prefix, composing).Where(v => (expected == DataKind.Any || v.Value.kind == expected) && Prefix(v.Key, prefix, composing))
                    .Select(v => new Completion(v.Key + " : " + KindName(v.Value.kind), before + v.Key)).ToArray();
            }
            int dot = input.LastIndexOf('.');
            if (dot >= 0)
            {
                string objectPath = input.Substring(0, dot);
                string prefix = input.Substring(dot + 1);
                try
                {
                    Entity entity = ResolveEntity(objectPath.Split('.'), locals);
                    var output = new List<Completion>();
                    foreach (var child in entity.Children)
                    {
                        string preferred = CommandVocabulary.Name(child.Key, child.Value.Names, Language);
                        string name = CommandVocabulary.Match(preferred, child.Value.AllNames.Concat(new[] { child.Key }), prefix, composing);
                        if (name != null && child.Value.Access.Denial(this, locals?.Values) == null)
                            output.Add(new Completion(name + " · " + child.Value.LabelFor(Language), leading + objectPath + "." + name + "."));
                    }
                    foreach (Operation op in entity.Operations)
                    {
                        string name = CommandVocabulary.Match(op.NameFor(Language), op.AllNames, prefix, composing);
                        if (name != null && (!IsDayEnded || DryRun || op.Property) && op.Access.Denial(this, locals?.Values) == null)
                            output.Add(new Completion(op.SignatureFor(Language, name) + " · " + op.LabelFor(Language),
                                leading + objectPath + "." + name + (op.Property ? "" : op.Parameters.Length == 0 && !op.Variadic ? "()" : "(")));
                    }
                    return output;
                }
                catch (CommandException) { return Array.Empty<Completion>(); }
            }
            return AvailableValues(locals, input, composing).Where(v => Prefix(v.Key, input, composing)).Select(v =>
                new Completion(v.Key + " : " + KindName(v.Value.kind), leading + v.Key + (Entity(v.Value.id) != null ? "." : ""))).ToArray();
        }

        private IEnumerable<KeyValuePair<string, Datum>> AvailableValues(Dictionary<string, Datum> locals, string prefix, bool composing)
        {
            if (locals != null) foreach (var local in locals) if (!local.Key.StartsWith("\u0001", StringComparison.Ordinal)) yield return local;
            foreach (var slot in data) if (locals == null || !locals.ContainsKey(slot.Key)) yield return new KeyValuePair<string, Datum>(slot.Key, slot.Value.value);
            foreach (var root in roots)
                if (root.Key == root.Value.Id && root.Value.Access.Denial(this) == null)
                {
                    string name = CommandVocabulary.Match(root.Value.NameFor(Language), root.Value.AllNames.Concat(roots.Where(r => r.Value == root.Value).Select(r => r.Key)), prefix, composing);
                    if (name != null && (locals == null || !locals.ContainsKey(name)))
                        yield return new KeyValuePair<string, Datum>(name, Datum.Ref(root.Value.Kind, root.Value.Id, root.Value.LabelFor(Language)));
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
