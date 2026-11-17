using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Codetective.Terminal;

namespace Codetective.Gameplay
{
    /// <summary>One layout renderer for the physical print and the close-only overlay.</summary>
    internal static class PaperContentView
    {
        public static RectTransform Create(Transform parent, Font font, PaperSection section, float width, bool interactive)
        {
            if (section.profile != null)
                return ProfileFormView.CreatePage(parent, font, section.profile, section.portrait, section.title, section.text, width);
            PaperLayout layout = section.layout;
            bool authored = layout != null && layout.blocks != null && layout.blocks.Length > 0;
            float logicalWidth = authored ? Mathf.Max(1, layout.width) : width;
            RectTransform page = OverlayWidgets.Rect("PrintedContent", parent, Vector2.zero, new Vector2(logicalWidth, 1));
            if (authored)
            {
                page.sizeDelta = new Vector2(logicalWidth, Mathf.Max(1, layout.height));
                foreach (PaperLayoutBlock block in layout.blocks)
                {
                    if (block == null) continue;
                    Vector2 position = new Vector2(block.position.x, -block.position.y);
                    if (block.kind == PaperBlockKind.Image)
                    {
                        if (block.image == null) continue;
                        Image image = OverlayWidgets.Rect("PrintedImage", page, position, block.size).gameObject.AddComponent<Image>();
                        image.sprite = block.image; image.preserveAspect = true; image.color = Color.white;
                        PaperClue clue = section.clues.FirstOrDefault(c => c.label == block.clueKeyword);
                        image.raycastTarget = interactive && clue != null;
                        if (image.raycastTarget) image.gameObject.AddComponent<PaperImageClue>().Clue = clue;
                        page.sizeDelta = new Vector2(logicalWidth, Mathf.Max(page.sizeDelta.y, block.position.y + block.size.y));
                    }
                    else
                    {
                        string value = block.kind == PaperBlockKind.Headline ? section.title : block.kind == PaperBlockKind.Body ? section.text : block.text;
                        PaperClueText text = Text(page, font, position, block.size, value, block.fontSize, section.clues, interactive);
                        // An authored text box may grow, so long text remains available when scrolling.
                        text.rectTransform.sizeDelta = new Vector2(block.size.x, Mathf.Max(block.size.y, text.preferredHeight));
                        page.sizeDelta = new Vector2(logicalWidth, Mathf.Max(page.sizeDelta.y, block.position.y + text.rectTransform.sizeDelta.y));
                    }
                }
                float scale = width / logicalWidth;
                RectTransform scaled = OverlayWidgets.Rect("PageLayout", parent, Vector2.zero, new Vector2(width, page.sizeDelta.y * scale));
                page.SetParent(scaled, false); page.localScale = Vector3.one * scale;
                return scaled;
            }
            PaperClueText title = Text(page, font, Vector2.zero, new Vector2(width, 1), section.title, 32, section.clues, interactive);
            float headingHeight = title.preferredHeight + 20;
            title.rectTransform.sizeDelta = new Vector2(width, headingHeight);
            PaperClueText body = Text(page, font, new Vector2(0, -headingHeight), new Vector2(width, 1), section.text, 24, section.clues, interactive);
            body.rectTransform.sizeDelta = new Vector2(width, body.preferredHeight + 12);
            page.sizeDelta = new Vector2(width, headingHeight + body.rectTransform.sizeDelta.y);
            return page;
        }

        private static PaperClueText Text(Transform parent, Font font, Vector2 position, Vector2 size, string value, int fontSize,
            IReadOnlyList<PaperClue> clues, bool interactive)
        {
            PaperClueText text = OverlayWidgets.Rect("PrintedText", parent, position, size).gameObject.AddComponent<PaperClueText>();
            text.font = font; text.fontSize = Mathf.Max(1, fontSize); text.text = value ?? ""; text.supportRichText = false;
            text.alignment = TextAnchor.UpperLeft; text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = new Color(0.12f, 0.11f, 0.08f); text.Clues = clues; text.raycastTarget = interactive && clues.Count > 0;
            return text;
        }
    }

}
