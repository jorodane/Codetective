using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Codetective.Operations
{
    [Serializable] public sealed class TextTranslation
    {
        public string key = "", language = "ko", text = "";
        public TextTranslation Copy() => (TextTranslation)MemberwiseClone();
    }
    /// <summary>Content presentation only. Keys never enter the command identifier registry.</summary>
    public sealed class ContentTextCatalog
    {
        private readonly Dictionary<string, Dictionary<string, string>> entries = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        public static string Locale(string value)
        {
            string locale = (value ?? "").Trim().Replace('_', '-').ToLowerInvariant();
            if (!Regex.IsMatch(locale, @"^[a-z]{2,8}(?:-[a-z0-9]{1,8})*$")) throw new CommandException("표시 언어 코드를 확인하세요: " + value);
            return locale;
        }
        public void Install(IEnumerable<TextTranslation> values)
        {
            var pending = entries.ToDictionary(p => p.Key, p => new Dictionary<string, string>(p.Value, StringComparer.Ordinal), StringComparer.Ordinal);
            foreach (TextTranslation row in values ?? Array.Empty<TextTranslation>())
            {
                if (row == null || string.IsNullOrWhiteSpace(row.key) || row.text == null) throw new CommandException("번역 키 또는 문구가 비어 있습니다.");
                string locale = Locale(row.language);
                if (!pending.TryGetValue(row.key, out var translations)) pending.Add(row.key, translations = new Dictionary<string, string>(StringComparer.Ordinal));
                if (translations.ContainsKey(locale)) throw new CommandException("번역 키/언어가 중복됩니다: " + row.key + " / " + locale);
                translations.Add(locale, row.text);
            }
            entries.Clear(); foreach (var entry in pending) entries.Add(entry.Key, entry.Value);
        }
        public string Resolve(string key, string fallback, string language, string fallbackLanguage = "ko")
        {
            if (!string.IsNullOrEmpty(key) && entries.TryGetValue(key, out var translations))
                foreach (string locale in Parents(language).Concat(Parents(fallbackLanguage)).Distinct(StringComparer.Ordinal))
                    if (translations.TryGetValue(locale, out string text)) return text;
            return string.IsNullOrEmpty(fallback) ? key ?? "" : fallback;
        }
        private static IEnumerable<string> Parents(string value)
        {
            string locale = Locale(value);
            while (true) { yield return locale; int separator = locale.LastIndexOf('-'); if (separator < 0) break; locale = locale.Substring(0, separator); }
        }
        internal void CopyTo(ContentTextCatalog target) => target.Install(entries.SelectMany(p => p.Value.Select(t => new TextTranslation { key = p.Key, language = t.Key, text = t.Value })));
    }
    public sealed partial class KernelRuntime
    {
        public ContentTextCatalog Texts { get; } = new ContentTextCatalog();
        private string displayLanguage = "ko";
        public string DisplayLanguage
        {
            get => displayLanguage;
            set { string locale = ContentTextCatalog.Locale(value); if (displayLanguage != locale) { displayLanguage = locale; Revision++; } }
        }
        public string Text(string key, string fallback = "") => Texts.Resolve(key, fallback, DisplayLanguage);
        public string DisplayName(Entity entity) => entity == null ? "" : Text(entity.LabelKey, entity.Label);
        public string KnownName(string id, string fallback = "")
        {
            Datum known = LastKnownValue(id, "name");
            return known.kind == DataKind.Empty ? fallback : Format(known);
        }
        /// <summary>Renders acquired values without reading new world state or altering their raw contents.</summary>
        public string Format(Datum value)
        {
            if (value == null) return "";
            if (value.kind == DataKind.Array) return "[" + string.Join(", ", value.Elements.Select(Format)) + "]";
            if (value.kind == DataKind.Dictionary) return "{" + string.Join(", ", value.Elements.Select(Format)) + "}";
            if (value.kind == DataKind.Text) return Text(value.textKey, string.IsNullOrEmpty(value.text) ? value.label : value.text);
            if (value.CanReferenceEntity)
            {
                string label = Text(value.labelKey, value.label), content = Text(value.textKey, value.text);
                return string.IsNullOrEmpty(content) || content == label ? label : label + " — " + content;
            }
            return value.ToString();
        }
        public string ObservationText(ObservationRecord record)
        {
            var text = new StringBuilder();
            text.Append(record.observedAt?.ToString() ?? "DAY " + record.observedDay + " " + TimeOfDay(record.observedMinute)).Append(" · ").Append(Text(record.sourceKey, record.source));
            if (record.aboutTime != null) text.Append("\n대상 시각: ").Append(record.aboutTime);
            if (record.receivedAt != null && record.receivedAt.ToString() != record.observedAt?.ToString()) text.Append("\n수신: ").Append(record.receivedAt);
            text.Append('\n').Append(string.IsNullOrEmpty(record.statement) && string.IsNullOrEmpty(record.statementKey) ? record.field + ": " + Format(record.value) : Text(record.statementKey, record.statement));
            if (record.kind == InformationKind.Inference) text.Append("\nKernel 추론");
            if (record.kind == InformationKind.Synthesis) text.Append("\n조합 정보");
            return text.ToString();
        }
        private ObservationRecord ObservationEvidence(string id)
        {
            if (id == null || !knowledgeOnly.Contains(id) || !id.StartsWith("observation_", StringComparison.Ordinal) || !int.TryParse(id.Substring(12), out int number)) return null;
            return observations.FirstOrDefault(o => o.number == number && ObservationVisible(o));
        }
        private string ObservationEvidenceName(ObservationRecord record) => Text(record.sourceKey, record.source) + " / 관측 " + record.number;
    }
}
