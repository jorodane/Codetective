using UnityEngine;
using UnityEngine.EventSystems;

namespace Codetective.Gameplay
{
    public sealed class PaperImageClue : MonoBehaviour, IPointerClickHandler
    {
        public PaperClue Clue;
        public void OnPointerClick(PointerEventData eventData) { if (eventData.button == PointerEventData.InputButton.Left) Clue?.collect?.Invoke(); }
    }
}
