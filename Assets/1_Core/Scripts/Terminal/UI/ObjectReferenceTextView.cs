using System;
using System.Collections.Generic;
using System.Linq;
using Codetective.Operations;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    /// <summary>Colors object references in an existing uGUI Text and hit-tests their actual line positions.</summary>
    [RequireComponent(typeof(Text)), DisallowMultipleComponent]
    public sealed class ObjectReferenceTextView : MonoBehaviour, ICanvasRaycastFilter, IPointerClickHandler
    {
        private static readonly Color LinkColor = new Color(1f, 0.77f, 0.35f);
        private readonly List<(Rect bounds, ObjectReferenceSpan span)> areas = new List<(Rect, ObjectReferenceSpan)>();
        private ObjectReferenceSpan[] spans = Array.Empty<ObjectReferenceSpan>();
        private Action<string, string> clicked;
        private Text label;

        public static ObjectReferenceTextView Attach(Text label, Action<string, string> onClick = null)
        {
            var view = label.GetComponent<ObjectReferenceTextView>() ?? label.gameObject.AddComponent<ObjectReferenceTextView>();
            view.label = label;
            view.clicked = onClick;
            label.supportRichText = false;
            var mesh = label.GetComponent<CrtMeshEffect>() ?? label.gameObject.AddComponent<CrtMeshEffect>();
            mesh.BeforeBend = view.Decorate;
            if (onClick != null) label.raycastTarget = false;
            return view;
        }

        public void Show(ObjectReferenceText content, Func<string, bool> visible)
        {
            label.text = content.Text;
            SetSpans(content.Spans.Where(span => visible(span.Id)));
        }

        internal void SetSpans(IEnumerable<ObjectReferenceSpan> spans)
        {
            var next = spans.ToArray();
            if (clicked != null) label.raycastTarget = next.Length > 0;
            if (this.spans.SequenceEqual(next)) return;
            this.spans = next;
            areas.Clear();
            label.SetVerticesDirty();
        }

        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera) => clicked != null &&
            RectTransformUtility.ScreenPointToLocalPointInRectangle(label.rectTransform, screenPoint, eventCamera, out Vector2 point) && TryHit(point, out _);

        public void OnPointerClick(PointerEventData eventData)
        {
            if (clicked == null || eventData.button != PointerEventData.InputButton.Left) return;
            using (new CrtPointerProjection(eventData, GetComponentInParent<CrtScreen>()))
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(label.rectTransform, eventData.position, eventData.pressEventCamera, out Vector2 point) &&
                    TryHit(point, out ObjectReferenceSpan span))
                    clicked(span.Id, label.text.Substring(span.Start, span.Length));
        }

        private bool TryHit(Vector2 point, out ObjectReferenceSpan span)
        {
            foreach (var area in areas)
                if (area.bounds.Contains(point) && area.span.End <= label.text.Length) { span = area.span; return true; }
            span = default;
            return false;
        }

        private void Decorate(VertexHelper mesh)
        {
            areas.Clear();
            if (spans.Length == 0 || mesh.currentVertCount == 0) return;

            var layout = new TextRangeLayout(label);
            foreach (ObjectReferenceSpan span in spans)
                foreach (Rect bounds in layout.Rects(span.Start, span.End)) areas.Add((bounds, span));

            UIVertex vertex = default;
            for (int i = 0; i + 3 < mesh.currentVertCount; i += 4)
            {
                mesh.PopulateUIVertex(ref vertex, i); Vector2 corner = vertex.position;
                mesh.PopulateUIVertex(ref vertex, i + 2);
                if (!TryHit((corner + (Vector2)vertex.position) * 0.5f, out _)) continue;
                for (int j = 0; j < 4; j++)
                {
                    mesh.PopulateUIVertex(ref vertex, i + j);
                    vertex.color = new Color(LinkColor.r, LinkColor.g, LinkColor.b, vertex.color.a / 255f);
                    mesh.SetUIVertex(vertex, i + j);
                }
            }
        }
    }

    /// <summary>Maps plain-text ranges to the rendered lines of a uGUI label.</summary>
    internal readonly struct TextRangeLayout
    {
        private readonly string text;
        private readonly IList<UICharInfo> characters;
        private readonly IList<UILineInfo> lines;
        private readonly float scale;
        private readonly Vector2 offset;

        public TextRangeLayout(Text label)
        {
            text = label.text;
            characters = label.cachedTextGenerator.characters;
            lines = label.cachedTextGenerator.lines;
            scale = 1f / label.pixelsPerUnit;
            offset = label.PixelAdjustPoint(Vector2.zero);
        }

        public IEnumerable<Rect> Rects(int start, int end)
        {
            if (lines.Count == 0) yield break;
            end = Mathf.Min(end, text.Length, characters.Count);
            int line = 0;
            while (start < end)
            {
                while (line + 1 < lines.Count && lines[line + 1].startCharIdx <= start) line++;
                int stop = line + 1 < lines.Count ? Mathf.Min(end, lines[line + 1].startCharIdx) : end;
                int last = stop - 1;
                while (last >= start && (text[last] == '\r' || text[last] == '\n')) last--;
                if (last >= start)
                {
                    float left = characters[start].cursorPos.x;
                    float right = characters[last].cursorPos.x + characters[last].charWidth;
                    UILineInfo metrics = lines[line];
                    yield return new Rect(left * scale + offset.x, (metrics.topY - metrics.height) * scale + offset.y,
                        Mathf.Max(1, right - left) * scale, metrics.height * scale);
                }
                start = stop;
            }
        }
    }
}
