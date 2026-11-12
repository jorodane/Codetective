using System;

namespace Codetective.Terminal
{
    public enum CompletionKey { Tab, Enter, Space }
    public enum CompletionKeyAction { Accept, Submit, TypeCharacter, None }

    /// <summary>A proposed edit. Displaying it never changes the input or executes a command.</summary>
    public readonly struct InlineCompletion
    {
        public string Text { get; }
        public int Caret { get; }
        public InlineCompletion(string text, int caret)
        {
            Text = text ?? "";
            Caret = Math.Max(0, Math.Min(caret, Text.Length));
        }
        public bool Changes(TerminalCompletionInput input) => input.Anchor == input.Caret && Text != input.Text;
        public string PreviewFrom(int visibleStart) => Text.Substring(Math.Max(0, Math.Min(visibleStart, Caret)),
            Caret - Math.Max(0, Math.Min(visibleStart, Caret)));
        public static CompletionKeyAction Resolve(CompletionKey key, TerminalCompletionInput input, InlineCompletion? suggestion, bool submitOnEnter)
        {
            if (suggestion.HasValue && suggestion.Value.Changes(input)) return CompletionKeyAction.Accept;
            if (key == CompletionKey.Enter) return submitOnEnter ? CompletionKeyAction.Submit : CompletionKeyAction.TypeCharacter;
            return key == CompletionKey.Space ? CompletionKeyAction.TypeCharacter : CompletionKeyAction.None;
        }
    }
}
