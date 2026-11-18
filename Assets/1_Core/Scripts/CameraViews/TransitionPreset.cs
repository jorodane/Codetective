using UnityEngine;

namespace Codetective.CameraViews
{
    public enum TransitionStyle { Cut, CameraMove, ScreenOverlap, FullScreenFade, TerminalFade }
    [CreateAssetMenu(menuName = "Codetective/Presentation/Transition", fileName = "Transition")]
    public sealed class TransitionPreset : ScriptableObject
    {
        public TransitionStyle style = TransitionStyle.CameraMove;
        [Tooltip("Fade/overlap duration. CameraMove retains the angle's own Transition Duration.")]
        [Min(0)] public float duration = 0.3f;
        [Min(0)] public float hold = 0.05f;
        public Color color = Color.black;
    }
}
