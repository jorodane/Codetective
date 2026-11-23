using System;
using System.Collections.Generic;
using System.Linq;

namespace Codetective.Operations
{
    [Serializable] public sealed class NewspaperClue
    {
        public string id;
        public string keyword;
        public string fact;
        public StoryEffects onCollect = new StoryEffects();
        public NewspaperClue Copy() => new NewspaperClue { id = id, keyword = keyword, fact = fact, onCollect = onCollect?.Copy() ?? new StoryEffects() };
    }
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
        public NewspaperClue[] clues = Array.Empty<NewspaperClue>();
        public NewsArticle Copy() => new NewsArticle { id = id, headline = headline, body = body, firstDay = firstDay, lastDay = lastDay,
            priority = priority, condition = condition?.Copy() ?? new StoryCondition(), readFacts = readFacts.ToArray(), onRead = onRead?.Copy() ?? new StoryEffects(),
            clues = (clues ?? Array.Empty<NewspaperClue>()).Select(c => c.Copy()).ToArray() };
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
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (NewspaperClue clue in article.clues ?? Array.Empty<NewspaperClue>())
            {
                if (clue == null || string.IsNullOrWhiteSpace(clue.id) || string.IsNullOrWhiteSpace(clue.keyword) || !ids.Add(clue.id))
                    throw new CommandException("신문 단서 ID와 표시 단어가 필요하며 기사 안에서 중복될 수 없습니다.");
                clue.onCollect?.Validate();
            }
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
        public void ReadNewspaper() => ReadNewspaper(Day);
        public void ReadNewspaper(int day)
        {
            if (day == Day) DeliverNewspaper();
            if (!editions.TryGetValue(day, out NewspaperEdition edition) || edition.read) return;
            foreach (string id in edition.articleIds.Where(news.ContainsKey))
            { NewsArticle article = news[id]; Facts.UnionWith(article.readFacts); ApplyStoryEffects(article.onRead); CollectNewsArticle(article); }
            edition.read = true;
            SetTrigger("newspaper.read", true);
            SetTrigger("newspaper.day." + day + ".read", true);
            Report("DAY " + day + " 신문 확인 — 새로운 데이터와 권한을 확인하세요."); NarrativeUpdated();
        }
        public void RecordNewspaperClue(string articleId, string clueId) => RecordNewspaperClue(Day, articleId, clueId);
        public void RecordNewspaperClue(int day, string articleId, string clueId)
        {
            if (day == Day) DeliverNewspaper();
            if (!editions.TryGetValue(day, out NewspaperEdition edition) || !edition.articleIds.Contains(articleId) ||
                !news.TryGetValue(articleId, out NewsArticle article))
                throw new CommandException("해당 날짜에 발행된 기사에서만 단서를 기록할 수 있습니다.");
            NewspaperClue clue = article.clues.FirstOrDefault(c => c.id == clueId) ?? throw new CommandException("기사의 단서를 찾을 수 없습니다.");
            string key = "newspaper." + articleId + "." + clueId;
            if (RecordedClues.Contains(key)) return;
            if (!string.IsNullOrWhiteSpace(clue.fact)) Facts.Add(clue.fact);
            ApplyStoryEffects(clue.onCollect); RecordedClues.Add(key); SetTrigger(key, true);
            CollectNewsClue(article, clue);
            Revision++; NarrativeUpdated();
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
