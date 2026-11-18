using System;
using Codetective.Terminal;
using UnityEngine;

namespace Codetective.Interaction
{
    /// <summary>Connect the camera's pause flow to the menu rendered on the scene's CRT.</summary>
    [DisallowMultipleComponent]
    public sealed class PauseMenuView : MonoBehaviour
    {
        public event Action ResumeRequested;
        private MonitorPageHost host;
        private SystemMenuMonitorView view;
        private SystemMenuActions actions = new SystemMenuActions();
        public bool CanShow => host != null;
        public void BindTerminal(MonitorPageHost value) => host = value;
        public void Configure(SystemMenuActions value) => actions = value ?? new SystemMenuActions();
        public void Show(bool title = false)
        {
            if (host == null) return;
            host.Initialize();
            if (view == null)
            {
                var root = new GameObject("SystemMenu", typeof(RectTransform)); root.layer = 5;
                root.transform.SetParent(host.transform, false);
                view = root.AddComponent<SystemMenuMonitorView>();
            }
            actions.Resume = () => ResumeRequested?.Invoke();
            view.Initialize(host, actions); view.Show(title);
        }
        public bool HandleEscape() => view != null && view.HandleEscape();
        public void Hide() { if (view != null) view.Hide(); }
        public void ShowFeedback(string value) { if (view != null) view.ShowFeedback(value); }
        private void OnDestroy()
        { if (view != null) { if (Application.isPlaying) Destroy(view.gameObject); else DestroyImmediate(view.gameObject); } }
    }
}
