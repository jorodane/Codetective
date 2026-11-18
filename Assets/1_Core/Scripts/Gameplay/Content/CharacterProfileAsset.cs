using UnityEngine;

namespace Codetective.Gameplay
{
    [CreateAssetMenu(menuName = "Codetective/Profiles/Character", fileName = "CharacterProfile")]
    public sealed class CharacterProfileAsset : VisualProfileAsset
    {
        public Vector3 seatOffset;
        [Min(0)] public float seatedHipHeight = 0.5f;
        public bool applySeatedPose = true;
        [Min(0.1f)] public float portraitDistance = 0.85f;
        public Vector3 portraitOffset = new Vector3(0, 0.1f, 0);
    }
}
