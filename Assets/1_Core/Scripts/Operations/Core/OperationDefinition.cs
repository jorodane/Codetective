using System;
using System.Linq;

namespace Codetective.Operations
{
    [Serializable] public sealed class OperationInput
    {
        public string Name;
        public DataKind Kind = DataKind.Any;
        public string SchemaId = "";
        public OperationInput Copy() => (OperationInput)MemberwiseClone();
    }
    [Serializable] public sealed class OperationFieldWrite
    {
        public string Field;
        public int ArgumentIndex = -1;
        public int TargetArgumentIndex = -1;
        public Datum Value = Datum.Empty;
        public OperationFieldWrite Copy() => new OperationFieldWrite { Field = Field, ArgumentIndex = ArgumentIndex, TargetArgumentIndex = TargetArgumentIndex, Value = Value.Copy() };
    }
    [Serializable] public sealed class MemberToggle
    {
        public string Name;
        public bool Function = true;
        public bool Enabled = true;
        public MemberToggle Copy() => (MemberToggle)MemberwiseClone();
    }
    [Serializable] public sealed class WorldRequirement
    {
        public string subjectId = "", field = "";
        public int argumentIndex = -1;
        public Datum expected = Datum.Empty;
        public WorldRequirement Copy() => new WorldRequirement { subjectId = subjectId, field = field, argumentIndex = argumentIndex, expected = expected.Copy() };
        public bool Matches(Invocation call)
        {
            Entity subject = argumentIndex >= 0 && argumentIndex < call.Arguments.Length ? call.Runtime.Entity(call.Arguments[argumentIndex].id) :
                string.IsNullOrEmpty(subjectId) ? call.Target : call.Runtime.Entity(subjectId);
            if (subject == null || subject.GetVariableDefinition(field) == null) return false;
            return SameValue(subject.GetValue(field), expected);
        }
        private static bool SameValue(Datum actual, Datum expected)
        {
            if (actual.CanReferenceEntity && expected.CanReferenceEntity) return actual.id == expected.id;
            if (actual.kind != expected.kind) return false;
            if (actual.kind == DataKind.Array || actual.kind == DataKind.Dictionary)
            {
                Datum[] left = actual.Elements, right = expected.Elements;
                return left.Length == right.Length && left.Zip(right, SameValue).All(equal => equal);
            }
            return actual.id == expected.id && actual.text == expected.text && actual.number == expected.number;
        }
    }
    [Serializable] public sealed class ActionResponseDefinition
    {
        public string[] argumentIds = Array.Empty<string>();
        public StoryCondition condition = new StoryCondition();
        public WorldRequirement[] world = Array.Empty<WorldRequirement>();
        public ObservationDefinition[] reports = Array.Empty<ObservationDefinition>();
        public string[] discoveries = Array.Empty<string>();
        public OperationFieldWrite[] writes = Array.Empty<OperationFieldWrite>();
        public StoryEffects effects = new StoryEffects();
        public Datum result = Datum.Empty;
        public ActionResponseDefinition Copy() => new ActionResponseDefinition { argumentIds = argumentIds.ToArray(), condition = condition.Copy(), world = world.Select(w => w.Copy()).ToArray(),
            reports = reports.Select(r => r.Copy()).ToArray(), discoveries = discoveries.ToArray(), writes = writes.Select(w => w.Copy()).ToArray(), effects = effects.Copy(), result = result.Copy() };
        public bool Matches(Invocation call) => condition.Matches(call.Runtime) && world.All(w => w.Matches(call)) && argumentIds.Length <= call.Arguments.Length &&
            argumentIds.Select((id, i) => string.IsNullOrEmpty(id) || call.Arguments[i].id == id).All(v => v);
    }

