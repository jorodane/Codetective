using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Codetective.Operations
{
    public readonly struct ObjectReferenceSpan
    {
        public int Start { get; }
        public int Length { get; }
        public int End => Start + Length;
        public string Id { get; }

        public ObjectReferenceSpan(int start, int length, string id)
        { Start = start; Length = length; Id = id; }

        public ObjectReferenceSpan Shift(int offset) => new ObjectReferenceSpan(Start + offset, Length, Id);
    }

    /// <summary>Write [[objectId|visible label]] in story text to attach a stable object ID to a phrase.</summary>
    public sealed class ObjectReferenceText
    {
        private static readonly Regex Links = new Regex(@"\[\[([^|\[\]\r\n]+)\|([^\[\]\r\n]+)\]\]");
        public string Text { get; }
        public IReadOnlyList<ObjectReferenceSpan> Spans { get; }

        public ObjectReferenceText(string text, IReadOnlyList<ObjectReferenceSpan> spans = null)
        { Text = text ?? ""; Spans = spans ?? Array.Empty<ObjectReferenceSpan>(); }

        public static ObjectReferenceText Parse(string source)
        {
            if (string.IsNullOrEmpty(source) || !source.Contains("[[")) return new ObjectReferenceText(source);
            var text = new StringBuilder();
            var spans = new List<ObjectReferenceSpan>();
            int start = 0;
            foreach (Match link in Links.Matches(source))
            {
                text.Append(source, start, link.Index - start);
                string label = link.Groups[2].Value;
                spans.Add(new ObjectReferenceSpan(text.Length, label.Length, link.Groups[1].Value));
                text.Append(label);
                start = link.Index + link.Length;
            }
            text.Append(source, start, source.Length - start);
            return new ObjectReferenceText(text.ToString(), spans);
        }

        public static string PlainText(string source) => string.IsNullOrEmpty(source) || !source.Contains("[[")
            ? source ?? "" : Links.Replace(source, "$2");

        public ObjectReferenceText Slice(int start, int length) => new ObjectReferenceText(Text.Substring(start, length),
            Spans.Where(span => span.Start >= start && span.End <= start + length).Select(span => span.Shift(-start)).ToArray());

        public static ObjectReferenceText Join(string separator, IEnumerable<ObjectReferenceText> entries)
        {
            var text = new StringBuilder();
            var spans = new List<ObjectReferenceSpan>();
            foreach (ObjectReferenceText entry in entries)
            {
                if (text.Length > 0) text.Append(separator);
                foreach (ObjectReferenceSpan span in entry.Spans) spans.Add(span.Shift(text.Length));
                text.Append(entry.Text);
            }
            return new ObjectReferenceText(text.ToString(), spans);
        }
    }
}
