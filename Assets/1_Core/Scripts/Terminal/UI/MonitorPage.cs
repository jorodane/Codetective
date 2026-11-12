using UnityEngine;
using UnityEngine.Events;

namespace Codetective.Terminal
{
    /// <summary>A cached page root. Disabling its view does not dispose of its input, scroll position, or data.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
    public sealed class MonitorPage : MonoBehaviour
    {
        [SerializeField] private string pageId;
        [SerializeField] private string title;
        [SerializeField] private bool showInNavigation = true;
        [SerializeField] private bool terminalPage;
        [SerializeField] private UnityEvent shown = new UnityEvent();
        [SerializeField] private UnityEvent hidden = new UnityEvent();
        public string Id => pageId;
        public string Title => string.IsNullOrWhiteSpace(title) ? pageId : title;
        public bool ShowInNavigation => showInNavigation;
        public bool TerminalPage => terminalPage;
        internal void Show() { gameObject.SetActive(true); shown.Invoke(); }
        internal void Hide() { hidden.Invoke(); gameObject.SetActive(false); }
    }
}
