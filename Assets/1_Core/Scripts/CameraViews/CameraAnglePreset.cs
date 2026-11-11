using UnityEngine;

namespace Codetective.CameraViews
{
    [CreateAssetMenu(menuName = "Codetective/Camera/Angle Preset", fileName = "CameraAngle")]
    public sealed class CameraAnglePreset : ScriptableObject
    {
        [SerializeField] private string angleId;
        [Tooltip("Position in world space, or relative to the controller's optional Angle Origin.")]
        [SerializeField] private Vector3 position;
        [Tooltip("Euler rotation in degrees, in the same space as Position.")]
        [SerializeField] private Vector3 eulerAngles;
        [SerializeField, Range(1f, 179f)] private float fieldOfView = 60f;
        [Tooltip("Time in seconds to enter this angle. Zero changes the view immediately.")]
        [SerializeField, Min(0f)] private float transitionDuration = 0.65f;
        [SerializeField] private AnimationCurve blendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public string Id => string.IsNullOrWhiteSpace(angleId) ? name : angleId.Trim();
        public Pose Pose => new Pose(position, Quaternion.Euler(eulerAngles));
        public float FieldOfView => Mathf.Clamp(fieldOfView, 1f, 179f);
        public float TransitionDuration => Mathf.Max(0f, transitionDuration);
        public AnimationCurve BlendCurve => blendCurve;
    }
}
