using System;
using System.Collections.Generic;
using System.Linq;
using Codetective.Operations;
using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    /// <summary>Draws command frames in the same curved layout as the transcript and its object links.</summary>
    internal sealed class TerminalTranscriptStyleView : MaskableGraphic
    {
        private static readonly Color32 DialogueColor = new Color(0.62f, 0.96f, 0.76f);
        private static readonly Color32 ExampleColor = new Color(0.86f, 1f, 0.91f);
        private readonly List<Rect> dialogue = new List<Rect>();
        private readonly List<Rect> examples = new List<Rect>();
        private Text label;
        private TerminalTranscript transcript;
        private float width;

        internal static TerminalTranscriptStyleView Attach(Text label)
        {
            var rect = MonitorWidgets.Rect("CommandExampleFrames", label.transform, Vector2.zero, Vector2.zero);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = label.rectTransform.pivot;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var view = rect.gameObject.AddComponent<TerminalTranscriptStyleView>();
            view.label = label;
            view.color = new Color(0.48f, 0.76f, 0.62f);
            view.raycastTarget = false;
            rect.gameObject.AddComponent<CrtMeshEffect>();

            // Color dialogue first so the link pass can still highlight references in it.
            var textMesh = label.GetComponent<CrtMeshEffect>();
            textMesh.BeforeBend = (Action<VertexHelper>)view.TintText + textMesh.BeforeBend;
            return view;
        }

        internal void Show(TerminalTranscript content)
        {
            float nextWidth = label.rectTransform.rect.width;
            if (transcript != null && Mathf.Approximately(width, nextWidth) && transcript.Text == content.Text &&
                transcript.Dialogue.SequenceEqual(content.Dialogue) && transcript.CommandExamples.SequenceEqual(content.CommandExamples)) return;

            bool hadFrames = transcript != null && transcript.CommandExamples.Count > 0;
            transcript = content;
            width = nextWidth;
            label.SetVerticesDirty();
            if (hadFrames || content.CommandExamples.Count > 0) SetVerticesDirty();
        }

        private void TintText(VertexHelper mesh)
        {
            if (transcript == null || transcript.Dialogue.Count == 0 || mesh.currentVertCount == 0) return;
            var layout = new TextRangeLayout(label);
            dialogue.Clear();
            examples.Clear();
            foreach ((int start, int length) in transcript.Dialogue) dialogue.AddRange(layout.Rects(start, start + length));
            foreach ((int start, int length) in transcript.CommandExamples) examples.AddRange(layout.Rects(start, start + length));

            UIVertex vertex = default;
            for (int i = 0; i + 3 < mesh.currentVertCount; i += 4)
            {
                mesh.PopulateUIVertex(ref vertex, i);
                Vector2 corner = vertex.position;
                mesh.PopulateUIVertex(ref vertex, i + 2);
                Vector2 center = (corner + (Vector2)vertex.position) * 0.5f;
                Color32 tint;
                if (Contains(examples, center)) tint = ExampleColor;
                else if (Contains(dialogue, center)) tint = DialogueColor;
                else continue;
                for (int j = i; j < i + 4; j++)
                {
                    mesh.PopulateUIVertex(ref vertex, j);
                    tint.a = vertex.color.a;
                    vertex.color = tint;
                    mesh.SetUIVertex(vertex, j);
                }
            }
        }

        private static bool Contains(List<Rect> ranges, Vector2 point)
        {
            foreach (Rect range in ranges)
            {
                if (point.y >= range.yMax) break;
                if (range.Contains(point)) return true;
            }
            return false;
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (transcript == null || transcript.CommandExamples.Count == 0 || label.font == null) return;
            // The frame graphic can rebuild before its parent text after a viewport resize.
            label.cachedTextGenerator.Populate(label.text, label.GetGenerationSettings(label.rectTransform.rect.size));
            var layout = new TextRangeLayout(label);
            const float padding = 5f, thickness = 1.5f;
            foreach ((int start, int length) in transcript.CommandExamples)
                foreach (Rect bounds in layout.Rects(start, start + length))
                {
                    float left = bounds.xMin - padding, right = bounds.xMax + padding;
                    float bottom = bounds.yMin + 1, top = bounds.yMax - 1;
                    Stroke(mesh, left, bottom, right, bottom + thickness);
                    Stroke(mesh, left, top - thickness, right, top);
                    Stroke(mesh, left, bottom + thickness, left + thickness, top - thickness);
                    Stroke(mesh, right - thickness, bottom + thickness, right, top - thickness);
                }
        }

        private void Stroke(VertexHelper mesh, float left, float bottom, float right, float top)
        {
            int start = mesh.currentVertCount;
            mesh.AddVert(new Vector3(left, bottom), color, Vector2.zero);
            mesh.AddVert(new Vector3(left, top), color, Vector2.zero);
            mesh.AddVert(new Vector3(right, top), color, Vector2.zero);
            mesh.AddVert(new Vector3(right, bottom), color, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start + 2, start + 3, start);
        }
    }
}
