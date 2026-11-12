using System;
using System.Collections.Generic;
using System.Linq;
using Codetective.Operations;

namespace Codetective.Gameplay
{
    /// <summary>Builds designer-owned instances. This API is never registered as a player command.</summary>
    public sealed class EntityAssetCompiler
    {
        private readonly KernelRuntime runtime;
        private readonly HashSet<EntitySchemaAsset> types = new HashSet<EntitySchemaAsset>();
        private readonly HashSet<EntityInstanceAsset> objects = new HashSet<EntityInstanceAsset>();
        private readonly Dictionary<EntitySchemaAsset, EntitySchema> compiled = new Dictionary<EntitySchemaAsset, EntitySchema>();
        private readonly HashSet<EntitySchemaAsset> visiting = new HashSet<EntitySchemaAsset>();

        private EntityAssetCompiler(KernelRuntime runtime) { this.runtime = runtime; }
        public static void Install(KernelRuntime runtime, EntitySchemaAsset[] schemas, EntityInstanceAsset[] instances,
            EntityDefinition[] legacyObjects = null)
        {
            var compiler = new EntityAssetCompiler(runtime);
            foreach (EntitySchemaAsset schema in schemas ?? Array.Empty<EntitySchemaAsset>()) compiler.Collect(schema);
            foreach (EntityInstanceAsset instance in instances ?? Array.Empty<EntityInstanceAsset>()) compiler.Collect(instance);
            foreach (EntitySchemaAsset schema in compiler.types) compiler.Declare(schema);
            foreach (EntitySchemaAsset schema in compiler.types) compiler.Fields(schema);
            foreach (EntitySchemaAsset schema in compiler.types)
                foreach (ActionDefinition action in schema.functions) compiler.compiled[schema].Operations.Add(compiler.Function(action, compiler.compiled[schema].FindVariable).Compile());
            runtime.NotifySchemaChanged();
            // Declare all identities before filling references, including cycles between two instances.
            foreach (EntityInstanceAsset asset in compiler.objects)
            {
                if (asset.schema == null) throw new CommandException("인스턴스 스키마가 없습니다: " + asset.name);
                Entity entity = runtime.CreateInstance(asset.schema.schemaId, asset.instanceId, asset.displayName, false);
                entity.Aliases = asset.aliases; entity.Names = asset.names; entity.Access = asset.access.Compile();
            }
            foreach (EntityInstanceAsset asset in compiler.objects)
            {
                Entity entity = runtime.Entity(asset.instanceId);
                foreach (FieldDefinition field in asset.fields) entity.AddVariable(compiler.Field(field));
            }
            foreach (EntityInstanceAsset asset in compiler.objects)
            {
                Entity entity = runtime.Entity(asset.instanceId);
                foreach (ActionDefinition action in asset.functions) entity.AddFunction(compiler.Function(action, entity.GetVariableDefinition));
                foreach (InitialFieldDefinition initial in asset.initialValues) entity.SetValue(initial.field, compiler.Value(initial.value, initial.reference));
                foreach (ChildInstanceDefinition child in asset.children)
                {
                    if (child.instance == null || !CommandParser.IsIdentifier(child.name)) throw new CommandException("하위 개체 연결이 유효하지 않습니다: " + asset.instanceId);
                    if (entity.FindVariable(child.name) != null || entity.Find(child.name) != null) throw new CommandException("하위 개체 이름이 멤버와 충돌합니다: " + child.name);
                    entity.Children.Add(child.name, runtime.Entity(child.instance.instanceId));
                }
                foreach (string name in asset.disabledVariables) entity.SetVariableEnabled(name, false);
                foreach (string name in asset.disabledFunctions) entity.SetFunctionEnabled(name, false);
                entity.ValidateMembers();
                if (asset.exposeRoot) runtime.Register(entity);
            }
            foreach (EntityDefinition legacy in legacyObjects ?? Array.Empty<EntityDefinition>()) runtime.Register(compiler.Legacy(legacy, 0));
            runtime.NotifySchemaChanged();
        }

