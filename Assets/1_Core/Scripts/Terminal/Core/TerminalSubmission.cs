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

        public bool TryTake(TerminalCompletionInput current, out string line) => TryTake(current, out line, out _);

        public bool TryTake(TerminalCompletionInput current, out string line, out bool hadComposition, bool allowEmpty = false)
        {
            line = string.Empty;
            hadComposition = false;
            if (!pending)
                return false;
            pending = false;
            // Keep IME cleanup required even if the OS finished composing between
            // the Enter gesture and delivery of the submitted command.
            hadComposition = requested.IsComposing || current.IsComposing;
            // The OS may deliver committed characters after its Enter event. If a
            // composition was visible at the keypress, its snapshot is already complete.
            line = requested.IsComposing ? requested.Text : current.Text;
            return !string.IsNullOrWhiteSpace(line) || (allowEmpty && !hadComposition && line.Length == 0 && requested.Text.Length == 0);
        }

        public void Cancel() => pending = false;

        /// <summary>A completion used this physical gesture; its duplicate UI/IME event must not submit.</summary>
        public void Consume(int frame) { requestFrame = frame; pending = false; }
    }
}
