using System;
using System.Linq;
using Codetective.Operations;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    /// <summary>Handles terminal keys in the same event queue as text input, avoiding duplicate submits.</summary>
    [AddComponentMenu("Codetective/Terminal Input Field")]
    [DefaultExecutionOrder(-100)]
    public sealed class TerminalInputField : CompletingInputField
    {
        public event Action<int> CompletionSelectionMoved;
        public event Action CompletionDismissed;

        private CrtScreen crtScreen;
        private CanvasRenderer caretRenderer;
        private PointerEventData projectedDrag;
        private readonly TerminalReferenceDraft draft = new TerminalReferenceDraft();
        private ObjectReferenceTextView referenceColors;
        private int savedAnchor = -1, savedCaret = -1;
        private int editStart = int.MaxValue;

        private CrtScreen CurvedScreen => crtScreen != null ? crtScreen :
            crtScreen = GetComponentInParent<CrtScreen>();

        public override void OnPointerDown(PointerEventData eventData)
        {
            bool entering = !isFocused && IsActive() && IsInteractable() && eventData.button == PointerEventData.InputButton.Left;
            using (new CrtPointerProjection(eventData, CurvedScreen))
            {
                base.OnPointerDown(eventData);
                if (entering && textComponent != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    textComponent.rectTransform, eventData.position, eventData.pressEventCamera, out Vector2 point))
                    FocusAt(draft.Snap(Mathf.Clamp(GetCharacterIndexFromPosition(point) + m_DrawStart, 0, text.Length)));
                SnapSelection();
            }
        }

        public void FocusFromEnter()
        {
            IgnoreEnterForCurrentFrame();
            FocusAtEnd();
        }

        public override void OnDrag(PointerEventData eventData)
        {
            if (CurvedScreen == null || !CurvedScreen.IsCurved)
            {
                base.OnDrag(eventData);
                SnapSelection();
                return;
            }

            // InputField's out-of-bounds selection coroutine retains this event object.
            // Give it a persistent projected copy, rather than restoring its coordinates.
            if (projectedDrag == null)
                projectedDrag = new PointerEventData(EventSystem.current);
            using (new CrtPointerProjection(eventData, CurvedScreen))
            {
                projectedDrag.Reset();
                projectedDrag.position = eventData.position;
                projectedDrag.pressPosition = eventData.pressPosition;
                projectedDrag.delta = eventData.delta;
                projectedDrag.button = eventData.button;
                projectedDrag.pointerId = eventData.pointerId;
                projectedDrag.pointerCurrentRaycast = eventData.pointerCurrentRaycast;
                projectedDrag.pointerPressRaycast = eventData.pointerPressRaycast;
                base.OnDrag(projectedDrag);
                SnapSelection();
                if (projectedDrag.used)
                    eventData.Use();
            }
        }

        public string EncodeReferences(string value)
        {
            draft.Update(text, editStart);
            return draft.Encode(value, value == text ? int.MaxValue : Mathf.Min(m_CaretPosition, m_CaretSelectPosition));
        }

        public InlineCompletion DecodeCompletion(string value, Func<string, string> label) => draft.DecodeCompletion(value, label);

        public bool InsertObjectReference(string id, string label)
        {
            if (string.IsNullOrWhiteSpace(label) || string.IsNullOrEmpty(id)) return false;
            CommitComposition();
            draft.Update(text);
            int anchor = Mathf.Clamp(isFocused ? m_CaretSelectPosition : savedAnchor < 0 ? text.Length : savedAnchor, 0, text.Length);
            int caret = Mathf.Clamp(isFocused ? m_CaretPosition : savedCaret < 0 ? text.Length : savedCaret, 0, text.Length);
            int start = draft.Snap(Mathf.Min(anchor, caret), anchor == caret ? 0 : -1);
            int end = draft.Snap(Mathf.Max(anchor, caret), anchor == caret ? 0 : 1);
            int separator = start > 0 && text[start - 1] == '=' ? 1 : 0;
            if (characterLimit > 0 && text.Length - (end - start) + label.Length + separator > characterLimit) return false;
            savedAnchor = savedCaret = draft.Insert(start, end, id, label);
            SetTextWithoutNotify(draft.Content.Text);
            m_CaretSelectPosition = m_CaretPosition = savedCaret;
            UpdateLabel();
            onValueChanged.Invoke(text);
            return true;
        }

        public void FocusDraft()
        {
            IgnoreEnterForCurrentFrame();
            FocusAt(Mathf.Clamp(savedCaret < 0 ? text.Length : savedCaret, 0, text.Length));
        }

        public void ClearDraft()
        {
            draft.Set(new ObjectReferenceText(""));
            savedAnchor = savedCaret = -1;
            SetTextWithoutNotify("");
        }

        private void RememberSelection()
        {
            if (!isFocused) return;
            CommitComposition(false);
            savedAnchor = m_CaretSelectPosition;
            savedCaret = m_CaretPosition;
        }

        public override void OnDeselect(BaseEventData eventData)
        { RememberSelection(); base.OnDeselect(eventData); }

        public override void ReleaseFocus()
        { RememberSelection(); base.ReleaseFocus(); }

        protected override void ApplyCompletionText(InlineCompletion completion)
        {
            if (completion.References != null) draft.Set(completion.References);
            else draft.Update(completion.Text);
            base.ApplyCompletionText(completion);
        }

        protected override void ReplaceCommittedRange(int start, int end, string replacement)
        {
            base.ReplaceCommittedRange(start, end, replacement);
            draft.Update(text, start);
        }

        protected override void ProcessEditingKey(Event inputEvent)
        {
            draft.Update(text);
            if (readOnly || IsComposing || draft.Content.Spans.Count == 0) { base.ProcessEditingKey(inputEvent); return; }
            KeyCode key = inputEvent.keyCode;
            bool backwards = key == KeyCode.Backspace;
            if (m_CaretPosition == m_CaretSelectPosition && (backwards || key == KeyCode.Delete) &&
                draft.TryDeletion(m_CaretPosition, backwards, out ObjectReferenceSpan span))
            { m_CaretSelectPosition = span.Start; m_CaretPosition = span.End; }
            SnapSelection();
            editStart = Mathf.Min(m_CaretPosition, m_CaretSelectPosition);
            if (backwards && m_CaretPosition == m_CaretSelectPosition) editStart = Mathf.Max(0, editStart - 1);
            try { base.ProcessEditingKey(inputEvent); draft.Update(text, editStart); }
            finally { editStart = int.MaxValue; }
            SnapSelection(key == KeyCode.LeftArrow ? -1 : key == KeyCode.RightArrow ? 1 : 0);
        }

        private void SnapSelection(int direction = 0)
        {
            if (IsComposing || draft.Content.Spans.Count == 0) return;
            if (m_CaretPosition == m_CaretSelectPosition)
                m_CaretPosition = m_CaretSelectPosition = draft.Snap(m_CaretPosition, direction);
            else
            {
                bool forwards = m_CaretPosition > m_CaretSelectPosition;
                m_CaretPosition = draft.Snap(m_CaretPosition, forwards ? 1 : -1);
                m_CaretSelectPosition = draft.Snap(m_CaretSelectPosition, forwards ? -1 : 1);
            }
        }

        protected override void LateUpdate()
        {
            base.LateUpdate();
            if (textComponent == null) return;
            draft.Update(text);
            if (referenceColors == null && draft.Content.Spans.Count == 0) return;
            if (referenceColors == null) referenceColors = ObjectReferenceTextView.Attach(textComponent);
            ObjectReferenceText visible = draft.View(CompletionInput.Text, Mathf.Min(m_CaretPosition, m_CaretSelectPosition));
            int from = isFocused ? m_DrawStart : 0;
            int to = from + textComponent.text.Length;
            referenceColors.SetSpans(visible.Spans.Where(span => span.End > from && span.Start < to).Select(span =>
                new ObjectReferenceSpan(Mathf.Max(span.Start, from) - from, Mathf.Min(span.End, to) - Mathf.Max(span.Start, from), span.Id)));
        }

        public override void Rebuild(CanvasUpdate update)
        {
            base.Rebuild(update);
            if (update != CanvasUpdate.LatePreRender || CurvedScreen == null || (!CurvedScreen.IsCurved && !CurvedScreen.DisplayEnabled) ||
                m_Mesh == null || m_Mesh.vertexCount == 0 || textComponent == null)
                return;

            // The native InputField caret/selection is a CanvasRenderer, not a Graphic,
            // so it does not run BaseMeshEffect like the text and background do.
            if (caretRenderer == null)
            {
                Transform caret = textComponent.transform.parent.Find(name + " Input Caret");
                if (caret != null)
                    caretRenderer = caret.GetComponent<CanvasRenderer>();
            }
            if (caretRenderer == null)
                return;

            using (var vertices = new VertexHelper(m_Mesh))
            {
                CurvedScreen.BendMesh(vertices, caretRenderer.transform);
                vertices.FillMesh(m_Mesh);
            }
            caretRenderer.SetMesh(m_Mesh);
            caretRenderer.SetMaterial(CurvedScreen.DisplayMaterialFor(Graphic.defaultGraphicMaterial), 0);

            if (m_CursorVerts != null && selectionAnchorPosition == selectionFocusPosition &&
                EventSystem.current != null && EventSystem.current.currentInputModule != null)
            {
                Vector3 caret = CurvedScreen.BendPoint(caretRenderer.transform, m_CursorVerts[0].position);
                var canvas = GetComponentInParent<Canvas>();
                Vector2 point = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera,
                    caretRenderer.transform.TransformPoint(caret));
                point.y = Screen.height - point.y;
                EventSystem.current.currentInputModule.input.compositionCursorPos = point;
            }
        }

        protected override bool HandleEditorKey(KeyCode key, bool protectComposition)
        {
            switch (key)
            {
                case KeyCode.UpArrow:
                case KeyCode.DownArrow:
                    if (!protectComposition)
                        CompletionSelectionMoved?.Invoke(key == KeyCode.DownArrow ? 1 : -1);
                    return true;
                case KeyCode.Escape:
                    if (!protectComposition)
                        CompletionDismissed?.Invoke();
                    return true;
                default:
                    return false;
            }
        }
    }
}
