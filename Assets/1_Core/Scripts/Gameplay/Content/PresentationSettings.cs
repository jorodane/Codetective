using Codetective.CameraViews;
using UnityEngine;

namespace Codetective.Gameplay
{
    [CreateAssetMenu(menuName = "Codetective/Presentation/Settings", fileName = "PresentationSettings")]
    public sealed class PresentationSettings : ScriptableObject
    {
        public TransitionPreset defaultAngle;
        [Tooltip("Fallback when an angle has no Exit Transition. Empty uses Default Angle.")]
        public TransitionPreset defaultReturn;
        [Tooltip("Menu focus always preserves the terminal; this clip supplies its fade duration.")]
        public TransitionPreset menu;
        public TransitionPreset day, visit;
        [Tooltip("Loading/title changes always cover the whole screen; this clip supplies color and duration.")]
        public TransitionPreset scene;
        [Range(0, 1)] public float terminalFocusOpacity = 0.94f;
        [Min(0)] public float dayCardSeconds = 1.3f;
        [Range(0.3f, 0.8f)] public float dayTitleWidth = 0.52f;
        [Min(0)] public float dayTitleFadeSeconds = 0.4f;
        [Min(0)] public float daySubtitleDelay = 0.25f;
        [Min(0)] public float daySubtitleFadeSeconds = 0.4f;
        [Min(0)] public float dayTextFadeSeconds = 0.9f;
        public AudioClip mailArrivalSound;
        [Range(0, 1)] public float mailArrivalVolume = 0.45f;
        [Min(0.15f)] public float mailBlinkSeconds = 0.55f;
        [Min(0.5f)] public float mailBannerSeconds = 1.2f;
    }
}
