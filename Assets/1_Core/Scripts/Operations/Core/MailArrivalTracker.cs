using System;
using System.Collections.Generic;
using System.Linq;

namespace Codetective.Operations
{
    /// <summary>Transient notifications for newly available mail. Opening a save seeds the baseline without replaying old arrivals.</summary>
    public sealed class MailArrivalTracker
    {
        private readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> pending = new HashSet<string>(StringComparer.Ordinal);
        private bool signalPending;
        public int PendingCount => pending.Count;

        public void Initialize(KernelRuntime runtime)
        {
            seen.Clear(); Acknowledge();
            foreach (StoryMail message in Available(runtime)) seen.Add(message.id);
        }
        public void Observe(KernelRuntime runtime)
        {
            var unread = new HashSet<string>(StringComparer.Ordinal);
            foreach (StoryMail message in Available(runtime))
            {
                bool firstArrival = seen.Add(message.id);
                if (runtime.IsMailRead(message.id)) continue;
                unread.Add(message.id);
                if (firstArrival) { pending.Add(message.id); signalPending = true; }
            }
            pending.IntersectWith(unread);
            if (pending.Count == 0) signalPending = false;
        }
        public bool TakeSignal()
        {
            bool result = signalPending && pending.Count > 0;
            signalPending = false;
            return result;
        }
        public void Acknowledge() { pending.Clear(); signalPending = false; }
        private static IEnumerable<StoryMail> Available(KernelRuntime runtime) => runtime.Inbox(Mailbox.Kernel).Concat(runtime.Inbox(Mailbox.Clinic));
    }
}
