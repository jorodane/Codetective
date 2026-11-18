using Codetective.CameraViews;
using UnityEngine;

namespace Codetective.Gameplay
{
    [CreateAssetMenu(menuName = "Codetective/Presentation/Settings", fileName = "PresentationSettings")]
    public sealed class PresentationSettings : ScriptableObject
    {
        public TransitionPreset defaultAngle;
        [Tooltip("Menu focus always preserves the terminal; this clip supplies its fade duration.")]
        public TransitionPreset menu;
        public TransitionPreset day, visit;
        [Tooltip("Loading/title changes always cover the whole screen; this clip supplies color and duration.")]
        public TransitionPreset scene;
        [Range(0, 1)] public float terminalFocusOpacity = 0.995f;
        [Min(0)] public float dayCardSeconds = 1.3f;
    }
}
