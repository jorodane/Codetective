using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Gameplay
{
    internal static class NewspaperSpreadView
    {
        public const int ArticlesPerSpread = 4;
        private static readonly Color Ink = new Color(0.13f, 0.098f, 0.063f);
        private static readonly Color RuleColor = new Color(0.28f, 0.23f, 0.16f, 0.7f);

        public static GameObject Create(RectTransform parent, Font font, Vector2 size, string masthead, string issueLine,
            IReadOnlyList<PaperSection> sections, int firstArticle, bool hasPageTurns, Action<int> select)
        {
            const float margin = 30, gutter = 32, top = 154, rowGap = 17;
            RectTransform spread = OverlayWidgets.Rect("NewspaperSpread", parent, Vector2.zero, size);
            float printWidth = size.x - margin * 2;
            Rule(spread, "MastheadTopRule", margin, 23, printWidth, 1.5f, RuleColor);
            Text name = Type(spread, font, margin, 29, printWidth, 75, masthead, 61);
            name.alignment = TextAnchor.MiddleCenter;
            name.fontStyle = FontStyle.Bold;
            Text date = Type(spread, font, margin, 105, printWidth, 23, issueLine, 18);
            date.alignment = TextAnchor.MiddleRight;
            Type(spread, font, margin, 105, printWidth * 0.4f, 23, "조간", 18);
            Rule(spread, "MastheadBottomRule", margin, 134, printWidth, 3, RuleColor);
            Rule(spread, "MastheadHairline", margin, 141, printWidth, 1, RuleColor);

            float footer = hasPageTurns ? 71 : 30;
            float printHeight = size.y - top - footer;
            float leadHeight = (printHeight - rowGap) * 0.57f;
            float pageWidth = (printWidth - gutter) * 0.5f;
            float fold = size.x * 0.5f;
            Rule(spread, "CenterFoldShade", fold - 4, 149, 8, printHeight + 5, new Color(0.27f, 0.21f, 0.13f, 0.18f));
            Rule(spread, "CenterFold", fold - 0.5f, 149, 1, printHeight + 5, new Color(0.27f, 0.21f, 0.13f, 0.44f));

            int count = Mathf.Min(ArticlesPerSpread, sections.Count - firstArticle);
            for (int i = 0; i < count; i++)
            {
                int index = firstArticle + i;
                bool lead = i < 2;
                float x = margin + i % 2 * (pageWidth + gutter);
                float y = lead ? top : top + leadHeight + rowGap;
                float height = lead ? leadHeight : printHeight - leadHeight - rowGap;
                Article(spread, font, sections[index], new Rect(x, y, pageWidth, height), index, lead, select);
            }
            return spread.gameObject;
        }

        private static void Article(Transform parent, Font font, PaperSection article, Rect area, int index, bool lead, Action<int> select)
        {
            Button button = OverlayWidgets.Button(parent, font, new Vector2(area.x, -area.y), area.size, "", () => select(index));
            button.name = "PrintedArea_" + index;
            button.image.color = Color.clear;
            button.image.canvasRenderer.cullTransparentMesh = false;
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.GetComponentInChildren<Text>().enabled = false;
            RectTransform articleRoot = (RectTransform)button.transform;
            const float inset = 6;
            float width = area.width - inset * 2;
            Rule(articleRoot, "ArticleRule", inset, 0, width, 1, RuleColor);
            Text sectionLabel = Type(articleRoot, font, inset, 8, width, 25, article.rubric, 19);
            sectionLabel.color = new Color(0.31f, 0.19f, 0.11f);
            sectionLabel.fontStyle = FontStyle.Bold;

            float headingHeight = lead ? 91 : 55;
            Text title = Type(articleRoot, font, inset, 36, width, headingHeight, article.title, lead ? 38 : 28);
            title.fontStyle = FontStyle.Bold;
            Fit(title, lead ? 27 : 21);
            float bodyTop = 42 + headingHeight;
            float bodyHeight = area.height - bodyTop - 4;
            float columnWidth = (width - 18) * 0.5f;
            string copy = string.IsNullOrWhiteSpace(article.printSummary) ? article.text : article.printSummary;
            Sprite photo = article.layout?.blocks?.FirstOrDefault(block => block != null && block.kind == PaperBlockKind.Image && block.image != null)?.image;
            string right;
            if (photo != null)
            {
                Image photoView = OverlayWidgets.Rect("PrintedPhoto", articleRoot, new Vector2(inset, -bodyTop),
                    new Vector2(columnWidth, bodyHeight)).gameObject.AddComponent<Image>();
                photoView.sprite = photo; photoView.preserveAspect = true; photoView.raycastTarget = false;
                right = Excerpt(copy, lead ? 140 : 90);
            }
            else
            {
                (string left, string second) = SplitColumns(Excerpt(copy, lead ? 270 : 160));
                Fit(Type(articleRoot, font, inset, bodyTop, columnWidth, bodyHeight, left, lead ? 22 : 20), 17);
                right = second;
            }
            Fit(Type(articleRoot, font, inset + columnWidth + 18, bodyTop, columnWidth, bodyHeight, right, lead ? 22 : 20), 17);
            Rule(articleRoot, "ColumnRule", inset + columnWidth + 8.5f, bodyTop, 1, bodyHeight - 4, new Color(0.28f, 0.23f, 0.16f, 0.27f));
        }

        private static string Excerpt(string value, int limit)
        {
            string text = value?.Trim() ?? "";
            if (text.Length <= limit) return text;
            int space = text.LastIndexOf(' ', limit - 1);
            return text.Substring(0, space > limit * 0.8f ? space : limit).TrimEnd() + "…";
        }

        private static (string left, string right) SplitColumns(string text)
        {
            int middle = text.Length / 2;
            int paragraph = text.IndexOf("\n\n", StringComparison.Ordinal);
            if (paragraph > middle * 0.6f && paragraph < middle * 1.4f)
                return (text.Substring(0, paragraph), text.Substring(paragraph + 2).TrimStart());
            if (middle == 0) return (text, "");
            int space = text.IndexOf(' ', middle);
            int split = space >= middle && space < middle * 1.25f ? space : middle;
            return (text.Substring(0, split).TrimEnd(), text.Substring(split).TrimStart());
        }

        private static Text Type(Transform parent, Font font, float x, float y, float width, float height, string value, int size)
        {
            Text text = OverlayWidgets.Label(parent, font, new Vector2(x, -y), new Vector2(width, height), value ?? "", size);
            text.color = Ink;
            text.alignment = TextAnchor.UpperLeft;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static void Fit(Text text, int min)
        { text.resizeTextForBestFit = true; text.resizeTextMinSize = min; text.resizeTextMaxSize = text.fontSize; }

        private static void Rule(Transform parent, string name, float x, float y, float width, float height, Color color)
        {
            var image = OverlayWidgets.Rect(name, parent, new Vector2(x, -y), new Vector2(width, height)).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }
    }
}
