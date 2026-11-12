using System;
using System.Collections.Generic;
using System.Linq;

namespace Codetective.Operations
{
    public sealed partial class KernelRuntime
    {
        public SchemaRegistry Schemas { get; }
        public IReadOnlyDictionary<string, Entity> Entities => entities;
        public KernelRuntime(SchemaRegistry schemas = null) { Schemas = schemas ?? BuiltinSchemas.Create(); }

        /// <summary>System/designer API only. No constructor or allocator is registered in the command language.</summary>
        public Entity CreateInstance(string schemaId, string id, string label, bool exposeRoot = true)
        {
            EntitySchema schema = Schemas.Require(schemaId);
            Schemas.Validate();
            var entity = new Entity(id, label, schema);
            if (exposeRoot) Register(entity);
            else IndexEntity(entity, new HashSet<Entity>());
            Revision++;
            return entity;
        }

        public Datum Reference(Entity entity)
        {
            if (entity == null) throw new CommandException("개체 참조가 비어 있습니다.");
            Datum value = Datum.Ref(entity.Kind, entity.Id, entity.Label);
            value.schemaId = entity.Schema?.Id ?? "";
            return value;
        }

        public string TypeName(Datum value)
        {
            EntitySchema schema = value.CanReferenceEntity ? ReferenceTarget(value)?.Schema ?? Schemas.Find(value.schemaId) : null;
            return schema != null ? schema.NameFor(Language) : KindName(value.kind);
        }

        private void ConfigureInstance(Entity entity)
        {
            entity.ValueValidator = (field, value) => Schemas.Accepts(field.Kind, field.SchemaId, value, Entity);
            entity.ValueNormalizer = TagReference;
            entity.ValueChanged = () => { Revision++; NarrativeUpdated(); };
            entity.MemberValidator = ValidateInstanceMembers;
        }
        public void NotifySchemaChanged()
        {
            Schemas.Validate();
            foreach (Entity entity in entities.Values) entity.ValidateMembers();
            Revision++;
        }
        private void ValidateInstanceMembers(Entity entity) => ValidateInstanceMembers(entity, Entity);
        private void ValidateInstanceMembers(Entity entity, Func<string, Entity> resolve)
        {
            foreach (VariableDefinition field in entity.LocalVariables)
            {
                if (!string.IsNullOrEmpty(field.SchemaId)) Schemas.Require(field.SchemaId);
                if (!CommandParser.IsIdentifier(field.Code) || CommandParser.IsAllocationKeyword(field.Code) ||
                    (field.DefaultValue.kind != DataKind.Empty && !Schemas.Accepts(field.Kind, field.SchemaId, field.DefaultValue, resolve)))
                    throw new CommandException("인스턴스 변수 선언/기본값이 유효하지 않습니다: " + field.Code);
            }
            foreach (Operation operation in entity.LocalFunctions)
            {
                if (!string.IsNullOrEmpty(operation.ReturnSchemaId)) Schemas.Require(operation.ReturnSchemaId);
                foreach (Parameter parameter in operation.Parameters) if (!string.IsNullOrEmpty(parameter.SchemaId)) Schemas.Require(parameter.SchemaId);
            }
            foreach (DataSlot stored in entity.CaptureValues())
            {
                VariableDefinition field = entity.GetVariableDefinition(stored.name);
                if (field != null && stored.value.kind != DataKind.Empty && !Schemas.Accepts(field.Kind, field.SchemaId, stored.value, resolve))
                    throw new CommandException("변경된 선언과 기존 변수의 자료형이 다릅니다: " + stored.name);
            }
        }

