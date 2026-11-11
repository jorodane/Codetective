using System;

namespace Codetective.Terminal
{
    /// <summary>The text visible in the field, including an IME's uncommitted composition.</summary>
    public readonly struct TerminalCompletionInput
    {
        public string Text { get; }
        public int Anchor { get; }
        public int Caret { get; }
        public bool IsComposing { get; }
        public bool IsAtEnd => Anchor == Text.Length && Caret == Text.Length;

        public TerminalCompletionInput(string committedText, string composition, int anchor, int caret)
        {
            committedText = committedText ?? string.Empty;
            composition = composition ?? string.Empty;
            anchor = Math.Max(0, Math.Min(anchor, committedText.Length));
            caret = Math.Max(0, Math.Min(caret, committedText.Length));
            // Match InputField.UpdateLabel: composition is inserted at the raw anchor.
            Text = composition.Length == 0 ? committedText : committedText.Insert(anchor, composition);
            Anchor = anchor + composition.Length;
            Caret = caret + composition.Length;
            IsComposing = composition.Length > 0;
        }
    }
}
