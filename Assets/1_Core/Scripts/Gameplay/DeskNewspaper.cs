using System.Linq;
using Codetective.Interaction;
using Codetective.Operations;
using UnityEngine;

namespace Codetective.Gameplay
{
    /// <summary>Physical morning delivery; the reader uses the edition frozen by KernelRuntime.</summary>
    public sealed class DeskNewspaper : MonoBehaviour
    {
        private GameObject paper;
        private NewspaperArticleAsset[] layouts;
        public HoldableItem Item { get; private set; }
        public PaperDocument Document { get; private set; }
        public void Initialize(Transform anchor, ItemPlacementSurface desk, Font font, ItemCarryController carry, NewspaperArticleAsset[] articles = null)
        {
            layouts = articles ?? System.Array.Empty<NewspaperArticleAsset>();
            paper = new GameObject("DailyNewspaper"); paper.transform.SetParent(transform, false);
            Vector3 position = anchor != null ? anchor.position : new Vector3(-0.15f, 0.9f, -2.57f);
            paper.transform.SetPositionAndRotation(position, anchor != null ? anchor.rotation : Quaternion.identity);
            Item = paper.AddComponent<HoldableItem>();
            Item.Configure("desk.newspaper", "오늘의 신문", HeldItemUse.Newspaper);
            Document = paper.AddComponent<PaperDocument>();
            Document.Initialize(Item, carry, carry.ViewCamera, font, new Vector2(0.44f, 0.28f));
            if (desk != null && desk.TryGetPosition(Item, position, out Vector3 slot, false))
                paper.transform.SetPositionAndRotation(desk.transform.TransformPoint(slot), desk.transform.rotation);
            carry.Register(Item);
        }
        public void Refresh(KernelRuntime runtime)
        {
            runtime.DeliverNewspaper();
            PaperSection[] sections = runtime.NewspaperArticles(runtime.Day).Select(article => new PaperSection
            {
                title = article.headline, text = article.body,
                layout = layouts.FirstOrDefault(asset => asset != null && asset.article.id == article.id)?.layout,
                clues = article.clues.Select(clue => new PaperClue { label = clue.keyword,
                    collect = () => runtime.RecordNewspaperClue(article.id, clue.id) }).ToArray()
            }).ToArray();
            if (sections.Length == 0) sections = new[] { new PaperSection { title = "조간 · 사회 / 지역", text = "오늘의 추가 보도는 없습니다." } };
            Document.SetContent("CODETECTIVE DAILY / DAY " + runtime.Day.ToString("D2"), sections);
            paper.SetActive(true);
        }
        private void OnDestroy() { if (paper != null) Destroy(paper); }
    }
}
