using System;
using System.Linq;
using Codetective.Operations;
using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    /// <summary>A paper-file browser sharing the acquired information and journals with the terminal.</summary>
    public sealed class ProfileArchiveView : MonoBehaviour
    {
        private static readonly Color Ink = new Color(0.13f, 0.12f, 0.09f);
        private KernelRuntime runtime;
        private Font font;
        private Func<string, Sprite> portraitFor;
        private Action<string> selected;
        private ScrollRect listScroll, detailScroll;
        private RectTransform rows, detailPage;
        private Text selectionHint, pageLabel;
        private Button previous, next;
        private int revision = -1, pageIndex;
        private string listKey, detailKey;
        public string SelectedProfileId { get; private set; }
        public bool IsVisible => gameObject.activeSelf;

        public void Initialize(KernelRuntime world, Font typeface, Func<string, Sprite> portrait, Action<string> onSelected, Action close)
        {
            runtime = world; font = typeface != null ? typeface : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            portraitFor = portrait; selected = onSelected;
            gameObject.AddComponent<Image>().color = new Color(0.82f, 0.79f, 0.69f, 0.99f);
            Label(transform, new Vector2(24, -18), new Vector2(930, 42), "인물 프로필 / 상담 기록", 28);
            Button(transform, new Vector2(1040, -18), new Vector2(96, 40), "닫기", close);
            listScroll = Scroll(transform, "ProfileList", new Vector2(24, -82), new Vector2(230, 518));
            rows = MonitorWidgets.Rect("AcquiredProfiles", listScroll.viewport, Vector2.zero, new Vector2(218, 518)); listScroll.content = rows;
            detailScroll = Scroll(transform, "ProfileDetails", new Vector2(280, -82), new Vector2(856, 468));
            selectionHint = Label(detailScroll.viewport, new Vector2(24, -30), new Vector2(800, 160), "왼쪽 목록에서 열람할 인물을 선택하세요.\n확보한 정보와 상담 일지가 이 파일에 기록됩니다.", 24);
            previous = Button(transform, new Vector2(280, -566), new Vector2(130, 38), "이전 일지", () => TurnPage(-1));
            next = Button(transform, new Vector2(1006, -566), new Vector2(130, 38), "다음 일지", () => TurnPage(1));
            pageLabel = Label(transform, new Vector2(440, -568), new Vector2(520, 36), "", 20); pageLabel.alignment = TextAnchor.MiddleCenter;
            previous.gameObject.SetActive(false); next.gameObject.SetActive(false);
            gameObject.SetActive(false);
        }
        public void Show()
        {
            if (runtime == null) return;
            SelectedProfileId = null; pageIndex = 0; detailKey = null; revision = -1;
            gameObject.SetActive(true); Refresh(); listScroll.verticalNormalizedPosition = 1;
        }
        public void Hide() => gameObject.SetActive(false);
        public bool SelectProfile(string id)
        {
            if (!IsVisible || runtime == null || !runtime.VisibleProfileIds.Contains(id)) return false;
            SelectedProfileId = id; pageIndex = 0; detailKey = null; selected?.Invoke(id); Refresh(); return true;
        }
        private void Update() { if (runtime != null && revision != runtime.Revision) Refresh(); }
        public void Refresh()
        {
            if (runtime == null || !IsVisible) return;
            string[] ids = runtime.VisibleProfileIds.OrderBy(id => id == "kernel" ? 0 : 1).ThenBy(ProfileLabel, StringComparer.Ordinal).ToArray();
            string key = string.Join("\n", ids.Select(id => id + ":" + ProfileLabel(id)));
            if (key != listKey)
            {
                listKey = key; float position = listScroll.verticalNormalizedPosition;
                for (int i = rows.childCount - 1; i >= 0; i--) Discard(rows.GetChild(i).gameObject);
                for (int i = 0; i < ids.Length; i++)
                {
                    string id = ids[i];
                    Button button = Button(rows, new Vector2(0, -i * 54), new Vector2(218, 46), ProfileLabel(id), () => SelectProfile(id));
                    button.gameObject.name = "Profile_" + id;
                }
                rows.sizeDelta = new Vector2(218, Mathf.Max(518, ids.Length * 54)); listScroll.verticalNormalizedPosition = position;
            }
            if (SelectedProfileId != null && !ids.Contains(SelectedProfileId)) { SelectedProfileId = null; detailKey = null; }
            if (SelectedProfileId == null)
            {
                ClearPage(); selectionHint.gameObject.SetActive(true); pageLabel.text = "";
                previous.gameObject.SetActive(false); next.gameObject.SetActive(false);
            }
            else RefreshDetail();
            revision = runtime.Revision;
        }
        private string ProfileLabel(string id)
        {
            PersonProfile card = runtime.ProfileCard(id);
            return id == "kernel" || string.IsNullOrEmpty(card.Name) || card.Name.StartsWith("[", StringComparison.Ordinal) ? card.DisplayName : card.Name;
        }
        private void RefreshDetail()
        {
            int count = Math.Max(1, runtime.NotesFor(SelectedProfileId).Count()); pageIndex = Mathf.Clamp(pageIndex, 0, count - 1);
            PersonProfile card = runtime.ProfileCard(SelectedProfileId); Sprite portrait = portraitFor?.Invoke(SelectedProfileId);
            string journal = runtime.ProfileJournalText(SelectedProfileId, pageIndex);
            string key = string.Join("\n", new[] { SelectedProfileId, card.Name, card.Gender, card.Age, card.Birthday, card.Occupation, card.Family, journal,
                pageIndex.ToString(), count.ToString(), portrait != null ? portrait.GetInstanceID().ToString() : "" });
            if (detailKey != key)
            {
                bool samePage = detailPage != null && detailKey != null; float position = samePage ? detailScroll.verticalNormalizedPosition : 1;
                ClearPage(); selectionHint.gameObject.SetActive(false);
                detailPage = ProfileFormView.CreatePage(detailScroll.viewport, font, card, portrait,
                    "상담 일지 " + (pageIndex + 1) + " / " + count, journal, 832);
                detailScroll.content = detailPage; detailScroll.verticalNormalizedPosition = position; detailKey = key;
            }
            previous.gameObject.SetActive(true); next.gameObject.SetActive(true);
            previous.interactable = pageIndex > 0; next.interactable = pageIndex + 1 < count;
            pageLabel.text = ProfileLabel(SelectedProfileId) + " · " + (pageIndex + 1) + " / " + count;
        }
        private void TurnPage(int direction)
        {
            if (!IsVisible || SelectedProfileId == null || !runtime.VisibleProfileIds.Contains(SelectedProfileId)) return;
            int count = Math.Max(1, runtime.NotesFor(SelectedProfileId).Count());
            pageIndex = Mathf.Clamp(pageIndex + direction, 0, count - 1); detailKey = null; Refresh();
        }
        private void ClearPage()
        {
            detailScroll.content = null;
            if (detailPage != null) { Discard(detailPage.gameObject); detailPage = null; }
        }
        private Text Label(Transform parent, Vector2 position, Vector2 size, string value, int fontSize)
        {
            Text label = MonitorWidgets.Rect("Label", parent, position, size).gameObject.AddComponent<Text>();
            label.font = font; label.fontSize = fontSize; label.text = value; label.color = Ink; label.supportRichText = false; label.raycastTarget = false; return label;
        }
        private Button Button(Transform parent, Vector2 position, Vector2 size, string value, Action action)
        {
            RectTransform rect = MonitorWidgets.Rect(value, parent, position, size);
            Image image = rect.gameObject.AddComponent<Image>(); image.color = new Color(0.69f, 0.66f, 0.56f);
            Button button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None }; button.onClick.AddListener(() => action?.Invoke());
            Label(rect, new Vector2(6, -4), size - new Vector2(12, 8), value, 20).alignment = TextAnchor.MiddleCenter; return button;
        }
        private ScrollRect Scroll(Transform parent, string name, Vector2 position, Vector2 size)
        {
            RectTransform viewport = MonitorWidgets.Rect(name, parent, position, size);
            viewport.gameObject.AddComponent<Image>().color = new Color(0.88f, 0.86f, 0.77f); viewport.gameObject.AddComponent<RectMask2D>();
            ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport;
            scroll.horizontal = false; scroll.vertical = true; scroll.scrollSensitivity = 32; scroll.movementType = ScrollRect.MovementType.Clamped; return scroll;
        }
        private static void Discard(GameObject value)
        { value.SetActive(false); if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
    }
}
