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
        private readonly HashSet<EntityDefinition> legacyVisiting = new HashSet<EntityDefinition>();

        private EntityAssetCompiler(KernelRuntime runtime) { this.runtime = runtime; }
        public static EntityInstanceAsset[] ReferencedInstances(EntitySchemaAsset[] schemas, EntityInstanceAsset[] instances)
        {
            var compiler = new EntityAssetCompiler(null);
            foreach (EntitySchemaAsset schema in schemas ?? Array.Empty<EntitySchemaAsset>()) compiler.Collect(schema);
            foreach (EntityInstanceAsset instance in instances ?? Array.Empty<EntityInstanceAsset>()) compiler.Collect(instance);
            return compiler.objects.ToArray();
        }
        public static void Install(KernelRuntime runtime, EntitySchemaAsset[] schemas, EntityInstanceAsset[] instances,
            EntityDefinition[] legacyObjects = null)
        {
            var compiler = new EntityAssetCompiler(runtime);
            foreach (EntitySchemaAsset schema in schemas ?? Array.Empty<EntitySchemaAsset>()) compiler.Collect(schema);
            foreach (EntityInstanceAsset instance in instances ?? Array.Empty<EntityInstanceAsset>()) compiler.Collect(instance);
            if (compiler.objects.GroupBy(a => a.instanceId, StringComparer.Ordinal).Any(g => g.Count() > 1)) throw new CommandException("자료 에셋의 인스턴스 ID가 중복됩니다.");
            foreach (EntitySchemaAsset schema in compiler.types) compiler.Declare(schema);
            foreach (EntitySchemaAsset schema in compiler.types) compiler.Fields(schema);
            foreach (EntitySchemaAsset schema in compiler.types)
                foreach (ActionDefinition action in schema.functions) compiler.compiled[schema].Operations.Add(compiler.Function(action, compiler.compiled[schema].FindVariable).Compile());
            runtime.NotifySchemaChanged();
            // Declare all identities before filling references, including cycles between two instances.
            foreach (EntityInstanceAsset asset in compiler.objects)
            {
                if (string.IsNullOrEmpty(asset.SchemaId)) throw new CommandException("인스턴스 스키마가 없습니다: " + asset.name);
                Entity entity = runtime.Entity(asset.instanceId);
                if (entity == null) entity = runtime.CreateInstance(asset.SchemaId, asset.instanceId, string.IsNullOrWhiteSpace(asset.displayName) ? asset.name : asset.displayName, false);
                else if (!asset.reuseExisting || !entity.Schema.IsA(runtime.Schemas.Require(asset.SchemaId))) throw new CommandException("인스턴스 ID가 중복되거나 재사용 자료형이 다릅니다: " + asset.instanceId);
                if (!string.IsNullOrEmpty(asset.BuiltinSchemaId) && !entity.Schema.IsA(runtime.Schemas.Require(asset.BuiltinSchemaId))) throw new CommandException("에셋의 기본 자료형과 스키마가 다릅니다: " + asset.instanceId);
                entity.Label = string.IsNullOrWhiteSpace(asset.displayName) ? asset.name : asset.displayName;
                entity.LabelKey = asset.displayNameKey;
                entity.Aliases = asset.aliases; entity.Names = asset.names; entity.Access = asset.access.Compile();
                entity.Viewer = asset.viewer != null ? asset.viewer.Compile() : null;
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
                foreach (InitialFieldDefinition initial in asset.Values) entity.SetValue(initial.field, compiler.Value(initial.value, initial.reference));
                var localizedFields = new HashSet<string>(StringComparer.Ordinal);
                foreach (FieldTextKey binding in asset.textKeys ?? Array.Empty<FieldTextKey>())
                {
                    VariableDefinition field = binding == null ? null : entity.GetVariableDefinition(binding.field);
                    if (field == null || field.Kind != DataKind.Text || field.Getter != null || string.IsNullOrWhiteSpace(binding.key) || !localizedFields.Add(field.Code))
                        throw new CommandException("번역할 텍스트 필드 또는 키가 유효하지 않습니다: " + asset.instanceId);
                    Datum value = entity.GetValue(field.Code);
                    value = value.kind == DataKind.Empty ? Datum.Text("") : value.Copy(); value.textKey = binding.key;
                    entity.SetValue(field.Code, value);
                }
                foreach (ChildInstanceDefinition child in asset.children)
                {
                    if (child.instance == null || !CommandParser.IsIdentifier(child.name)) throw new CommandException("하위 개체 연결이 유효하지 않습니다: " + asset.instanceId);
                    if (entity.FindVariable(child.name) != null || entity.Find(child.name) != null) throw new CommandException("하위 개체 이름이 멤버와 충돌합니다: " + child.name);
                    entity.Children.Add(child.name, runtime.Entity(child.instance.instanceId));
                }
                foreach (string name in asset.disabledVariables) entity.SetVariableEnabled(name, false);
                foreach (string name in asset.disabledFunctions) entity.SetFunctionEnabled(name, false);
                entity.ValidateMembers();
                if (asset.exposeRoot) runtime.Register(entity, false);
            }
            foreach (EntityDefinition legacy in legacyObjects ?? Array.Empty<EntityDefinition>()) runtime.Register(compiler.Legacy(legacy, 0));
            runtime.NotifySchemaChanged();
            foreach (EntityInstanceAsset asset in compiler.objects.Where(a => a.KnownOnStart)) runtime.Discover(asset.instanceId, "초기 작전망", true);
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
            foreach (InitialFieldDefinition initial in asset.Values) Collect(initial.reference);
            foreach (EntityInstanceAsset dependency in asset.Dependencies) Collect(dependency);
            foreach (ChildInstanceDefinition child in asset.children) Collect(child.instance);
        }
        private EntitySchema Declare(EntitySchemaAsset asset)
        {
            if (compiled.TryGetValue(asset, out EntitySchema known)) return known;
            if (!visiting.Add(asset) || visiting.Count > 64) throw new CommandException("스키마 부모에 순환이 있거나 깊이(64)를 초과했습니다: " + asset.name);
            EntitySchema parent = asset.parent != null ? Declare(asset.parent) : runtime.Schemas.Require(asset.builtinParent);
            var schema = new EntitySchema(asset.schemaId, asset.displayName, asset.inheritKind ? parent.Kind : asset.kind, parent)
            { Aliases = asset.aliases, Names = asset.names, LabelKey = asset.displayNameKey, Access = asset.access.Compile(), Viewer = asset.viewer != null ? asset.viewer.Compile() : null };
            runtime.Schemas.Register(schema);
            compiled.Add(asset, schema); visiting.Remove(asset);
            return schema;
        }
        private void Fields(EntitySchemaAsset asset)
        {
            foreach (FieldDefinition field in asset.fields)
                compiled[asset].Variables.Add(Field(field));
        }
        private VariableDefinition Field(FieldDefinition field) => new VariableDefinition { Code = field.code, Label = field.label, LabelKey = field.labelKey,
                    Kind = string.IsNullOrEmpty(field.typeName) ? field.kind : DataTypes.Parameter(field.code, field.typeName).Kind,
                    SchemaId = !string.IsNullOrEmpty(field.typeName) ? DataTypes.Parameter(field.code, field.typeName).SchemaId : field.referenceType != null ? field.referenceType.schemaId : "", DefaultValue = field.defaultValue.Copy(),
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
                ReturnSchemaId = returned?.SchemaId ?? (action.returnInstance != null ? action.returnInstance.SchemaId : ""),
                Property = action.property, Overrides = action.overrides,
                Parameters = Array.ConvertAll(action.parameters, p => new OperationInput { Name = p.name,
                    Kind = string.IsNullOrEmpty(p.typeName) ? p.kind : DataTypes.Parameter(p.name, p.typeName).Kind,
                    SchemaId = !string.IsNullOrEmpty(p.typeName) ? DataTypes.Parameter(p.name, p.typeName).SchemaId : p.referenceType != null ? p.referenceType.schemaId : "" }),
                Minutes = action.minutes, Access = new AccessRule { Facts = action.requiredFacts, Authorities = action.requiredAuthorities,
                    ForbiddenFacts = action.forbiddenFacts, RequiredData = action.requiredData, NeedsReadyThread = action.needsReadyThread,
                    PlayerAccess = action.playerAccess, Story = action.story?.Copy() ?? new StoryCondition() }, StoryEffects = action.storyEffects?.Copy() ?? new StoryEffects(),
                Result = action.result.Copy(), ReturnField = action.returnField, ReturnEntityId = action.returnInstance != null ? action.returnInstance.instanceId : "",
                UnlockFacts = action.unlockFacts, GrantAuthorities = action.grantAuthorities, MemberChanges = action.memberChanges,
                DispatchToThread = action.dispatchToThread, Reports = (action.reports ?? Array.Empty<ObservationDefinition>()).Select(r => r.Copy()).ToArray(), Discoveries = (action.discoveries ?? Array.Empty<string>()).ToArray(),
                Incidents = (action.incidents ?? Array.Empty<string>()).ToArray(), Responses = (action.responses ?? Array.Empty<ActionResponseDefinition>()).Select(r => r.Copy()).ToArray(),
                FieldWrites = action.fieldWrites.Select(w => new OperationFieldWrite { Field = w.field, ArgumentIndex = w.argumentIndex,
                    TargetArgumentIndex = w.useArgumentTarget ? w.targetArgumentIndex : -1,
                    Value = w.reference != null ? new Datum { kind = DataKind.Entity, id = w.reference.instanceId, label = w.reference.displayName, labelKey = w.reference.displayNameKey,
                        schemaId = w.reference.SchemaId } : w.value.Copy() }).ToArray() };
            foreach (FieldWriteDefinition write in action.fieldWrites)
            {
                if (write.useArgumentTarget) continue;
                VariableDefinition field = findField(write.field) ?? throw new CommandException("함수가 변경할 변수가 없습니다: " + write.field);
                if (field.Getter != null || write.argumentIndex < -1 || write.argumentIndex >= action.parameters.Length)
                    throw new CommandException("변수 변경 설정이 유효하지 않습니다: " + write.field);
            }
            return operation;
        }

        private Entity Legacy(EntityDefinition definition, int depth)
        {
            if (definition == null) throw new CommandException("하위 개체 정의가 비어 있습니다.");
            if (depth > 64) throw new CommandException("하위 개체 깊이(64)를 초과했습니다.");
            if (!legacyVisiting.Add(definition)) throw new CommandException("하위 개체 정의에 순환 참조가 있습니다: " + definition.code);
            try
            {
                var schema = new EntitySchema("Content_" + definition.code, definition.label, definition.kind, runtime.Schemas.ForKind(definition.kind));
                foreach (ActionDefinition action in definition.actions) schema.Operations.Add(Function(action, schema.FindVariable).Compile());
                runtime.Schemas.Register(schema);
                var entity = new Entity(definition.code, definition.label, schema) { Aliases = definition.aliases, Names = definition.names,
                    Access = new AccessRule { Facts = definition.requiredFacts, Authorities = definition.requiredAuthorities, ForbiddenFacts = definition.forbiddenFacts } };
                foreach (EntityDefinition child in definition.children)
                {
                    Entity instance = Legacy(child, depth + 1);
                    entity.Children.Add(child.code, instance);
                }
                return entity;
            }
            finally { legacyVisiting.Remove(definition); }
        }
    }
}
