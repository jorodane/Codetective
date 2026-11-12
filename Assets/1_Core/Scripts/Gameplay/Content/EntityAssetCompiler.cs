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
            foreach (EntitySchemaAsset schema in compiler.types) compiler.Functions(schema);
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
                foreach (InitialFieldDefinition initial in asset.initialValues) entity.SetValue(initial.field, compiler.Value(initial.value, initial.reference));
                foreach (ChildInstanceDefinition child in asset.children)
                {
                    if (child.instance == null || !CommandParser.IsIdentifier(child.name)) throw new CommandException("하위 개체 연결이 유효하지 않습니다: " + asset.instanceId);
                    if (entity.FindVariable(child.name) != null || entity.Find(child.name) != null) throw new CommandException("하위 개체 이름이 멤버와 충돌합니다: " + child.name);
                    entity.Children.Add(child.name, runtime.Entity(child.instance.instanceId));
                }
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
            foreach (ActionDefinition function in asset.functions)
            {
                foreach (InputDefinition input in function.parameters) Collect(input.referenceType);
                Collect(function.returnInstance);
                foreach (FieldWriteDefinition write in function.fieldWrites) Collect(write.reference);
            }
        }
        private void Collect(EntityInstanceAsset asset)
        {
            if (asset == null || !objects.Add(asset)) return;
            Collect(asset.schema);
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
                compiled[asset].Variables.Add(new VariableDefinition { Code = field.code, Label = field.label, Kind = field.kind,
                    SchemaId = field.referenceType != null ? field.referenceType.schemaId : "", DefaultValue = field.defaultValue.Copy(),
                    Writable = field.writable, Overrides = field.overrides, Aliases = field.aliases, Names = field.names, Access = field.access.Compile() });
        }
        private void Functions(EntitySchemaAsset asset)
        { foreach (ActionDefinition function in asset.functions) compiled[asset].Operations.Add(Function(function, compiled[asset])); }

        private Datum Value(Datum constant, EntityInstanceAsset reference) => reference != null ?
            runtime.Reference(runtime.Entity(reference.instanceId) ?? throw new CommandException("개체 참조가 없습니다: " + reference.instanceId)) : (constant ?? Datum.Empty).Copy();

        private Operation Function(ActionDefinition action, EntitySchema owner)
        {
            VariableDefinition returned = string.IsNullOrEmpty(action.returnField) ? null : owner.FindVariable(action.returnField) ??
                throw new CommandException("반환 변수가 없습니다: " + action.returnField);
            var operation = new Operation { Code = action.code, Label = action.label, Aliases = action.aliases, Names = action.names,
                ReturnKind = returned?.Kind ?? (action.returnInstance != null ? DataKind.Entity : action.result.kind),
                ReturnSchemaId = returned?.SchemaId ?? (action.returnInstance != null ? action.returnInstance.schema.schemaId : ""),
                Property = action.property, Overrides = action.overrides,
                Parameters = Array.ConvertAll(action.parameters, p => new Parameter(p.name, p.kind, p.referenceType != null ? p.referenceType.schemaId : "")),
                Minutes = action.minutes, Access = new AccessRule { Facts = action.requiredFacts, Authorities = action.requiredAuthorities,
                    ForbiddenFacts = action.forbiddenFacts, RequiredData = action.requiredData, NeedsReadyThread = action.needsReadyThread } };
            foreach (FieldWriteDefinition write in action.fieldWrites)
            {
                VariableDefinition field = owner.FindVariable(write.field) ?? throw new CommandException("함수가 변경할 변수가 없습니다: " + write.field);
                if (field.Getter != null || write.argumentIndex < -1 || write.argumentIndex >= action.parameters.Length)
                    throw new CommandException("변수 변경 설정이 유효하지 않습니다: " + write.field);
            }
            operation.Invoke = c =>
            {
                // Resolve by stable ID in the invocation runtime, so tests and restored worlds stay isolated.
                var writes = action.fieldWrites.Select(w => new KeyValuePair<string, Datum>(w.field,
                    w.argumentIndex >= 0 ? c.Arguments[w.argumentIndex] : w.reference != null ?
                        c.Runtime.Reference(c.Runtime.Entity(w.reference.instanceId)) : w.value.Copy())).ToArray();
                foreach (var write in writes)
                {
                    VariableDefinition field = c.Target.FindVariable(write.Key);
                    if (write.Value.kind != DataKind.Empty && !c.Runtime.Schemas.Accepts(field.Kind, field.SchemaId, write.Value, c.Runtime.Entity))
                        throw new CommandException("함수의 변수 변경 자료형이 맞지 않습니다: " + write.Key);
                }
                foreach (var write in writes) c.Target.SetValue(write.Key, write.Value);
                c.Runtime.Facts.UnionWith(action.unlockFacts); c.Runtime.Authorities.UnionWith(action.grantAuthorities);
                return returned != null ? c.Target.GetValue(returned.Code) : action.returnInstance != null ?
                    c.Runtime.Reference(c.Runtime.Entity(action.returnInstance.instanceId)) : action.result.Copy();
            };
            return operation;
        }

        private Entity Legacy(EntityDefinition definition, int depth)
        {
            if (depth > 64) throw new CommandException("하위 개체 깊이(64)를 초과했습니다.");
            var schema = new EntitySchema("Content_" + definition.code, definition.label, definition.kind, runtime.Schemas.ForKind(definition.kind));
            foreach (ActionDefinition action in definition.actions) schema.Operations.Add(Function(action, schema));
            runtime.Schemas.Register(schema);
            var entity = new Entity(definition.code, definition.label, schema) { Aliases = definition.aliases, Names = definition.names,
                Access = new AccessRule { Facts = definition.requiredFacts, Authorities = definition.requiredAuthorities, ForbiddenFacts = definition.forbiddenFacts } };
            foreach (EntityDefinition child in definition.children) entity.Children.Add(child.code, Legacy(child, depth + 1));
            return entity;
        }
    }
}
