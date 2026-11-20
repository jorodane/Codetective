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
        public string titleKey = "";
        public string[] fields = Array.Empty<string>();
        public bool hideUnknown = true;
        public DataViewerDefinition Copy() => new DataViewerDefinition { kind = kind, title = title, titleKey = titleKey, fields = (fields ?? Array.Empty<string>()).ToArray(), hideUnknown = hideUnknown };
    }
    public sealed class DataViewerField
    {
        public string Code, Label;
        public string DisplayValue;
        public Datum Value;
        public bool Known;
    }
    public sealed class DataViewerModel
    {
        public string Id, Name, Title, PhotoId;
        public DataViewerKind Kind;
        public DataViewerField[] Fields = Array.Empty<DataViewerField>();
        public ObservationRecord[] Information = Array.Empty<ObservationRecord>();
        public string[] InformationText = Array.Empty<string>();
        public string[] RelatedIds = Array.Empty<string>();
        public string Text()
        {
            var text = new StringBuilder(); text.AppendLine(Name); text.AppendLine(Title);
            foreach (DataViewerField field in Fields) text.AppendLine("\n" + field.Label + "\n" + (field.Known ? field.DisplayValue : "[미확인]"));
            if (Information.Length > 0) text.AppendLine("\n획득 정보 / 이력");
            foreach (string record in InformationText) text.AppendLine("\n" + record);
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
            ObservationRecord origin = ObservationEvidence(id);
            var fields = new List<DataViewerField>();
            foreach (VariableDefinition field in target.Variables.Where(f => requested.Length == 0 ? f.Code != "id" && f.Code != "name" : requested.Contains(f.Code)))
            {
                if (origin != null && field.Code == "description") continue;
                bool access = VariableDenial(target, field, null) == null;
                Datum value = access ? LastKnownValue(id, field.Code) : Datum.Empty;
                if (definition.hideUnknown && value.kind == DataKind.Empty) continue;
                fields.Add(new DataViewerField { Code = field.Code, Label = Text(field.LabelKey, field.NameFor(Language)), Value = value, DisplayValue = Format(value), Known = value.kind != DataKind.Empty });
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
            string name = origin != null ? ObservationEvidenceName(origin) : KnownName(id, "미확인 대상");
            ObservationRecord[] information = Observations.Where(o => o.subjectId == id && o.field != "id" && o.field != "name" &&
                (o.kind != InformationKind.Observation || !string.IsNullOrEmpty(o.statement) || !string.IsNullOrEmpty(o.statementKey) || observations.Count(h => h.subjectId == id && h.field == o.field) > 1))
                .Concat(origin != null ? new[] { origin } : Array.Empty<ObservationRecord>()).OrderBy(ObservationOrder).ThenBy(o => o.number).ToArray();
            return new DataViewerModel { Id = id, Name = name, Title = Text(definition.titleKey, string.IsNullOrEmpty(definition.title) ? Text(target.Schema.LabelKey, target.Schema.NameFor(Language)) : definition.title),
                Kind = definition.kind, PhotoId = photo, Fields = fields.ToArray(), Information = information, InformationText = information.Select(ObservationText).ToArray(),
                RelatedIds = related.Where(r => r != id).ToArray() };
        }
        private string PhotoFor(string id)
        {
            Datum image = LastKnownValue(id, "image");
            return image.kind == DataKind.Text ? image.text ?? "" : "";
        }
    }
}
