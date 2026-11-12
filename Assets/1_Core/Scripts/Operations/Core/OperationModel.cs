using System;
using System.Collections.Generic;
using System.Linq;

namespace Codetective.Operations
{
    public enum DataKind { Empty, Any, Number, Text, Person, Item, Statement, Photo, Document, Log, Place, Time, Analysis, Rule, Relationship, Clue, Protocol, Thread, Task, Entity, Boolean }
    public enum AgentState { Ready, Executing, Waiting, Disconnected }

    [Serializable]
    public sealed class Datum
    {
        public DataKind kind;
        public string id = "";
        public string label = "";
        public string text = "";
        public double number;
        public string schemaId = "";
        public bool CanReferenceEntity => kind != DataKind.Empty && kind != DataKind.Number && kind != DataKind.Text &&
            kind != DataKind.Boolean && kind != DataKind.Protocol && kind != DataKind.Task;
        public static Datum Empty => new Datum();
        public static Datum Ref(DataKind kind, string id, string label, string text = "") =>
            new Datum { kind = kind, id = id, label = label, text = text };
        public static Datum Text(string text) => new Datum { kind = DataKind.Text, text = text, label = text };
        public static Datum Number(double value) => new Datum { kind = DataKind.Number, number = value, label = value.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        public static Datum Boolean(bool value) => new Datum { kind = DataKind.Boolean, number = value ? 1 : 0, label = value ? "true" : "false" };
        public Datum Copy() => (Datum)MemberwiseClone();
        public override string ToString() => kind == DataKind.Empty ? "비어 있음" :
            string.IsNullOrEmpty(text) || text == label ? label : label + " — " + text;
    }

    [Serializable]
    public sealed class DataSlot
    {
        public int id;
        public string name;
        public Datum value = Datum.Empty;
    }

    public sealed class Parameter
    {
        public string Name { get; }
        public DataKind Kind { get; }
        public string SchemaId { get; }
        public Parameter(string name, DataKind kind, string schemaId = "") { Name = name; Kind = kind; SchemaId = schemaId ?? ""; }
        public string TypeName(CommandLocale language) => string.IsNullOrEmpty(SchemaId) ? CommandVocabulary.KindName(Kind, language) : SchemaId;
    }

    [Serializable]
    public sealed class AccessRule
    {
        public string[] Facts = Array.Empty<string>();
        public string[] Authorities = Array.Empty<string>();
        public string[] ForbiddenFacts = Array.Empty<string>();
        public DataKind RequiredData = DataKind.Empty;
        public bool NeedsReadyThread;
        [NonSerialized] public Func<KernelRuntime, bool> Condition;
        [NonSerialized] public Func<KernelRuntime, Entity, bool> TargetCondition;
        public AccessRule Copy() => new AccessRule { Facts = Facts.ToArray(), Authorities = Authorities.ToArray(), ForbiddenFacts = ForbiddenFacts.ToArray(),
            RequiredData = RequiredData, NeedsReadyThread = NeedsReadyThread, Condition = Condition, TargetCondition = TargetCondition };

        public string Denial(KernelRuntime runtime, IEnumerable<Datum> locals = null, Entity target = null)
        {
            if (Facts.Any(f => !runtime.Facts.Contains(f))) return "아직 필요한 정보를 알지 못합니다.";
            if (Authorities.Any(a => !runtime.Authorities.Contains(a))) return "작전 권한이 없습니다.";
            if (ForbiddenFacts.Any(runtime.Facts.Contains)) return "현재 금지된 권한입니다.";
            if (RequiredData != DataKind.Empty && !runtime.Data.Values.Any(v => v.value.kind == RequiredData) &&
                !(locals ?? Array.Empty<Datum>()).Any(v => v.kind == RequiredData))
                return "필요한 데이터를 보유하고 있지 않습니다.";
            if (NeedsReadyThread && !runtime.Threads.Any(t => t.State == AgentState.Ready)) return "활동 가능한 Thread가 없습니다.";
            if (Condition != null && !Condition(runtime)) return "현재 상태에서는 수행할 수 없습니다.";
            if (TargetCondition != null && (target == null || !TargetCondition(runtime, target))) return "현재 대상 상태에서는 수행할 수 없습니다.";
            return null;
        }
    }

