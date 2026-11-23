using System.Linq;
using Codetective.Interaction;
using Codetective.Operations;
using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Gameplay
{
    /// <summary>The person across the desk speaks here. The monitor stores only deliberately collected clues and notes.</summary>
    public sealed class ConsultationOverlay : MonoBehaviour
    {
        private KernelGameController game;
        private InspectionController inspection;
        private DialoguePresentationDefinition presentation;
        private Camera cameraView;
        private Renderer[] terminalParts;
        private RectTransform bubble;
        private Text speaker, sentence, feedback;
        private Button main, clue, firstResponse, secondResponse;
        private AudioSource audioSource;
        private bool reservedCameraSpace;
        private const float Margin = 16f;
        private int nextAcknowledgement;
        private float readyAt;
        private bool showingSequence;
        public float TopInset => bubble != null && bubble.gameObject.activeSelf ? bubble.sizeDelta.y + 2f * Margin : 0f;

        public void Initialize(KernelGameController game, InspectionController inspection, Transform parent, Font font,
            DialoguePresentationDefinition presentation, Camera cameraView, Transform terminalBody)
        {
            this.game = game; this.inspection = inspection; this.presentation = presentation; this.cameraView = cameraView;
            terminalParts = terminalBody != null ? terminalBody.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            bubble = OverlayWidgets.Rect("PatientSpeechBubble", parent, new Vector2(-Margin, -Margin), new Vector2(560, 216), Vector2.one);
            Image image = bubble.gameObject.AddComponent<Image>(); image.color = new Color(0.08f, 0.1f, 0.085f);
            main = bubble.gameObject.AddComponent<Button>(); main.targetGraphic = image;
            main.transition = Selectable.Transition.None;
            main.onClick.AddListener(() => Acknowledge(nextAcknowledgement++ % 2));
            speaker = OverlayWidgets.Label(bubble, font, new Vector2(16, -8), new Vector2(640, 26), "", 20);
            sentence = OverlayWidgets.Label(bubble, font, new Vector2(16, -39), new Vector2(640, 96), "", 22);
            sentence.resizeTextForBestFit = true; sentence.resizeTextMinSize = 16; sentence.resizeTextMaxSize = 22;
            clue = OverlayWidgets.Button(bubble, font, Vector2.zero, new Vector2(640, 32), "", CollectClue);
            clue.GetComponentInChildren<Text>().color = new Color(1, 0.38f, 0.32f);
            firstResponse = OverlayWidgets.Button(bubble, font, Vector2.zero, new Vector2(310, 36), "음.", () => Respond(0));
            secondResponse = OverlayWidgets.Button(bubble, font, Vector2.zero, new Vector2(310, 36), "아아.", () => Respond(1));
            foreach (Button response in new[] { firstResponse, secondResponse })
            {
                Text label = response.GetComponentInChildren<Text>();
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 14;
                label.resizeTextMaxSize = 20;
            }
            feedback = OverlayWidgets.Label(bubble, font, Vector2.zero, new Vector2(640, 24), "말풍선을 눌러 응답", 17);
            audioSource = gameObject.AddComponent<AudioSource>(); audioSource.playOnAwake = false; audioSource.spatialBlend = 0;
            Refresh();
        }
        private void LateUpdate()
        {
            if (game == null || bubble == null) return;
            Refresh();
        }
        private void Position()
        {
            DialoguePlacement placement = presentation != null ? presentation.placements.FirstOrDefault(p => p.angleId == game.CurrentView) : null;
            Vector2 preferred = placement != null ? placement.size : new Vector2(560, 216);
            Rect screen = ((RectTransform)bubble.parent).rect;
            if (screen.width <= Margin * 2f || screen.height <= Margin * 2f) return;
            float width = Mathf.Min(preferred.x, screen.width - Margin * 2f);
            float height = Mathf.Min(preferred.y, screen.height - Margin * 2f);
            float minimum = Mathf.Min(height, 196f);
            float terminalTop = TerminalTop(1f - (width + Margin) / screen.width);
            float available = (1f - terminalTop) * screen.height - Margin * 2f;
            height = Mathf.Clamp(available, minimum, height);
            if (available < minimum && terminalTop > 0f)
            {
                // Keep the authored camera pose/FOV. Open up the rendered view from the
                // bottom edge so the whole CRT stays clickable under the reserved space.
                float scale = (1f - (height + Margin * 2f) / screen.height) / terminalTop;
                Matrix4x4 framing = Matrix4x4.identity;
                framing.m00 = framing.m11 = Mathf.Clamp(scale, 0.1f, 1f);
                framing.m13 = framing.m11 - 1f;
                cameraView.projectionMatrix = framing * cameraView.projectionMatrix;
                reservedCameraSpace = true;
            }
            bubble.sizeDelta = new Vector2(width, height);
            width -= 32f;
            speaker.rectTransform.sizeDelta = new Vector2(width, 26);
            Place(clue, new Vector2(16, -height + 83), new Vector2(width, 28));
            Place(firstResponse, new Vector2(16, -height + 52), new Vector2((width - 10) / 2, 29));
            Place(secondResponse, new Vector2(26 + (width - 10) / 2, -height + 52), new Vector2((width - 10) / 2, 29));
            feedback.rectTransform.anchoredPosition = new Vector2(16, -height + 21); feedback.rectTransform.sizeDelta = new Vector2(width, 20);
        }

        private float TerminalTop(float bubbleLeft)
        {
            float top = 0;
            if (cameraView == null) return top;
            foreach (Renderer part in terminalParts)
            {
                if (part == null || !part.enabled || !part.gameObject.activeInHierarchy) continue;
                Bounds bounds = part.bounds;
                Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
                Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 point = cameraView.WorldToViewportPoint(corner);
                    if (point.z <= cameraView.nearClipPlane) continue;
                    min = Vector2.Min(min, point); max = Vector2.Max(max, point);
                }
                if (max.x >= bubbleLeft && min.x < 1f && min.y < 1f) top = Mathf.Max(top, max.y);
            }
            return top;
        }

        private void RestoreCameraFraming()
        {
            if (reservedCameraSpace && cameraView != null) cameraView.ResetProjectionMatrix();
            reservedCameraSpace = false;
        }

        private void OnDisable() { RestoreCameraFraming(); if (bubble != null) bubble.gameObject.SetActive(false); }
        private static void Place(Button button, Vector2 position, Vector2 size)
        {
            RectTransform rect = (RectTransform)button.transform; rect.anchoredPosition = position; rect.sizeDelta = size;
            button.GetComponentInChildren<Text>().rectTransform.sizeDelta = size - new Vector2(12, 6);
        }
        private void Refresh()
        {
            ConsultationLine line = game.Runtime.CurrentConsultation;
            AppointmentProgress visit = game.Runtime.CurrentAppointment;
            bool sequence = game.Runtime.IsClinicTransitioning;
            bool visible = (line != null || sequence) && !game.IsDateTransitioning && !inspection.PresentingOnTerminal && inspection.Mode != InspectionMode.Menu &&
                !inspection.ModalInputBlocked && (game.Carry == null || game.Carry.HeldItem == null);
            RestoreCameraFraming();
            bubble.gameObject.SetActive(visible); if (!visible) return;
            Position();
            bool hasClue = !sequence && !string.IsNullOrWhiteSpace(line.keyword);
            int choices = sequence ? 0 : line.ResponseCount;
            bool routine = !string.IsNullOrEmpty(visit?.episodeId);
            bool acknowledgements = !sequence && !routine && choices == 0;
            clue.gameObject.SetActive(hasClue);
            firstResponse.gameObject.SetActive(acknowledgements || choices >= 1);
            secondResponse.gameObject.SetActive(acknowledgements || choices >= 2);
            sentence.rectTransform.sizeDelta = new Vector2(bubble.sizeDelta.x - 32f,
                bubble.sizeDelta.y - (hasClue ? 130f : acknowledgements || choices > 0 ? 98f : 66f));
            if (sequence)
            {
                showingSequence = true;
                speaker.text = visit.phase == VisitPhase.Closing ? "Kernel" : visit.patientName;
                sentence.text = game.Runtime.VisitPresentationText; main.interactable = false;
                feedback.text = visit.phase == VisitPhase.Arriving ? "입실 중" : visit.phase == VisitPhase.Departing ? "퇴실 중" : "상담 마무리";
                return;
            }
            if (showingSequence) { showingSequence = false; feedback.text = "말풍선을 눌러 응답"; }
            speaker.text = visit?.patientName ?? line.speaker; sentence.text = line.sentence;
            if (hasClue) clue.GetComponentInChildren<Text>().text = "● " + line.keyword + (game.Runtime.RecordedClues.Contains(line.fact) ? "  [기록됨]" : "");
            if (firstResponse.gameObject.activeSelf) firstResponse.GetComponentInChildren<Text>().text = choices >= 1 ? line.responses[0] : "음.";
            if (secondResponse.gameObject.activeSelf) secondResponse.GetComponentInChildren<Text>().text = choices >= 2 ? line.responses[1] : "아아.";
            bool ready = CanReply;
            main.interactable = ready && choices == 0;
            firstResponse.interactable = secondResponse.interactable = clue.interactable = ready;
            if (choices > 0) feedback.text = "응답을 고르세요.";
            else if (routine && ready) feedback.text = "말풍선을 눌러 응답";
        }
        private bool CanReply => Time.unscaledTime >= readyAt && !game.IsViewTransitioning && !inspection.ModalInputBlocked &&
            inspection.Mode != InspectionMode.Menu && !game.Runtime.IsDayEnded;
        private void Respond(int index)
        {
            ConsultationLine line = game.Runtime.CurrentConsultation;
            if (!CanReply || line == null) return;
            if (line.ResponseCount == 0) { Acknowledge(index); return; }
            if (index >= line.ResponseCount) return;
            readyAt = Time.unscaledTime + 0.25f;
            feedback.text = line.responses[index];
            game.Runtime.ReplyToConsultationChoice(index); Refresh();
        }
        private void Acknowledge(int index)
        {
            if (!CanReply || game.Runtime.CurrentConsultation?.ResponseCount != 0) return;
            AudioClip clip = presentation != null && index < presentation.acknowledgements.Length ? presentation.acknowledgements[index] : null;
            if (clip != null) audioSource.PlayOneShot(clip);
            readyAt = Time.unscaledTime + Mathf.Max(0.25f, clip != null ? clip.length : 0);
            feedback.text = index == 0 ? "음." : "아아."; game.Runtime.Reply(); Refresh();
        }
        private void CollectClue()
        {
            if (inspection.ModalInputBlocked || inspection.Mode == InspectionMode.Menu) return;
            try { game.Runtime.RecordClue(game.Runtime.ConsultationIndex); feedback.text = "프로필에 단서를 기록했습니다."; }
            catch (CommandException error) { feedback.text = error.Message; }
        }
    }
}
