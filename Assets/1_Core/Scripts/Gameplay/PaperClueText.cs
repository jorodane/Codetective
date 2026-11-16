using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Codetective.Gameplay
{
    /// <summary>Clues are hit-tested in the actual body text, rather than shown as extra buttons.</summary>
    public sealed class PaperClueText : Text, IPointerClickHandler
    {
        public IReadOnlyList<PaperClue> Clues = Array.Empty<PaperClue>();
        private readonly HashSet<PaperClue> collected = new HashSet<PaperClue>();
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, eventData.position, eventData.pressEventCamera, out Vector2 point)) return;
            foreach (PaperClue clue in Clues)
                if (CharacterRects(clue.label).Any(rect => rect.Contains(point)))
                { clue.collect?.Invoke(); collected.Add(clue); SetVerticesDirty(); return; }
        }
        protected override void OnPopulateMesh(VertexHelper helper)
        {
            base.OnPopulateMesh(helper);
            UIVertex vertex = default;
            foreach (PaperClue clue in Clues)
                foreach (Rect rect in CharacterRects(clue.label))
                    for (int i = 0; i + 3 < helper.currentVertCount; i += 4)
                    {
                        helper.PopulateUIVertex(ref vertex, i); Vector2 corner = vertex.position;
                        helper.PopulateUIVertex(ref vertex, i + 2); Vector2 other = vertex.position;
                        if (!rect.Contains((corner + other) * 0.5f)) continue;
                        for (int j = 0; j < 4; j++)
                        {
                            helper.PopulateUIVertex(ref vertex, i + j);
                            vertex.color = collected.Contains(clue) ? new Color(0.3f, 0.4f, 0.3f) : new Color(0.65f, 0.1f, 0.07f);
                            helper.SetUIVertex(vertex, i + j);
                        }
                    }
        }
        private IEnumerable<Rect> CharacterRects(string keyword)
        {
            if (string.IsNullOrEmpty(keyword)) yield break;
            IList<UICharInfo> characters = cachedTextGenerator.characters; IList<UILineInfo> lines = cachedTextGenerator.lines;
            if (lines.Count == 0) yield break;
            float scale = 1 / pixelsPerUnit; Vector2 offset = PixelAdjustPoint(Vector2.zero);
            for (int start = text.IndexOf(keyword, StringComparison.Ordinal); start >= 0; start = text.IndexOf(keyword, start + keyword.Length, StringComparison.Ordinal))
                for (int i = start; i < start + keyword.Length && i < characters.Count; i++)
                {
                    if (text[i] == '\r' || text[i] == '\n') continue;
                    int line = 0; while (line + 1 < lines.Count && lines[line + 1].startCharIdx <= i) line++;
                    UICharInfo character = characters[i]; UILineInfo row = lines[line];
                    yield return new Rect(character.cursorPos.x * scale + offset.x, (row.topY - row.height) * scale + offset.y,
                        Mathf.Max(1, character.charWidth) * scale, row.height * scale);
                }
        }
    }
}
