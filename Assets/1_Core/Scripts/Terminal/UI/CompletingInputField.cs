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
        private bool wasComposing;
        private InlineCompletion? pendingCompletion;
        private bool pendingHadComposition;
        private InlineCompletion? preview;
        private Text ghost;
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

        protected override void OnEnable()
        {
            base.OnEnable();
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
                    if (!handled) KeyPressed(keyEvent);
                    UpdateLabel();
                }
                else if ((keyEvent.type == EventType.ValidateCommand || keyEvent.type == EventType.ExecuteCommand) && keyEvent.commandName == "SelectAll")
                    SelectAll();
            }
            UpdateLabel();
            eventData.Use();
        }

        protected virtual bool HandleEditorKey(KeyCode key, bool protectComposition) => protectComposition &&
            (key == KeyCode.Escape || key == KeyCode.UpArrow || key == KeyCode.DownArrow);

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
        }

        private bool Fits(InlineCompletion suggestion) => characterLimit <= 0 || suggestion.Text.Length <= characterLimit;

        private void ApplyPendingCompletion()
        {
            InlineCompletion accepted = pendingCompletion.Value;
            pendingCompletion = null;
            submission.Cancel();
            if (IsCompositionProtected) FinishImeSession();
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
            if (ghost != null) return;
            GameObject copy = Instantiate(textComponent.gameObject, textComponent.transform.parent, false);
            copy.name = "InlineCompletionPreview";
            copy.transform.SetSiblingIndex(textComponent.transform.GetSiblingIndex());
            ghost = copy.GetComponent<Text>();
            ghost.raycastTarget = false;
            ghost.supportRichText = false;
            ghost.gameObject.SetActive(false);
            if (GetComponentInParent<CrtScreen>() != null && ghost.GetComponent<CrtMeshEffect>() == null) copy.AddComponent<CrtMeshEffect>();
        }

        private void DrawInlinePreview()
        {
            if (!preview.HasValue || !isFocused || !IsInteractable() || textComponent == null || ghost == null || !preview.Value.Changes(CompletionInput))
            { HideInlinePreview(); return; }
            ghost.gameObject.SetActive(true);
            ghost.color = new Color(textComponent.color.r, textComponent.color.g, textComponent.color.b, 0.32f);
            ghost.text = preview.Value.PreviewFrom(m_DrawStart);
            // During Korean composition the proposed syllable can replace the partial one.
            // Display that full proposal faintly rather than superimposing different glyphs.
            bool extendsTypedText = preview.Value.Text.StartsWith(CompletionInput.Text, StringComparison.Ordinal);
            textComponent.canvasRenderer.SetAlpha(extendsTypedText ? 1f : 0f);
        }

        private void HideInlinePreview()
        {
            if (ghost != null) ghost.gameObject.SetActive(false);
            if (textComponent != null) textComponent.canvasRenderer.SetAlpha(1f);
        }

        public void FocusAtEnd() => FocusAt(text.Length);
        private void FocusAt(int position)
        {
            Select();
            if (!isFocused) { ActivateInputField(); focusCaret = position; }
            else { m_CaretPosition = m_CaretSelectPosition = position; UpdateLabel(); }
        }

        public void ReleaseFocus()
        {
            submission.Cancel(); pendingCompletion = null; preview = null; focusCaret = -1;
            wasComposing = false;
            HideInlinePreview();
            DeactivateInputField();
        }

        private void FinishImeSession()
        {
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject)
                EventSystem.current.SetSelectedGameObject(null);
            DeactivateInputField();
            wasComposing = false;
        }

        public override void OnDeselect(BaseEventData eventData)
        {
            submission.Cancel(); pendingCompletion = null; preview = null;
            IsLosingFocus = true;
            try { base.OnDeselect(eventData); }
            finally { IsLosingFocus = false; wasComposing = false; HideInlinePreview(); }
        }

        protected override void OnDisable()
        {
            Canvas.preWillRenderCanvases -= DrawInlinePreview;
            submission.Cancel(); pendingCompletion = null; preview = null;
            HideInlinePreview();
            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            if (ghost != null) Destroy(ghost.gameObject);
            base.OnDestroy();
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
