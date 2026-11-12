using System;
using System.Collections.Generic;

namespace Codetective.Terminal
{
    /// <summary>Navigation belongs to the monitor; world/camera modes and page contents have their own lifetimes.</summary>
    public sealed class PageNavigation
    {
        private readonly HashSet<string> pages = new HashSet<string>(StringComparer.Ordinal);
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
        public void Register(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("페이지 ID를 지정하세요.", nameof(id));
            if (!pages.Add(id)) throw new ArgumentException("페이지 ID가 중복됩니다: " + id, nameof(id));
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
        public bool Forward()
        {
            if (!CanGoForward) return false;
            cursor++; Changed?.Invoke(Current); return true;
        }
    }
}