        private bool Accepts(Parameter parameter, Datum value) => Schemas.Accepts(parameter.Kind, parameter.SchemaId, value, Entity);
        private Entity ReferenceTarget(Datum value) => value.CanReferenceEntity ? Entity(value.id) : null;
        private Datum PlayerValue(Datum value, Dictionary<string, Datum> locals)
        {
            Entity target = ReferenceTarget(value);
            if (target != null) CheckEntityAccess(target, locals);
            return value.Copy();
        }
        private bool PlayerValueVisible(Datum value, Dictionary<string, Datum> locals) =>
            ReferenceTarget(value) == null || EntityDenial(ReferenceTarget(value), locals) == null;
        public bool IsPlayerVisible(Datum value) => value != null && PlayerValueVisible(value, null);
        public bool IsPlayerVisible(Entity entity) => entity != null && EntityDenial(entity) == null;
        private Datum TagReference(Datum value)
        {
            Entity target = ReferenceTarget(value);
            if (target != null) { value.kind = target.Kind; value.schemaId = target.Schema.Id; }
            return value;
        }
        private Datum Invoke(Entity target, Operation operation, Datum[] arguments, string agent)
        {
            Datum result = NormalizeReference(operation.Invoke(new Invocation { Runtime = this, Target = target, Arguments = arguments, AgentId = agent }));
            if (result.kind != DataKind.Empty && !string.IsNullOrEmpty(operation.ReturnSchemaId) && !Schemas.Accepts(operation.ReturnKind, operation.ReturnSchemaId, result, Entity))
                throw new CommandException("함수의 반환 자료형이 맞지 않습니다: " + operation.Code);
            return result;
        }
        private void CheckEntityAccess(Entity entity, Dictionary<string, Datum> locals = null)
        {
            if (!entity.Exposed("entity", "", entity.AccessRules)) throw new CommandException("현재 제공되지 않는 대상입니다.");
            foreach (AccessRule rule in entity.AccessRules) CheckAccess(rule, locals, entity);
        }
        private string EntityDenial(Entity entity, Dictionary<string, Datum> locals = null) =>
            !entity.Exposed("entity", "", entity.AccessRules) ? "현재 제공되지 않는 대상입니다." : entity.AccessRules.Select(r => r.Denial(this, locals?.Values, entity, true)).FirstOrDefault(d => d != null);
        private void CheckOperationAccess(Entity target, Operation operation, Dictionary<string, Datum> locals)
        {
            if (!target.Exposed("function", operation.Code, target.OperationRules(operation))) throw new CommandException("현재 제공되지 않는 함수입니다.");
            foreach (AccessRule rule in target.OperationRules(operation)) CheckAccess(rule, locals, target);
        }
        private string OperationDenial(Entity target, Operation operation, Dictionary<string, Datum> locals) =>
            !target.Exposed("function", operation.Code, target.OperationRules(operation)) ? "현재 제공되지 않는 함수입니다." : target.OperationRules(operation).Select(r => r.Denial(this, locals?.Values, target, true)).FirstOrDefault(d => d != null);
        private string VariableDenial(Entity target, VariableDefinition field, Dictionary<string, Datum> locals) =>
            !target.Exposed("variable", field.Code, target.VariableRules(field)) ? "현재 제공되지 않는 변수입니다." : target.VariableRules(field).Select(r => r.Denial(this, locals?.Values, target, true)).FirstOrDefault(d => d != null);
        private void CheckVariableAccess(Entity target, VariableDefinition field, Dictionary<string, Datum> locals)
        {
            string denial = VariableDenial(target, field, locals); if (denial != null) throw new CommandException(denial);
        }

        private VariableDefinition CheckVariableWrite(Entity entity, string name, Dictionary<string, Datum> locals)
        {
            VariableDefinition field = entity.FindVariable(name) ?? throw new CommandException("존재하지 않는 변수입니다: " + name);
            if (!field.Writable || field.Getter != null) throw new CommandException("읽기 전용 변수입니다: " + name);
            CheckVariableAccess(entity, field, locals);
            if (IsDayEnded && !DryRun) throw new CommandException("오늘의 업무 시간이 끝났습니다.");
            return field;
        }

        private Datum NormalizeReference(Datum value)
        {
            if (value == null) return Datum.Empty;
            value = value.Copy();
            if (string.IsNullOrEmpty(value.id) || value.kind == DataKind.Task || value.kind == DataKind.Protocol) return value;
            Entity target = Entity(value.id);
            if (target == null && IsRecord(value.kind))
            {
                EntitySchema schema = Schemas.ForKind(value.kind);
                target = new Entity(value.id, value.label, schema);
                target.SetValue("description", Datum.Text(value.text));
                IndexEntity(target, new HashSet<Entity>());
            }
            return TagReference(value);
        }

        private static bool IsRecord(DataKind kind) => kind == DataKind.Statement || kind == DataKind.Document || kind == DataKind.Photo ||
            kind == DataKind.Analysis || kind == DataKind.Log || kind == DataKind.Rule || kind == DataKind.Relationship || kind == DataKind.Clue;

        private KernelRuntime CopyWorld()
        {
            var registry = new SchemaRegistry();
            foreach (EntitySchema schema in Schemas.All) registry.Register(schema);
            var copy = new KernelRuntime(registry) { DryRun = true, Language = Language, StoryStartFact = StoryStartFact };
            foreach (StoryMail message in mail.Values) copy.RegisterMail(message);
            foreach (NewsArticle article in news.Values) copy.RegisterNews(article);
            foreach (Entity original in entities.Values)
            {
                var clone = new Entity(original.Id, original.Label, original.Schema)
                { Aliases = original.Aliases.ToArray(), Names = original.Names.ToArray(), Access = original.Access };
                clone.Operations.AddRange(original.Operations);
                copy.entities.Add(clone.Id, clone);
            }
            foreach (var root in roots) copy.roots.Add(root.Key, copy.entities[root.Value.Id]);
            copy.Consultation.AddRange(Consultation);
            copy.Restore(Capture());
            return copy;
        }

