using Codetective.Operations;
using UnityEngine;

namespace Codetective.Gameplay
{
    [CreateAssetMenu(menuName = "Codetective/Story/Newspaper Article", fileName = "NewspaperArticle")]
    public sealed class NewspaperArticleAsset : ScriptableObject { public NewsArticle article = new NewsArticle(); }
}
