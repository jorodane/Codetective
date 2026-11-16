using System.Linq;
using Codetective.Operations;
using NUnit.Framework;

namespace Codetective.Tests.Operations
{
    public sealed class PaperInteractionStateTests
    {
        private static KernelRuntime World()
        {
            KernelRuntime runtime = ClinicScenario.Create();
            runtime.RegisterNews(new NewsArticle { id = "optional", headline = "지역 소식", body = "기록에서 찾을 단서", lastDay = 1,
                clues = new[] { new NewspaperClue { id = "funding", keyword = "연구 지원", fact = "paper.evidence",
                    onCollect = new StoryEffects { triggers = new[] { new TriggerValue { id = "evidence.funding", value = true } } } } } });
            return runtime;
        }
        [Test]
        public void ReadingThePaperDoesNotCollectOptionalCluesUntilClicked()
        {
            KernelRuntime r = World(); r.ReadNewspaper();
            Assert.That(r.Facts.Contains("paper.evidence"), Is.False);
            r.RecordNewspaperClue("optional", "funding");
            Assert.That(r.Facts.Contains("paper.evidence"), Is.True);
            Assert.That(r.GetTrigger("evidence.funding"), Is.True);
            r.SetTrigger("evidence.funding", false); r.RecordNewspaperClue("optional", "funding");
            Assert.That(r.GetTrigger("evidence.funding"), Is.False);
        }
        [Test]
        public void CollectedCluesSurviveReloadAndUnavailableArticlesCannotGrantEvidence()
        {
            KernelRuntime r = World(); r.RecordNewspaperClue("optional", "funding");
            KernelRuntime restored = World(); restored.Restore(r.Capture());
            restored.SetTrigger("evidence.funding", false); restored.RecordNewspaperClue("optional", "funding");
            Assert.That(restored.GetTrigger("evidence.funding"), Is.False);
            restored.Tick(540); restored.NextDay();
            Assert.That(() => restored.RecordNewspaperClue("optional", "funding"), Throws.TypeOf<CommandException>());
        }
        [Test]
        public void StoredItemsMoveBetweenDrawersAndRestoreWithoutSharingSnapshotObjects()
        {
            KernelRuntime r = World(); r.SetItemStorage("paper", "drawer1"); r.SetItemStorage("profile", "drawer2");
            r.SetItemStorage("paper", "drawer2"); Assert.That(r.StoredItems.Count, Is.EqualTo(2));
            KernelSnapshot saved = r.Capture(); KernelRuntime restored = World(); restored.Restore(saved);
            saved.storedItems[0].containerId = "modified";
            Assert.That(restored.StoredItems.All(s => s.containerId == "drawer2"), Is.True);
            restored.SetItemStorage("paper", null);
            Assert.That(restored.StoredItems.Single().itemId, Is.EqualTo("profile"));
            Assert.That(r.StoredItems.Count, Is.EqualTo(2));
        }
        [Test]
        public void LegacySavesHaveNoStoredItemsAndDuplicateRecordsAreRejected()
        {
            KernelRuntime r = World(); KernelSnapshot saved = r.Capture(); saved.storedItems = null;
            r.SetItemStorage("paper", "drawer"); r.Restore(saved); Assert.That(r.StoredItems, Is.Empty);
            saved.storedItems = new[] { new StoredItemState { itemId = "paper", containerId = "one" }, new StoredItemState { itemId = "paper", containerId = "two" } };
            Assert.That(() => r.Restore(saved), Throws.TypeOf<CommandException>());
        }
    }
}
