using System;
using System.Collections.Generic;
using System.Linq;

namespace Codetective.Operations
{
    [Serializable]
    public sealed class VariableDefinition
    {
        public string Code;
        public string Label;
        public DataKind Kind = DataKind.Any;
        public string SchemaId = "";
        public Datum DefaultValue = Datum.Empty;
        public bool Writable;
        public bool Overrides;
        public string[] Aliases = Array.Empty<string>();
        public LocalizedCommandName[] Names = Array.Empty<LocalizedCommandName>();
        public AccessRule Access = new AccessRule();
        [NonSerialized] public Func<Entity, Datum> Getter;
        public IEnumerable<string> AllNames => CommandVocabulary.Names(Code, Aliases, Names);
        public bool Matches(string name) => AllNames.Contains(name);
        public string NameFor(CommandLocale language) => CommandVocabulary.Name(Code, Names, language);
        public string LabelFor(CommandLocale language) => CommandVocabulary.Description(Label, Names, language);
        public VariableDefinition Copy() => new VariableDefinition { Code = Code, Label = Label, Kind = Kind, SchemaId = SchemaId,
            DefaultValue = DefaultValue.Copy(), Writable = Writable, Overrides = Overrides, Getter = Getter,
            Aliases = Aliases.ToArray(), Names = Names.Select(n => new LocalizedCommandName(n.language, n.name, n.description)).ToArray(), Access = Access.Copy() };
    }

    /// <summary>A data type, separate from its instances and from containment between world objects.</summary>
    public sealed class EntitySchema
    {
        public string Id;
        public string Label;
        public DataKind Kind;
        public EntitySchema Parent;
        public string[] Aliases = Array.Empty<string>();
        public LocalizedCommandName[] Names = Array.Empty<LocalizedCommandName>();
        public AccessRule Access = new AccessRule();
        public readonly List<VariableDefinition> Variables = new List<VariableDefinition>();
        public readonly List<Operation> Operations = new List<Operation>();
        public EntitySchema(string id, string label, DataKind kind, EntitySchema parent = null)
        { Id = id; Label = label; Kind = kind; Parent = parent; }
        public IEnumerable<string> AllNames => CommandVocabulary.Names(Id, Aliases, Names);
        public string NameFor(CommandLocale language) => CommandVocabulary.Name(Id, Names, language);
        public IEnumerable<EntitySchema> Lineage()
        {
            var seen = new HashSet<EntitySchema>();
            var stack = new Stack<EntitySchema>();
            for (EntitySchema schema = this; schema != null; schema = schema.Parent)
            {
                if (!seen.Add(schema) || seen.Count > 64) throw new CommandException("자료형 상속에 순환이 있거나 깊이(64)를 초과했습니다: " + Id);
                stack.Push(schema);
            }
            return stack;
        }
        public bool IsA(EntitySchema expected) => Lineage().Any(s => s == expected || s.Id == expected.Id);
        public IEnumerable<VariableDefinition> EffectiveVariables => Merge(Lineage().SelectMany(s => s.Variables), v => v.Code);
        public IEnumerable<Operation> EffectiveOperations => Merge(Lineage().SelectMany(s => s.Operations), o => o.Code);
        public VariableDefinition FindVariable(string name) => EffectiveVariables.FirstOrDefault(v => v.Matches(name));
        public Operation FindOperation(string name) => EffectiveOperations.FirstOrDefault(o => o.Matches(name));
        internal static IEnumerable<T> Merge<T>(IEnumerable<T> members, Func<T, string> key)
        {
            var values = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (T member in members) values[key(member)] = member;
            return values.Values;
        }
    }

    public sealed class SchemaRegistry
    {
        private readonly Dictionary<string, EntitySchema> schemas = new Dictionary<string, EntitySchema>(StringComparer.Ordinal);
        public IEnumerable<EntitySchema> All => schemas.Values;
        public EntitySchema Find(string name) => name == null ? null : schemas.TryGetValue(name, out EntitySchema exact) ? exact :
            schemas.Values.FirstOrDefault(s => s.AllNames.Contains(name));
        public EntitySchema Require(string name) => Find(name) ?? throw new CommandException("등록되지 않은 자료형입니다: " + name);
        public EntitySchema ForKind(DataKind kind) => All.FirstOrDefault(s => s.Id == kind.ToString()) ?? Require("Entity");

