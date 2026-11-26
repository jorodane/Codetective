using System;
using System.Collections.Generic;

namespace Codetective.Operations
{
    /// <summary>Terminal text with the ranges of command responses, independent of their visual layout.</summary>
    public sealed class TerminalTranscript
    {
        public ObjectReferenceText Content { get; }
        public string Text => Content.Text;
        public IReadOnlyList<(int Start, int Length)> Responses { get; }

        public TerminalTranscript(string text) : this(new ObjectReferenceText(text)) { }

        public TerminalTranscript(ObjectReferenceText content)
            : this(content, Array.Empty<(int, int)>()) { }

        private TerminalTranscript(ObjectReferenceText content, IReadOnlyList<(int, int)> responses)
        { Content = content; Responses = responses; }

        public static TerminalTranscript Response(string text) => string.IsNullOrEmpty(text)
            ? new TerminalTranscript("") : new TerminalTranscript(new ObjectReferenceText(text), new[] { (0, text.Length) });

        public TerminalTranscript Slice(int start, int length)
        {
            var responses = new List<(int, int)>();
            foreach ((int responseStart, int responseLength) in Responses)
            {
                int left = Math.Max(start, responseStart);
                int right = Math.Min(start + length, responseStart + responseLength);
                if (left < right) responses.Add((left - start, right - left));
            }
            return new TerminalTranscript(Content.Slice(start, length), responses);
        }

        public static TerminalTranscript Join(string separator, IEnumerable<TerminalTranscript> entries)
        {
            var text = new List<ObjectReferenceText>();
            var responses = new List<(int, int)>();
            int offset = 0;
            foreach (TerminalTranscript entry in entries)
            {
                if (offset > 0) offset += separator.Length;
                foreach ((int start, int length) in entry.Responses) responses.Add((offset + start, length));
                text.Add(entry.Content);
                offset += entry.Text.Length;
            }
            return new TerminalTranscript(ObjectReferenceText.Join(separator, text), responses);
        }
    }
}
