using System;
using System.Collections.Generic;
using System.Linq;

namespace Codetective.Operations
{
    [Serializable] public sealed class NewsArticle
    {
        public string id;
        public string headline;
        public string body;
        public int firstDay = 1;
        public int lastDay = int.MaxValue;
        public int priority;
        public StoryCondition condition = new StoryCondition();
        public string[] readFacts = Array.Empty<string>();
        public StoryEffects onRead = new StoryEffects();
        public NewsArticle Copy() => new NewsArticle { id = id, headline = headline, body = body, firstDay = firstDay, lastDay = lastDay,
            priority = priority, condition = condition?.Copy() ?? new StoryCondition(), readFacts = readFacts.ToArray(), onRead = onRead?.Copy() ?? new StoryEffects() };
    }
    [Serializable] public sealed class NewspaperEdition
    {
        public int day;
        public string[] articleIds = Array.Empty<string>();
        public bool read;
        public NewspaperEdition Copy() => new NewspaperEdition { day = day, articleIds = articleIds.ToArray(), read = read };
    }

    public sealed partial class KernelRuntime
    {
        private readonly Dictionary<string, NewsArticle> news = new Dictionary<string, NewsArticle>(StringComparer.Ordinal);
        private readonly Dictionary<int, NewspaperEdition> editions = new Dictionary<int, NewspaperEdition>();
        public NewspaperEdition TodayNewspaper => editions.TryGetValue(Day, out NewspaperEdition edition) ? edition.Copy() : null;
        public void RegisterNews(NewsArticle article)
        {
            if (article == null || string.IsNullOrWhiteSpace(article.id) || article.firstDay < 1 || article.lastDay < article.firstDay || news.ContainsKey(article.id))
                throw new CommandException("신문 기사 ID/발행일이 유효하지 않거나 중복됩니다.");
            article.onRead?.Validate(); news.Add(article.id, article.Copy());
        }
        /// <summary>Freeze the morning edition. Later quest changes affect tomorrow's newspaper.</summary>
        public void DeliverNewspaper()
        {
            if (editions.ContainsKey(Day)) return;
            editions.Add(Day, new NewspaperEdition { day = Day, articleIds = news.Values
                .Where(a => a.firstDay <= Day && a.lastDay >= Day && a.condition.Matches(this))
                .OrderByDescending(a => a.priority).ThenBy(a => a.id, StringComparer.Ordinal).Select(a => a.id).ToArray() });
            Revision++; NarrativeUpdated();
        }
        public IReadOnlyList<NewsArticle> NewspaperArticles(int day) => editions.TryGetValue(day, out NewspaperEdition edition) ?
            edition.articleIds.Where(news.ContainsKey).Select(id => news[id].Copy()).ToArray() : Array.Empty<NewsArticle>();
        public void ReadNewspaper()
        {
            DeliverNewspaper();
            NewspaperEdition edition = editions[Day];
            if (edition.read) return;
            foreach (string id in edition.articleIds.Where(news.ContainsKey))
            { NewsArticle article = news[id]; Facts.UnionWith(article.readFacts); ApplyStoryEffects(article.onRead); }
            edition.read = true;
            SetTrigger("newspaper.read", true);
            SetTrigger("newspaper.day." + Day + ".read", true);
            Report("DAY " + Day + " 신문 확인 — 새로운 데이터와 권한을 확인하세요."); NarrativeUpdated();
        }
        private void RestoreNewspapers(NewspaperEdition[] saved)
        {
            editions.Clear();
            foreach (NewspaperEdition edition in saved ?? Array.Empty<NewspaperEdition>())
            {
                if (edition.day < 1 || edition.day > Day || edition.articleIds == null) throw new CommandException("저장된 신문 발행일이 유효하지 않습니다.");
                editions.Add(edition.day, edition.Copy());
            }
            DeliverNewspaper();
        }
    }
}
