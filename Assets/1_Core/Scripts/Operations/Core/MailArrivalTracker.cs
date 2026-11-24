using System;
using System.Collections.Generic;
using System.Linq;

namespace Codetective.Operations
{
    /// <summary>Transient notifications for newly available mail. Opening a save seeds the baseline without replaying old arrivals.</summary>
    public sealed class MailArrivalTracker
    {
        private readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> pending = new List<string>();
        private bool signalPending;
        public int PendingCount => pending.Count;
        public string LatestPendingId => pending.Count > 0 ? pending[pending.Count - 1] : null;

        public void Initialize(KernelRuntime runtime)
        {
            seen.Clear(); Acknowledge();
            foreach (StoryMail message in Available(runtime)) seen.Add(message.id);
        }
        public void Observe(KernelRuntime runtime)
        {
            var unread = new HashSet<string>(StringComparer.Ordinal);
            foreach (StoryMail message in Available(runtime).OrderBy(m => m.day).ThenBy(m => m.mailbox))
            {
                bool firstArrival = seen.Add(message.id);
                if (runtime.IsMailRead(message.id)) continue;
                unread.Add(message.id);
                if (firstArrival) { pending.Add(message.id); signalPending = true; }
            }
            pending.RemoveAll(id => !unread.Contains(id));
            if (pending.Count == 0) signalPending = false;
        }
        public bool TakeSignal()
        {
            bool result = signalPending && pending.Count > 0;
            signalPending = false;
            return result;
        }
        public void Acknowledge(string id) => pending.Remove(id);
        public void Acknowledge() { pending.Clear(); signalPending = false; }
        private static IEnumerable<StoryMail> Available(KernelRuntime runtime) => runtime.Inbox(Mailbox.Kernel).Concat(runtime.Inbox(Mailbox.Clinic));
    }
}
