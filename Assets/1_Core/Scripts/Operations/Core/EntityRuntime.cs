using System;
using System.Collections.Generic;
using System.Linq;

namespace Codetective.Operations
{
    public sealed partial class KernelRuntime
    {
        public SchemaRegistry Schemas { get; }
        public IReadOnlyDictionary<string, Entity> Entities => entities;
        private EntitySnapshot[] scenarioEntityDefaults = Array.Empty<EntitySnapshot>();
        private DiscoveryRecord[] scenarioDiscoveryDefaults = Array.Empty<DiscoveryRecord>();
        public void CaptureScenarioEntityDefaults()
        {
            KernelSnapshot snapshot = Capture(); scenarioEntityDefaults = snapshot.entities;
            scenarioDiscoveryDefaults = snapshot.discoveries.Select(d => d.Copy()).ToArray();
        }
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
            value.labelKey = entity.LabelKey;
            value.schemaId = entity.Schema?.Id ?? "";
            return value;
        }

        public string TypeName(Datum value)
        {
            if (value.kind == DataKind.Array || value.kind == DataKind.Dictionary) return string.IsNullOrEmpty(value.schemaId) ? KindName(value.kind) : value.schemaId;
            EntitySchema schema = value.CanReferenceEntity ? ReferenceTarget(value)?.Schema ?? Schemas.Find(value.schemaId) : null;
            return schema != null ? schema.NameFor(Language) : KindName(value.kind);
        }

