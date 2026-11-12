using System;
using System.Collections.Generic;
using System.Linq;

namespace Codetective.Operations
{
    public sealed partial class Entity
    {
        private readonly Dictionary<string, VariableDefinition> localVariables = new Dictionary<string, VariableDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, Operation> localFunctions = new Dictionary<string, Operation>(StringComparer.Ordinal);
        private readonly Dictionary<string, OperationDefinition> functionDefinitions = new Dictionary<string, OperationDefinition>(StringComparer.Ordinal);
        private readonly HashSet<string> disabledVariables = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> disabledFunctions = new HashSet<string>(StringComparer.Ordinal);
        internal Action<Entity> MemberValidator;

        public IEnumerable<VariableDefinition> LocalVariables => localVariables.Values;
        public IEnumerable<Operation> LocalFunctions => localFunctions.Values;
        internal IEnumerable<VariableDefinition> AllVariables => EntitySchema.Merge(
            (Schema?.EffectiveVariables ?? Enumerable.Empty<VariableDefinition>()).Concat(localVariables.Values), v => v.Code);
        internal IEnumerable<Operation> AllOperations => EntitySchema.Merge(
            (Schema?.EffectiveOperations ?? Enumerable.Empty<Operation>()).Concat(Operations).Concat(localFunctions.Values), o => o.Code);
        /// <summary>System lookup includes disabled members, whose stored values still belong to this instance.</summary>
        public VariableDefinition GetVariableDefinition(string name) => AllVariables.FirstOrDefault(v => v.Matches(name));

        /// <summary>World/story API. Player assignments can only update fields already declared and writable.</summary>
        public void AddVariable(VariableDefinition definition, bool replace = false)
        {
            if (definition == null || definition.Getter != null || definition.Access.Condition != null || definition.Access.TargetCondition != null)
                throw new CommandException("인스턴스 변수에는 저장 가능한 선언을 사용하세요. 시스템 getter/조건은 스키마에 정의하세요.");
            VariableDefinition copy = definition.Copy();
            bool existed = localVariables.TryGetValue(copy.Code ?? "", out VariableDefinition prior);
            if (existed && !replace) throw new CommandException("이미 등록된 인스턴스 변수입니다: " + copy.Code);
            localVariables[copy.Code ?? ""] = copy;
            try { ValidateMembers(); }
            catch { if (existed) localVariables[copy.Code ?? ""] = prior; else localVariables.Remove(copy.Code ?? ""); throw; }
            ValueChanged?.Invoke();
        }

        public bool RemoveVariable(string name)
        {
            VariableDefinition field = localVariables.Values.FirstOrDefault(v => v.Matches(name));
            if (field == null) return false;
            localVariables.Remove(field.Code); values.Remove(field.Code); disabledVariables.Remove(field.Code);
            ValueChanged?.Invoke(); return true;
        }

        public void AddFunction(OperationDefinition definition, bool replace = false)
        {
            if (definition == null) throw new CommandException("인스턴스 함수 선언이 비어 있습니다.");
            OperationDefinition copy = definition.Copy();
            Operation operation = copy.Compile();
            bool existed = localFunctions.TryGetValue(copy.Code ?? "", out Operation prior);
            if (existed && !replace) throw new CommandException("이미 등록된 인스턴스 함수입니다: " + copy.Code);
            localFunctions[copy.Code ?? ""] = operation;
            try { ValidateMembers(); }
            catch { if (existed) localFunctions[copy.Code ?? ""] = prior; else localFunctions.Remove(copy.Code ?? ""); throw; }
            functionDefinitions[copy.Code] = copy;
            ValueChanged?.Invoke();
        }

        public bool RemoveFunction(string name)
        {
            Operation function = localFunctions.Values.FirstOrDefault(f => f.Matches(name));
            if (function == null) return false;
            localFunctions.Remove(function.Code); functionDefinitions.Remove(function.Code); disabledFunctions.Remove(function.Code);
            ValueChanged?.Invoke(); return true;
        }

        public void SetVariableEnabled(string name, bool enabled)
        {
            VariableDefinition field = GetVariableDefinition(name) ?? throw new CommandException("존재하지 않는 변수입니다: " + name);
            SetEnabled(disabledVariables, field.Code, enabled);
        }
        public void SetFunctionEnabled(string name, bool enabled)
        {
            Operation function = AllOperations.FirstOrDefault(o => o.Matches(name)) ?? throw new CommandException("존재하지 않는 함수입니다: " + name);
            SetEnabled(disabledFunctions, function.Code, enabled);
        }
        private void SetEnabled(HashSet<string> disabled, string code, bool enabled)
        { if (enabled ? disabled.Remove(code) : disabled.Add(code)) ValueChanged?.Invoke(); }

        public void ValidateMembers()
        {
            var layer = new EntitySchema("Instance_" + Id, Label, Kind, Schema);
            layer.Variables.AddRange(localVariables.Values);
            layer.Operations.AddRange(localFunctions.Values);
            SchemaRegistry.ValidateMembers(layer);
            foreach (string name in Children.SelectMany(c => c.Value.AllNames.Concat(new[] { c.Key })))
                if (AllVariables.Any(v => v.Matches(name)) || AllOperations.Any(o => o.Matches(name)))
                    throw new CommandException("인스턴스 멤버가 하위 개체 이름과 충돌합니다: " + name);
            foreach (Operation operation in Operations)
                if (localVariables.Values.Any(v => v.AllNames.Intersect(operation.AllNames).Any()) || localFunctions.Values.Any(f => f.AllNames.Intersect(operation.AllNames).Any()))
                    throw new CommandException("인스턴스 멤버가 기존 함수와 충돌합니다: " + operation.Code);
            MemberValidator?.Invoke(this);
        }
        internal VariableDefinition[] CaptureLocalVariables() => localVariables.Values.Select(v => v.Copy()).ToArray();
        internal OperationDefinition[] CaptureLocalFunctions() => functionDefinitions.Values.Select(f => f.Copy()).ToArray();
        internal string[] CaptureDisabledVariables() => disabledVariables.ToArray();
        internal string[] CaptureDisabledFunctions() => disabledFunctions.ToArray();
        internal void RestoreMembers(VariableDefinition[] fields, OperationDefinition[] functions, string[] hiddenFields, string[] hiddenFunctions)
        {
            foreach (VariableDefinition field in fields ?? Array.Empty<VariableDefinition>()) AddVariable(field);
            foreach (OperationDefinition function in functions ?? Array.Empty<OperationDefinition>()) AddFunction(function);
            foreach (string name in hiddenFields ?? Array.Empty<string>()) SetVariableEnabled(name, false);
            foreach (string name in hiddenFunctions ?? Array.Empty<string>()) SetFunctionEnabled(name, false);
        }
    }
}
