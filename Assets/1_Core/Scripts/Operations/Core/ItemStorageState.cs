using System;
using System.Collections.Generic;
using System.Linq;

namespace Codetective.Operations
{
    [Serializable] public sealed class StoredItemState
    {
        public string itemId;
        public string containerId;
        public StoredItemState Copy() => (StoredItemState)MemberwiseClone();
    }
    public sealed partial class KernelRuntime
    {
        private readonly Dictionary<string, StoredItemState> storedItems = new Dictionary<string, StoredItemState>(StringComparer.Ordinal);
        public IReadOnlyList<StoredItemState> StoredItems => storedItems.Values.OrderBy(s => s.itemId, StringComparer.Ordinal).Select(s => s.Copy()).ToArray();
        public void SetItemStorage(string itemId, string containerId)
        {
            if (string.IsNullOrWhiteSpace(itemId)) throw new CommandException("물건 ID가 필요합니다.");
            if (string.IsNullOrWhiteSpace(containerId))
            { if (!storedItems.Remove(itemId)) return; }
            else
            {
                if (storedItems.TryGetValue(itemId, out StoredItemState previous) && previous.containerId == containerId) return;
                storedItems[itemId] = new StoredItemState { itemId = itemId, containerId = containerId };
            }
            Revision++; NarrativeUpdated();
        }
        private void RestoreItemStorage(StoredItemState[] states)
        {
            storedItems.Clear();
            foreach (StoredItemState state in states ?? Array.Empty<StoredItemState>())
            {
                if (state == null || string.IsNullOrWhiteSpace(state.itemId) || string.IsNullOrWhiteSpace(state.containerId) || storedItems.ContainsKey(state.itemId))
                    throw new CommandException("저장된 물건의 보관 위치가 유효하지 않습니다.");
                storedItems.Add(state.itemId, state.Copy());
            }
        }
    }
}
