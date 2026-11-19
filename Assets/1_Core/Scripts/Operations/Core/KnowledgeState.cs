using System;
using System.Collections.Generic;
using System.Linq;

namespace Codetective.Operations
{
    [Serializable] public sealed class DiscoveryRecord
    {
        public string entityId, source; public int day; public double minute;
        public DiscoveryRecord Copy() => (DiscoveryRecord)MemberwiseClone();
    }
    [Serializable] public sealed class ObservationRecord
    {
        public int number;
        public string subjectId = "", keywordId = "", field = "", sourceId = "", source = "", statement = "";
        public int observedDay, receivedDay;
        public double observedMinute, receivedMinute;
        public Datum value = Datum.Empty;
        public ObservationRecord Copy() => new ObservationRecord { number = number, subjectId = subjectId, keywordId = keywordId,
            field = field, sourceId = sourceId, source = source, statement = statement, observedDay = observedDay,
            receivedDay = receivedDay, observedMinute = observedMinute, receivedMinute = receivedMinute, value = value.Copy() };
    }
    [Serializable] public sealed class WorldEventRecord
    {
        public int number, day; public double minute;
        public string subjectId, field, description;
        public Datum before = Datum.Empty, after = Datum.Empty;
        public WorldEventRecord Copy() => new WorldEventRecord { number = number, day = day, minute = minute, subjectId = subjectId,
            field = field, description = description, before = before.Copy(), after = after.Copy() };
    }
    // K/S/F is authoring metadata; no player-facing API exposes this classification.
    public enum InformationRole { Unclassified, Key, Supporting, Flavor }
    [Serializable] public sealed class ObservationDefinition
    {
        public string subjectId = "", keywordId = "", field = "", sourceId = "", source = "", statement = "";
        public Datum value = Datum.Empty;
        public bool useCurrentField;
        public InformationRole role;
        public ObservationDefinition Copy() => new ObservationDefinition { subjectId = subjectId, keywordId = keywordId, field = field,
            sourceId = sourceId, source = source, statement = statement, value = value.Copy(), useCurrentField = useCurrentField, role = role };
    }

    public sealed partial class KernelRuntime
    {
        private readonly Dictionary<string, DiscoveryRecord> discoveries = new Dictionary<string, DiscoveryRecord>(StringComparer.Ordinal);
        private readonly List<ObservationRecord> observations = new List<ObservationRecord>();
        private readonly List<WorldEventRecord> worldEvents = new List<WorldEventRecord>();
        private int nextObservation = 1, nextWorldEvent = 1;
        private string executingAgent;
        private bool invokingAction;
        internal bool KnowsFlag(string flag) => Facts.Contains(flag) || !string.IsNullOrEmpty(executingAgent) && (Agent(executingAgent)?.KnowledgeFlags.Contains(flag) ?? false);
        internal bool HasAuthority(string authority) => Authorities.Contains(authority) || !string.IsNullOrEmpty(executingAgent) && (Agent(executingAgent)?.GrantedAuthorities.Contains(authority) ?? false);
        internal void GrantKnowledge(string[] flags, string[] authorities)
        {
            ThreadAgent agent = string.IsNullOrEmpty(executingAgent) ? null : Agent(executingAgent);
            if (agent == null) { Facts.UnionWith(flags); Authorities.UnionWith(authorities); }
            else { agent.KnowledgeFlags.UnionWith(flags); agent.GrantedAuthorities.UnionWith(authorities); }
        }
        public IReadOnlyList<DiscoveryRecord> Discoveries => discoveries.Values.Where(d => Entity(d.entityId) == null || IsPlayerVisible(Entity(d.entityId))).Select(d => d.Copy()).ToArray();
        public IReadOnlyList<ObservationRecord> Observations => observations.Where(o => Entity(o.subjectId) == null || IsPlayerVisible(Entity(o.subjectId))).Select(o => o.Copy()).ToArray();
        /// <summary>Designer/world API. The terminal never exposes undiscovered truth or this event history.</summary>
        public IReadOnlyList<WorldEventRecord> WorldHistory => worldEvents.Select(e => e.Copy()).ToArray();
        public bool IsDiscovered(string id) => id != null && discoveries.ContainsKey(id);
        public void RefreshInitialKnowledge()
        {
            foreach (DiscoveryRecord record in discoveries.Values.Where(d => d.source == "초기 작전망" || d.source == "Kernel 초기 프로필").ToArray())
                Discover(record.entityId, record.source, true, false);
        }