        private void ValidateProtocolTypes(Protocol protocol)
        {
            foreach (Parameter parameter in protocol.Parameters)
                if (!string.IsNullOrEmpty(parameter.SchemaId)) Schemas.Require(parameter.SchemaId);
        }

        private EntitySchema SchemaOf(Datum value) => value.CanReferenceEntity ? ReferenceTarget(value)?.Schema ?? Schemas.Find(value.schemaId) ??
            Schemas.Find(value.kind.ToString()) : null;

        private void RestoreEntities(EntitySnapshot[] savedEntities)
        {
            if (savedEntities == null) return; // Version 1 saves made before schemas retain the scenario defaults.
            var restored = new Dictionary<string, Entity>(StringComparer.Ordinal);
            foreach (EntitySnapshot saved in savedEntities)
            {
                EntitySchema schema = Schemas.Require(saved.schemaId);
                var entity = new Entity(saved.id, saved.label, schema)
                { Aliases = saved.aliases ?? Array.Empty<string>(), Names = saved.names ?? Array.Empty<LocalizedCommandName>() };
                if (entities.TryGetValue(saved.id, out Entity original))
                {
                    entity.Access = original.Access;
                    entity.Operations.AddRange(original.Operations);
                }
                restored.Add(entity.Id, entity);
            }
            // Definitions must exist on every instance before restoring values and cross-instance references.
            foreach (EntitySnapshot saved in savedEntities)
            {
                Entity original = Entity(saved.id);
                restored[saved.id].RestoreMembers(saved.localVariables ?? original?.CaptureLocalVariables(),
                    saved.localFunctions ?? original?.CaptureLocalFunctions(), saved.disabledVariables ?? original?.CaptureDisabledVariables(),
                    saved.disabledFunctions ?? original?.CaptureDisabledFunctions());
                restored[saved.id].RestorePlayerAccess(saved.playerAccess ?? original?.CapturePlayerAccess());
            }
            var restoredRoots = new Dictionary<string, Entity>(StringComparer.Ordinal);
            foreach (EntitySnapshot saved in savedEntities)
            {
                Entity entity = restored[saved.id];
                foreach (DataSlot value in saved.values ?? Array.Empty<DataSlot>())
                {
                    VariableDefinition field = entity.GetVariableDefinition(value.name) ?? throw new CommandException("저장된 변수의 스키마가 없습니다: " + value.name);
                    if (value.value.kind != DataKind.Empty && !Schemas.Accepts(field.Kind, field.SchemaId, value.value,
                        id => restored.TryGetValue(id, out Entity target) ? target : null)) throw new CommandException("저장된 변수의 자료형이 다릅니다: " + value.name);
                    entity.SetValue(field.Code, value.value);
                }
                foreach (EntityChildSnapshot child in saved.children ?? Array.Empty<EntityChildSnapshot>())
                    entity.Children.Add(child.name, restored.TryGetValue(child.id, out Entity target) ? target : throw new CommandException("저장된 개체 참조를 찾을 수 없습니다: " + child.id));
                foreach (string root in saved.rootNames ?? Array.Empty<string>()) restoredRoots.Add(root, entity);
            }
            foreach (Entity entity in restored.Values)
            {
                entity.ValidateMembers();
                ValidateInstanceMembers(entity, id => restored.TryGetValue(id, out Entity referenced) ? referenced : null);
            }
            entities.Clear(); foreach (var pair in restored) entities.Add(pair.Key, pair.Value);
            roots.Clear(); foreach (var pair in restoredRoots) roots.Add(pair.Key, pair.Value);
            foreach (Entity entity in entities.Values) ConfigureInstance(entity);
        }

        private Entity ResolveCompletionEntity(string[] path, Dictionary<string, Datum> locals)
        {
            try { return ResolveEntity(path, locals); }
            catch (CommandException)
            {
                if (path.Length == 0 || locals == null || !locals.TryGetValue(path[0], out Datum value) || ReferenceTarget(value) != null) throw;
                EntitySchema schema = SchemaOf(value);
                if (schema == null) throw;
                var preview = new Entity("", schema.Label, schema);
                CheckEntityAccess(preview, locals);
                for (int i = 1; i < path.Length; i++)
                {
                    VariableDefinition field = preview.FindVariable(path[i]);
                    if (field == null || string.IsNullOrEmpty(field.SchemaId)) throw new CommandException("대상 자료형을 알 수 없습니다.");
                    CheckVariableAccess(preview, field, locals);
                    schema = Schemas.Require(field.SchemaId);
                    preview = new Entity("", schema.Label, schema);
                    CheckEntityAccess(preview, locals);
                }
                return preview;
            }
        }
    }
}
