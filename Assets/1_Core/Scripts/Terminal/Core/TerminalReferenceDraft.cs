using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Codetective.Operations;

namespace Codetective.Terminal
{
    /// <summary>Tracks the identities of object links while the visible command is edited as ordinary text.</summary>
    public sealed class TerminalReferenceDraft
    {
        public ObjectReferenceText Content { get; private set; } = new ObjectReferenceText("");

        public void Set(ObjectReferenceText content) => Content = content;
        public void Update(string text, int editStart = int.MaxValue) => Content = View(text, editStart);

        public ObjectReferenceText View(string text, int editStart = int.MaxValue)
        {
            string before = Content.Text;
            if (text == before) return Content;
            if (Content.Spans.Count == 0) return new ObjectReferenceText(text);
            int start = 0, oldEnd = before.Length, newEnd = text.Length;
            int prefixLimit = Math.Min(editStart, Math.Min(oldEnd, newEnd));
            while (start < prefixLimit && before[start] == text[start]) start++;
            while (oldEnd > start && newEnd > start && before[oldEnd - 1] == text[newEnd - 1]) { oldEnd--; newEnd--; }
            return new ObjectReferenceText(text, TranslateSpans(start, oldEnd, newEnd - oldEnd).ToArray());
        }

        private IEnumerable<ObjectReferenceSpan> TranslateSpans(int start, int end, int shift)
        {
            foreach (ObjectReferenceSpan span in Content.Spans)
            {
                if (span.End <= start) yield return span;
                else if (span.Start >= end) yield return span.Shift(shift);
            }
        }

        public int Snap(int position, int direction = 0)
        {
            foreach (ObjectReferenceSpan span in Content.Spans)
                if (position > span.Start && position < span.End)
                    return direction < 0 || direction == 0 && position - span.Start < span.End - position ? span.Start : span.End;
            return position;
        }

        public bool TryDeletion(int position, bool backwards, out ObjectReferenceSpan reference)
        {
            foreach (ObjectReferenceSpan span in Content.Spans)
                if (backwards ? position > span.Start && position <= span.End : position >= span.Start && position < span.End)
                { reference = span; return true; }
            reference = default;
            return false;
        }

        public int Insert(int start, int end, string id, string label)
        {
            string text = Content.Text;
            string inserted = start > 0 && text[start - 1] == '=' ? " " + label : label;
            int caret = start + inserted.Length;
            var span = new ObjectReferenceSpan(caret - label.Length, label.Length, id);
            var spans = TranslateSpans(start, end, caret - end).Append(span).OrderBy(link => link.Start).ToArray();
            Set(new ObjectReferenceText(text.Remove(start, end - start).Insert(start, inserted), spans));
            return caret;
        }

        public string Encode(string text, int editStart = int.MaxValue)
        {
            ObjectReferenceText visible = View(text, editStart);
            if (visible.Spans.Count == 0) return text;
            var result = new StringBuilder();
            int start = 0;
            foreach (ObjectReferenceSpan span in visible.Spans)
            {
                result.Append(text, start, span.Start - start).Append(CommandObjectReference.Write(span.Id));
                start = span.End;
            }
            return result.Append(text, start, text.Length - start).ToString();
        }

        public InlineCompletion DecodeCompletion(string command, Func<string, string> objectLabel)
        {
            var captions = Content.Spans.ToLookup(span => span.Id, span => Content.Text.Substring(span.Start, span.Length));
            var remaining = new Dictionary<string, Queue<string>>(StringComparer.Ordinal);
            var text = new StringBuilder();
            var spans = new List<ObjectReferenceSpan>();
            bool addsReference = false;
            for (int i = 0; i < command.Length;)
            {
                if (command[i] != '@') { text.Append(command[i++]); continue; }
                int start = i++;
                while (i < command.Length && CommandObjectReference.IsCodeCharacter(command[i])) i++;
                string token = command.Substring(start, i - start);
                if (!CommandObjectReference.TryRead(token, out string id)) { text.Append(token); continue; }
                if (!remaining.TryGetValue(id, out Queue<string> labels)) remaining[id] = labels = new Queue<string>(captions[id]);
                bool newReference = labels.Count == 0;
                string label = newReference ? objectLabel(id) : labels.Dequeue();
                if (string.IsNullOrEmpty(label)) { text.Append(token); continue; }
                addsReference |= newReference;
                spans.Add(new ObjectReferenceSpan(text.Length, label.Length, id));
                text.Append(label);
            }
            return new InlineCompletion(new ObjectReferenceText(text.ToString(), spans), text.Length, addsReference);
        }
    }
}
