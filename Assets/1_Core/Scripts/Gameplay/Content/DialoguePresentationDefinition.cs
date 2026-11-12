using System;
using UnityEngine;

namespace Codetective.Gameplay
{
    [CreateAssetMenu(menuName = "Codetective/Story/Dialogue Presentation", fileName = "DialoguePresentation")]
    public sealed class DialoguePresentationDefinition : ScriptableObject
    {
        public AudioClip[] acknowledgements = Array.Empty<AudioClip>();
        public DialoguePlacement[] placements = Array.Empty<DialoguePlacement>();
    }
    [Serializable] public sealed class DialoguePlacement
    {
        public string angleId;
        public Vector2 anchor = new Vector2(0.5f, 0);
        public Vector2 offset = new Vector2(0, 18);
        public Vector2 size = new Vector2(680, 248);
    }
}