        public void Register(EntitySchema schema)
        {
            if (schema == null) throw new CommandException("자료형이 비어 있습니다.");
            EntitySchema[] lineage = schema.Lineage().ToArray();
            if (lineage[0].Id != "Entity" || lineage[0].Parent != null || lineage[0].Kind != DataKind.Entity)
                throw new CommandException("자료형의 최상위 부모는 Entity여야 합니다.");
            foreach (EntitySchema item in lineage)
            {
                if (!CommandParser.IsIdentifier(item.Id)) throw new CommandException("자료형 ID가 유효하지 않습니다: " + item.Id);
                if (schemas.TryGetValue(item.Id, out EntitySchema other) && other != item) throw new CommandException("자료형 ID가 중복됩니다: " + item.Id);
                foreach (string name in item.AllNames)
                {
                    if (!CommandParser.IsIdentifier(name)) throw new CommandException("자료형 이름에 공백/기호를 사용할 수 없습니다: " + name);
                    EntitySchema existing = Find(name);
                    if (existing != null && existing != item) throw new CommandException("자료형 별칭이 중복됩니다: " + name);
                }
                ValidateMembers(item);
                schemas[item.Id] = item;
            }
        }

        public void Validate()
        {
            foreach (EntitySchema schema in All.ToArray())
            {
                Register(schema);
                foreach (VariableDefinition variable in schema.Variables)
                {
                    if (!string.IsNullOrEmpty(variable.SchemaId)) Require(variable.SchemaId);
                    if (variable.DefaultValue.kind != DataKind.Empty && !Accepts(variable.Kind, variable.SchemaId, variable.DefaultValue, null))
                        throw new CommandException("변수 기본값의 자료형이 맞지 않습니다: " + schema.Id + "." + variable.Code);
                }
                foreach (Operation operation in schema.Operations)
                {
                    if (!string.IsNullOrEmpty(operation.ReturnSchemaId)) Require(operation.ReturnSchemaId);
                    foreach (Parameter parameter in operation.Parameters) if (!string.IsNullOrEmpty(parameter.SchemaId)) Require(parameter.SchemaId);
                }
            }
        }

        internal static void ValidateMembers(EntitySchema schema)
        {
            var own = new HashSet<string>(StringComparer.Ordinal);
            foreach (VariableDefinition field in schema.Variables)
            {
                if (!CommandParser.IsIdentifier(field.Code) || !own.Add(field.Code)) throw new CommandException("변수 이름이 유효하지 않거나 중복됩니다: " + field.Code);
                if (schema.Parent?.FindOperation(field.Code) != null) throw new CommandException("변수 이름이 상속 함수와 충돌합니다: " + field.Code);
                VariableDefinition inherited = schema.Parent?.FindVariable(field.Code);
                if (inherited != null && (!field.Overrides || field.Kind != inherited.Kind || field.SchemaId != inherited.SchemaId ||
                    (!inherited.Writable && field.Writable) || inherited.Getter != null))
                    throw new CommandException("상속 변수 재정의가 호환되지 않습니다: " + schema.Id + "." + field.Code);
                if (inherited == null && field.Overrides) throw new CommandException("재정의할 부모 변수가 없습니다: " + field.Code);
            }
            foreach (Operation operation in schema.Operations)
            {
                if (!CommandParser.IsIdentifier(operation.Code) || !own.Add(operation.Code)) throw new CommandException("함수 이름이 유효하지 않거나 중복됩니다: " + operation.Code);
                if (schema.Parent?.FindVariable(operation.Code) != null) throw new CommandException("함수 이름이 상속 변수와 충돌합니다: " + operation.Code);
                Operation inherited = schema.Parent?.FindOperation(operation.Code);
                if (inherited != null && (!operation.Overrides || !SameSignature(inherited, operation)))
                    throw new CommandException("상속 함수 재정의의 입력/반환 자료형이 호환되지 않습니다: " + schema.Id + "." + operation.Code);
                if (inherited == null && operation.Overrides) throw new CommandException("재정의할 부모 함수가 없습니다: " + operation.Code);
            }
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var member in schema.EffectiveVariables.Select(v => new { v.Code, Names = v.AllNames }).Concat(
                schema.EffectiveOperations.Select(o => new { o.Code, Names = o.AllNames })))
                foreach (string name in member.Names)
                {
                    if (!CommandParser.IsIdentifier(name)) throw new CommandException("멤버 이름에 공백/기호를 사용할 수 없습니다: " + name);
                    if (names.TryGetValue(name, out string other) && other != member.Code) throw new CommandException("멤버 별칭이 충돌합니다: " + name);
                    names[name] = member.Code;
                }
        }

