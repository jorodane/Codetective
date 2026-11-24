using System;
using System.Linq;
using Codetective.Operations;
using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    /// <summary>The right-hand person list. Profile content is printed on the authored desk sheet.</summary>
    public sealed class ProfileArchiveView : MonoBehaviour
    {
        private KernelRuntime runtime;
        private Font font;
        private Func<string, bool> select;
        private Func<string> current;
        private Func<bool> allowed;
        private ScrollRect scroll;
        private RectTransform rows;
        private Vector2 archivePosition, archiveSize;
        private float viewportHeight = 410f;
        private int rowCount;
        private int revision = -1;
        private string listKey, lastSelected;
        private bool lastAllowed;
        public string SelectedProfileId => current?.Invoke();
        public bool IsVisible => isActiveAndEnabled;
        public void Initialize(KernelRuntime world, Font typeface, Func<string, bool> choose, Func<string> selected, Func<bool> canChoose, Action close)
        {
            runtime = world; font = typeface != null ? typeface : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            select = choose; current = selected; allowed = canChoose;
            RectTransform archive = (RectTransform)transform;
            archivePosition = archive.anchoredPosition; archiveSize = archive.sizeDelta;
            gameObject.AddComponent<Image>().color = new Color(0.80f, 0.77f, 0.67f, 0.97f);
            Label(transform, new Vector2(14, -12), new Vector2(150, 36), "인물 파일", 24);
            RectTransform closeRect = MonitorWidgets.Rect("Close", archive, new Vector2(173, -12), new Vector2(63, 36));
            Image closeImage = closeRect.gameObject.AddComponent<Image>(); closeImage.color = new Color(0.68f, 0.66f, 0.57f);
            Button closeButton = closeRect.gameObject.AddComponent<Button>(); closeButton.targetGraphic = closeImage;
            closeButton.navigation = new Navigation { mode = Navigation.Mode.None };
            closeButton.onClick.AddListener(() => close());
            Label(closeRect, Vector2.zero, closeRect.sizeDelta, "닫기", 17).alignment = TextAnchor.MiddleCenter;
            Label(transform, new Vector2(14, -50), new Vector2(222, 48), "인물을 선택하면\n서류를 가져옵니다.", 16);
            RectTransform viewport = MonitorWidgets.Rect("People", transform, new Vector2(12, -108), new Vector2(226, 410));
            viewport.gameObject.AddComponent<Image>().color = new Color(0.88f, 0.86f, 0.77f); viewport.gameObject.AddComponent<RectMask2D>();
            scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.horizontal = false;
            scroll.scrollSensitivity = 30; scroll.movementType = ScrollRect.MovementType.Clamped;
            rows = MonitorWidgets.Rect("AcquiredProfiles", viewport, Vector2.zero, new Vector2(214, 410)); scroll.content = rows;
            gameObject.SetActive(false);
        }
        public void Show() { if (runtime != null) { gameObject.SetActive(true); revision = -1; Refresh(); } }
        public void Hide() => gameObject.SetActive(false);
        public void SetTopInset(float inset)
        {
            float shift = Mathf.Clamp(inset + archivePosition.y, 0f, 310f);
            float height = 410f - shift;
            if (Mathf.Approximately(viewportHeight, height)) return;
            viewportHeight = height;
            RectTransform archive = (RectTransform)transform;
            archive.anchoredPosition = archivePosition - Vector2.up * shift;
            archive.sizeDelta = archiveSize - Vector2.up * shift;
            scroll.viewport.sizeDelta = new Vector2(226, viewportHeight);
            rows.sizeDelta = new Vector2(214, Mathf.Max(viewportHeight, rowCount * 54));
        }
        public bool SelectProfile(string id)
        {
            if (!IsVisible || runtime == null || !(allowed?.Invoke() ?? false) || !runtime.VisibleProfileIds.Contains(id)) return false;
            bool accepted = select?.Invoke(id) ?? false; Refresh(); return accepted;
        }
        private void Update()
        {
            if (runtime != null && (revision != runtime.Revision || lastSelected != SelectedProfileId || lastAllowed != (allowed?.Invoke() ?? false))) Refresh();
        }
        public void Refresh()
        {
            if (runtime == null || !IsVisible) return;
            string[] ids = runtime.VisibleProfileIds.OrderBy(id => id == "kernel" ? 0 : 1).ThenBy(ProfileLabel, StringComparer.Ordinal).ToArray();
            string key = string.Join("\n", ids.Select(id => id + ":" + ProfileLabel(id)));
            if (listKey != key)
            {
                float position = scroll.verticalNormalizedPosition;
                for (int i = rows.childCount - 1; i >= 0; i--) { GameObject old = rows.GetChild(i).gameObject; old.SetActive(false); if (Application.isPlaying) Destroy(old); else DestroyImmediate(old); }
                for (int i = 0; i < ids.Length; i++)
                {
                    string id = ids[i]; RectTransform rect = MonitorWidgets.Rect("Profile_" + id, rows, new Vector2(0, -i * 54), new Vector2(214, 46));
                    Image image = rect.gameObject.AddComponent<Image>(); Button button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
                    button.navigation = new Navigation { mode = Navigation.Mode.None }; button.onClick.AddListener(() => SelectProfile(id));
                    Label(rect, new Vector2(8, -6), new Vector2(198, 34), ProfileLabel(id), 20).alignment = TextAnchor.MiddleLeft;
                }
                rowCount = ids.Length;
                rows.sizeDelta = new Vector2(214, Mathf.Max(viewportHeight, rowCount * 54)); scroll.verticalNormalizedPosition = position; listKey = key;
            }
            lastSelected = SelectedProfileId; lastAllowed = allowed?.Invoke() ?? false;
            foreach (Button button in rows.GetComponentsInChildren<Button>())
            {
                button.interactable = lastAllowed;
                button.image.color = button.name == "Profile_" + lastSelected ? new Color(0.57f, 0.64f, 0.49f) : new Color(0.75f, 0.72f, 0.63f);
            }
            revision = runtime.Revision;
        }
        private string ProfileLabel(string id) => ProfileFormView.ListName(runtime.ProfileCard(id));
        private Text Label(Transform parent, Vector2 position, Vector2 size, string value, int fontSize)
        {
            Text label = MonitorWidgets.Rect("Label", parent, position, size).gameObject.AddComponent<Text>();
            label.font = font; label.fontSize = fontSize; label.text = value; label.color = new Color(0.13f, 0.12f, 0.09f);
            label.supportRichText = false; label.raycastTarget = false;
            return label;
        }
    }
}
