using UnityEngine;

namespace Codetective.CameraViews
{
    public enum TransitionStyle { Cut, CameraMove, ScreenOverlap, FullScreenFade, TerminalFade }
    [CreateAssetMenu(menuName = "Codetective/Presentation/Transition", fileName = "Transition")]
    public sealed class TransitionPreset : ScriptableObject
    {
        public TransitionStyle style = TransitionStyle.CameraMove;
        [Tooltip("Duration of the camera movement, overlap or each fade phase.")]
        [Min(0)] public float duration = 0.3f;
        [Min(0)] public float hold = 0.05f;
        public Color color = Color.black;
        [Tooltip("Optional movement curve. Empty uses the angle's blend curve.")]
        public AnimationCurve cameraCurve;
        [Tooltip("How much darker the middle of a screen overlap becomes.")]
        [Range(0, 1)] public float overlapDarkness = 0.6f;
    }
}