    /// <summary>Serializable function behavior. All targets are resolved in the invocation's world, including a test copy.</summary>
    [Serializable] public sealed class OperationDefinition
    {
        public int FormatVersion = 2;
        public string Code;
        public string Label;
        public string[] Aliases = Array.Empty<string>();
        public LocalizedCommandName[] Names = Array.Empty<LocalizedCommandName>();
        public OperationInput[] Parameters = Array.Empty<OperationInput>();
        public DataKind ReturnKind;
        public string ReturnSchemaId = "";
        public bool Property;
        public bool Overrides;
        public double Minutes;
        public AccessRule Access = new AccessRule();
        public Datum Result = Datum.Empty;
        public string ReturnField = "";
        public string ReturnEntityId = "";
        public OperationFieldWrite[] FieldWrites = Array.Empty<OperationFieldWrite>();
        public string[] UnlockFacts = Array.Empty<string>();
        public string[] GrantAuthorities = Array.Empty<string>();
        public MemberToggle[] MemberChanges = Array.Empty<MemberToggle>();
        public StoryEffects StoryEffects = new StoryEffects();
        public bool DispatchToThread;
        public ObservationDefinition[] Reports = Array.Empty<ObservationDefinition>();
        public string[] Discoveries = Array.Empty<string>();
        public string[] Incidents = Array.Empty<string>();
        public ActionResponseDefinition[] Responses = Array.Empty<ActionResponseDefinition>();

        public OperationDefinition Copy() => new OperationDefinition { Code = Code, Label = Label, Aliases = Aliases.ToArray(),
            Names = Names.Select(n => new LocalizedCommandName(n.language, n.name, n.description)).ToArray(),
            Parameters = Parameters.Select(p => p.Copy()).ToArray(), ReturnKind = ReturnKind, ReturnSchemaId = ReturnSchemaId,
            Property = Property, Overrides = Overrides, Minutes = Minutes, Access = Access.Copy(), Result = Result.Copy(),
            ReturnField = ReturnField, ReturnEntityId = ReturnEntityId, FieldWrites = FieldWrites.Select(CopyWrite).ToArray(),
            UnlockFacts = UnlockFacts.ToArray(), GrantAuthorities = GrantAuthorities.ToArray(), MemberChanges = MemberChanges.Select(m => m.Copy()).ToArray(), StoryEffects = StoryEffects?.Copy() ?? new StoryEffects(),
            DispatchToThread = DispatchToThread, Reports = (Reports ?? Array.Empty<ObservationDefinition>()).Select(r => r.Copy()).ToArray(),
            Discoveries = (Discoveries ?? Array.Empty<string>()).ToArray(), Incidents = (Incidents ?? Array.Empty<string>()).ToArray(),
            Responses = (Responses ?? Array.Empty<ActionResponseDefinition>()).Select(r => r.Copy()).ToArray() };
        private OperationFieldWrite CopyWrite(OperationFieldWrite write)
        { OperationFieldWrite copy = write.Copy(); if (FormatVersion < 2) copy.TargetArgumentIndex = -1; return copy; }

        public Operation Compile()
        {
            if (FormatVersion < 2) return Copy().Compile();
            StoryEffects?.Validate();
            if (Access.Condition != null || Access.TargetCondition != null)
                throw new CommandException("저장 가능한 함수 선언에는 사실/권한 조건을 사용하세요.");
            if (!CommandParser.IsIdentifier(Code) || CommandParser.IsAllocationKeyword(Code) || double.IsNaN(Minutes) || double.IsInfinity(Minutes) || Minutes < 0)
                throw new CommandException("함수 이름 또는 소요 시간이 유효하지 않습니다: " + Code);
            if (Parameters.Any(p => !CommandParser.IsIdentifier(p.Name)) || Parameters.Select(p => p.Name).Distinct().Count() != Parameters.Length ||
                FieldWrites.Any(w => !CommandParser.IsIdentifier(w.Field) || w.ArgumentIndex < -1 || w.ArgumentIndex >= Parameters.Length || w.TargetArgumentIndex < -1 || w.TargetArgumentIndex >= Parameters.Length) ||
                MemberChanges.Any(m => !CommandParser.IsIdentifier(m.Name)) || (Property && Parameters.Length != 0))
                throw new CommandException("함수 입력/변수 변경 선언이 유효하지 않습니다: " + Code);
            foreach (ActionResponseDefinition response in Responses ?? Array.Empty<ActionResponseDefinition>())
            {
                response.effects.Validate();
                if (response.argumentIds.Length > Parameters.Length || response.world.Any(w => !CommandParser.IsIdentifier(w.field) || w.argumentIndex < -1 || w.argumentIndex >= Parameters.Length) ||
                    response.writes.Any(w => !CommandParser.IsIdentifier(w.Field) || w.ArgumentIndex < -1 || w.ArgumentIndex >= Parameters.Length || w.TargetArgumentIndex < -1 || w.TargetArgumentIndex >= Parameters.Length))
                    throw new CommandException("대상별 응답의 입력/조건/변경 설정이 유효하지 않습니다: " + Code);
            }
            OperationDefinition definition = Copy();
            AccessRule access = Access.Copy();
            access.TargetCondition = (r, e) => definition.HasTargets(e);
            return new Operation { Code = Code, Label = Label, Aliases = Aliases.ToArray(), Names = definition.Names,
                Parameters = Parameters.Select(p => new Parameter(p.Name, p.Kind, p.SchemaId)).ToArray(), ReturnKind = ReturnKind, ReturnSchemaId = ReturnSchemaId,
                Property = Property, Overrides = Overrides, Minutes = Minutes, Access = access, Invoke = definition.Invoke, DispatchToThread = DispatchToThread };
        }

        private bool HasTargets(Entity entity) => (string.IsNullOrEmpty(ReturnField) || entity.GetVariableDefinition(ReturnField) != null) &&
            FieldWrites.All(w => w.TargetArgumentIndex >= 0 || entity.GetVariableDefinition(w.Field) != null) && MemberChanges.All(m => m.Function ?
                entity.AllOperations.Any(o => o.Matches(m.Name)) : entity.GetVariableDefinition(m.Name) != null);

        private Datum Invoke(Invocation invocation)
        {
            Entity target = invocation.Target; KernelRuntime runtime = invocation.Runtime;
            if (Responses.Length > 0)
            {
                ActionResponseDefinition response = Responses.FirstOrDefault(r => r.Matches(invocation)) ?? throw new CommandException("현재 소재/상황에 해당하는 응답이 없습니다.");
                var selected = Copy(); selected.Responses = Array.Empty<ActionResponseDefinition>();
                selected.Reports = Reports.Concat(response.reports).ToArray(); selected.Discoveries = Discoveries.Concat(response.discoveries).ToArray();
                selected.FieldWrites = FieldWrites.Concat(response.writes).ToArray(); selected.Result = response.result.Copy();
                selected.StoryEffects = new StoryEffects { triggers = StoryEffects.triggers.Concat(response.effects.triggers).ToArray(), quests = StoryEffects.quests.Concat(response.effects.quests).ToArray() };
                return selected.Invoke(invocation);
            }
            if (!HasTargets(target)) throw new CommandException("함수에 필요한 인스턴스 멤버가 없습니다: " + Code);
            runtime.ValidateReports(invocation, Reports, Discoveries);
            Datum returned = string.IsNullOrEmpty(ReturnEntityId) ? null : runtime.Reference(runtime.Entity(ReturnEntityId));
            var writes = FieldWrites.SelectMany(w =>
            {
                Datum[] subjects = w.TargetArgumentIndex < 0 ? new[] { runtime.Reference(target) } :
                    invocation.Arguments[w.TargetArgumentIndex].kind == DataKind.Array ? invocation.Arguments[w.TargetArgumentIndex].Elements : new[] { invocation.Arguments[w.TargetArgumentIndex] };
                return subjects.Select(s => new { Target = runtime.Entity(s.id), Definition = w,
                    Value = w.ArgumentIndex >= 0 ? invocation.Arguments[w.ArgumentIndex].Copy() : w.Value.Copy() });
            }).ToArray();
            foreach (var write in writes)
            {
                VariableDefinition field = write.Target?.GetVariableDefinition(write.Definition.Field) ?? throw new CommandException("행동 대상의 변수가 없습니다: " + write.Definition.Field);
                if (field.Getter != null || (write.Value.kind != DataKind.Empty && !runtime.Schemas.Accepts(field.Kind, field.SchemaId, write.Value, runtime.Entity)))
                    throw new CommandException("함수의 변수 변경 자료형이 맞지 않습니다: " + field.Code);
            }
            // Validate every write before changing values, knowledge, or available conversation topics.
            foreach (var write in writes) write.Target.SetValue(write.Definition.Field, write.Value);
            runtime.GrantKnowledge(UnlockFacts, GrantAuthorities);
            runtime.ApplyStoryEffects(StoryEffects);
            foreach (MemberToggle change in MemberChanges)
                if (change.Function) target.SetFunctionEnabled(change.Name, change.Enabled); else target.SetVariableEnabled(change.Name, change.Enabled);
            runtime.ApplyReports(invocation, Reports, Discoveries, Incidents);
            return !string.IsNullOrEmpty(ReturnField) ? target.GetValue(ReturnField) : returned ?? Result.Copy();
        }
    }
}
