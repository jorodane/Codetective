using System;
using System.Collections.Generic;
using Codetective.Interaction;
using UnityEngine;
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
        public IReadOnlyList<PaperClue> clues = Array.Empty<PaperClue>();
    }

    /// <summary>Readable ink stays on the physical sheet. Printed regions open an interactive magnification.</summary>
    public sealed class PaperDocument : MonoBehaviour
    {
        public HoldableItem Item { get; private set; }
        public string Title { get; private set; }
        public IReadOnlyList<PaperSection> Sections { get; private set; } = Array.Empty<PaperSection>();
        public event Action<PaperDocument, int> AreaClicked;
        public event Action<int> ChangeProfile;
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

        public void Initialize(HoldableItem item, ItemCarryController controller, Camera camera, Font typeface, Vector2 size)
        {
            Item = item; carry = controller; font = typeface != null ? typeface : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            pageSize = size / 0.0004f; Item.ConfigurePaper(new Vector3(size.x, 0.004f, size.y));
            stock = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")); stock.color = new Color(0.83f, 0.80f, 0.69f);
            sheet = GameObject.CreatePrimitive(PrimitiveType.Cube); sheet.name = "PaperSheet"; sheet.transform.SetParent(transform, false);
            sheet.transform.localScale = new Vector3(size.x, 0.004f, size.y); sheet.GetComponent<Renderer>().sharedMaterial = stock;
            roll = GameObject.CreatePrimitive(PrimitiveType.Cylinder); roll.name = "RolledNewspaper"; roll.transform.SetParent(transform, false);
            roll.transform.localRotation = Quaternion.Euler(0, 0, 90); roll.transform.localScale = new Vector3(0.052f, 0.14f, 0.052f);
            roll.GetComponent<Renderer>().sharedMaterial = stock; roll.SetActive(false);
            ink = (RectTransform)new GameObject("PrintedDocument", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(CanvasGroup)).transform;
            ink.SetParent(transform, false); ink.localPosition = new Vector3(0, 0.0025f, 0); ink.localRotation = Quaternion.Euler(90, 0, 0);
            ink.sizeDelta = pageSize; ink.localScale = Vector3.one * 0.0004f;
            Canvas canvas = ink.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
            inkInteraction = ink.GetComponent<CanvasGroup>();
            title = OverlayWidgets.Label(ink, font, new Vector2(26, -20), new Vector2(pageSize.x - 52, 100), "", 36);
            title.color = new Color(0.1f, 0.08f, 0.06f);
            var pickup = OverlayWidgets.Button(ink, font, new Vector2(24, -pageSize.y + 62), new Vector2(180, 42), "들기 / 놓기", () =>
            { if (!carry.CanManipulate) return; if (Item.IsHeld) carry.PutDown(); else carry.TryPickup(Item); });
            pickup.image.color = new Color(0.64f, 0.61f, 0.51f);
            if (Item.Use == HeldItemUse.Profile)
            {
                OverlayWidgets.Button(ink, font, new Vector2(pageSize.x - 290, -pageSize.y + 62), new Vector2(122, 42), "이전 인물", () => ChangeProfile?.Invoke(-1));
                OverlayWidgets.Button(ink, font, new Vector2(pageSize.x - 158, -pageSize.y + 62), new Vector2(132, 42), "다음 인물", () => ChangeProfile?.Invoke(1));
            }
            Item.PoseChanged += RefreshShape; RefreshShape();
        }
        public void SetContent(string heading, IReadOnlyList<PaperSection> sections)
        {
            string key = heading;
            foreach (PaperSection section in sections) key += "\n" + section.title + "\n" + section.text;
            Title = heading; Sections = sections;
            if (contentKey == key) return; contentKey = key;
            title.text = heading;
            foreach (GameObject old in regions) { old.SetActive(false); Discard(old); } regions.Clear();
            int count = Mathf.Min(4, sections.Count);
            float height = (pageSize.y - 200) / Mathf.Max(1, count);
            for (int i = 0; i < count; i++)
            {
                int index = i; PaperSection section = sections[i];
                Button area = OverlayWidgets.Button(ink, font, new Vector2(26, -124 - i * height), new Vector2(pageSize.x - 52, height - 10), "", () => AreaClicked?.Invoke(this, index));
                area.name = "PrintedArea_" + i; area.image.color = new Color(0.3f, 0.25f, 0.18f, 0.035f);
                area.navigation = new Navigation { mode = Navigation.Mode.None };
                Text preview = area.GetComponentInChildren<Text>(); preview.alignment = TextAnchor.UpperLeft; preview.fontSize = 24;
                preview.color = new Color(0.1f, 0.08f, 0.06f); preview.verticalOverflow = VerticalWrapMode.Truncate;
                preview.text = section.title + "\n" + section.text;
                regions.Add(area.gameObject);
            }
        }
        public void RefreshShape()
        {
            bool rolled = Item.Use == HeldItemUse.Newspaper && Item.IsStored;
            sheet.SetActive(!rolled); ink.gameObject.SetActive(!rolled); roll.SetActive(rolled);
        }
        private void Update()
        {
            if (Item == null || inkInteraction == null) return;
            bool accessible = carry.CanManipulate && !(Item.IsHeld && carry.IsPlacing) && (!Item.IsStored || Item.Storage.CanRetrieve);
            inkInteraction.interactable = inkInteraction.blocksRaycasts = accessible;
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
