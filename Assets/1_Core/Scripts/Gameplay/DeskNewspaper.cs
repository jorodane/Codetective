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
        public HoldableItem Item { get; private set; }
        public PaperDocument Document { get; private set; }
        public void Initialize(Transform anchor, Transform desk, Font font, ItemCarryController carry)
        {
            paper = new GameObject("DailyNewspaper"); paper.transform.SetParent(transform, false);
            Vector3 position = anchor != null ? anchor.position : new Vector3(-0.15f, 0.91f, -2.66f);
            if (desk != null)
            {
                Renderer[] surfaces = desk.GetComponentsInChildren<Renderer>();
                if (surfaces.Length > 0) position.y = Mathf.Max(position.y, surfaces.Max(r => r.bounds.max.y) + 0.005f);
            }
            paper.transform.SetPositionAndRotation(position, anchor != null ? anchor.rotation : Quaternion.identity);
            Item = paper.AddComponent<HoldableItem>();
            Item.Configure("desk.newspaper", "오늘의 신문", HeldItemUse.Newspaper);
            Document = paper.AddComponent<PaperDocument>();
            Document.Initialize(Item, carry, Camera.main, font, new Vector2(0.44f, 0.28f)); carry.Register(Item);
        }
        public void Refresh(KernelRuntime runtime)
        {
            runtime.DeliverNewspaper();
            PaperSection[] sections = runtime.NewspaperArticles(runtime.Day).Select(article => new PaperSection
            {
                title = article.headline, text = article.body,
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
