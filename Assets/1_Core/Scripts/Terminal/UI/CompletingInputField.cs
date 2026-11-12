using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Codetective.Terminal
{
    /// <summary>Shared IME-safe completion gestures for the console and multiline protocol editor.</summary>
    public class CompletingInputField : InputField
    {
        public Func<TerminalCompletionInput, InlineCompletion?> CompletionQuery { get; set; }
        public event Action CompletionApplied;
        public event Action CompletionListRequested;
        public event Action<string> LineSubmitted;
        public bool IsLosingFocus { get; private set; }
        public bool IsComposing => CompositionText.Length > 0;
        public bool IsCompositionProtected => IsComposing || wasComposing;
        public TerminalCompletionInput CompletionInput => new TerminalCompletionInput(text, CompositionText, m_CaretPosition, m_CaretSelectPosition);

        private readonly Event keyEvent = new Event();
        private readonly TerminalSubmission submission = new TerminalSubmission();
        private readonly ImeSession imeSession = new ImeSession();
        private readonly TextGenerator previewLayout = new TextGenerator();
        private ScopedImeInput imeInput;
        private bool wasComposing;
        private InlineCompletion? pendingCompletion;
        private bool pendingHadComposition;
        private string pendingComposition = "";
        private InlineCompletion? preview;
        private Text ghost;
        private Graphic suppressedPlaceholder;
        private bool placeholderWasActive;
        private int handledFrame = -1;
        private readonly int[] keyEventFrames = { -1, -1, -1 };
        private int focusCaret = -1;

        private string CompositionText
        {
            get
            {
                EventSystem system = EventSystem.current;
                return isFocused && system != null && system.currentSelectedGameObject == gameObject && system.currentInputModule != null ?
                    system.currentInputModule.input.compositionString ?? "" : "";
            }
        }

        public override void OnSelect(BaseEventData eventData)
        {
            imeInput = ScopedImeInput.Acquire(this, imeSession);
            base.OnSelect(eventData);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (textComponent != null) textComponent.canvasRenderer.SetAlpha(1f);
            Canvas.preWillRenderCanvases += DrawInlinePreview;
        }

        protected virtual void Update()
        {
            // Capture the visible proposal before uGUI/IME can consume the confirming key.
            if (isFocused && IsInteractable() && IsCompositionProtected) PollPhysicalKeys();
        }

        protected override void LateUpdate()
        {
            if (!IsInteractable()) return;
            base.LateUpdate();
            if (focusCaret >= 0 && isFocused)
            {
                m_CaretPosition = m_CaretSelectPosition = Mathf.Clamp(focusCaret, 0, text.Length);
                focusCaret = -1;
                UpdateLabel();
            }
            if (isFocused) PollPhysicalKeys();
            wasComposing = IsComposing;
            if (pendingCompletion.HasValue)
            {
                ApplyPendingCompletion();
                return;
            }
            if (isFocused && submission.TryTake(CompletionInput, out string line))
            {
                FinishImeSession();
                LineSubmitted?.Invoke(line);
            }
        }

        private void PollPhysicalKeys()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) Poll(CompletionKey.Enter);
            if (keyboard.tabKey.wasPressedThisFrame) Poll(CompletionKey.Tab);
            if (keyboard.spaceKey.wasPressedThisFrame) Poll(CompletionKey.Space);
#endif
        }

        private void Poll(CompletionKey key)
        {
            if (keyEventFrames[(int)key] != Time.frameCount) HandleCompletionKey(key);
        }

        public override void OnUpdateSelected(BaseEventData eventData)
        {
            if (!isFocused) return;
            while (Event.PopEvent(keyEvent))
            {
                if (keyEvent.rawType == EventType.KeyDown)
                {
                    // Preserve ordinary text typed immediately after accepting a Latin suggestion.
                    // IME commits are drained first and the final replacement is applied in LateUpdate.
                    if (pendingCompletion.HasValue && !pendingHadComposition && !TryGetCompletionKey(keyEvent.keyCode, out _)) ApplyPendingCompletion();
                    bool handled;
                    if (TryGetCompletionKey(keyEvent.keyCode, out CompletionKey key))
                    {
                        keyEventFrames[(int)key] = Time.frameCount;
                        handled = IsHeldRepeat(key) || HandleCompletionKey(key);
                    }
                    else handled = HandleEditorKey(keyEvent.keyCode, IsCompositionProtected);
                    if (!handled) ProcessEditingKey(keyEvent);
                    UpdateLabel();
                }
                else if ((keyEvent.type == EventType.ValidateCommand || keyEvent.type == EventType.ExecuteCommand) && keyEvent.commandName == "SelectAll")
                { CommitComposition(); SelectAll(); }
            }
            UpdateLabel();
            eventData.Use();
        }

        protected virtual bool HandleEditorKey(KeyCode key, bool protectComposition) => protectComposition &&
            (key == KeyCode.Escape || key == KeyCode.UpArrow || key == KeyCode.DownArrow);

        private void ProcessEditingKey(Event inputEvent)
        {
            bool composing = IsComposing;
            if (composing && (inputEvent.keyCode == KeyCode.Backspace || inputEvent.keyCode == KeyCode.Delete)) return;
            if (composing && (inputEvent.keyCode == KeyCode.LeftArrow || inputEvent.keyCode == KeyCode.RightArrow ||
                inputEvent.keyCode == KeyCode.Home || inputEvent.keyCode == KeyCode.End || inputEvent.control || inputEvent.command)) CommitComposition();
            char character = inputEvent.character;
            if (character == '\0' || inputEvent.control || inputEvent.command || inputEvent.alt) { KeyPressed(inputEvent); return; }
            if (imeSession.IgnoreCommitEcho(character)) return;
            // uGUI's caret setters add composition length. A committed character must be
            // inserted with raw indices, even if the backend still exposes the old syllable.
            imeSession.Suppress();
            try { KeyPressed(inputEvent); }
            finally { imeSession.Resume(); }
        }

        private bool HandleCompletionKey(CompletionKey key)
        {
            if (handledFrame == Time.frameCount) return true;
            TerminalCompletionInput current = CompletionInput;
            InlineCompletion? suggestion = CompletionQuery?.Invoke(current);
            if (suggestion.HasValue && !Fits(suggestion.Value)) suggestion = null;
            bool submitOnEnter = lineType != LineType.MultiLineNewline;
            switch (InlineCompletion.Resolve(key, current, suggestion, submitOnEnter))
            {
                case CompletionKeyAction.Accept:
                    AcceptCompletion(suggestion.Value);
                    return true;
                case CompletionKeyAction.Submit:
                    handledFrame = Time.frameCount;
                    submission.Request(Time.frameCount, current);
                    return true;
                case CompletionKeyAction.None:
                    handledFrame = Time.frameCount;
                    CompletionListRequested?.Invoke();
                    return true;
                default: return false;
            }
        }

        public void AcceptCompletion(InlineCompletion suggestion)
        {
            if (!Fits(suggestion) || !suggestion.Changes(CompletionInput)) return;
            handledFrame = Time.frameCount;
            submission.Consume(Time.frameCount);
            pendingCompletion = suggestion;
            pendingHadComposition = IsCompositionProtected;
            pendingComposition = CompositionText;
        }

        private bool Fits(InlineCompletion suggestion) => characterLimit <= 0 || suggestion.Text.Length <= characterLimit;

        private void ApplyPendingCompletion()
        {
            InlineCompletion accepted = pendingCompletion.Value;
            pendingCompletion = null;
            submission.Cancel();
            if (pendingHadComposition || IsCompositionProtected)
            {
                imeSession.Consume(pendingComposition, imeInput != null ? imeInput.RawComposition : "");
                FinishImeSession();
            }
            SetTextWithoutNotify(accepted.Text);
            FocusAt(accepted.Caret);
            preview = null;
            CompletionApplied?.Invoke();
        }

        public void SetInlinePreview(InlineCompletion? suggestion)
        {
            preview = suggestion.HasValue && Fits(suggestion.Value) && suggestion.Value.Changes(CompletionInput) ? suggestion : null;
            if (preview.HasValue && textComponent != null) EnsureGhost();
            else HideInlinePreview();
        }

        private void EnsureGhost()
        {
            // Replace previews created by the older full-line implementation after a script reload.
            Transform legacy = textComponent.transform.parent.Find("InlineCompletionPreview");
            if (legacy != null && legacy != textComponent.transform)
            {
                if (ghost != null && ghost.transform == legacy) ghost = null;
                legacy.name = "RetiredInlineCompletionPreview";
                DestroyPreview(legacy.gameObject);
            }
            if (ghost != null && ghost.transform.parent != textComponent.transform) { DestroyPreview(ghost.gameObject); ghost = null; }
            if (ghost != null) return;
            var copy = new GameObject("InlineCompletionPreview", typeof(RectTransform), typeof(Text));
            copy.layer = gameObject.layer;
            copy.transform.SetParent(textComponent.transform, false);
            ghost = copy.GetComponent<Text>();
            ghost.raycastTarget = false;
            ghost.supportRichText = false;
            ghost.rectTransform.anchorMin = ghost.rectTransform.anchorMax = ghost.rectTransform.pivot = new Vector2(0, 1);
            ghost.gameObject.SetActive(false);
            if (GetComponentInParent<CrtScreen>() != null && ghost.GetComponent<CrtMeshEffect>() == null) copy.AddComponent<CrtMeshEffect>();
        }

        private void DrawInlinePreview()
        {
            if (!preview.HasValue || !isFocused || !IsInteractable() || textComponent == null || ghost == null || !preview.Value.Changes(CompletionInput))
            { HideInlinePreview(); return; }
            TerminalCompletionInput current = CompletionInput;
            string suffix = preview.Value.SuffixFor(current);
            if (suffix.Length == 0) { HideInlinePreview(); return; }
            Rect rect = textComponent.rectTransform.rect;
            string visible = textComponent.text ?? "";
            previewLayout.Populate(visible + "\u200B", textComponent.GetGenerationSettings(rect.size));
            int cursor = Mathf.Clamp(current.Caret - m_DrawStart, 0, previewLayout.characters.Count - 1);
            if (previewLayout.characters.Count == 0 || previewLayout.lines.Count == 0) { HideInlinePreview(); return; }
            int line = 0;
            while (line + 1 < previewLayout.lines.Count && previewLayout.lines[line + 1].startCharIdx <= cursor) line++;
            float units = textComponent.pixelsPerUnit;
            float x = Mathf.Min(rect.xMax, previewLayout.characters[cursor].cursorPos.x / units);
            float top = previewLayout.lines[line].topY / units;
            ghost.rectTransform.anchoredPosition = new Vector2(x - rect.xMin, top - rect.yMax);
            ghost.rectTransform.sizeDelta = new Vector2(Mathf.Max(0, rect.xMax - x), Mathf.Max(0, top - rect.yMin));
            ghost.font = textComponent.font; ghost.fontSize = textComponent.fontSize; ghost.fontStyle = textComponent.fontStyle;
            ghost.lineSpacing = textComponent.lineSpacing; ghost.alignment = TextAnchor.UpperLeft;
            ghost.horizontalOverflow = textComponent.horizontalOverflow; ghost.verticalOverflow = VerticalWrapMode.Truncate;
            ghost.material = textComponent.material;
            ghost.gameObject.SetActive(true);
            ghost.color = new Color(textComponent.color.r, textComponent.color.g, textComponent.color.b, 0.32f);
            ghost.text = suffix;
            SuppressPlaceholder();
        }

        private void HideInlinePreview()
        {
            if (ghost != null) ghost.gameObject.SetActive(false);
            RestorePlaceholder();
            if (placeholder != null) placeholder.enabled = CompletionInput.Text.Length == 0;
        }

        private void SuppressPlaceholder()
        {
            if (placeholder == null) return;
            if (suppressedPlaceholder != placeholder)
            {
                RestorePlaceholder();
                suppressedPlaceholder = placeholder;
                placeholderWasActive = placeholder.gameObject.activeSelf;
            }
            // UpdateLabel can re-enable the Graphic during a font rebuild. Keeping its
            // object inactive prevents the help text from drawing over the empty ghost.
            placeholder.gameObject.SetActive(false);
            placeholder.enabled = false;
        }

        private void RestorePlaceholder()
        {
            if (suppressedPlaceholder != null) suppressedPlaceholder.gameObject.SetActive(placeholderWasActive);
            suppressedPlaceholder = null;
        }

        public void FocusAtEnd() => FocusAt(text.Length);
        private void FocusAt(int position)
        {
            Select();
            EnsureImeInput();
            if (!isFocused) { ActivateInputField(); focusCaret = position; }
            else { m_CaretPosition = m_CaretSelectPosition = position; UpdateLabel(); }
        }

        public void ReleaseFocus()
        {
            submission.Cancel(); pendingCompletion = null; preview = null; focusCaret = -1;
            wasComposing = false;
            HideInlinePreview();
            EndImeSession();
            DeactivateInputField();
        }

        private void FinishImeSession()
        {
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject)
                EventSystem.current.SetSelectedGameObject(null);
            EndImeSession();
            DeactivateInputField();
            wasComposing = false;
        }

        public override void OnDeselect(BaseEventData eventData)
        {
            submission.Cancel(); pendingCompletion = null; preview = null;
            IsLosingFocus = true;
            try { EndImeSession(); base.OnDeselect(eventData); }
            finally { IsLosingFocus = false; wasComposing = false; HideInlinePreview(); }
        }

        protected override void OnDisable()
        {
            Canvas.preWillRenderCanvases -= DrawInlinePreview;
            submission.Cancel(); pendingCompletion = null; preview = null;
            EndImeSession();
            HideInlinePreview();
            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            if (ghost != null) DestroyPreview(ghost.gameObject);
            ((IDisposable)previewLayout).Dispose();
            base.OnDestroy();
        }

        private void DestroyPreview(GameObject previewObject)
        {
            previewObject.SetActive(false);
            if (Application.isPlaying) Destroy(previewObject);
            else DestroyImmediate(previewObject);
        }

        public override void OnPointerDown(PointerEventData eventData)
        {
            EnsureImeInput();
            if (isFocused) CommitComposition();
            base.OnPointerDown(eventData);
        }

        public override void OnBeginDrag(PointerEventData eventData)
        {
            CommitComposition();
            base.OnBeginDrag(eventData);
        }

        private void CommitComposition(bool restartIme = true)
        {
            string composition = CompositionText;
            if (composition.Length == 0) return;
            string raw = imeInput != null ? imeInput.RawComposition : composition;
            imeSession.Consume(composition, raw);
            if (!readOnly)
            {
                int start = Mathf.Clamp(Mathf.Min(m_CaretPosition, m_CaretSelectPosition), 0, text.Length);
                int end = Mathf.Clamp(Mathf.Max(m_CaretPosition, m_CaretSelectPosition), start, text.Length);
                SetTextWithoutNotify(text.Remove(start, end - start).Insert(start, composition));
                m_CaretPosition = m_CaretSelectPosition = Mathf.Min(text.Length, start + composition.Length);
                onValueChanged.Invoke(text);
            }
            if (restartIme && imeInput != null) imeInput.RestartIme();
            wasComposing = false;
            UpdateLabel();
        }

        private void EndImeSession()
        {
            CommitComposition(false);
            imeSession.End("", imeInput != null ? imeInput.RawComposition : "");
            if (imeInput != null) imeInput.Release(this);
            m_CaretPosition = Mathf.Clamp(m_CaretPosition, 0, text.Length);
            m_CaretSelectPosition = Mathf.Clamp(m_CaretSelectPosition, 0, text.Length);
            UpdateLabel();
        }

        private void EnsureImeInput()
        {
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject &&
                (imeInput == null || !imeInput.Owns(this))) imeInput = ScopedImeInput.Acquire(this, imeSession);
        }

        public override void OnPointerClick(PointerEventData eventData)
        {
            EnsureImeInput();
            base.OnPointerClick(eventData);
        }

        protected virtual void OnApplicationFocus(bool focused)
        {
            if (!focused && isFocused) { EndImeSession(); DeactivateInputField(); HideInlinePreview(); }
        }

        private static bool TryGetCompletionKey(KeyCode key, out CompletionKey completionKey)
        {
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter) { completionKey = CompletionKey.Enter; return true; }
            if (key == KeyCode.Tab) { completionKey = CompletionKey.Tab; return true; }
            completionKey = CompletionKey.Space;
            return key == KeyCode.Space;
        }

        private static bool IsHeldRepeat(CompletionKey key)
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (key == CompletionKey.Enter) return (keyboard.enterKey.isPressed || keyboard.numpadEnterKey.isPressed) &&
                    !keyboard.enterKey.wasPressedThisFrame && !keyboard.numpadEnterKey.wasPressedThisFrame;
                if (key == CompletionKey.Tab) return keyboard.tabKey.isPressed && !keyboard.tabKey.wasPressedThisFrame;
                return keyboard.spaceKey.isPressed && !keyboard.spaceKey.wasPressedThisFrame;
            }
#endif
            return false;
        }
    }
}