        private void ConfigureInstance(Entity entity)
        {
            entity.ValueValidator = (field, value) => Schemas.Accepts(field.Kind, field.SchemaId, value, Entity);
            entity.ValueNormalizer = TagReference;
            entity.ValueChanged = () => { Revision++; NarrativeUpdated(); };
            entity.FieldChanged = (field, before, after) => RecordWorldChange(entity, field, before, after);
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
                if (!string.IsNullOrEmpty(field.SchemaId)) Schemas.ValidateType(field.SchemaId);
                if (!CommandParser.IsIdentifier(field.Code) || CommandParser.IsAllocationKeyword(field.Code) ||
                    (field.DefaultValue.kind != DataKind.Empty && !Schemas.Accepts(field.Kind, field.SchemaId, field.DefaultValue, resolve)))
                    throw new CommandException("인스턴스 변수 선언/기본값이 유효하지 않습니다: " + field.Code);
            }
            foreach (Operation operation in entity.LocalFunctions)
            {
                if (!string.IsNullOrEmpty(operation.ReturnSchemaId)) Schemas.ValidateType(operation.ReturnSchemaId);
                foreach (Parameter parameter in operation.Parameters) if (!string.IsNullOrEmpty(parameter.SchemaId)) Schemas.ValidateType(parameter.SchemaId);
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
            if (value.kind == DataKind.Array || value.kind == DataKind.Dictionary)
            {
                foreach (Datum item in value.Elements) PlayerValue(item, locals);
                return value.Copy();
            }
            Entity target = ReferenceTarget(value);
            if (target != null) CheckEntityAccess(target, locals);
            return value.Copy();
        }
        private bool PlayerValueVisible(Datum value, Dictionary<string, Datum> locals) =>
            value.kind == DataKind.Array || value.kind == DataKind.Dictionary ? value.Elements.All(v => PlayerValueVisible(v, locals)) :
            ReferenceTarget(value) == null || EntityDenial(ReferenceTarget(value), locals) == null;
        public bool IsPlayerVisible(Datum value) => value != null && PlayerValueVisible(value, null);
        public bool IsPlayerVisible(Entity entity) => entity != null && EntityDenial(entity) == null;
        public bool CanLinkObject(string id) => IsPlayerVisible(Entity(id));
        private Datum TagReference(Datum value)
        {
            if (value.kind == DataKind.Array || value.kind == DataKind.Dictionary)
            { Datum result = CollectionValue.Create(value.kind, value.Elements.Select(TagReference)); result.schemaId = value.schemaId; return result; }
            Entity target = ReferenceTarget(value);
            if (target != null)
            {
                value.kind = target.Kind; value.schemaId = target.Schema.Id;
                if (string.IsNullOrEmpty(value.labelKey) && value.label == target.Label) value.labelKey = target.LabelKey;
            }
            return value;
        }
        private Datum Invoke(Entity target, Operation operation, Datum[] arguments, string agent)
        {
            if (terminalCommandTrace != null && string.IsNullOrEmpty(agent)) terminalCommandTrace.actions.Add(new TerminalActionProof { targetId = target.Id, operation = operation.Code, arguments = arguments.Length });
            bool fieldAction = operation.DispatchToThread;
            if (fieldAction && string.IsNullOrEmpty(agent) && !DryRun)
            {
                ThreadAgent ready = Threads.FirstOrDefault(t => t.State == AgentState.Ready) ?? throw new CommandException("활동 가능한 Thread가 없습니다.");
                return DispatchAction(ready.Id, target, operation.Code, arguments);
            }
            Datum result;
            bool previous = invokingAction; invokingAction = true;
            try { result = NormalizeReference(operation.Invoke(new Invocation { Runtime = this, Target = target, Arguments = arguments, AgentId = agent })); }
            finally { invokingAction = previous; }
            if (result.kind != DataKind.Empty && !string.IsNullOrEmpty(operation.ReturnSchemaId) && !Schemas.Accepts(operation.ReturnKind, operation.ReturnSchemaId, result, Entity))
                throw new CommandException("함수의 반환 자료형이 맞지 않습니다: " + operation.Code);
            return result;
        }
        private void CheckEntityAccess(Entity entity, Dictionary<string, Datum> locals = null)
        {
            if (string.IsNullOrEmpty(executingAgent) && !string.IsNullOrEmpty(entity.Id) && !IsDiscovered(entity.Id)) throw new CommandException("아직 발견하지 않은 대상입니다.");
            if (!entity.Exposed("entity", "", entity.AccessRules)) throw new CommandException("현재 제공되지 않는 대상입니다.");
            foreach (AccessRule rule in entity.AccessRules) CheckAccess(rule, locals, entity);
        }
        private string EntityDenial(Entity entity, Dictionary<string, Datum> locals = null) =>
            string.IsNullOrEmpty(executingAgent) && !IsDiscovered(entity.Id) ? "아직 발견하지 않은 대상입니다." :
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
            if (value.kind == DataKind.Array || value.kind == DataKind.Dictionary)
            {
                Datum normalized = CollectionValue.Create(value.kind, value.Elements.Select(NormalizeReference)); normalized.schemaId = value.schemaId; return normalized;
            }
            if (string.IsNullOrEmpty(value.id) || value.kind == DataKind.Task || value.kind == DataKind.Protocol) return value;
            Entity target = Entity(value.id);
            if (target == null && IsRecord(value.kind))
            {
                EntitySchema schema = Schemas.ForKind(value.kind);
                target = new Entity(value.id, value.label, schema);
                target.SetValue("description", Datum.Text(value.text));
                IndexEntity(target, new HashSet<Entity>());
                knowledgeOnly.Add(target.Id);
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
            Texts.CopyTo(copy.Texts); copy.DisplayLanguage = DisplayLanguage;
            foreach (TerminalConversationDefinition definition in terminalConversations.Values) copy.RegisterTerminalConversation(definition);
            foreach (StoryMail message in mail.Values) copy.mail.Add(message.id, message.Copy());
            foreach (var entry in worldSchedule) copy.worldSchedule.Add(entry.Key, entry.Value.Copy());
            foreach (NewsArticle article in news.Values) copy.RegisterNews(article);
            foreach (Entity original in entities.Values)
            {
                var clone = new Entity(original.Id, original.Label, original.Schema)
                { Aliases = original.Aliases.ToArray(), Names = original.Names.ToArray(), Access = original.Access, LabelKey = original.LabelKey };
                clone.Operations.AddRange(original.Operations);
                copy.entities.Add(clone.Id, clone);
            }
            foreach (var root in roots) copy.roots.Add(root.Key, copy.entities[root.Value.Id]);
            foreach (var plan in clinicPlans) copy.clinicPlans.Add(plan.Key, plan.Value.Copy());
            foreach (var pool in consultationPools) copy.RegisterConsultationPool(pool.Key, pool.Value);
            foreach (var definition in appointmentDefinitions) copy.appointmentDefinitions.Add(definition.Key, definition.Value.Copy());
            foreach (var offset in appointmentLines) copy.appointmentLines.Add(offset.Key, offset.Value);
            copy.legacyDialogueCount = legacyDialogueCount;
            copy.legacyDialogueCaptured = legacyDialogueCaptured;
            foreach (var request in reservationRequests) copy.reservationRequests.Add(request.Key, request.Value.Copy());
            copy.Consultation.AddRange(Consultation.Select(l => l.Copy()));
            copy.Restore(Capture());
            return copy;
        }

        private void ValidateProtocolTypes(Protocol protocol)
        {
            foreach (Parameter parameter in protocol.Parameters)
                if (!string.IsNullOrEmpty(parameter.SchemaId)) Schemas.ValidateType(parameter.SchemaId);
        }

        private EntitySchema SchemaOf(Datum value) => value.CanReferenceEntity ? ReferenceTarget(value)?.Schema ?? Schemas.Find(value.schemaId) ??
            Schemas.Find(value.kind.ToString()) : null;

