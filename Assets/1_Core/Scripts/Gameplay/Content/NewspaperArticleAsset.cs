using System;
using Codetective.Operations;
using UnityEngine;

namespace Codetective.Gameplay
{
    [CreateAssetMenu(menuName = "Codetective/Story/Newspaper Article", fileName = "NewspaperArticle")]
    public sealed class NewspaperArticleAsset : ScriptableObject
    {
        public NewsArticle article = new NewsArticle();
        public PaperLayout layout = new PaperLayout();
    }

    [Serializable] public sealed class PaperLayout
    {
        [Min(1)] public float width = 900;
        [Min(1)] public float height = 1100;
        [Tooltip("Empty uses the full headline and body. The same layout is printed and magnified.")]
        public PaperLayoutBlock[] blocks = Array.Empty<PaperLayoutBlock>();
    }
    public enum PaperBlockKind { Headline, Body, Text, Image }
    [Serializable] public sealed class PaperLayoutBlock
    {
        public PaperBlockKind kind;
        [Tooltip("Position from the top-left of the page, in layout units.")]
        public Vector2 position;
        public Vector2 size = new Vector2(400, 300);
        [Min(1)] public int fontSize = 24;
        public string text;
        public Sprite image;
        [Tooltip("Optional clue keyword for an image hotspot.")]
        public string clueKeyword;
    }
}