        private static bool SameSignature(Operation a, Operation b) => a.Property == b.Property && a.Variadic == b.Variadic &&
            a.ReturnKind == b.ReturnKind && a.ReturnSchemaId == b.ReturnSchemaId && a.Parameters.Length == b.Parameters.Length &&
            a.Parameters.Zip(b.Parameters, (x, y) => x.Kind == y.Kind && x.SchemaId == y.SchemaId).All(v => v);

        public bool Accepts(DataKind kind, string schemaId, Datum value, Func<string, Entity> resolve)
        {
            EntitySchema actual = value.CanReferenceEntity && !string.IsNullOrEmpty(value.id) ? resolve?.Invoke(value.id)?.Schema : null;
            if (actual == null && value.CanReferenceEntity && !string.IsNullOrEmpty(value.schemaId)) actual = Find(value.schemaId);
            if (!string.IsNullOrEmpty(schemaId)) return actual != null && actual.IsA(Require(schemaId));
            if (kind == DataKind.Any) return true;
            if (actual != null && actual.Lineage().Any(s => s.Kind == kind)) return true;
            return value.kind == kind;
        }
    }

    public sealed partial class Entity
    {
        public EntitySchema Schema { get; internal set; }
        internal Func<VariableDefinition, Datum, bool> ValueValidator;
        internal Func<Datum, Datum> ValueNormalizer;
        internal Action ValueChanged;
        private readonly Dictionary<string, Datum> values = new Dictionary<string, Datum>(StringComparer.Ordinal);
        public IEnumerable<VariableDefinition> Variables => AllVariables.Where(v => !disabledVariables.Contains(v.Code));
        public IEnumerable<Operation> EffectiveOperations => AllOperations.Where(o => !disabledFunctions.Contains(o.Code));
        public IEnumerable<AccessRule> AccessRules => (Schema?.Lineage().Select(s => s.Access) ?? Enumerable.Empty<AccessRule>()).Concat(new[] { Access });
        public Entity(string id, string label, EntitySchema schema) : this(id, label, schema.Kind) { Schema = schema; }
        public VariableDefinition FindVariable(string name) => Variables.FirstOrDefault(v => v.Matches(name));
        public Datum GetValue(string name)
        {
            VariableDefinition field = GetVariableDefinition(name) ?? throw new CommandException("존재하지 않는 변수입니다: " + name);
            Datum value = field.Getter != null ? field.Getter(this) : values.TryGetValue(field.Code, out Datum stored) ? stored : field.DefaultValue;
            Datum copy = value.Copy();
            return ValueNormalizer != null ? ValueNormalizer(copy) : copy;
        }
        public void SetValue(string name, Datum value)
        {
            VariableDefinition field = GetVariableDefinition(name) ?? throw new CommandException("존재하지 않는 변수입니다: " + name);
            if (field.Getter != null) throw new CommandException("시스템 식별 변수는 변경할 수 없습니다: " + name);
            if (value != null && value.kind != DataKind.Empty && ValueValidator != null && !ValueValidator(field, value))
                throw new CommandException("변수에 저장할 자료형이 맞지 않습니다: " + field.Code);
            Datum stored = (value ?? Datum.Empty).Copy();
            values[field.Code] = ValueNormalizer != null ? ValueNormalizer(stored) : stored;
            ValueChanged?.Invoke();
        }
        internal IEnumerable<AccessRule> VariableRules(VariableDefinition field) => (Schema?.Lineage().SelectMany(s => s.Variables) ?? Enumerable.Empty<VariableDefinition>()).Concat(localVariables.Values)
            .Where(v => v.Code == field.Code).Select(v => v.Access);
        internal IEnumerable<AccessRule> OperationRules(Operation operation) => (Schema?.Lineage().SelectMany(s => s.Operations) ?? Enumerable.Empty<Operation>())
            .Concat(Operations).Concat(localFunctions.Values).Where(o => o.Code == operation.Code).Select(o => o.Access);
        internal DataSlot[] CaptureValues() => values.Select(p => new DataSlot { name = p.Key, value = p.Value.Copy() }).ToArray();
        internal void ClearValues() => values.Clear();
    }
}
