using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Codetective.Operations
{
    public enum DataViewerKind { Generic, Concept, Person, Place, Item, Tool, Event, Statement, Photo, Keyword, Organization, Time, Relationship }
    [Serializable] public sealed class DataViewerDefinition
    {
        public DataViewerKind kind;
        public string title = "";
        public string[] fields = Array.Empty<string>();
        public bool hideUnknown = true;
        public DataViewerDefinition Copy() => new DataViewerDefinition { kind = kind, title = title, fields = (fields ?? Array.Empty<string>()).ToArray(), hideUnknown = hideUnknown };
    }
    public sealed class DataViewerField
    {
        public string Code, Label;
        public Datum Value;
        public bool Known;
    }
    public sealed class DataViewerModel
    {
        public string Id, Name, Title, PhotoId;
        public DataViewerKind Kind;
        public DataViewerField[] Fields = Array.Empty<DataViewerField>();
        public ObservationRecord[] Information = Array.Empty<ObservationRecord>();
        public string[] RelatedIds = Array.Empty<string>();
        public string Text()
        {
            var text = new StringBuilder(); text.AppendLine(Name); text.AppendLine(Title);
            foreach (DataViewerField field in Fields) text.AppendLine("\n" + field.Label + "\n" + (field.Known ? field.Value.ToString() : "[미확인]"));
            if (Information.Length > 0) text.AppendLine("\n획득 정보 / 이력");
            foreach (ObservationRecord record in Information)
            {
                text.AppendLine("\n" + (record.observedAt?.ToString() ?? "DAY " + record.observedDay + " " + KernelRuntime.TimeOfDay(record.observedMinute)) + " · " + record.source);
                if (record.aboutTime != null) text.AppendLine("대상 시각: " + record.aboutTime);
                if (record.receivedAt != null && record.receivedAt.ToString() != record.observedAt?.ToString()) text.AppendLine("수신: " + record.receivedAt);
                text.AppendLine(string.IsNullOrEmpty(record.statement) ? record.field + ": " + record.value : record.statement);
                if (record.kind == InformationKind.Inference) text.AppendLine("Kernel 추론");
                if (record.kind == InformationKind.Synthesis) text.AppendLine("조합 정보");
            }
            return text.ToString().TrimEnd();
        }
    }
    public sealed partial class Entity
    {
        public DataViewerDefinition Viewer;
    }
    public sealed partial class KernelRuntime
    {
        public DataViewerModel ViewData(string id)
        {
            Entity target = Entity(id) ?? throw new CommandException("열람할 대상이 없습니다: " + id);
            CheckEntityAccess(target);
            DataViewerDefinition definition = target.Viewer ?? target.Schema.Lineage().Reverse().Select(s => s.Viewer).FirstOrDefault(v => v != null) ?? new DataViewerDefinition();
            string[] requested = definition.fields ?? Array.Empty<string>();
            var fields = new List<DataViewerField>();
            foreach (VariableDefinition field in target.Variables.Where(f => requested.Length == 0 ? f.Code != "id" && f.Code != "name" : requested.Contains(f.Code)))
            {
                bool access = VariableDenial(target, field, null) == null;
                Datum value = access ? LastKnownValue(id, field.Code) : Datum.Empty;
                if (definition.hideUnknown && value.kind == DataKind.Empty) continue;
                fields.Add(new DataViewerField { Code = field.Code, Label = field.NameFor(Language), Value = value, Known = value.kind != DataKind.Empty });
            }
            if (requested.Length > 0) fields = fields.OrderBy(f => Array.IndexOf(requested, f.Code)).ToList();
            string photo = definition.kind == DataViewerKind.Photo ? PhotoFor(id) : "";
            Datum portrait = target.FindVariable("portrait") != null ? LastKnownValue(id, "portrait") : Datum.Empty;
            if (portrait.kind == DataKind.Empty && target.FindVariable("photo") != null) portrait = LastKnownValue(id, "photo");
            if (portrait.kind == DataKind.Photo) photo = PhotoFor(portrait.id);
            var related = new HashSet<string>(StringComparer.Ordinal);
            void References(Datum value)
            {
                if (value.kind == DataKind.Array || value.kind == DataKind.Dictionary) { foreach (Datum item in value.Elements) References(item); }
                else if (value.CanReferenceEntity && Entity(value.id) != null && IsPlayerVisible(value)) related.Add(value.id);
            }
            foreach (DataViewerField field in fields) if (field.Known) References(field.Value);
            string name = LastKnownValue(id, "name").text;
            return new DataViewerModel { Id = id, Name = string.IsNullOrEmpty(name) ? "미확인 대상" : name, Title = string.IsNullOrEmpty(definition.title) ? target.Schema.NameFor(Language) : definition.title,
                Kind = definition.kind, PhotoId = photo, Fields = fields.ToArray(), Information = Observations.Where(o => o.subjectId == id && o.field != "id" && o.field != "name" &&
                    (o.kind != InformationKind.Observation || !string.IsNullOrEmpty(o.statement) || observations.Count(h => h.subjectId == id && h.field == o.field) > 1)).OrderBy(ObservationOrder).ThenBy(o => o.number).ToArray(),
                RelatedIds = related.Where(r => r != id).ToArray() };
        }
        private string PhotoFor(string id)
        {
            Datum image = LastKnownValue(id, "image");
            return image.kind == DataKind.Text ? image.text ?? "" : "";
        }
    }
}
