namespace Codetective.Terminal
{
    /// <summary>Owns composition lifetime; stale backend text must never outlive its field session.</summary>
    public sealed class ImeSession
    {
        private bool active;
        private bool hasEvent;
        private string composition = "";
        private string ignoredRaw;
        private string commitEcho = "";
        private int echoIndex;
        private int suppressDepth;

        public void Begin(string raw)
        {
            active = true; hasEvent = false; composition = ""; ignoredRaw = raw ?? "";
        }

        public string Read(string raw)
        {
            if (!active || suppressDepth > 0) return "";
            if (hasEvent) return composition;
            raw = raw ?? "";
            if (ignoredRaw != null)
            {
                if (raw == ignoredRaw) return "";
                ignoredRaw = null;
            }
            return raw;
        }

        public void CompositionChanged(string value)
        {
            if (!active) return;
            composition = value ?? "";
            hasEvent = true;
            if (composition.Length > 0) { commitEcho = ""; echoIndex = 0; }
        }

        public void Consume(string value, string raw)
        {
            composition = ""; hasEvent = true; ignoredRaw = raw ?? "";
            if (!string.IsNullOrEmpty(value)) { commitEcho = value; echoIndex = 0; }
        }

        public void End(string value, string raw) { Consume(value, raw); active = false; }
        public void Suppress() { suppressDepth++; }
        public void Resume() { if (suppressDepth > 0) suppressDepth--; }
        public string TakeCommitEcho()
        {
            string remaining = commitEcho.Substring(echoIndex);
            commitEcho = ""; echoIndex = 0;
            return remaining;
        }
        public void ExpectCommitEcho(string value)
        {
            if (!string.IsNullOrEmpty(value)) { commitEcho = value; echoIndex = 0; }
        }

        public bool IgnoreCommitEcho(char value)
        {
            if (echoIndex >= commitEcho.Length) return false;
            if (commitEcho[echoIndex] != value) { commitEcho = ""; echoIndex = 0; return false; }
            echoIndex++;
            if (echoIndex == commitEcho.Length) { commitEcho = ""; echoIndex = 0; }
            return true;
        }
    }
}