        private void Collect(EntitySchemaAsset asset)
        {
            if (asset == null || !types.Add(asset)) return;
            Collect(asset.parent);
            foreach (FieldDefinition field in asset.fields) Collect(field.referenceType);
            foreach (ActionDefinition function in asset.functions) Collect(function);
        }
        private void Collect(EntityInstanceAsset asset)
        {
            if (asset == null || !objects.Add(asset)) return;
            Collect(asset.schema);
            foreach (FieldDefinition field in asset.fields) Collect(field.referenceType);
            foreach (ActionDefinition function in asset.functions) Collect(function);
            foreach (InitialFieldDefinition initial in asset.initialValues) Collect(initial.reference);
            foreach (ChildInstanceDefinition child in asset.children) Collect(child.instance);
        }
        private EntitySchema Declare(EntitySchemaAsset asset)
        {
            if (compiled.TryGetValue(asset, out EntitySchema known)) return known;
            if (!visiting.Add(asset) || visiting.Count > 64) throw new CommandException("스키마 부모에 순환이 있거나 깊이(64)를 초과했습니다: " + asset.name);
            EntitySchema parent = asset.parent != null ? Declare(asset.parent) : runtime.Schemas.Require(asset.builtinParent);
            var schema = new EntitySchema(asset.schemaId, asset.displayName, asset.inheritKind ? parent.Kind : asset.kind, parent)
            { Aliases = asset.aliases, Names = asset.names, Access = asset.access.Compile() };
            runtime.Schemas.Register(schema);
            compiled.Add(asset, schema); visiting.Remove(asset);
            return schema;
        }
        private void Fields(EntitySchemaAsset asset)
        {
            foreach (FieldDefinition field in asset.fields)
                compiled[asset].Variables.Add(Field(field));
        }
        private VariableDefinition Field(FieldDefinition field) => new VariableDefinition { Code = field.code, Label = field.label, Kind = field.kind,
                    SchemaId = field.referenceType != null ? field.referenceType.schemaId : "", DefaultValue = field.defaultValue.Copy(),
                    Writable = field.writable, Overrides = field.overrides, Aliases = field.aliases, Names = field.names, Access = field.access.Compile() };
        private void Collect(ActionDefinition function)
        {
            foreach (InputDefinition input in function.parameters) Collect(input.referenceType);
            Collect(function.returnInstance);
            foreach (FieldWriteDefinition write in function.fieldWrites) Collect(write.reference);
        }

        private Datum Value(Datum constant, EntityInstanceAsset reference) => reference != null ?
            runtime.Reference(runtime.Entity(reference.instanceId) ?? throw new CommandException("개체 참조가 없습니다: " + reference.instanceId)) : (constant ?? Datum.Empty).Copy();

        private OperationDefinition Function(ActionDefinition action, Func<string, VariableDefinition> findField)
        {
            VariableDefinition returned = string.IsNullOrEmpty(action.returnField) ? null : findField(action.returnField) ??
                throw new CommandException("반환 변수가 없습니다: " + action.returnField);
            var operation = new OperationDefinition { Code = action.code, Label = action.label, Aliases = action.aliases, Names = action.names,
                ReturnKind = returned?.Kind ?? (action.returnInstance != null ? DataKind.Entity : action.result.kind),
                ReturnSchemaId = returned?.SchemaId ?? (action.returnInstance != null ? action.returnInstance.schema.schemaId : ""),
                Property = action.property, Overrides = action.overrides,
                Parameters = Array.ConvertAll(action.parameters, p => new OperationInput { Name = p.name, Kind = p.kind, SchemaId = p.referenceType != null ? p.referenceType.schemaId : "" }),
                Minutes = action.minutes, Access = new AccessRule { Facts = action.requiredFacts, Authorities = action.requiredAuthorities,
                    ForbiddenFacts = action.forbiddenFacts, RequiredData = action.requiredData, NeedsReadyThread = action.needsReadyThread },
                Result = action.result.Copy(), ReturnField = action.returnField, ReturnEntityId = action.returnInstance != null ? action.returnInstance.instanceId : "",
                UnlockFacts = action.unlockFacts, GrantAuthorities = action.grantAuthorities, MemberChanges = action.memberChanges,
                FieldWrites = action.fieldWrites.Select(w => new OperationFieldWrite { Field = w.field, ArgumentIndex = w.argumentIndex,
                    Value = w.reference != null ? new Datum { kind = DataKind.Entity, id = w.reference.instanceId, label = w.reference.displayName,
                        schemaId = w.reference.schema != null ? w.reference.schema.schemaId : "" } : w.value.Copy() }).ToArray() };
            foreach (FieldWriteDefinition write in action.fieldWrites)
            {
                VariableDefinition field = findField(write.field) ?? throw new CommandException("함수가 변경할 변수가 없습니다: " + write.field);
                if (field.Getter != null || write.argumentIndex < -1 || write.argumentIndex >= action.parameters.Length)
                    throw new CommandException("변수 변경 설정이 유효하지 않습니다: " + write.field);
            }
            return operation;
        }

        private Entity Legacy(EntityDefinition definition, int depth)
        {
            if (depth > 64) throw new CommandException("하위 개체 깊이(64)를 초과했습니다.");
            var schema = new EntitySchema("Content_" + definition.code, definition.label, definition.kind, runtime.Schemas.ForKind(definition.kind));
            foreach (ActionDefinition action in definition.actions) schema.Operations.Add(Function(action, schema.FindVariable).Compile());
            runtime.Schemas.Register(schema);
            var entity = new Entity(definition.code, definition.label, schema) { Aliases = definition.aliases, Names = definition.names,
                Access = new AccessRule { Facts = definition.requiredFacts, Authorities = definition.requiredAuthorities, ForbiddenFacts = definition.forbiddenFacts } };
            foreach (EntityDefinition child in definition.children) entity.Children.Add(child.code, Legacy(child, depth + 1));
            return entity;
        }
    }
}