        private void RestoreEntities(EntitySnapshot[] savedEntities, bool legacyKnowledge = false, bool legacyText = false)
        {
            if (savedEntities == null) return; // Version 1 saves made before schemas retain the scenario defaults.
            var savedIds = new HashSet<string>(savedEntities.Select(e => e.id), StringComparer.Ordinal);
            savedEntities = savedEntities.Concat(scenarioEntityDefaults.Where(e => !savedIds.Contains(e.id))).ToArray();
            var restored = new Dictionary<string, Entity>(StringComparer.Ordinal);
            foreach (EntitySnapshot saved in savedEntities)
            {
                EntitySchema schema = Schemas.Require(saved.schemaId);
                var entity = new Entity(saved.id, saved.label, schema)
                { Aliases = saved.aliases ?? Array.Empty<string>(), Names = saved.names ?? Array.Empty<LocalizedCommandName>(), LabelKey = saved.labelKey ?? "" };
                if (entities.TryGetValue(saved.id, out Entity original))
                {
                    entity.Access = original.Access;
                    if (legacyText && string.IsNullOrEmpty(entity.LabelKey) && entity.Label == original.Label) entity.LabelKey = original.LabelKey;
                    entity.Viewer = original.Viewer?.Copy();
                    entity.Operations.AddRange(original.Operations);
                }
                restored.Add(entity.Id, entity);
            }
            // Definitions must exist on every instance before restoring values and cross-instance references.
            foreach (EntitySnapshot saved in savedEntities)
            {
                Entity original = Entity(saved.id);
                VariableDefinition[] fields = (saved.localVariables ?? original?.CaptureLocalVariables() ?? Array.Empty<VariableDefinition>()).Select(f => f.Copy()).ToArray();
                OperationDefinition[] functions = (saved.localFunctions ?? original?.CaptureLocalFunctions() ?? Array.Empty<OperationDefinition>()).Select(f => f.Copy()).ToArray();
                if (legacyKnowledge)
                {
                    foreach (VariableDefinition field in fields)
                    {
                        VariableDefinition inherited = restored[saved.id].Schema.FindVariable(field.Code);
                        if (inherited == null) continue;
                        if (field.Code == "age" && field.Kind == DataKind.Text && inherited.Kind == DataKind.Number)
                        { field.Kind = DataKind.Number; field.DefaultValue = double.TryParse(field.DefaultValue.text, out double age) ? Datum.Number(age) : Datum.Empty; }
                        if (field.Kind == inherited.Kind && field.SchemaId == inherited.SchemaId) { field.Overrides = true; field.Writable &= inherited.Writable; }
                    }
                    foreach (OperationDefinition function in functions)
                        if (restored[saved.id].Schema.EffectiveOperations.Any(o => o.Key == function.Compile().Key)) function.Overrides = true;
                }
                restored[saved.id].RestoreMembers(fields,
                    functions, saved.disabledVariables ?? original?.CaptureDisabledVariables(),
                    saved.disabledFunctions ?? original?.CaptureDisabledFunctions());
                restored[saved.id].RestorePlayerAccess(saved.playerAccess ?? original?.CapturePlayerAccess());
            }
            var restoredRoots = new Dictionary<string, Entity>(StringComparer.Ordinal);
            foreach (EntitySnapshot saved in savedEntities)
            {
                Entity entity = restored[saved.id];
                if (saved.hasViewerOverride && saved.viewer != null) entity.Viewer = saved.viewer.Copy();
                foreach (DataSlot value in saved.values ?? Array.Empty<DataSlot>())
                {
                    VariableDefinition field = entity.GetVariableDefinition(value.name) ?? throw new CommandException("저장된 변수의 스키마가 없습니다: " + value.name);
                    Datum datum = value.value;
                    if (legacyText && datum.kind == DataKind.Text && string.IsNullOrEmpty(datum.textKey) && Entity(saved.id) is Entity authored && authored.GetVariableDefinition(field.Code) != null)
                    {
                        Datum template = authored.GetValue(field.Code);
                        if (template.kind == DataKind.Text && template.text == datum.text) { datum = datum.Copy(); datum.textKey = template.textKey; }
                    }
                    if (legacyKnowledge && field.Code == "age" && field.Kind == DataKind.Number && datum.kind == DataKind.Text)
                        datum = double.TryParse(datum.text, out double age) ? Datum.Number(age) : Datum.Empty;
                    if (field.Code == "connection" && field.SchemaId == "Place[]" && datum.kind == DataKind.Place) datum = Datum.List(datum);
                    if (datum.kind != DataKind.Empty && !Schemas.Accepts(field.Kind, field.SchemaId, datum,
                        id => restored.TryGetValue(id, out Entity target) ? target : null)) throw new CommandException("저장된 변수의 자료형이 다릅니다: " + value.name);
                    entity.SetValue(field.Code, datum);
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
