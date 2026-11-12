using System.Linq;
using Codetective.Operations;
using NUnit.Framework;

namespace Codetective.Tests.Operations
{
    public sealed class NewspaperTests
    {
        private static KernelRuntime World()
        {
            KernelRuntime r = ClinicScenario.Create();
            r.RegisterNews(new NewsArticle { id = "day1", headline = "정부 연구 지원 감축", firstDay = 1, lastDay = 1,
                readFacts = new[] { "funding_cut" }, onRead = new StoryEffects { triggers = new[] { new TriggerValue { id = "news.nsf_rehabilitation", value = true } } } });
            r.RegisterNews(new NewsArticle { id = "outcome", headline = "조사 이후의 변화", firstDay = 2, priority = 20,
                condition = new StoryCondition { quests = new[] { new QuestRequirement { id = "case", minimumStage = 100, status = QuestStatus.Completed } } } });
            r.DeliverNewspaper(); return r;
        }
        [Test]
        public void MorningDeliveryIsIdempotentAndReadingUnlocksARealConversationPermission()
        {
            KernelRuntime r = World(); Assert.That(r.TodayNewspaper.read, Is.False);
            Assert.That(r.Execute("client.askAboutFoundation()").Success, Is.False);
            r.DeliverNewspaper(); r.ReadNewspaper();
            Assert.That(r.TodayNewspaper.read, Is.True); Assert.That(r.Facts, Does.Contain("funding_cut"));
            Assert.That(r.Complete("client.askAbout"), Has.Count.EqualTo(1));
            Assert.That(r.Execute("client.askAboutFoundation()").Value.kind, Is.EqualTo(DataKind.Statement));
            r.SetTrigger("news.nsf_rehabilitation", false); r.ReadNewspaper();
            Assert.That(r.GetTrigger("news.nsf_rehabilitation"), Is.False); // Read effects run once, not every opening.
        }
        [Test]
        public void DailyEditionIsFrozenAndStoryOutcomesAppearTheFollowingMorning()
        {
            KernelRuntime r = World();
            r.SetQuestStage("case", 100, QuestStatus.Completed); r.DeliverNewspaper();
            Assert.That(r.NewspaperArticles(1).Select(a => a.id), Is.EqualTo(new[] { "day1" }));
            Assert.That(() => r.NextDay(), Throws.TypeOf<CommandException>());
            int started = 0; r.DayStarted += day => started = day;
            r.Tick(540); r.NextDay();
            Assert.That(r.Day, Is.EqualTo(2)); Assert.That(r.Minute, Is.EqualTo(540)); Assert.That(started, Is.EqualTo(2));
            Assert.That(r.TodayNewspaper.read, Is.False); Assert.That(r.NewspaperArticles(2).Single().id, Is.EqualTo("outcome"));
            r.SetQuestStage("case", 100, QuestStatus.Failed);
            Assert.That(r.NewspaperArticles(2).Single().id, Is.EqualTo("outcome"));
            r.Tick(540); r.NextDay(); Assert.That(r.NewspaperArticles(3), Is.Empty);
        }
        [Test]
        public void EditionsAndReadProgressSurviveReloadWithoutRefiringReadEffects()
        {
            KernelRuntime r = World(); r.ReadNewspaper(); r.SetTrigger("news.nsf_rehabilitation", false);
            KernelSnapshot saved = r.Capture(); KernelRuntime restored = World(); restored.Restore(saved);
            Assert.That(restored.TodayNewspaper.read, Is.True); restored.ReadNewspaper();
            Assert.That(restored.GetTrigger("news.nsf_rehabilitation"), Is.False);
            saved.newspapers[0].articleIds[0] = "mutated";
            Assert.That(restored.NewspaperArticles(1).Single().id, Is.EqualTo("day1"));
            NewspaperEdition copy = restored.TodayNewspaper; copy.read = false;
            Assert.That(restored.TodayNewspaper.read, Is.True);
        }
        [Test]
        public void OldSavesReceiveTheCurrentMorningEditionAndKeepTheirDay()
        {
            KernelRuntime r = World(); r.Tick(540); r.NextDay();
            KernelSnapshot saved = r.Capture(); saved.newspapers = null; saved.triggers = null; saved.quests = null;
            KernelRuntime restored = World(); restored.Restore(saved);
            Assert.That(restored.Day, Is.EqualTo(2)); Assert.That(restored.TodayNewspaper.day, Is.EqualTo(2));
            Assert.That(restored.TodayNewspaper.read, Is.False);
        }
        [Test]
        public void PrototypeTestsHaveIndependentEditionAndReadEffects()
        {
            KernelRuntime r = World(); r.Entity("hq").Operations.Add(new Operation { Code = "readPaper", ReturnKind = DataKind.Text,
                Invoke = c => { c.Runtime.ReadNewspaper(); return Datum.Text("read"); } });
            Assert.That(r.TestProtocol("protocol read() { return hq.readPaper(); }", "read()").Success, Is.True);
            Assert.That(r.TodayNewspaper.read, Is.False); Assert.That(r.GetTrigger("news.nsf_rehabilitation"), Is.False);
        }
    }
}
