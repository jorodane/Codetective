using System;

namespace Codetective.Terminal
{
    /// <summary>Coalesces physical and UI Enter events and preserves the IME text visible at Enter.</summary>
    public sealed class TerminalSubmission
    {
        private int requestFrame = -1;
        private TerminalCompletionInput requested;
        private bool pending;

        public void Request(int frame, TerminalCompletionInput input)
        {
            if (requestFrame == frame)
                return;
            requestFrame = frame;
            requested = input;
            pending = true;
        }

        public bool TryTake(TerminalCompletionInput current, out string line)
        {
            line = string.Empty;
            if (!pending)
                return false;
            pending = false;
            // The OS may deliver committed characters after its Enter event. If a
            // composition was visible at the keypress, its snapshot is already complete.
            line = requested.IsComposing ? requested.Text : current.Text;
            return !string.IsNullOrWhiteSpace(line);
        }

        public void Cancel() => pending = false;
    }
}
