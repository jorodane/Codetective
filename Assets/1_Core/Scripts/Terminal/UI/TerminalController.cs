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
        private bool AcceptsInput => interactionEnabled && pageVisible && !session.Runtime.TerminalInputLocked;

        public void SetPageVisible(bool visible)
        {
            if (pageVisible == visible) return;
            pageVisible = visible;
            if (!visible) { restoreCommandFocus = false; ClearConversationDisturbance(); }
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
            conversationInputLocked = session.Runtime.TerminalInputLocked;
            if (!AcceptsInput)
            {
                var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                if (selected == input.gameObject) EventSystem.current.SetSelectedGameObject(null);
                input.ReleaseFocus();
                DismissCompletions();
            }
            input.interactable = AcceptsInput;
            input.readOnly = !AcceptsInput;
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
            input.FocusFromEnter();
        }

        private static readonly Color SelectedColor = new Color(0.16f, 0.32f, 0.27f, 1f);
        private static readonly Color NormalColor = new Color(0.055f, 0.10f, 0.09f, 1f);
        private TerminalSession session = new TerminalSession();
        private readonly Queue<string> reports = new Queue<string>();
        private int runtimeRevision = -1;
        public void AttachRuntime(KernelRuntime runtime)
        {
            session.Runtime.Message -= QueueReport;
            session = new TerminalSession(runtime);
            session.Runtime.Message += QueueReport;
            ResetConversationPresentation();
            runtimeRevision = -1;
            GetComponent<MonitorPageHost>()?.AttachRuntime(runtime);
        }
        private void QueueReport(string message)
        { if (session.Runtime.TerminalConversationActive) session.Runtime.AppendTerminalConversationReport(message); else reports.Enqueue(message); }
        private void OnDestroy() => session.Runtime.Message -= QueueReport;
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

            history.Append(session.Runtime.TerminalUsesGuidedStartup ? "CODETECTIVE / KERNEL\n" + session.Runtime.Text("terminal.dialogue.standby", "작전망 연결 대기") : "CODETECTIVE 메모리 터미널 준비 완료\n\n" +
                           "Kernel 작전망 연결 완료\n" + session.Runtime.StartupGuide + "\n\n" +
                           "입력창 클릭 후 명령 입력 · Tab / Enter로 자동완성 확정 · 후보 이름 입력 중 Space 확정 · 다음 Enter 실행\n" +
                           "입력하지 않을 때 Tab 다음 페이지 / Shift+Tab 이전 페이지\n휠이나 스크롤바로 이전 기록을 볼 수 있습니다.");
            transcript.text = history.Text;
            RefreshCommandPrompt();
            completionPanel.SetActive(false);
            SetInteractionEnabled(interactionEnabled);
        }

        private void OnDisable()
        {
            restoreCommandFocus = false;
            ClearConversationDisturbance();
            input.LineSubmitted -= Submit;
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
            while (reports.Count > 0) AppendHistory("[보고] " + reports.Dequeue());
            UpdateConversationPresentation();
            RestoreCommandFocus();
            if (runtimeRevision != session.Runtime.Revision)
            {
                runtimeRevision = session.Runtime.Revision;
                RefreshCommandPrompt();
                RefreshCompletions();
            }
            TerminalCompletionInput current = input.CompletionInput;
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
                    transcriptScroll.verticalNormalizedPosition = 0f;
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

        private void Submit(string line)
        {
            if (!AcceptsInput || string.IsNullOrWhiteSpace(line))
                return;

            restoreCommandFocus = true;
            bool conversation = session.Runtime.TerminalConversationActive;
            MergeCompletedConversation();
            CommandResult result = session.Execute(line);
            if (!conversation) AppendHistory("> " + line + "\n" + (result.Success ? "" : "오류 ") + result.Message);

            input.SetTextWithoutNotify(string.Empty);
            ApplyInputInteraction();
            RestoreCommandFocus();
            visibility.Edit();
            RefreshCompletions();
        }

        private void AppendHistory(string entry)
        {
            RectTransform content = transcriptScroll.content;
            if (!scrollTranscript)
            {
                followTranscript = TerminalHistory.ShouldFollowLatest(content.rect.height,
                    TranscriptViewport.rect.height, content.anchoredPosition.y);
                preservedTranscriptPosition = content.anchoredPosition;
            }
            string before = history.Text;
            string removed = history.Append(entry);
            if (!followTranscript && removed.Length > 0)
            {
                string remaining = before.Substring(Math.Min(removed.Length, before.Length));
                preservedTranscriptPosition.y -= MeasureHistory(before) - MeasureHistory(remaining);
            }
            transcript.text = ConversationTranscript();
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

            completions = visibility.ShouldShow(current, lastFocus, AcceptsInput)
                ? session.Complete(lastText, current.IsComposing)
                : Array.Empty<TerminalCompletion>();
            // A fully typed word is already committed; keep longer names from taking it over.
            foreach (TerminalCompletion candidate in completions)
                if (candidate.ReplacementText == current.Text) { completions = Array.Empty<TerminalCompletion>(); break; }
            if (previousSelection != null)
                for (int i = 0; i < completions.Count; i++)
                    if (completions[i].ReplacementText == previousSelection) { selectedCompletion = i; break; }
            selectedCompletion = Mathf.Clamp(selectedCompletion, 0, Mathf.Max(0, completions.Count - 1));
            DrawCompletions();
        }

        private void DrawCompletions()
        {
            completionPanel.SetActive(completions.Count > 0);
            input.SetInlinePreview(completions.Count > 0 ? new InlineCompletion(completions[selectedCompletion].ReplacementText,
                completions[selectedCompletion].ReplacementText.Length) : (InlineCompletion?)null);
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
            string value = completions[selectedCompletion].ReplacementText;
            return new InlineCompletion(value, value.Length);
        }

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
            string value = completions[index].ReplacementText;
            input.AcceptCompletion(new InlineCompletion(value, value.Length));
        }

        private void DismissCompletions()
        {
            visibility.Dismiss();
            RefreshCompletions();
        }
    }
}
