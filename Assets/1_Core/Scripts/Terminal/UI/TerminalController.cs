using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

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

        private const int HistoryLimit = 100;
        private static readonly Color SelectedColor = new Color(0.16f, 0.32f, 0.27f, 1f);
        private static readonly Color NormalColor = new Color(0.055f, 0.10f, 0.09f, 1f);
        private readonly TerminalSession session = new TerminalSession();
        private readonly Queue<string> history = new Queue<string>();
        private IReadOnlyList<TerminalCompletion> completions = Array.Empty<TerminalCompletion>();
        private int selectedCompletion;
        private int firstVisibleCompletion;
        private bool dismissed;
        private bool scrollTranscript;
        private string lastText;
        private int lastCaret = -1;
        private int lastAnchor = -1;
        private bool lastFocus;
        private bool lastComposing;

        private void OnEnable()
        {
            input.LineSubmitted += Submit;
            input.CompletionRequested += AcceptCompletion;
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

            transcript.text = "CODETECTIVE 메모리 터미널 준비 완료\n\n" +
                              "저장 이름   : 변수 공간 할당\n삭제 이름   : 변수 공간 해제\n\n" +
                              "명령을 입력하고 Enter를 누르세요.\n자동완성은 Tab으로 적용합니다.";
            RefreshMemory();
            completionPanel.SetActive(false);
            SetInteractionEnabled(interactionEnabled);
        }

        private void OnDisable()
        {
            input.LineSubmitted -= Submit;
            input.CompletionRequested -= AcceptCompletion;
            input.CompletionSelectionMoved -= MoveCompletion;
            input.CompletionDismissed -= DismissCompletions;
            input.onValueChanged.RemoveListener(InputChanged);
            foreach (var button in completionButtons)
                button.onClick.RemoveAllListeners();
        }

        private void LateUpdate()
        {
            if (input.text != lastText || input.caretPosition != lastCaret ||
                input.selectionAnchorPosition != lastAnchor || input.isFocused != lastFocus ||
                input.IsComposing != lastComposing)
            {
                RefreshCompletions();
            }

            if (scrollTranscript)
            {
                Canvas.ForceUpdateCanvases();
                transcriptScroll.verticalNormalizedPosition = 0f;
                scrollTranscript = false;
            }
        }

        private void Submit(string line)
        {
            if (!interactionEnabled || string.IsNullOrWhiteSpace(line))
                return;

            CommandResult result = session.Execute(line);
            history.Enqueue("> " + line + "\n" + (result.Success ? "OK   " : "오류 ") + result.Message);
            while (history.Count > HistoryLimit)
                history.Dequeue();
            transcript.text = string.Join("\n\n", history);
            scrollTranscript = true;
            RefreshMemory();

            input.SetTextWithoutNotify(string.Empty);
            input.FocusAtEnd();
            dismissed = false;
            RefreshCompletions();
        }

        private void RefreshMemory()
        {
            memoryHeading.text = "MEMORY / " + session.Count;
            if (session.Count == 0)
            {
                memoryList.text = "할당된 공간 없음\n\n저장 이름\n명령으로 공간을\n만들어 보세요.";
                return;
            }

            var text = new StringBuilder();
            foreach (var slot in session.Memory)
                text.Append('#').Append(slot.Id.ToString("D4")).Append("  ").Append(slot.Name).Append('\n');
            memoryList.text = text.ToString();
        }

        private void InputChanged(string value)
        {
            dismissed = false;
            selectedCompletion = 0;
            RefreshCompletions();
        }

        private void RefreshCompletions(bool showEmpty = false)
        {
            lastText = input.text;
            lastCaret = input.caretPosition;
            lastAnchor = input.selectionAnchorPosition;
            lastFocus = input.isFocused;
            lastComposing = input.IsComposing;

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

            bool atEnd = lastCaret == lastText.Length && lastAnchor == lastText.Length;
            completions = !dismissed && lastFocus && !lastComposing && atEnd &&
                          (showEmpty || !string.IsNullOrWhiteSpace(lastText))
                ? session.Complete(lastText)
                : Array.Empty<TerminalCompletion>();
            selectedCompletion = Mathf.Clamp(selectedCompletion, 0, Mathf.Max(0, completions.Count - 1));
            DrawCompletions();
        }

        private void DrawCompletions()
        {
            completionPanel.SetActive(completions.Count > 0);
            if (completions.Count == 0)
                return;

            int visibleRows = completionButtons.Length;
            firstVisibleCompletion = Mathf.Clamp(selectedCompletion - visibleRows + 1, 0,
                Mathf.Max(0, completions.Count - visibleRows));
            completionHeading.text = $"자동완성  {selectedCompletion + 1}/{completions.Count}";
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

        private void AcceptCompletion()
        {
            if (!interactionEnabled)
                return;
            if (completions.Count == 0)
            {
                dismissed = false;
                RefreshCompletions(true);
            }
            if (completions.Count == 0)
                return;

            input.SetTextWithoutNotify(completions[selectedCompletion].ReplacementText);
            input.FocusAtEnd();
            // Completing a command opens its argument candidates. Completing a name closes the menu.
            dismissed = !input.text.EndsWith(" ", StringComparison.Ordinal);
            selectedCompletion = 0;
            RefreshCompletions();
        }

        private void AcceptVisibleCompletion(int row)
        {
            int index = firstVisibleCompletion + row;
            if (index >= completions.Count)
                return;
            selectedCompletion = index;
            AcceptCompletion();
        }

        private void DismissCompletions()
        {
            dismissed = true;
            RefreshCompletions();
        }
    }
}
