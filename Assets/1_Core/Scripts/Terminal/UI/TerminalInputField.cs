using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Codetective.Terminal
{
    /// <summary>Handles terminal keys in the same event queue as text input, avoiding duplicate submits.</summary>
    [AddComponentMenu("Codetective/Terminal Input Field")]
    public sealed class TerminalInputField : InputField
    {
        public event Action<string> LineSubmitted;
        public event Action CompletionRequested;
        public event Action<int> CompletionSelectionMoved;
        public event Action CompletionDismissed;

        private readonly Event keyEvent = new Event();
        private readonly TerminalSubmission submission = new TerminalSubmission();
        private bool wasComposing;
        private bool moveToEndAfterFocus;
        private CrtScreen crtScreen;
        private CanvasRenderer caretRenderer;
        private PointerEventData projectedDrag;
        public bool IsCompositionProtected => IsComposing || wasComposing;
        public bool IsLosingFocus { get; private set; }
        public TerminalCompletionInput CompletionInput =>
            new TerminalCompletionInput(text, CompositionText, m_CaretPosition, m_CaretSelectPosition);

        public override void OnDeselect(BaseEventData eventData)
        {
            submission.Cancel();
            IsLosingFocus = true;
            try
            {
                base.OnDeselect(eventData);
            }
            finally
            {
                wasComposing = false;
                IsLosingFocus = false;
            }
        }

        private CrtScreen CurvedScreen => crtScreen != null ? crtScreen :
            crtScreen = GetComponentInParent<CrtScreen>();

        public override void OnPointerDown(PointerEventData eventData)
        {
            using (new CrtPointerProjection(eventData, CurvedScreen))
                base.OnPointerDown(eventData);
        }

        public override void OnDrag(PointerEventData eventData)
        {
            if (CurvedScreen == null || !CurvedScreen.IsCurved)
            {
                base.OnDrag(eventData);
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
                if (projectedDrag.used)
                    eventData.Use();
            }
        }

        public override void Rebuild(CanvasUpdate update)
        {
            base.Rebuild(update);
            if (update != CanvasUpdate.LatePreRender || CurvedScreen == null || !CurvedScreen.IsCurved ||
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

        public bool IsComposing => CompositionText.Length > 0;

        private string CompositionText
        {
            get
            {
                var system = EventSystem.current;
                if (!isFocused || system == null || system.currentSelectedGameObject != gameObject ||
                    system.currentInputModule == null)
                    return string.Empty;
                return system.currentInputModule.input.compositionString ?? string.Empty;
            }
        }

        public void FocusAtEnd()
        {
            Select();
            if (!isFocused)
            {
                ActivateInputField();
                moveToEndAfterFocus = true;
            }
            else
            {
                MoveTextEnd(false);
            }
        }

        public void ReleaseFocus()
        {
            submission.Cancel();
            moveToEndAfterFocus = false;
            wasComposing = false;
            DeactivateInputField();
        }

        protected override void LateUpdate()
        {
            if (!IsInteractable())
                return;
            base.LateUpdate();
            if (moveToEndAfterFocus && isFocused)
            {
                MoveTextEnd(false);
                moveToEndAfterFocus = false;
            }
            wasComposing = IsComposing;

#if ENABLE_INPUT_SYSTEM
            // Some IMEs consume the first Enter without delivering a uGUI key event.
            Keyboard keyboard = Keyboard.current;
            if (isFocused && keyboard != null &&
                (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
                submission.Request(Time.frameCount, CompletionInput);
#endif
            if (isFocused && submission.TryTake(CompletionInput, out string line))
            {
                // Finish the native IME session before clearing/reactivating the field.
                // Its trailing commit must not leak into the next command.
                if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject)
                    EventSystem.current.SetSelectedGameObject(null);
                DeactivateInputField();
                wasComposing = false;
                LineSubmitted?.Invoke(line);
            }
        }

        protected override void OnDisable()
        {
            submission.Cancel();
            base.OnDisable();
        }

        public override void OnUpdateSelected(BaseEventData eventData)
        {
            if (!isFocused)
                return;

            // Keep IME candidate navigation intact; Enter instead submits the visible
            // text after this frame's composition/commit events have been processed.
            bool protectComposition = IsComposing || wasComposing;
            while (Event.PopEvent(keyEvent))
            {
                if (keyEvent.rawType == EventType.KeyDown)
                {
                    if (!HandleTerminalKey(keyEvent.keyCode, protectComposition))
                        KeyPressed(keyEvent);
                    UpdateLabel();
                }
                else if ((keyEvent.type == EventType.ValidateCommand ||
                          keyEvent.type == EventType.ExecuteCommand) && keyEvent.commandName == "SelectAll")
                {
                    SelectAll();
                }
            }

            UpdateLabel();
            eventData.Use();
        }

        private bool HandleTerminalKey(KeyCode key, bool protectComposition)
        {
            switch (key)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    submission.Request(Time.frameCount, CompletionInput);
                    return true;
                case KeyCode.Tab:
                    if (!protectComposition)
                        CompletionRequested?.Invoke();
                    return true;
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
