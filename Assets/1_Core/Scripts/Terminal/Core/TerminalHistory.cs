using System;
using System.Collections.Generic;
using System.Text;

namespace Codetective.Terminal
{
    /// <summary>Bounded scrollback; the character budget also keeps uGUI Text below its mesh vertex limit.</summary>
    public sealed class TerminalHistory
    {
        private readonly Queue<string> entries = new Queue<string>();
        private readonly int entryLimit;
        private readonly int characterLimit;
        private int characters;

        public string Text { get; private set; } = string.Empty;
        public int Count => entries.Count;

        public TerminalHistory(int entryLimit = 100, int characterLimit = 12000)
        {
            this.entryLimit = Math.Max(1, entryLimit);
            this.characterLimit = Math.Max(1, characterLimit);
        }

        /// <returns>The removed prefix, so a reader's scroll position can be preserved when old entries expire.</returns>
        public string Append(string entry)
        {
            entry = entry ?? string.Empty;
            if (entry.Length > characterLimit)
                entry = entry.Substring(entry.Length - characterLimit);
            characters += entry.Length + (entries.Count > 0 ? 2 : 0);
            entries.Enqueue(entry);
            var removed = new StringBuilder();
            while (entries.Count > 1 && (entries.Count > entryLimit || characters > characterLimit))
            {
                string oldest = entries.Dequeue();
                characters -= oldest.Length + 2;
                removed.Append(oldest).Append("\n\n");
            }
            Text = string.Join("\n\n", entries);
            return removed.ToString();
        }

        public static bool ShouldFollowLatest(float contentHeight, float viewportHeight, float topOffset)
        {
            return contentHeight <= viewportHeight ||
                   contentHeight - viewportHeight - Math.Max(0f, topOffset) <= 24f;
        }
    }
}