        public void Discover(string id, string source = "현장 보고", bool observeFields = false, bool listEvidence = true)
        {
            Entity entity = Entity(id) ?? throw new CommandException("발견 대상이 세계에 없습니다: " + id);
            if (!string.IsNullOrEmpty(executingAgent))
            {
                ThreadAgent agent = Agent(executingAgent);
                if (agent != null && !agent.Discoveries.Contains(id)) agent.Discoveries.Add(id);
                return;
            }
            bool fresh = !discoveries.ContainsKey(id);
            if (fresh) discoveries[id] = new DiscoveryRecord { entityId = id, source = source, day = Day, minute = Minute };
            if (fresh && listEvidence && source != "초기 작전망" && source != "이전 작전 기록" && source != "Kernel 초기 프로필" && !evidence.ContainsKey(id))
                evidence[id] = new EvidenceRecord { number = nextEvidenceNumber++, day = Day, minute = Minute, source = source, value = Reference(entity) };
            if (fresh)
            {
                Observe(new ObservationDefinition { subjectId = id, field = "id", value = Datum.Text(id), source = source });
                Observe(new ObservationDefinition { subjectId = id, field = "name", value = Datum.Text(entity.Label), source = source });
            }
            if (observeFields)
            {
                foreach (VariableDefinition field in entity.Variables.Where(f => f.Code != "id" && f.Code != "name" && VariableDenial(entity, f, null) == null))
                    Observe(new ObservationDefinition { subjectId = id, field = field.Code, value = entity.GetValue(field.Code), source = source });
                ObserveChildren(entity, source);
            }
            if (fresh) { Revision++; NarrativeUpdated(); }
        }
        public ObservationRecord Observe(ObservationDefinition definition, int observedDay = 0, double observedMinute = -1)
        {
            if (definition == null) throw new CommandException("관측 정보가 비어 있습니다.");
            string subject = definition.subjectId ?? "";
            Entity target = Entity(subject);
            if (subject.Length > 0 && target == null) throw new CommandException("관측 대상이 세계에 없습니다: " + subject);
            if (definition.useCurrentField && (target == null || target.GetVariableDefinition(definition.field) == null))
                throw new CommandException("관측할 세계 변수가 없습니다: " + definition.field);
            int day = observedDay == 0 ? Day : observedDay;
            double minute = observedMinute < 0 ? Minute : observedMinute;
            if (day < 1 || day > Day || minute < 0 || minute > 1440 || double.IsNaN(minute) || double.IsInfinity(minute) || day == Day && minute > Minute)
                throw new CommandException("관측 시각이 유효하지 않습니다.");
            var record = new ObservationRecord { number = nextObservation++, subjectId = subject, keywordId = definition.keywordId ?? "",
                field = definition.field ?? "", sourceId = definition.sourceId ?? "", source = definition.source ?? "", statement = definition.statement ?? "",
                value = definition.useCurrentField ? target.GetValue(definition.field) : definition.value.Copy(),
                observedDay = day, observedMinute = minute, receivedDay = Day, receivedMinute = Minute };
            ThreadAgent agent = string.IsNullOrEmpty(executingAgent) ? null : Agent(executingAgent);
            if (agent != null) agent.Observations.Add(record);
            else PublishObservation(record);
            return record.Copy();
        }
        private void PublishObservation(ObservationRecord record)
        {
            record = record.Copy(); record.receivedDay = Day; record.receivedMinute = Minute;
            if (!string.IsNullOrEmpty(record.subjectId) && !IsDiscovered(record.subjectId)) Discover(record.subjectId, record.source);
            DiscoverReferences(record.value, record.source);
            observations.Add(record); Revision++; NarrativeUpdated();
            if (!string.IsNullOrWhiteSpace(record.statement))
                CollectEvidence(Datum.Ref(DataKind.Statement, "observation_" + record.number, record.source + " / 관측 " + record.number,
                    record.statement + "\n관측 DAY " + record.observedDay + " " + TimeOfDay(record.observedMinute) + " / 수신 DAY " + record.receivedDay + " " + TimeOfDay(record.receivedMinute)), record.source);
        }
        private void DiscoverReferences(Datum value, string source)
        {
            if (value.kind == DataKind.Array || value.kind == DataKind.Dictionary)
            { foreach (Datum item in value.Elements) DiscoverReferences(item, source); return; }
            if (value.CanReferenceEntity && Entity(value.id) != null && !IsDiscovered(value.id)) Discover(value.id, source);
        }
        private IEnumerable<KeyValuePair<string, Entity>> KnownChildren(Entity parent)
        {
            foreach (var group in observations.Where(o => o.subjectId == parent.Id && o.field.StartsWith("$child:", StringComparison.Ordinal)).GroupBy(o => o.field))
            {
                ObservationRecord record = group.OrderByDescending(o => o.observedDay).ThenByDescending(o => o.observedMinute).ThenByDescending(o => o.number).First();
                Entity child = record.value.CanReferenceEntity ? Entity(record.value.id) : null;
                if (child != null) yield return new KeyValuePair<string, Entity>(group.Key.Substring(7), child);
            }
        }
        internal void ObserveChildren(Entity parent, string source)
        {
            string[] names = KnownChildren(parent).Select(p => p.Key).Concat(parent.Children.Keys).Distinct().ToArray();
            foreach (string name in names)
                Observe(new ObservationDefinition { subjectId = parent.Id, field = "$child:" + name, source = source,
                    value = parent.Children.TryGetValue(name, out Entity child) ? Reference(child) : Datum.Empty });
        }
        public Datum LastKnownValue(string subjectId, string field)
        {
            Entity entity = Entity(subjectId);
            if (entity == null || !IsPlayerVisible(entity)) return Datum.Empty;
            VariableDefinition definition = entity.GetVariableDefinition(field);
            if (definition == null || VariableDenial(entity, definition, null) != null) return Datum.Empty;
            ObservationRecord last = observations.Where(o => o.subjectId == subjectId && o.field == definition.Code)
                .OrderByDescending(o => o.observedDay).ThenByDescending(o => o.observedMinute).ThenByDescending(o => o.number).FirstOrDefault();
            return last != null && IsPlayerVisible(last.value) ? last.value.Copy() : Datum.Empty;
        }
        private Datum ReadPlayerField(Entity entity, VariableDefinition field)
        {
            if (!string.IsNullOrEmpty(executingAgent)) return entity.GetValue(field.Code);
            if (!observations.Any(o => o.subjectId == entity.Id && o.field == field.Code)) throw new CommandException("아직 보고받지 않은 정보입니다: " + field.Code);
            ObservationRecord last = observations.Where(o => o.subjectId == entity.Id && o.field == field.Code)
                .OrderByDescending(o => o.observedDay).ThenByDescending(o => o.observedMinute).ThenByDescending(o => o.number).First();
            if (!IsPlayerVisible(last.value)) throw new CommandException("현재 제공되지 않는 참조 정보입니다.");
            return LastKnownValue(entity.Id, field.Code);
        }
        private void RecordWorldChange(Entity entity, string field, Datum before, Datum after)
        {
            worldEvents.Add(new WorldEventRecord { number = nextWorldEvent++, day = Day, minute = Minute, subjectId = entity.Id,
                field = field, before = before.Copy(), after = after.Copy(), description = "세계 상태 변경" });
            if (invokingAction) Observe(new ObservationDefinition { subjectId = entity.Id, field = field, value = after.Copy(), source = executingAgent ?? "Kernel 명령" });
        }
        private void DeliverAgentKnowledge(ThreadAgent agent)
        {
            string previous = executingAgent; executingAgent = null;
            try
            {
                foreach (string id in agent.Discoveries) Discover(id, agent.Id);
                foreach (ObservationRecord report in agent.Observations) PublishObservation(report);
                if (agent.KnowledgeFlags.Count > 0 || agent.GrantedAuthorities.Count > 0)
                { Facts.UnionWith(agent.KnowledgeFlags); Authorities.UnionWith(agent.GrantedAuthorities); Revision++; NarrativeUpdated(); }
                agent.Discoveries.Clear(); agent.Observations.Clear();
                agent.KnowledgeFlags.Clear(); agent.GrantedAuthorities.Clear(); agent.CipherTable.Clear();
            }
            finally { executingAgent = previous; }
        }
        private void RestoreKnowledge(KernelSnapshot snapshot)
        {
            discoveries.Clear(); observations.Clear(); worldEvents.Clear(); nextObservation = nextWorldEvent = 1;
            if (snapshot.discoveries == null)
            {
                // Saves authored before separate knowledge retain their previous public information.
                foreach (Entity entity in entities.Values) Discover(entity.Id, "이전 작전 기록", true);
                return;
            }
            foreach (DiscoveryRecord item in snapshot.discoveries)
            {
                if (Entity(item.entityId) == null || discoveries.ContainsKey(item.entityId)) throw new CommandException("저장된 발견 대상이 유효하지 않습니다.");
                discoveries.Add(item.entityId, item.Copy());
            }
            foreach (ObservationRecord item in snapshot.observations ?? Array.Empty<ObservationRecord>())
            {
                if (item.number < 1 || observations.Any(o => o.number == item.number) || item.observedDay < 1 || item.receivedDay > Day ||
                    item.observedDay > item.receivedDay || item.value == null || !string.IsNullOrEmpty(item.subjectId) && Entity(item.subjectId) == null)
                    throw new CommandException("저장된 관측 정보가 유효하지 않습니다.");
                observations.Add(item.Copy()); nextObservation = Math.Max(nextObservation, item.number + 1);
            }
            foreach (WorldEventRecord item in snapshot.worldEvents ?? Array.Empty<WorldEventRecord>())
            { worldEvents.Add(item.Copy()); nextWorldEvent = Math.Max(nextWorldEvent, item.number + 1); }
            foreach (ThreadAgent agent in Threads)
                foreach (ObservationRecord item in agent.Observations) nextObservation = Math.Max(nextObservation, item.number + 1);
            var previouslyExisting = new HashSet<string>((snapshot.entities ?? Array.Empty<EntitySnapshot>()).Select(e => e.id), StringComparer.Ordinal);
            foreach (DiscoveryRecord initial in scenarioDiscoveryDefaults.Where(d => !previouslyExisting.Contains(d.entityId) && !IsDiscovered(d.entityId)))
                Discover(initial.entityId, "초기 작전망", true);
        }
    }
}
