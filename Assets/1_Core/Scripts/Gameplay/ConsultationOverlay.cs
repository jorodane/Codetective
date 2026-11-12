using System;
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
        private RectTransform bubble;
        private Text speaker, sentence, feedback;
        private Button main, clue, hmm, ah;
        private AudioSource audioSource;
        private string positionedView;
        private int nextAcknowledgement;
        private float readyAt;
        public void Initialize(KernelGameController game, InspectionController inspection, Transform parent, Font font, DialoguePresentationDefinition presentation)
        {
            this.game = game; this.inspection = inspection; this.presentation = presentation;
            bubble = OverlayWidgets.Rect("PatientSpeechBubble", parent, Vector2.zero, new Vector2(680, 248), new Vector2(0.5f, 0));
            Image image = bubble.gameObject.AddComponent<Image>(); image.color = new Color(0.08f, 0.1f, 0.085f, 0.97f);
            main = bubble.gameObject.AddComponent<Button>(); main.targetGraphic = image;
            main.onClick.AddListener(() => Acknowledge(nextAcknowledgement++ % 2));
            speaker = OverlayWidgets.Label(bubble, font, new Vector2(16, -10), new Vector2(640, 28), "", 21);
            sentence = OverlayWidgets.Label(bubble, font, new Vector2(16, -46), new Vector2(640, 96), "", 24);
            clue = OverlayWidgets.Button(bubble, font, Vector2.zero, new Vector2(640, 32), "", CollectClue);
            clue.GetComponentInChildren<Text>().color = new Color(1, 0.38f, 0.32f);
            hmm = OverlayWidgets.Button(bubble, font, Vector2.zero, new Vector2(310, 36), "음.", () => Acknowledge(0));
            ah = OverlayWidgets.Button(bubble, font, Vector2.zero, new Vector2(310, 36), "아아.", () => Acknowledge(1));
            feedback = OverlayWidgets.Label(bubble, font, Vector2.zero, new Vector2(640, 24), "말풍선을 눌러 응답", 17);
            audioSource = gameObject.AddComponent<AudioSource>(); audioSource.playOnAwake = false; audioSource.spatialBlend = 0;
            Position(); Refresh();
        }
        private void Update()
        {
            if (game == null || bubble == null) return;
            if (positionedView != game.CurrentView) Position();
            Refresh();
        }
        private void Position()
        {
            positionedView = game.CurrentView;
            DialoguePlacement placement = presentation != null ? presentation.placements.FirstOrDefault(p => p.angleId == positionedView) : null;
            placement = placement ?? new DialoguePlacement();
            bubble.anchorMin = bubble.anchorMax = bubble.pivot = placement.anchor;
            bubble.anchoredPosition = placement.offset; bubble.sizeDelta = placement.size;
            float width = placement.size.x - 32, height = placement.size.y;
            speaker.rectTransform.sizeDelta = new Vector2(width, 28);
            sentence.rectTransform.sizeDelta = new Vector2(width, height - 150);
            Place(clue, new Vector2(16, -height + 96), new Vector2(width, 32));
            Place(hmm, new Vector2(16, -height + 58), new Vector2((width - 10) / 2, 34));
            Place(ah, new Vector2(26 + (width - 10) / 2, -height + 58), new Vector2((width - 10) / 2, 34));
            feedback.rectTransform.anchoredPosition = new Vector2(16, -height + 23); feedback.rectTransform.sizeDelta = new Vector2(width, 22);
        }
        private static void Place(Button button, Vector2 position, Vector2 size)
        {
            RectTransform rect = (RectTransform)button.transform; rect.anchoredPosition = position; rect.sizeDelta = size;
            button.GetComponentInChildren<Text>().rectTransform.sizeDelta = size - new Vector2(12, 6);
        }
        private void Refresh()
        {
            ConsultationLine line = game.Runtime.CurrentConsultation;
            bool visible = line != null && !game.Runtime.IsDayEnded && inspection.Mode != InspectionMode.Menu;
            bubble.gameObject.SetActive(visible); if (!visible) return;
            speaker.text = line.speaker; sentence.text = line.sentence;
            clue.GetComponentInChildren<Text>().text = "● " + line.keyword + (game.Runtime.RecordedClues.Contains(line.fact) ? "  [기록됨]" : "");
            bool ready = Time.unscaledTime >= readyAt && !game.IsViewTransitioning;
            main.interactable = hmm.interactable = ah.interactable = clue.interactable = ready;
        }
        private void Acknowledge(int index)
        {
            if (Time.unscaledTime < readyAt || game.IsViewTransitioning || inspection.Mode == InspectionMode.Menu || game.Runtime.IsDayEnded || game.Runtime.CurrentConsultation == null) return;
            AudioClip clip = presentation != null && index < presentation.acknowledgements.Length ? presentation.acknowledgements[index] : null;
            if (clip != null) audioSource.PlayOneShot(clip);
            readyAt = Time.unscaledTime + Mathf.Max(0.25f, clip != null ? clip.length : 0);
            feedback.text = index == 0 ? "음." : "아아."; game.Runtime.Reply(); Refresh();
        }
        private void CollectClue()
        {
            try { game.Runtime.RecordClue(game.Runtime.ConsultationIndex); feedback.text = "프로필에 단서를 기록했습니다."; }
            catch (CommandException error) { feedback.text = error.Message; }
        }
    }
}
