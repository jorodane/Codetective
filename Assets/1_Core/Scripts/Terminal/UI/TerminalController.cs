using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Codetective.Operations;

namespace Codetective.Terminal
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class TerminalController : MonoBehaviour
    {
        [SerializeField] private TerminalInputField input;
        [SerializeField] private Text transcript;
        [SerializeField] private ScrollRect transcriptScroll;
        [SerializeField] private Text memoryList;
        [SerializeField] private Text memoryHeading;
        [SerializeField] private GameObject completionPanel;
        [SerializeField] private Text completionHeading;
        [SerializeField] private Button[] completionButtons;
        [SerializeField] private Text[] completionLabels;
        [SerializeField] private bool interactionEnabled = true;

        public bool InteractionEnabled => interactionEnabled;
        public bool IsCompositionProtected => input != null && input.IsCompositionProtected;

        public void SetInteractionEnabled(bool enabled)
        {
            interactionEnabled = enabled;
            if (!enabled)
            {
                var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                if (selected != null && selected.transform.IsChildOf(transform))
                    EventSystem.current.SetSelectedGameObject(null);
                input.ReleaseFocus();
                DismissCompletions();
            }
            input.interactable = enabled;
            input.readOnly = !enabled;
            var group = GetComponent<CanvasGroup>();
            if (group != null)
            {
                group.interactable = enabled;
                group.blocksRaycasts = enabled;
            }
            if (enabled)
                input.FocusAtEnd();
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
            runtimeRevision = -1;
        }
        private void QueueReport(string message) => reports.Enqueue(message);
        private void OnDestroy() => session.Runtime.Message -= QueueReport;
        private readonly TerminalHistory history = new TerminalHistory();
        private IReadOnlyList<TerminalCompletion> completions = Array.Empty<TerminalCompletion>();
        private int selectedCompletion;
        private int firstVisibleCompletion;
        private bool dismissed;
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

            history.Append("CODETECTIVE 메모리 터미널 준비 완료\n\n" +
                           "Kernel 작전망 연결 완료\n" + session.Runtime.StartupGuide + "\n\n" +
                           "Tab / Enter / Space 자동완성 확정 · 다음 Enter 실행\n휠이나 스크롤바로 이전 기록을 볼 수 있습니다.");
            transcript.text = history.Text;
            RefreshMemory();
            completionPanel.SetActive(false);
            SetInteractionEnabled(interactionEnabled);
        }

        private void OnDisable()
        {
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
            if (runtimeRevision != session.Runtime.Revision)
            {
                runtimeRevision = session.Runtime.Revision;
                RefreshMemory();
                if (input.placeholder is Text placeholder)
                    placeholder.text = session.Runtime.Language == CommandLocale.Korean ? "명령 입력 · 도움말 / 저장 이름" : "Command · help / save name";
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
                    dismissed = false;
                    selectedCompletion = 0;
                }
                RefreshCompletions(current);
            }

            if (scrollTranscript)
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
            if (!interactionEnabled || string.IsNullOrWhiteSpace(line))
                return;

            CommandResult result = session.Execute(line);
            AppendHistory("> " + line + "\n" + (result.Success ? "" : "오류 ") + result.Message);
            RefreshMemory();

            input.SetTextWithoutNotify(string.Empty);
            input.FocusAtEnd();
            dismissed = false;
            RefreshCompletions();
        }

        private void AppendHistory(string entry)
        {
            RectTransform content = transcriptScroll.content;
            followTranscript = TerminalHistory.ShouldFollowLatest(content.rect.height,
                TranscriptViewport.rect.height, content.anchoredPosition.y);
            preservedTranscriptPosition = content.anchoredPosition;
            string before = history.Text;
            string removed = history.Append(entry);
            if (!followTranscript && removed.Length > 0)
            {
                string remaining = before.Substring(Math.Min(removed.Length, before.Length));
                preservedTranscriptPosition.y -= MeasureHistory(before) - MeasureHistory(remaining);
            }
            transcript.text = history.Text;
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

        private void RefreshMemory()
        {
            memoryHeading.text = (session.Runtime.Language == CommandLocale.Korean ? "데이터 / " : "DATA / ") + session.Count;
            if (session.Count == 0)
            {
                memoryList.text = session.Runtime.Language == CommandLocale.Korean ? "할당된 공간 없음\n\n저장 이름\n명령으로 공간을\n만들어 보세요." : "No allocated data\n\nsave name\nallocates a new slot.";
                return;
            }

            var text = new StringBuilder();
            foreach (var slot in session.Memory)
                text.Append(slot.Name).Append("\n").Append(session.Runtime.KindName(slot.Value.kind)).Append(" · ").Append(slot.Value.label).Append("\n\n");
            memoryList.text = text.ToString();
        }

        private void InputChanged(string value)
        {
            // Deselect commits the composition before selecting a completion button.
            // Preserve the visible choices until that button's click can be delivered.
            if (input.IsLosingFocus)
                return;
            dismissed = false;
            selectedCompletion = 0;
            RefreshCompletions();
        }

        private void RefreshCompletions(bool showEmpty = false)
        {
            RefreshCompletions(input.CompletionInput, showEmpty);
        }

        private void RefreshCompletions(TerminalCompletionInput current, bool showEmpty = false)
        {
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

            completions = interactionEnabled && !dismissed && lastFocus && current.IsAtEnd &&
                          (showEmpty || !string.IsNullOrWhiteSpace(lastText))
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
                dismissed = false;
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
                dismissed = false;
                selectedCompletion = 0;
            }
            RefreshCompletions(current);
            if (completions.Count == 0) return null;
            string value = completions[selectedCompletion].ReplacementText;
            return new InlineCompletion(value, value.Length);
        }

        private void RequestCompletions() { dismissed = false; RefreshCompletions(true); }

        private void CompletionApplied()
        {
            dismissed = false;
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
            dismissed = true;
            RefreshCompletions();
        }
    }
}