    public sealed class Operation
    {
        public string Code;
        public string Label;
        public string[] Aliases = Array.Empty<string>();
        public LocalizedCommandName[] Names = Array.Empty<LocalizedCommandName>();
        public Parameter[] Parameters = Array.Empty<Parameter>();
        public DataKind ReturnKind;
        public string ReturnSchemaId = "";
        public bool Overrides;
        public bool Property;
        public bool Variadic;
        public double Minutes;
        public AccessRule Access = new AccessRule();
        public Func<Invocation, Datum> Invoke;
        public string Signature => Property ? Code : Code + "(" +
            string.Join(", ", Parameters.Select(p => p.TypeName(CommandLocale.English) + " " + p.Name)) + (Variadic ? ", ..." : "") + ")";
        public string NameFor(CommandLocale language) => CommandVocabulary.Name(Code, Names, language);
        public string LabelFor(CommandLocale language) => CommandVocabulary.Description(Label, Names, language);
        public string SignatureFor(CommandLocale language, string name = null) => Property ? name ?? NameFor(language) : (name ?? NameFor(language)) + "(" +
            string.Join(", ", Parameters.Select(p => p.TypeName(language) + " " + CommandVocabulary.ParameterName(p.Name, language))) + (Variadic ? ", ..." : "") + ")";
        public IEnumerable<string> AllNames => CommandVocabulary.Names(Code, Aliases, Names);
        public bool Matches(string name) => AllNames.Contains(name);
    }

    public sealed partial class Entity
    {
        public string Id;
        public string Label;
        public DataKind Kind;
        public string[] Aliases = Array.Empty<string>();
        public LocalizedCommandName[] Names = Array.Empty<LocalizedCommandName>();
        public AccessRule Access = new AccessRule();
        public readonly Dictionary<string, Entity> Children = new Dictionary<string, Entity>(StringComparer.Ordinal);
        // Legacy C# hooks, configured when constructing a world. Mutable, persistent functions use AddFunction.
        public readonly List<Operation> Operations = new List<Operation>();
        public Entity(string id, string label, DataKind kind) { Id = id; Label = label; Kind = kind; }
        public string NameFor(CommandLocale language) => CommandVocabulary.Name(Id, Names, language);
        public string LabelFor(CommandLocale language) => CommandVocabulary.Description(Label, Names, language);
        public IEnumerable<string> AllNames => CommandVocabulary.Names(Id, Aliases, Names);
        public Entity Child(string name) => Children.TryGetValue(name, out Entity child) ? child :
            Children.Values.FirstOrDefault(e => e.AllNames.Contains(name));
        public Operation Find(string name) => EffectiveOperations.FirstOrDefault(o => o.Matches(name));
    }

    public sealed class Invocation
    {
        public KernelRuntime Runtime;
        public Entity Target;
        public Datum[] Arguments;
        public string AgentId;
    }

    public sealed class Completion
    {
        public string Label;
        public string Replacement;
        public Completion(string label, string replacement) { Label = label; Replacement = replacement; }
    }

    public sealed class ExecutionResult
    {
        public bool Success;
        public string Message;
        public Datum Value;
        public static ExecutionResult Ok(string message, Datum value = null) => new ExecutionResult { Success = true, Message = message, Value = value ?? Datum.Empty };
        public static ExecutionResult Error(string message) => new ExecutionResult { Message = message, Value = Datum.Empty };
    }

    public sealed class CommandException : Exception
    {
        public CommandException(string message) : base(message) { }
    }

    [Serializable]
    public sealed class ConsultationLine
    {
        public string profileId = "client";
        public string speaker = "상담 손님";
        public string sentence;
        public string keyword;
        public string fact;
        public string dataName;
        public DataKind kind = DataKind.Clue;
    }

    [Serializable]
    public sealed class KernelSnapshot
    {
        public int version = 1;
        public int day;
        public double minute;
        public double suspicion;
        public double exposure;
        public double responseMinutes;
        public int consultationIndex;
        public bool dayEnded;
        public bool coffeeReceived;
        public string[] facts;
        public string[] authorities;
        public DataSlot[] data;
        public string[] protocols;
        public string[] recordedClues;
        public AgentSnapshot[] agents;
        public EntitySnapshot[] entities;
        public ConsultationNote[] consultationNotes;
        public int nextConsultationNoteId;
        public MailProgress[] mailProgress;
        public DrawerState[] drawers;
    }

    [Serializable]
    public sealed class EntitySnapshot
    {
        public string id;
        public string label;
        public string schemaId;
        public string[] rootNames;
        public string[] aliases;
        public LocalizedCommandName[] names;
        public DataSlot[] values;
        public EntityChildSnapshot[] children;
        public VariableDefinition[] localVariables;
        public OperationDefinition[] localFunctions;
        public string[] disabledVariables;
        public string[] disabledFunctions;
    }
    [Serializable] public sealed class EntityChildSnapshot { public string name; public string id; }

    [Serializable]
    public sealed class AgentSnapshot
    {
        public string id;
        public AgentState state;
        public string message;
        public string location;
        public Datum report;
        public double delay;
        public string sceneAction;
        public FrameSnapshot[] frames;
    }

    [Serializable]
    public sealed class FrameSnapshot
    {
        public string source;
        public int cursor;
        public string returnTarget;
        public DataSlot[] locals;
    }
}
