using System;
using System.Collections.Generic;

namespace Codetective.Operations
{
    /// <summary>Terminal text and its semantic ranges, independent of the monitor layout.</summary>
    public sealed class TerminalTranscript
    {
        private static readonly IReadOnlyList<(int, int)> NoRanges = Array.Empty<(int, int)>();
        public ObjectReferenceText Content { get; }
        public string Text => Content.Text;
        public IReadOnlyList<(int Start, int Length)> Responses { get; }
        public IReadOnlyList<(int Start, int Length)> Dialogue { get; }
        public IReadOnlyList<(int Start, int Length)> CommandExamples { get; }

        public TerminalTranscript(string text) : this(new ObjectReferenceText(text)) { }

        public TerminalTranscript(ObjectReferenceText content)
            : this(content, NoRanges, NoRanges, NoRanges) { }

        private TerminalTranscript(ObjectReferenceText content, IReadOnlyList<(int, int)> responses,
            IReadOnlyList<(int, int)> dialogue, IReadOnlyList<(int, int)> commandExamples)
        {
            Content = content;
            Responses = responses;
            Dialogue = dialogue;
            CommandExamples = commandExamples;
        }

        public static TerminalTranscript Response(string text) => new TerminalTranscript(new ObjectReferenceText(text),
            WholeText(text?.Length ?? 0), NoRanges, NoRanges);

        public static TerminalTranscript Spoken(ObjectReferenceText text, IReadOnlyList<(int, int)> examples) =>
            new TerminalTranscript(text, NoRanges, WholeText(text.Text.Length), examples);

        private static IReadOnlyList<(int, int)> WholeText(int length) => length > 0 ? new[] { (0, length) } : NoRanges;

        public TerminalTranscript Slice(int start, int length) => new TerminalTranscript(Content.Slice(start, length),
            SliceRanges(Responses, start, length), SliceRanges(Dialogue, start, length), SliceRanges(CommandExamples, start, length));

        private static IReadOnlyList<(int, int)> SliceRanges(IReadOnlyList<(int Start, int Length)> ranges, int start, int length)
        {
            if (ranges.Count == 0) return NoRanges;
            var sliced = new List<(int, int)>();
            foreach ((int rangeStart, int rangeLength) in ranges)
            {
                int left = Math.Max(start, rangeStart);
                int right = Math.Min(start + length, rangeStart + rangeLength);
                if (left < right) sliced.Add((left - start, right - left));
            }
            return sliced;
        }

        public static TerminalTranscript Join(string separator, IEnumerable<TerminalTranscript> entries)
        {
            var text = new List<ObjectReferenceText>();
            var responses = new List<(int, int)>();
            var dialogue = new List<(int, int)>();
            var examples = new List<(int, int)>();
            int offset = 0;
            foreach (TerminalTranscript entry in entries)
            {
                if (offset > 0) offset += separator.Length;
                ShiftRanges(entry.Responses, responses, offset);
                ShiftRanges(entry.Dialogue, dialogue, offset);
                ShiftRanges(entry.CommandExamples, examples, offset);
                text.Add(entry.Content);
                offset += entry.Text.Length;
            }
            return new TerminalTranscript(ObjectReferenceText.Join(separator, text), responses, dialogue, examples);
        }

        private static void ShiftRanges(IReadOnlyList<(int Start, int Length)> source, List<(int, int)> destination, int offset)
        {
            foreach ((int start, int length) in source) destination.Add((offset + start, length));
        }
    }
}
