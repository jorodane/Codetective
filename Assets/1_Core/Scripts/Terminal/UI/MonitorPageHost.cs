using System;
using System.Collections.Generic;
using Codetective.Operations;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    [DefaultExecutionOrder(-150), DisallowMultipleComponent]
    public sealed class MonitorPageHost : MonoBehaviour
    {
        [SerializeField] private TerminalController terminal;
        [SerializeField] private Text heading;
        [SerializeField] private RectTransform navigationRoot;
        [SerializeField] private string initialPage = "clinic";
        private readonly Dictionary<string, MonitorPage> pages = new Dictionary<string, MonitorPage>(StringComparer.Ordinal);
        private readonly Dictionary<string, Button> tabs = new Dictionary<string, Button>(StringComparer.Ordinal);
        private readonly PageNavigation navigation = new PageNavigation();
        private RectTransform tabContent;
        private CrtScrollRect tabScroll;
        private Button back;
        private Button forward;
        private KernelRuntime runtime;
        private bool initialized;
        public MonitorPage CurrentPage { get; private set; }
        public string SelectedProfileId { get; set; } = "client";
        public bool CanGoBack => navigation.CanGoBack;
        public bool CanGoForward => navigation.CanGoForward;
        public bool IsCompositionProtected
        {
            get
            {
                if (CurrentPage == null) return false;
                foreach (CompletingInputField input in CurrentPage.GetComponentsInChildren<CompletingInputField>(true))
                    if (input.isFocused && input.IsCompositionProtected) return true;
                return false;
            }
        }
        public event Action<MonitorPage> PageChanged;
        private Font Font => heading != null && heading.font != null ? heading.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        private void Awake() => Initialize();
        public void Initialize()
        {
            if (initialized || navigationRoot == null) return;
            terminal = terminal != null ? terminal : GetComponent<TerminalController>();
            runtime = runtime ?? terminal?.Runtime;
            BuildNavigation();
            navigation.Changed += Display;
            initialized = true;
            foreach (MonitorPage page in GetComponentsInChildren<MonitorPage>(true)) Register(page);
            if (!TryNavigate(initialPage))
                foreach (string id in pages.Keys) { TryNavigate(id); break; }
        }
        public void AttachRuntime(KernelRuntime value)
        {
            runtime = value;
            if (!initialized) return;
            foreach (MonitorPage page in pages.Values) InitializeContent(page);
        }
        public void Register(MonitorPage page)
        {
            if (page == null || page.transform == transform || !page.transform.IsChildOf(transform))
                throw new ArgumentException("페이지는 모니터 아래의 별도 루트여야 합니다.");
            foreach (MonitorPage other in pages.Values)
                if (page.transform.IsChildOf(other.transform) || other.transform.IsChildOf(page.transform))
                    throw new ArgumentException("페이지 루트끼리는 중첩할 수 없습니다.");
            navigation.Register(page.Id);
            pages.Add(page.Id, page);
            page.gameObject.SetActive(false);
            InitializeContent(page);
            if (page.ShowInNavigation)
            {
                string id = page.Id;
                int index = tabs.Count;
                tabs.Add(id, MonitorWidgets.Button(tabContent, Font, new Vector2(index * 152, 0), new Vector2(144, 44), page.Title, () => ShowPage(id)));
                tabContent.sizeDelta = new Vector2(tabs.Count * 152 - 8, 44);
            }
        }
        private void InitializeContent(MonitorPage page)
        {
            if (runtime == null) return;
            foreach (MonoBehaviour component in page.GetComponentsInChildren<MonoBehaviour>(true))
                if (component is IMonitorPageContent content) content.Initialize(runtime, Font);
        }
        public bool TryNavigate(string id) => initialized && navigation.Navigate(id);
        public void ShowPage(string id) => TryNavigate(id);
        public void GoBack() => navigation.Back();
        public void GoForward() => navigation.Forward();

        private void Display(string id)
        {
            if (CurrentPage != null)
            {
                // Commit IME before hiding the page. Pending Enter/completion actions must not run while hidden.
                foreach (CompletingInputField input in CurrentPage.GetComponentsInChildren<CompletingInputField>(true)) input.ReleaseFocus();
                GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                if (selected != null && selected.transform.IsChildOf(CurrentPage.transform)) EventSystem.current.SetSelectedGameObject(null);
                terminal?.SetPageVisible(false);
                CurrentPage.Hide();
            }
            CurrentPage = pages[id];
            CurrentPage.Show();
            terminal?.SetPageVisible(CurrentPage.TerminalPage);
            if (heading != null) heading.text = "CODETECTIVE / " + CurrentPage.Title;
            foreach (var tab in tabs) tab.Value.image.color = tab.Key == id ? new Color(0.16f, 0.32f, 0.23f) : MonitorWidgets.ButtonColor;
            back.interactable = CanGoBack; forward.interactable = CanGoForward;
            RevealTab(id);
            PageChanged?.Invoke(CurrentPage);
        }
        private void RevealTab(string id)
        {
            if (!tabs.TryGetValue(id, out Button button)) return;
            Canvas.ForceUpdateCanvases();
            RectTransform tab = (RectTransform)button.transform;
            float visible = tabScroll.viewport.rect.width;
            float maximum = Mathf.Max(0, tabContent.rect.width - visible);
            float left = -tabContent.anchoredPosition.x;
            if (tab.anchoredPosition.x < left) left = tab.anchoredPosition.x;
            else if (tab.anchoredPosition.x + tab.rect.width > left + visible) left = tab.anchoredPosition.x + tab.rect.width - visible;
            tabContent.anchoredPosition = new Vector2(-Mathf.Clamp(left, 0, maximum), 0);
        }
        private void BuildNavigation()
        {
            back = MonitorWidgets.Button(navigationRoot, Font, Vector2.zero, new Vector2(44, 44), "<", GoBack);
            forward = MonitorWidgets.Button(navigationRoot, Font, new Vector2(52, 0), new Vector2(44, 44), ">", GoForward);
            back.interactable = forward.interactable = false;
            RectTransform viewport = MonitorWidgets.Rect("Pages", navigationRoot, new Vector2(112, 0), new Vector2(navigationRoot.rect.width - 112, 44));
            viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.01f);
            viewport.gameObject.AddComponent<CrtMeshEffect>(); viewport.gameObject.AddComponent<RectMask2D>();
            tabContent = MonitorWidgets.Rect("Tabs", viewport, Vector2.zero, new Vector2(0, 44));
            tabScroll = viewport.gameObject.AddComponent<CrtScrollRect>();
            tabScroll.content = tabContent; tabScroll.viewport = viewport; tabScroll.horizontal = true; tabScroll.vertical = false;
            tabScroll.movementType = ScrollRect.MovementType.Clamped; tabScroll.scrollSensitivity = 40;
        }
        private void OnDestroy() => navigation.Changed -= Display;
    }
}
