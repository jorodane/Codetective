using System.Collections.Generic;
using Codetective.Operations;
using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    [DisallowMultipleComponent, RequireComponent(typeof(MonitorPage))]
    public sealed class HomeMonitorPage : MonoBehaviour, IMonitorPageContent
    {
        private MonitorPageHost host;
        private Font font;
        private RectTransform menu;
        private readonly List<string> destinations = new List<string>();

        public void Initialize(KernelRuntime runtime, Font typeface)
        {
            host = GetComponentInParent<MonitorPageHost>(); font = typeface;
            if (menu == null)
            {
                MonitorWidgets.Label(transform, font, new Vector2(28, -100), new Vector2(1064, 58), "업무를 선택하세요.", 36);
                menu = MonitorWidgets.Rect("HomeMenu", transform, new Vector2(28, -194), new Vector2(1064, 450));
            }
            RefreshMenu();
        }
        private void OnEnable() { if (menu != null) RefreshMenu(); }
        private void Update() { if (host != null && menu != null && destinations.Count != host.NavigationPages.Count) RefreshMenu(); }
        private void RefreshMenu()
        {
            IReadOnlyList<MonitorPage> pages = host.NavigationPages;
            if (destinations.Count == pages.Count) return;
            foreach (Transform child in menu)
            { child.gameObject.SetActive(false); if (Application.isPlaying) Destroy(child.gameObject); else DestroyImmediate(child.gameObject); }
            destinations.Clear();
            for (int i = 0; i < pages.Count; i++)
            {
                MonitorPage page = pages[i]; string id = page.Id; destinations.Add(id);
                Button button = MonitorWidgets.Button(menu, font, new Vector2(i % 2 * 546, -(i / 2) * 220), new Vector2(518, 196), page.Title, () => host.ShowPage(id));
                Text title = button.GetComponentInChildren<Text>();
                title.rectTransform.anchoredPosition = new Vector2(24, -22); title.rectTransform.sizeDelta = new Vector2(470, 52);
                title.fontSize = 36; title.alignment = TextAnchor.UpperLeft;
                MonitorWidgets.Label(button.transform, font, new Vector2(24, -100), new Vector2(470, 64), Description(id), 24);
            }
        }
        private static string Description(string id)
        {
            switch (id)
            {
                case "kernel": return "명령 작성 · 권한 목록";
                case "clinic": return "인물 프로필 · 상담 기록";
                case "mail": return "커널 메일 · 상담소 메일 · 예약 문의";
                case "schedule": return "날짜별 예약 · 상담 일정";
                default: return "열기";
            }
        }
    }
}
