using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Codetective.Operations
{
    [Serializable] public sealed class EvidenceRecord
    {
        public int number, day;
        public double minute;
        public bool hasAcquiredTime = true;
        public string source;
        public Datum value;
        public EvidenceRecord Copy() => new EvidenceRecord { number = number, day = day, minute = minute, hasAcquiredTime = hasAcquiredTime,
            source = source, value = value?.Copy() };
    }
    public sealed class EvidenceEntry
    {
        public string Id, Name, Description, Source;
        public int Number, Day;
        public double Minute;
        public bool HasAcquiredTime;
        public Datum Value;
        public string[] References;
    }

    public sealed partial class KernelRuntime
    {
        private readonly Dictionary<string, EvidenceRecord> evidence = new Dictionary<string, EvidenceRecord>(StringComparer.Ordinal);
        private int nextEvidenceNumber = 1;

        // The collection owns knowledge/items; data slots only hold command references to them.
        public IReadOnlyList<EvidenceEntry> Evidence => evidence.Values.OrderBy(e => e.number)
            .Where(e => EvidenceKind(e.value) && IsPlayerVisible(e.value)).Select(EvidenceView).ToArray();
        private bool EvidenceKind(Datum value)
        {
            if (value == null || !value.CanReferenceEntity || value.kind == DataKind.Any || value.kind == DataKind.Thread || string.IsNullOrEmpty(value.id)) return false;
            EntitySchema schema = ReferenceTarget(value)?.Schema ?? Schemas.Find(value.schemaId), thread = Schemas.Find("Thread");
            return schema == null || thread == null || !schema.IsA(thread);
        }
        public bool CollectEvidence(Datum value, string source = "명령 결과") => CollectEvidence(value, source, true, true);
        private bool CollectEvidence(Datum value, string source, bool acquiredTime, bool notify, bool observeFields = true)
        {
            if (!EvidenceKind(value)) return false;
            Datum reference = NormalizeReference(value);
            if (!EvidenceKind(reference) || evidence.ContainsKey(reference.id)) return false;
            if (Entity(reference.id) != null) Discover(reference.id, source, observeFields, false);
            evidence.Add(reference.id, new EvidenceRecord { number = nextEvidenceNumber++, day = Day, minute = Minute,
                hasAcquiredTime = acquiredTime, source = source ?? "", value = reference.Copy() });
            if (notify) { Revision++; NarrativeUpdated(); }
            return true;
        }
        private Datum ObserveEvidence(Datum value, Dictionary<string, Datum> locals, string agent, bool actionResult = false)
        {
            // A Thread's local findings reach Kernel through its final report, not while its protocol is still running.
            if (string.IsNullOrEmpty(agent)) CollectEvidence(value);
            else if (actionResult && EvidenceKind(value) && Entity(value.id) is Entity result)
            {
                Discover(result.Id, agent);
                // A received document's content is observed now. Returning an object reference
                // alone does not inspect all of that object's otherwise unknown state.
                if (IsRecord(value.kind))
                    foreach (VariableDefinition field in result.Variables.Where(f => VariableDenial(result, f, locals) == null))
                        Observe(new ObservationDefinition { subjectId = result.Id, field = field.Code, useCurrentField = true, source = agent });
            }
            return PlayerValue(value, locals);
        }
        private EvidenceEntry EvidenceView(EvidenceRecord record)
        {
            Entity target = ReferenceTarget(record.value); string description = record.value.text;
            if (target != null)
            {
                VariableDefinition field = target.FindVariable("description");
                bool allowed = field != null && VariableDenial(target, field, null) == null;
                Datum visible = allowed ? LastKnownValue(target.Id, field.Code) : Datum.Empty;
                description = !allowed || !IsPlayerVisible(visible) ? "" : visible.kind == DataKind.Empty || visible.kind == DataKind.Text && string.IsNullOrWhiteSpace(visible.text) && string.IsNullOrEmpty(visible.textKey) ? Text(record.value.textKey, record.value.text) : Format(visible);
            }
            string name = target != null ? KnownName(target.Id, Text(record.value.labelKey, record.value.label)) : Text(record.value.labelKey, record.value.label);
            ObservationRecord origin = ObservationEvidence(record.value.id);
            if (origin != null) { name = ObservationEvidenceName(origin); description = ObservationText(origin); }
            return new EvidenceEntry { Id = record.value.id, Number = record.number, Name = string.IsNullOrWhiteSpace(name) ? record.value.id : name,
                Description = string.IsNullOrWhiteSpace(description) ? "현재 열람 가능한 상세 설명이 없습니다." : description,
                Source = record.source, Day = record.day, Minute = record.minute, HasAcquiredTime = record.hasAcquiredTime,
                Value = target != null ? Reference(target) : record.value.Copy(),
                References = data.Values.Where(s => s.value.CanReferenceEntity && s.value.id == record.value.id && IsPlayerVisible(s.value)).OrderBy(s => s.id).Select(s => s.name).ToArray() };
        }
        public string SuggestEvidenceReference(string id)
        {
            EvidenceEntry entry = RequireEvidence(id);
            if (entry.References.Length > 0) return entry.References[0];
            string basis = "evidence" + entry.Number, name = basis; int suffix = 2;
            while (data.ContainsKey(name) || roots.ContainsKey(name) || protocols.ContainsKey(name)) name = basis + "_" + suffix++;
            return name;
        }
        public void ReferenceEvidence(string id, string name)
        {
            EvidenceEntry entry = RequireEvidence(id); name = Normalize(name ?? ""); CheckName(name);
            if (data.TryGetValue(name, out DataSlot slot) && slot.value.kind != DataKind.Empty && (!slot.value.CanReferenceEntity || slot.value.id != id))
                throw new CommandException("이미 다른 데이터가 연결된 이름입니다. 다른 참조 이름을 입력하세요.");
            Store(name, entry.Value); NarrativeUpdated();
        }
        private EvidenceEntry RequireEvidence(string id) => Evidence.FirstOrDefault(e => e.Id == id) ?? throw new CommandException("현재 열람할 수 없는 자료입니다.");

        private static string NewsEvidenceId(string article, string clue = null)
        {
            string Encode(string value) => string.Concat(Encoding.UTF8.GetBytes(value).Select(b => b.ToString("x2")));
            return "news_" + Encode(article) + (clue == null ? "" : "_" + Encode(clue));
        }
        private void CollectNewsArticle(NewsArticle article, bool legacy = false)
            => CollectEvidence(Datum.Ref(DataKind.Document, NewsEvidenceId(article.id), article.headline, article.body), "신문", !legacy, !legacy);
        private void CollectNewsClue(NewsArticle article, NewspaperClue clue, bool legacy = false)
            => CollectEvidence(Datum.Ref(DataKind.Clue, NewsEvidenceId(article.id, clue.id), clue.keyword, article.headline + "\n\n" + article.body), "신문 단서", !legacy, !legacy);
        private void RestoreEvidence(KernelSnapshot snapshot)
        {
            evidence.Clear(); nextEvidenceNumber = 1;
            var numbers = new HashSet<int>();
            foreach (EvidenceRecord item in snapshot.evidence ?? Array.Empty<EvidenceRecord>())
            {
                if (item == null || !EvidenceKind(item.value) || string.IsNullOrWhiteSpace(item.value.id) || item.number < 1 || !numbers.Add(item.number) ||
                    item.day < 1 || item.day > Day || double.IsNaN(item.minute) || double.IsInfinity(item.minute) || item.minute < 0 || item.minute > 1440 || evidence.ContainsKey(item.value.id))
                    throw new CommandException("저장된 확보 자료가 유효하지 않거나 중복됩니다.");
                EvidenceRecord restored = item.Copy(); restored.value = NormalizeReference(restored.value); evidence.Add(restored.value.id, restored);
                Entity legacy = Entity(restored.value.id);
                if (snapshot.entities == null && legacy != null && IsRecord(restored.value.kind) && !string.IsNullOrEmpty(restored.value.text) && string.IsNullOrEmpty(legacy.GetValue("description").text))
                    legacy.SetValue("description", Datum.Text(restored.value.text));
            }
            nextEvidenceNumber = Math.Max(snapshot.nextEvidenceNumber, evidence.Count == 0 ? 1 : evidence.Values.Max(e => e.number) + 1);
            if (snapshot.evidence != null) return;
            foreach (DataSlot slot in data.Values) CollectEvidence(slot.value, "이전 기록", false, false);
            foreach (ConsultationLine line in Consultation.Where(l => RecordedClues.Contains(l.fact)))
                CollectEvidence(Datum.Ref(line.kind, line.fact, line.keyword, line.sentence), "상담 단서", false, false);
            foreach (NewsArticle article in news.Values)
            {
                if (editions.Values.Any(e => e.read && e.articleIds.Contains(article.id))) CollectNewsArticle(article, true);
                foreach (NewspaperClue clue in article.clues ?? Array.Empty<NewspaperClue>())
                    if (RecordedClues.Contains("newspaper." + article.id + "." + clue.id)) CollectNewsClue(article, clue, true);
            }
            if (CoffeeReceived && Entity("coffee") != null) CollectEvidence(Reference(Entity("coffee")), "커피 전달", false, false);
        }
    }
}
