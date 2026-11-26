using System;
using System.Collections.Generic;
using Codetective.Interaction;
using Codetective.Operations;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Codetective.Gameplay
{
    public sealed class PaperClue
    {
        public string label;
        public Action collect;
    }
    public sealed class PaperSection
    {
        public string title;
        public string text;
        public string rubric;
        public string printSummary;
        public PaperLayout layout;
        public PersonProfile profile;
        public Sprite portrait;
        public IReadOnlyList<PaperClue> clues = Array.Empty<PaperClue>();
    }

    /// <summary>Readable ink stays on the physical sheet. Printed regions open an interactive magnification.</summary>
    public sealed class PaperDocument : MonoBehaviour
    {
        public HoldableItem Item { get; private set; }
        public string Title { get; private set; }
        public IReadOnlyList<PaperSection> Sections { get; private set; } = Array.Empty<PaperSection>();
        public event Action<PaperDocument, int> AreaClicked;
        public event Action<int> PageChanged;
        public int PageIndex { get; private set; }
        public int ContentRevision { get; private set; }
        public Font Typeface => font;
        private bool paginated;
        private string issueLine;
        private ItemCarryController carry;
        private Font font;
        private RectTransform ink;
        private Text title;
        private GameObject sheet, roll;
        private Material stock;
        private readonly List<GameObject> regions = new List<GameObject>();
        private Vector2 pageSize;
        private string contentKey;
        private CanvasGroup inkInteraction;
        private GraphicRaycaster inkRaycaster;
        private readonly List<RaycastResult> pointerHits = new List<RaycastResult>();
        private Button previousPage, nextPage;
        private Text pageLabel;

        public void Initialize(HoldableItem item, ItemCarryController controller, Camera camera, Font typeface, Vector2 size)
        {
            Item = item; carry = controller; font = typeface != null ? typeface : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            pageSize = size / 0.0004f; Item.ConfigurePaper(new Vector3(size.x, 0.004f, size.y));
            stock = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")); stock.color = new Color(0.83f, 0.80f, 0.69f);
            sheet = GameObject.CreatePrimitive(PrimitiveType.Cube); sheet.name = "PaperSheet"; sheet.transform.SetParent(transform, false);
            sheet.transform.localScale = new Vector3(size.x, 0.004f, size.y); sheet.GetComponent<Renderer>().sharedMaterial = stock;
            if (Item.Use == HeldItemUse.Newspaper)
            {
                Vector3 rollSize = HoldableItem.NewspaperRollSize;
                roll = GameObject.CreatePrimitive(PrimitiveType.Cylinder); roll.name = "RolledNewspaper"; roll.transform.SetParent(transform, false);
                roll.transform.localRotation = Quaternion.Euler(0, 0, 90);
                roll.transform.localScale = new Vector3(rollSize.y, rollSize.x * 0.5f, rollSize.z);
                roll.GetComponent<Renderer>().sharedMaterial = stock;
                Item.SetPlacementVisual(roll.transform);
            }
            ink = (RectTransform)new GameObject("PrintedDocument", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(CanvasGroup)).transform;
            ink.SetParent(transform, false); ink.localPosition = new Vector3(0, 0.0025f, 0); ink.localRotation = Quaternion.Euler(90, 0, 0);
            ink.sizeDelta = pageSize; ink.localScale = Vector3.one * 0.0004f;
            Canvas canvas = ink.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
            inkInteraction = ink.GetComponent<CanvasGroup>();
            inkRaycaster = ink.GetComponent<GraphicRaycaster>();
            title = OverlayWidgets.Label(ink, font, new Vector2(26, -20), new Vector2(pageSize.x - 52, 100), "", 36);
            title.color = new Color(0.1f, 0.08f, 0.06f);
            if (item.Use == HeldItemUse.Profile || item.Use == HeldItemUse.Newspaper)
            {
                previousPage = PageCorner(new Vector2(26, -pageSize.y + 66), "‹", () => TurnPage(-1));
                nextPage = PageCorner(new Vector2(pageSize.x - 90, -pageSize.y + 66), "›", () => TurnPage(1));
                pageLabel = OverlayWidgets.Label(ink, font, new Vector2(pageSize.x * 0.5f - 90, -pageSize.y + 60), new Vector2(180, 44), "", 22);
                pageLabel.alignment = TextAnchor.MiddleCenter; pageLabel.color = title.color;
                previousPage.gameObject.SetActive(false); nextPage.gameObject.SetActive(false);
            }
            Item.PoseChanged += RefreshShape; RefreshShape();
        }
        public void SetContent(string heading, IReadOnlyList<PaperSection> sections)
            => SetContentCore(heading, sections, false, 0);
        public void SetPagedContent(string heading, IReadOnlyList<PaperSection> pages, int pageIndex)
            => SetContentCore(heading, pages, true, pageIndex);
        public void SetNewspaperContent(string masthead, string issue, IReadOnlyList<PaperSection> articles)
            => SetContentCore(masthead, articles, false, 0, issue ?? "");

        private int PageCount(int sectionCount) => issueLine != null ? (sectionCount + NewspaperSpreadView.ArticlesPerSpread - 1) / NewspaperSpreadView.ArticlesPerSpread : sectionCount;

        public void SetPageIndex(int index)
        {
            int next = Mathf.Clamp(index, 0, Mathf.Max(0, PageCount(Sections.Count) - 1));
            if ((!paginated && issueLine == null) || PageIndex == next) return;
            SetContentCore(Title, Sections, paginated, next, issueLine); PageChanged?.Invoke(PageIndex);
        }
        private void SetContentCore(string heading, IReadOnlyList<PaperSection> sections, bool pageMode, int index, string issue = null)
        {
            paginated = pageMode; issueLine = issue;
            int pages = PageCount(sections.Count);
            PageIndex = Mathf.Clamp(index, 0, Mathf.Max(0, pages - 1));
            string key = heading + "|" + paginated + "|" + PageIndex + "|" + issue;
            foreach (PaperSection section in sections)
            {
                key += "\n" + section.title + "\n" + section.text + "|" + (section.portrait != null ? section.portrait.GetInstanceID() : 0);
                if (issue != null) key += "|" + section.rubric + "|" + section.printSummary;
                if (section.profile != null) key += "|" + section.profile.Name + "|" + section.profile.Gender + "|" + section.profile.Age + "|" + section.profile.Birthday + "|" + section.profile.Occupation + "|" + section.profile.Family;
            }
            Title = heading; Sections = sections;
            if (contentKey == key) return; contentKey = key; ContentRevision++;
            title.text = heading;
            title.gameObject.SetActive(issue == null);
            bool pageTurns = (paginated || issue != null) && pages > 1;
            if (previousPage != null)
            {
                previousPage.gameObject.SetActive(pageTurns); nextPage.gameObject.SetActive(pageTurns);
                previousPage.interactable = PageIndex > 0; nextPage.interactable = PageIndex + 1 < pages;
                pageLabel.text = pageTurns ? (PageIndex + 1) + " / " + pages : "";
            }
            foreach (GameObject old in regions) { old.SetActive(false); Discard(old); } regions.Clear();
            if (issue != null)
            {
                regions.Add(NewspaperSpreadView.Create(ink, font, pageSize, heading, issue, sections,
                    PageIndex * NewspaperSpreadView.ArticlesPerSpread, pageTurns, sectionIndex => AreaClicked?.Invoke(this, sectionIndex)));
                return;
            }
            int count = paginated ? Mathf.Min(1, sections.Count) : sections.Count;
            float height = (pageSize.y - (paginated ? 215 : 150)) / Mathf.Max(1, count);
            for (int i = 0; i < count; i++)
            {
                int sectionIndex = paginated ? PageIndex : i; PaperSection section = sections[sectionIndex];
                Button area = OverlayWidgets.Button(ink, font, new Vector2(26, -124 - i * height), new Vector2(pageSize.x - 52, height - 10), "", () => AreaClicked?.Invoke(this, sectionIndex));
                area.name = "PrintedArea_" + i; area.image.color = Color.clear; area.transition = Selectable.Transition.None;
                area.navigation = new Navigation { mode = Navigation.Mode.None };
                RectTransform content = PaperContentView.Create(area.transform, font, section, 900, false);
                float scale = Mathf.Min((pageSize.x - 72) / content.sizeDelta.x, (height - 24) / content.sizeDelta.y);
                content.localScale = Vector3.one * scale; content.anchoredPosition = new Vector2(10, -6);
                regions.Add(area.gameObject);
            }
        }
        private Button PageCorner(Vector2 position, string text, Action action)
        {
            Button button = OverlayWidgets.Button(ink, font, position, new Vector2(64, 44), text, action);
            button.image.color = Color.clear; button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None }; button.GetComponentInChildren<Text>().color = title.color;
            return button;
        }
        private void TurnPage(int direction)
        {
            if (!carry.CanManipulate) return;
            if (!Item.IsHeld) { carry.TryPickup(Item); return; }
            SetPageIndex(PageIndex + direction);
        }
        public void RefreshShape()
        {
            bool rolled = Item.IsRolled;
            sheet.SetActive(!rolled); ink.gameObject.SetActive(!rolled);
            if (roll != null) roll.SetActive(rolled);
        }
        private void Update()
        {
            if (Item == null || inkInteraction == null) return;
            bool accessible = carry.CanManipulate && (carry.HeldItem == null || Item.IsHeld && carry.IsExamining) && (!Item.IsStored || Item.Storage.CanRetrieve);
            inkInteraction.interactable = inkInteraction.blocksRaycasts = accessible;
        }
        internal Button PrintedButtonAt(PointerEventData pointer)
        {
            if (inkRaycaster == null || !inkRaycaster.isActiveAndEnabled || !inkInteraction.blocksRaycasts) return null;
            pointerHits.Clear();
            inkRaycaster.Raycast(pointer, pointerHits);
            foreach (RaycastResult hit in pointerHits)
                if (hit.gameObject.TryGetComponent(out Button button) && button.IsActive() && button.IsInteractable()) return button;
            return null;
        }
        private void OnDestroy()
        {
            if (Item != null) Item.PoseChanged -= RefreshShape;
            Discard(stock);
        }
        private static void Discard(UnityEngine.Object value)
        { if (value == null) return; if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
    }
}
