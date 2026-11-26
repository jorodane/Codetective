using System;
using System.Collections.Generic;
using System.Text;
using Codetective.Operations;

namespace Codetective.Terminal
{
    /// <summary>Bounded scrollback; the character budget also keeps uGUI Text below its mesh vertex limit.</summary>
    public sealed class TerminalHistory
    {
        private readonly Queue<TerminalTranscript> entries = new Queue<TerminalTranscript>();
        private readonly int entryLimit;
        private readonly int characterLimit;
        private int characters;

        public TerminalTranscript Transcript { get; private set; } = new TerminalTranscript("");
        public ObjectReferenceText Content => Transcript.Content;
        public string Text => Content.Text;
        public int Count => entries.Count;

        public TerminalHistory(int entryLimit = 100, int characterLimit = 12000)
        {
            this.entryLimit = Math.Max(1, entryLimit);
            this.characterLimit = Math.Max(1, characterLimit);
        }

        public string Append(string entry) => AppendContent(new ObjectReferenceText(entry));
        public string AppendContent(ObjectReferenceText entry) => AppendTranscript(new TerminalTranscript(entry));

        /// <returns>The removed prefix, so a reader's scroll position can be preserved when old entries expire.</returns>
        public string AppendTranscript(TerminalTranscript entry)
        {
            if (entry.Text.Length > characterLimit)
                entry = entry.Slice(entry.Text.Length - characterLimit, characterLimit);
            characters += entry.Text.Length + (entries.Count > 0 ? 2 : 0);
            entries.Enqueue(entry);
            var removed = new StringBuilder();
            while (entries.Count > 1 && (entries.Count > entryLimit || characters > characterLimit))
            {
                string oldest = entries.Dequeue().Text;
                characters -= oldest.Length + 2;
                removed.Append(oldest).Append("\n\n");
            }
            Transcript = TerminalTranscript.Join("\n\n", entries);
            return removed.ToString();
        }

        public static bool ShouldFollowLatest(float contentHeight, float viewportHeight, float topOffset)
        {
            return contentHeight <= viewportHeight ||
                   contentHeight - viewportHeight - Math.Max(0f, topOffset) <= 24f;
        }
    }
}
