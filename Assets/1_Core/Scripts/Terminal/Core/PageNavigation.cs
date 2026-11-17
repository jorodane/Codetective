using System;
using System.Collections.Generic;

namespace Codetective.Terminal
{
    /// <summary>Navigation belongs to the monitor; world/camera modes and page contents have their own lifetimes.</summary>
    public sealed class PageNavigation
    {
        private readonly HashSet<string> pages = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> tabOrder = new List<string>();
        private readonly List<string> history = new List<string>();
        private readonly int capacity;
        private int cursor = -1;
        public string Current => cursor >= 0 ? history[cursor] : null;
        public bool CanGoBack => cursor > 0;
        public bool CanGoForward => cursor + 1 < history.Count;
        public event Action<string> Changed;

        public PageNavigation(int capacity = 64)
        {
            if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity));
            this.capacity = capacity;
        }
        public void Register(string id, bool showInNavigation = true)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("페이지 ID를 지정하세요.", nameof(id));
            if (!pages.Add(id)) throw new ArgumentException("페이지 ID가 중복됩니다: " + id, nameof(id));
            if (showInNavigation) tabOrder.Add(id);
        }
        public bool Navigate(string id)
        {
            if (id == null || !pages.Contains(id) || Current == id) return false;
            if (CanGoForward) history.RemoveRange(cursor + 1, history.Count - cursor - 1);
            history.Add(id);
            if (history.Count > capacity) history.RemoveAt(0);
            cursor = history.Count - 1;
            Changed?.Invoke(Current);
            return true;
        }
        public bool Back()
        {
            if (!CanGoBack) return false;
            cursor--; Changed?.Invoke(Current); return true;
        }
        public bool CycleTab(int direction)
            => CycleTab(direction, Current);
        public bool CycleTab(int direction, string activeTab)
        {
            if (direction == 0 || tabOrder.Count == 0) return false;
            int index = tabOrder.IndexOf(activeTab);
            int next = index < 0 ? (direction > 0 ? 0 : tabOrder.Count - 1) :
                (index + (direction > 0 ? 1 : -1) + tabOrder.Count) % tabOrder.Count;
            return Navigate(tabOrder[next]);
        }
        public bool Forward()
        {
            if (!CanGoForward) return false;
            cursor++; Changed?.Invoke(Current); return true;
        }
    }
}
