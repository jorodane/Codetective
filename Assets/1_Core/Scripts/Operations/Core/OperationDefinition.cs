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
        public Datum Value = Datum.Empty;
        public OperationFieldWrite Copy() => new OperationFieldWrite { Field = Field, ArgumentIndex = ArgumentIndex, Value = Value.Copy() };
    }
    [Serializable] public sealed class MemberToggle
    {
        public string Name;
        public bool Function = true;
        public bool Enabled = true;
        public MemberToggle Copy() => (MemberToggle)MemberwiseClone();
    }

    /// <summary>Serializable function behavior. All targets are resolved in the invocation's world, including a test copy.</summary>
    [Serializable] public sealed class OperationDefinition
    {
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

        public OperationDefinition Copy() => new OperationDefinition { Code = Code, Label = Label, Aliases = Aliases.ToArray(),
            Names = Names.Select(n => new LocalizedCommandName(n.language, n.name, n.description)).ToArray(),
            Parameters = Parameters.Select(p => p.Copy()).ToArray(), ReturnKind = ReturnKind, ReturnSchemaId = ReturnSchemaId,
            Property = Property, Overrides = Overrides, Minutes = Minutes, Access = Access.Copy(), Result = Result.Copy(),
            ReturnField = ReturnField, ReturnEntityId = ReturnEntityId, FieldWrites = FieldWrites.Select(w => w.Copy()).ToArray(),
            UnlockFacts = UnlockFacts.ToArray(), GrantAuthorities = GrantAuthorities.ToArray(), MemberChanges = MemberChanges.Select(m => m.Copy()).ToArray(), StoryEffects = StoryEffects?.Copy() ?? new StoryEffects() };

        public Operation Compile()
        {
            StoryEffects?.Validate();
            if (Access.Condition != null || Access.TargetCondition != null)
                throw new CommandException("저장 가능한 함수 선언에는 사실/권한 조건을 사용하세요.");
            if (!CommandParser.IsIdentifier(Code) || CommandParser.IsAllocationKeyword(Code) || double.IsNaN(Minutes) || double.IsInfinity(Minutes) || Minutes < 0)
                throw new CommandException("함수 이름 또는 소요 시간이 유효하지 않습니다: " + Code);
            if (Parameters.Any(p => !CommandParser.IsIdentifier(p.Name)) || Parameters.Select(p => p.Name).Distinct().Count() != Parameters.Length ||
                FieldWrites.Any(w => !CommandParser.IsIdentifier(w.Field) || w.ArgumentIndex < -1 || w.ArgumentIndex >= Parameters.Length) ||
                MemberChanges.Any(m => !CommandParser.IsIdentifier(m.Name)) || (Property && Parameters.Length != 0))
                throw new CommandException("함수 입력/변수 변경 선언이 유효하지 않습니다: " + Code);
            OperationDefinition definition = Copy();
            AccessRule access = Access.Copy();
            access.TargetCondition = (r, e) => definition.HasTargets(e);
            return new Operation { Code = Code, Label = Label, Aliases = Aliases.ToArray(), Names = definition.Names,
                Parameters = Parameters.Select(p => new Parameter(p.Name, p.Kind, p.SchemaId)).ToArray(), ReturnKind = ReturnKind, ReturnSchemaId = ReturnSchemaId,
                Property = Property, Overrides = Overrides, Minutes = Minutes, Access = access, Invoke = definition.Invoke };
        }

        private bool HasTargets(Entity entity) => (string.IsNullOrEmpty(ReturnField) || entity.GetVariableDefinition(ReturnField) != null) &&
            FieldWrites.All(w => entity.GetVariableDefinition(w.Field) != null) && MemberChanges.All(m => m.Function ?
                entity.AllOperations.Any(o => o.Matches(m.Name)) : entity.GetVariableDefinition(m.Name) != null);

        private Datum Invoke(Invocation invocation)
        {
            Entity target = invocation.Target; KernelRuntime runtime = invocation.Runtime;
            if (!HasTargets(target)) throw new CommandException("함수에 필요한 인스턴스 멤버가 없습니다: " + Code);
            Datum returned = string.IsNullOrEmpty(ReturnEntityId) ? null : runtime.Reference(runtime.Entity(ReturnEntityId));
            Datum[] writes = FieldWrites.Select(w => w.ArgumentIndex >= 0 ? invocation.Arguments[w.ArgumentIndex].Copy() : w.Value.Copy()).ToArray();
            for (int i = 0; i < writes.Length; i++)
            {
                VariableDefinition field = target.GetVariableDefinition(FieldWrites[i].Field);
                if (field.Getter != null || (writes[i].kind != DataKind.Empty && !runtime.Schemas.Accepts(field.Kind, field.SchemaId, writes[i], runtime.Entity)))
                    throw new CommandException("함수의 변수 변경 자료형이 맞지 않습니다: " + field.Code);
            }
            // Validate every write before changing values, knowledge, or available conversation topics.
            for (int i = 0; i < writes.Length; i++) target.SetValue(FieldWrites[i].Field, writes[i]);
            runtime.Facts.UnionWith(UnlockFacts); runtime.Authorities.UnionWith(GrantAuthorities);
            runtime.ApplyStoryEffects(StoryEffects);
            foreach (MemberToggle change in MemberChanges)
                if (change.Function) target.SetFunctionEnabled(change.Name, change.Enabled); else target.SetVariableEnabled(change.Name, change.Enabled);
            return !string.IsNullOrEmpty(ReturnField) ? target.GetValue(ReturnField) : returned ?? Result.Copy();
        }
    }
}
