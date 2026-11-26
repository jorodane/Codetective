using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Codetective.Operations;

namespace Codetective.Terminal
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasGroup))]
    public sealed partial class TerminalController : MonoBehaviour
    {
        [SerializeField] private TerminalInputField input;
        [SerializeField] private Text transcript;
        [SerializeField] private ScrollRect transcriptScroll;
        [SerializeField] private GameObject completionPanel;
        [SerializeField] private Text completionHeading;
        [SerializeField] private Button[] completionButtons;
        [SerializeField] private Text[] completionLabels;
        [SerializeField] private bool interactionEnabled = true;

        public bool InteractionEnabled => interactionEnabled;
        public bool PageVisible => pageVisible;
        public KernelRuntime Runtime => session.Runtime;
        public bool IsCompositionProtected => (AcceptsInput && input != null && input.IsCompositionProtected) ||
            (GetComponent<MonitorPageHost>()?.IsCompositionProtected ?? false);
        private bool pageVisible = true;
        private bool restoreCommandFocus;
        private Button commandAction;
        private Text commandActionLabel;
        private bool AcceptsInput => interactionEnabled && pageVisible && !CommandInputBlocked;

        public void SetPageVisible(bool visible)
        {
            if (pageVisible == visible) return;
            pageVisible = visible;
            if (!visible) { restoreCommandFocus = false; ClearConversationDisturbance(); }
            RefreshConnectionPresentation();
            ApplyInputInteraction();
            RefreshAnswerPrompt();
        }

        public void SetInteractionEnabled(bool enabled)
        {
            interactionEnabled = enabled;
            if (!enabled) { restoreCommandFocus = false; ClearConversationDisturbance(); }
            if (!enabled)
            {
                var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                if (selected != null && selected.transform.IsChildOf(transform))
                    EventSystem.current.SetSelectedGameObject(null);
            }
            var group = GetComponent<CanvasGroup>();
            if (group != null)
            {
                group.interactable = enabled;
                group.blocksRaycasts = enabled;
            }
            ApplyInputInteraction();
        }

        private void ApplyInputInteraction()
        {
            conversationInputLocked = CommandInputBlocked;
            if (!AcceptsInput)
            {
                var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                if (selected == input.gameObject) EventSystem.current.SetSelectedGameObject(null);
                input.ReleaseFocus();
                DismissCompletions();
            }
            input.interactable = AcceptsInput;
            input.readOnly = !AcceptsInput;
            RefreshCommandAction(input.CompletionInput);
        }

        public bool TryFocusCommandInput()
        {
            if (!interactionEnabled || !isActiveAndEnabled) return false;
            MonitorPageHost host = GetComponent<MonitorPageHost>();
            if (host != null && (host.SystemMenuVisible || (host.CurrentPage?.Id != "kernel" && !host.TryNavigate("kernel")))) return false;
            if (!pageVisible) return false;
            if (AcceptsInput) input.FocusFromEnter();
            else
            {
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
                restoreCommandFocus = true;
            }
            return true;
        }

        public bool TryInsertObjectReference(string id, string label)
        {
            if (!isActiveAndEnabled || !Runtime.CanLinkObject(id)) return false;
            MonitorPageHost host = GetComponent<MonitorPageHost>();
            if (host != null && (host.SystemMenuVisible || (host.CurrentPage?.Id != "kernel" && !host.TryNavigate("kernel")))) return false;
            if (!input.InsertObjectReference(id, label)) return false;
            if (AcceptsInput) input.FocusDraft();
            return true;
        }

        private void RestoreCommandFocus()
        {
            if (!restoreCommandFocus) return;
            if (!isActiveAndEnabled || !interactionEnabled || !pageVisible)
            { restoreCommandFocus = false; return; }
            EventSystem events = EventSystem.current;
            if (events == null) return;
            // A reply may temporarily disable the field. Restore only the focus
            // owned by the submitted command; let another selected control keep it.
            if (events.currentSelectedGameObject != null && events.currentSelectedGameObject != input.gameObject)
            { restoreCommandFocus = false; return; }
            if (!AcceptsInput || Time.timeScale == 0f) return;
            restoreCommandFocus = false;
            input.FocusDraft();
        }

        private static readonly Color SelectedColor = new Color(0.16f, 0.32f, 0.27f, 1f);
        private static readonly Color NormalColor = new Color(0.055f, 0.10f, 0.09f, 1f);
        private TerminalSession session = new TerminalSession();
        private int runtimeRevision = -1;
        public void AttachRuntime(KernelRuntime runtime)
        {
            session = new TerminalSession(runtime);
            if (input != null) input.ClearDraft();
            ResetConversationPresentation();
            runtimeRevision = -1;
            GetComponent<MonitorPageHost>()?.AttachRuntime(runtime);
        }
        private readonly TerminalHistory history = new TerminalHistory();
        private IReadOnlyList<TerminalCompletion> completions = Array.Empty<TerminalCompletion>();
        private int selectedCompletion;
        private int firstVisibleCompletion;
        private readonly CompletionVisibility visibility = new CompletionVisibility();
        private bool scrollTranscript;
        private bool followTranscript;
        private Vector2 preservedTranscriptPosition;
        private string lastText;
        private int lastCaret = -1;
        private int lastAnchor = -1;
        private bool lastFocus;
        private bool lastComposing;

        private void OnEnable()
        {
            input.LineSubmitted += Submit;
            input.CanSubmitEmptyLine = CanContinueConversation;
            input.CompletionQuery = QueryCompletion;
            input.CompletionApplied += CompletionApplied;
            input.CompletionListRequested += RequestCompletions;
            input.CompletionSelectionMoved += MoveCompletion;
            input.CompletionDismissed += DismissCompletions;
            input.onValueChanged.AddListener(InputChanged);
            for (int i = 0; i < completionButtons.Length; i++)
            {
                int row = i;
                completionButtons[i].onClick.AddListener(() => AcceptVisibleCompletion(row));
            }
        }

        private void Start()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null && canvas.worldCamera == null)
                canvas.worldCamera = Camera.main;

            if (!session.Runtime.TerminalUsesGuidedStartup) history.Append("CODETECTIVE 메모리 터미널 준비 완료\n\n" +
                           "Kernel 작전망 연결 완료\n" + session.Runtime.StartupGuide);
            DrawTranscript(history.Content);
            RefreshCommandPrompt();
            completionPanel.SetActive(false);
            SetInteractionEnabled(interactionEnabled);
        }

        private void OnDisable()
        {
            restoreCommandFocus = false;
            ClearConversationDisturbance();
            input.LineSubmitted -= Submit;
            input.CanSubmitEmptyLine = null;
            input.CompletionQuery = null;
            input.CompletionApplied -= CompletionApplied;
            input.CompletionListRequested -= RequestCompletions;
            input.CompletionSelectionMoved -= MoveCompletion;
            input.CompletionDismissed -= DismissCompletions;
            input.onValueChanged.RemoveListener(InputChanged);
            foreach (var button in completionButtons)
                button.onClick.RemoveAllListeners();
        }

        private void LateUpdate()
        {
            UpdateConversationPresentation();
            RestoreCommandFocus();
            if (runtimeRevision != session.Runtime.Revision)
            {
                runtimeRevision = session.Runtime.Revision;
                RefreshCommandPrompt();
                DrawTranscript(ConversationTranscript());
                RefreshCompletions();
            }
            TerminalCompletionInput current = input.CompletionInput;
            RefreshCommandAction(current);
            bool textChanged = current.Text != lastText || current.IsComposing != lastComposing;
            if (textChanged || current.Caret != lastCaret || current.Anchor != lastAnchor ||
                input.isFocused != lastFocus)
            {
                // IME changes do not raise onValueChanged until the syllable is committed.
                if (textChanged)
                {
                    visibility.Edit();
                    selectedCompletion = 0;
                }
                RefreshCompletions(current);
            }

            if (scrollTranscript && pageVisible)
            {
                Canvas.ForceUpdateCanvases();
                if (followTranscript)
                {
                    transcriptScroll.StopMovement();
                    transcriptScroll.verticalNormalizedPosition = 0f;
                }
                else
                {
                    float maximum = Mathf.Max(0f, transcriptScroll.content.rect.height - TranscriptViewport.rect.height);
                    preservedTranscriptPosition.y = Mathf.Clamp(preservedTranscriptPosition.y, 0f, maximum);
                    transcriptScroll.content.anchoredPosition = preservedTranscriptPosition;
                }
                transcriptScroll.Rebuild(CanvasUpdate.PostLayout);
                scrollTranscript = false;
            }
        }

        private void BuildCommandAction()
        {
            var field = (RectTransform)input.transform;
            const float buttonWidth = 108f;
            const float inset = 6f;
            float reservedWidth = buttonWidth + inset;
            commandAction = MonitorWidgets.Button(field, input.textComponent.font, new Vector2(-reservedWidth, -inset),
                new Vector2(buttonWidth, field.rect.height - inset * 2), "CommandAction", ClickCommandAction);
            var buttonRect = (RectTransform)commandAction.transform;
            buttonRect.anchorMin = buttonRect.anchorMax = Vector2.one;
            buttonRect.anchoredPosition = new Vector2(-reservedWidth, -inset);
            commandActionLabel = commandAction.GetComponentInChildren<Text>();
            input.textComponent.rectTransform.offsetMax -= new Vector2(reservedWidth, 0);
            if (input.placeholder != null) input.placeholder.rectTransform.offsetMax -= new Vector2(reservedWidth, 0);
        }

        private void RefreshCommandAction(TerminalCompletionInput current)
        {
            if (commandAction == null)
            {
                if (!pageVisible) return;
                BuildCommandAction();
            }
            bool next = current.Text.Length == 0 && !input.IsCompositionProtected && Runtime.TerminalCanContinueLine;
            bool enabled = AcceptsInput && isActiveAndEnabled && Time.timeScale > 0f && (next || !string.IsNullOrWhiteSpace(current.Text));
            commandAction.interactable = enabled;
            commandActionLabel.text = next ? "Next" : "Send";
            commandActionLabel.color = enabled ? MonitorWidgets.Foreground : new Color(0.34f, 0.51f, 0.41f);
        }

        private void ClickCommandAction()
        {
            if (!AcceptsInput || !isActiveAndEnabled || Time.timeScale <= 0f) return;
            string visibleDraft = input.CompletionInput.Text;
            if (visibleDraft.Length == 0) { TryContinueConversation(); return; }
            if (string.IsNullOrWhiteSpace(visibleDraft)) return;

            // Commit any remaining IME text and cancel a queued keyboard submit before sending the draft.
            input.ReleaseFocus();
            Submit(input.text);
        }

        private void Submit(string line)
        {
            if (!AcceptsInput) return;
            if (line == "") { TryContinueConversation(); return; }
            if (string.IsNullOrWhiteSpace(line)) return;

            restoreCommandFocus = true;
            bool conversation = session.Runtime.TerminalConversationActive;
            MergeCompletedConversation();
            CommandResult result = session.ExecuteLinkedInput(input.EncodeReferences(line), line);
            if (!conversation) AppendHistory("> " + line + "\n" + (result.Success ? "" : "오류 ") + result.Message);

            input.ClearDraft();
            ApplyInputInteraction();
            RestoreCommandFocus();
            visibility.Edit();
            RefreshCompletions();
        }

        private void AppendHistory(string entry) => AppendHistoryContent(new ObjectReferenceText(entry));

        private void AppendHistoryContent(ObjectReferenceText entry)
        {
            QueueTranscriptScroll();
            string before = history.Text;
            string removed = history.AppendContent(entry);
            if (!followTranscript && removed.Length > 0)
            {
                string remaining = before.Substring(Math.Min(removed.Length, before.Length));
                preservedTranscriptPosition.y -= MeasureHistory(before) - MeasureHistory(remaining);
            }
            DrawTranscript(ConversationTranscript());
        }

        private void QueueTranscriptScroll(bool toLatest = false)
        {
            if (toLatest) followTranscript = true;
            else if (!scrollTranscript)
            {
                RectTransform content = transcriptScroll.content;
                preservedTranscriptPosition = content.anchoredPosition;
                followTranscript = TerminalHistory.ShouldFollowLatest(content.rect.height, TranscriptViewport.rect.height, preservedTranscriptPosition.y);
            }
            scrollTranscript = true;
        }

        private RectTransform TranscriptViewport => transcriptScroll.viewport != null ?
            transcriptScroll.viewport : (RectTransform)transcriptScroll.transform;

        private float MeasureHistory(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0f;
            var settings = transcript.GetGenerationSettings(new Vector2(transcript.rectTransform.rect.width, 0f));
            return transcript.cachedTextGeneratorForLayout.GetPreferredHeight(value, settings) / transcript.pixelsPerUnit;
        }

        private void InputChanged(string value)
        {
            // Deselect commits the composition before selecting a completion button.
            // Preserve the visible choices until that button's click can be delivered.
            if (input.IsLosingFocus)
                return;
            RefreshCommandAction(input.CompletionInput);
            visibility.Edit();
            selectedCompletion = 0;
            RefreshCompletions();
        }

        private void RefreshCompletions(bool showEmpty = false)
        {
            RefreshCompletions(input.CompletionInput, showEmpty);
        }

        private void RefreshCompletions(TerminalCompletionInput current, bool showEmpty = false)
        {
            if (showEmpty) visibility.Open();
            string previousSelection = selectedCompletion >= 0 && selectedCompletion < completions.Count ? completions[selectedCompletion].ReplacementText : null;
            lastText = current.Text;
            lastCaret = current.Caret;
            lastAnchor = current.Anchor;
            lastFocus = input.isFocused;
            lastComposing = current.IsComposing;

            // Pointer-down selects the candidate button before its click is delivered.
            // Keep the candidates alive until pointer-up can apply that choice.
            if (!lastFocus && EventSystem.current != null)
            {
                var selected = EventSystem.current.currentSelectedGameObject;
                foreach (var button in completionButtons)
                {
                    if (selected == button.gameObject)
                        return;
                }
            }

            string command = input.EncodeReferences(current.Text);
            completions = visibility.ShouldShow(current, lastFocus, AcceptsInput)
                ? session.Complete(command, current.IsComposing, linkObjects: true)
                : Array.Empty<TerminalCompletion>();
            // A fully typed word is already committed; keep longer names from taking it over.
            foreach (TerminalCompletion candidate in completions)
                if (candidate.ReplacementText == command) { completions = Array.Empty<TerminalCompletion>(); break; }
            if (previousSelection != null)
                for (int i = 0; i < completions.Count; i++)
                    if (completions[i].ReplacementText == previousSelection) { selectedCompletion = i; break; }
            selectedCompletion = Mathf.Clamp(selectedCompletion, 0, Mathf.Max(0, completions.Count - 1));
            DrawCompletions();
        }

        private void DrawCompletions()
        {
            completionPanel.SetActive(completions.Count > 0);
            input.SetInlinePreview(completions.Count > 0 ? DisplayCompletion(completions[selectedCompletion]) : (InlineCompletion?)null);
            if (completions.Count == 0)
                return;

            int visibleRows = completionButtons.Length;
            firstVisibleCompletion = Mathf.Clamp(selectedCompletion - visibleRows + 1, 0,
                Mathf.Max(0, completions.Count - visibleRows));
            completionHeading.text = (session.Runtime.Language == CommandLocale.Korean ? "권한 목록  " : "COMMANDS  ") + $"{selectedCompletion + 1}/{completions.Count}";
            for (int row = 0; row < visibleRows; row++)
            {
                int index = firstVisibleCompletion + row;
                completionButtons[row].gameObject.SetActive(index < completions.Count);
                if (index >= completions.Count)
                    continue;
                completionLabels[row].text = completions[index].DisplayText;
                completionButtons[row].image.color = index == selectedCompletion ? SelectedColor : NormalColor;
            }
        }

        private void MoveCompletion(int direction)
        {
            if (completions.Count == 0)
            {
                visibility.Open();
                RefreshCompletions(true);
                return;
            }
            selectedCompletion = (selectedCompletion + direction + completions.Count) % completions.Count;
            DrawCompletions();
        }

        private InlineCompletion? QueryCompletion(TerminalCompletionInput current)
        {
            if (current.Text != lastText || current.IsComposing != lastComposing)
            {
                visibility.Edit();
                selectedCompletion = 0;
            }
            RefreshCompletions(current);
            if (completions.Count == 0) return null;
            return DisplayCompletion(completions[selectedCompletion]);
        }

        private InlineCompletion DisplayCompletion(TerminalCompletion completion) => input.DecodeCompletion(completion.ReplacementText,
            id => Runtime.CanLinkObject(id) ? Runtime.ObjectName(id) : null);

        private void RequestCompletions() { visibility.Open(); RefreshCompletions(); }

        private void CompletionApplied()
        {
            visibility.Edit();
            selectedCompletion = 0;
            RefreshCompletions();
        }

        private void AcceptVisibleCompletion(int row)
        {
            int index = firstVisibleCompletion + row;
            if (index >= completions.Count)
                return;
            selectedCompletion = index;
            input.AcceptCompletion(DisplayCompletion(completions[index]));
        }

        private void DismissCompletions()
        {
            visibility.Dismiss();
            RefreshCompletions();
        }
    }
}
