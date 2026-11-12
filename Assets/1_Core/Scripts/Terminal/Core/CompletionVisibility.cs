namespace Codetective.Terminal
{
    /// <summary>Browsing an empty prompt is explicit UI state, independent of world refreshes.</summary>
    public sealed class CompletionVisibility
    {
        private bool allowEmpty;
        private bool dismissed;
        public void Open() { allowEmpty = true; dismissed = false; }
        public void Edit() { allowEmpty = false; dismissed = false; }
        public void Dismiss() { allowEmpty = false; dismissed = true; }
        public bool ShouldShow(TerminalCompletionInput input, bool focused, bool enabled) =>
            enabled && focused && !dismissed && input.IsAtEnd && (allowEmpty || !string.IsNullOrWhiteSpace(input.Text));
    }
}
