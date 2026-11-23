using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Codetective.Interaction;
using Codetective.Operations;
using UnityEngine;

namespace Codetective.Gameplay
{
    /// <summary>Delivers a physical copy of each edition and keeps copies stored by the player.</summary>
    public sealed class DeskNewspaper : MonoBehaviour
    {
        private const string LegacyItemId = "desk.newspaper";
        private const string EditionIdPrefix = LegacyItemId + ".day.";
        private readonly Dictionary<int, PaperDocument> copies = new Dictionary<int, PaperDocument>();
        private Vector3 deliveryPosition;
        private Quaternion deliveryRotation;
        private ItemPlacementSurface desk;
        private Font font;
        private ItemCarryController carry;
        private NewspaperArticleAsset[] layouts;

        public HoldableItem Item => Document != null ? Document.Item : null;
        public PaperDocument Document { get; private set; }
        public event Action<PaperDocument, int> AreaClicked;

        public void Initialize(Transform anchor, ItemPlacementSurface desktop, Font typeface, ItemCarryController controller, NewspaperArticleAsset[] articles = null)
        {
            deliveryPosition = anchor != null ? anchor.position : new Vector3(-0.15f, 0.9f, -2.57f);
            deliveryRotation = anchor != null ? anchor.rotation : Quaternion.identity;
            desk = desktop; font = typeface; carry = controller;
            layouts = articles ?? Array.Empty<NewspaperArticleAsset>();
        }

        public void Deliver(KernelRuntime runtime)
        {
            runtime.DeliverNewspaper();
            Document = GetEdition(runtime, runtime.Day);
        }

        public HoldableItem ItemForStorage(KernelRuntime runtime, string itemId)
        {
            if (itemId == LegacyItemId) return Item;
            int day = DayForItem(itemId);
            return day > 0 && day <= runtime.Day ? GetEdition(runtime, day).Item : null;
        }

        public void Read(KernelRuntime runtime, HoldableItem item)
        {
            int day = DayForItem(item.Id);
            if (day > 0) runtime.ReadNewspaper(day);
        }

        public void Discard(HoldableItem item)
        {
            int day = DayForItem(item.Id);
            if (!copies.TryGetValue(day, out PaperDocument copy)) return;
            if (copy == Document) Document = null;
            copies.Remove(day);
            copy.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(copy.gameObject); else DestroyImmediate(copy.gameObject);
        }

        private PaperDocument GetEdition(KernelRuntime runtime, int day)
        {
            if (copies.TryGetValue(day, out PaperDocument copy)) return copy;
            var paper = new GameObject("DailyNewspaper_" + day);
            paper.transform.SetParent(transform, false);
            paper.transform.SetPositionAndRotation(deliveryPosition, deliveryRotation);
            HoldableItem item = paper.AddComponent<HoldableItem>();
            item.Configure(EditionIdPrefix + day.ToString(CultureInfo.InvariantCulture), $"신문 · Day {day:00}", HeldItemUse.Newspaper);
            copy = paper.AddComponent<PaperDocument>();
            copy.Initialize(item, carry, carry.ViewCamera, font, new Vector2(0.44f, 0.28f));
            if (desk != null && (desk.TryGetPosition(item, deliveryPosition, out Vector3 slot, false) ||
                desk.TryGetPosition(item, out slot, false)))
                paper.transform.SetPositionAndRotation(desk.transform.TransformPoint(slot), desk.transform.rotation);
            carry.Register(item);
            copy.AreaClicked += (document, index) => AreaClicked?.Invoke(document, index);
            PaperSection[] sections = runtime.NewspaperArticles(day).Select(article => new PaperSection
            {
                title = article.headline, text = article.body,
                layout = layouts.FirstOrDefault(asset => asset != null && asset.article.id == article.id)?.layout,
                clues = article.clues.Select(clue => new PaperClue { label = clue.keyword,
                    collect = () => runtime.RecordNewspaperClue(day, article.id, clue.id) }).ToArray()
            }).ToArray();
            if (sections.Length == 0) sections = new[] { new PaperSection { title = "조간 · 사회 / 지역", text = "오늘의 추가 보도는 없습니다." } };
            copy.SetContent("CODETECTIVE DAILY / DAY " + day.ToString("D2"), sections);
            copies.Add(day, copy);
            return copy;
        }

        private static int DayForItem(string itemId)
        {
            if (itemId != null && itemId.StartsWith(EditionIdPrefix, StringComparison.Ordinal) &&
                int.TryParse(itemId.Substring(EditionIdPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int day))
                return day;
            return 0;
        }

        private void OnDestroy()
        {
            foreach (PaperDocument copy in copies.Values)
                if (copy != null)
                {
                    if (Application.isPlaying) Destroy(copy.gameObject); else DestroyImmediate(copy.gameObject);
                }
        }
    }
}
