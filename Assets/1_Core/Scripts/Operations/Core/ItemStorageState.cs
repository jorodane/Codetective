using System;
using System.Collections.Generic;
using System.Linq;

namespace Codetective.Operations
{
    [Serializable] public sealed class StoredItemState
    {
        public string itemId;
        public string containerId;
        public bool hasPosition;
        public float x, y, z;
        public int drawerSlot;
        public StoredItemState Copy() => (StoredItemState)MemberwiseClone();
    }
    public sealed partial class KernelRuntime
    {
        private readonly Dictionary<string, StoredItemState> storedItems = new Dictionary<string, StoredItemState>(StringComparer.Ordinal);
        public IReadOnlyList<StoredItemState> StoredItems => storedItems.Values.OrderBy(s => s.itemId, StringComparer.Ordinal).Select(s => s.Copy()).ToArray();
        public void SetItemStorage(string itemId, string containerId)
            => SetItemStorage(new StoredItemState { itemId = itemId, containerId = containerId });
        public void SetItemStorage(StoredItemState state)
        {
            if (state == null) throw new CommandException("물건의 배치 정보가 필요합니다.");
            string itemId = state.itemId, containerId = state.containerId;
            if (string.IsNullOrWhiteSpace(itemId)) throw new CommandException("물건 ID가 필요합니다.");
            ValidateItemPosition(state);
            if (string.IsNullOrWhiteSpace(containerId))
            { if (!storedItems.Remove(itemId)) return; }
            else
            {
                if (storedItems.TryGetValue(itemId, out StoredItemState previous) && previous.containerId == containerId &&
                    previous.drawerSlot == state.drawerSlot && previous.hasPosition == state.hasPosition &&
                    (!state.hasPosition || previous.x == state.x && previous.y == state.y && previous.z == state.z)) return;
                storedItems[itemId] = state.Copy();
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
                ValidateItemPosition(state);
                storedItems.Add(state.itemId, state.Copy());
            }
        }
        private static void ValidateItemPosition(StoredItemState state)
        {
            if (state.drawerSlot < 0 || state.drawerSlot > 0 && !state.hasPosition ||
                state.hasPosition && (float.IsNaN(state.x) || float.IsInfinity(state.x) || float.IsNaN(state.y) || float.IsInfinity(state.y) || float.IsNaN(state.z) || float.IsInfinity(state.z)))
                throw new CommandException("물건의 배치 좌표가 유효하지 않습니다.");
        }
    }
}
