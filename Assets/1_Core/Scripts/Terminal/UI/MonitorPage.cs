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
        [Tooltip("For a detail page, the main menu to highlight and cycle from. Empty uses this page's ID.")]
        [SerializeField] private string navigationPageId;
        [SerializeField] private bool terminalPage;
        [SerializeField] private UnityEvent shown = new UnityEvent();
        [SerializeField] private UnityEvent hidden = new UnityEvent();
        public string Id => pageId;
        public string Title => string.IsNullOrWhiteSpace(title) ? pageId : title;
        public bool ShowInNavigation => showInNavigation;
        public string NavigationPageId => string.IsNullOrWhiteSpace(navigationPageId) ? Id : navigationPageId;
        public bool TerminalPage => terminalPage;
        public void Configure(string id, string label, bool showInMenu = true, string mainMenuId = null)
        { pageId = id; title = label; showInNavigation = showInMenu; navigationPageId = mainMenuId; }
        internal void Show() { gameObject.SetActive(true); shown.Invoke(); }
        internal void Hide() { hidden.Invoke(); gameObject.SetActive(false); }
    }
}
