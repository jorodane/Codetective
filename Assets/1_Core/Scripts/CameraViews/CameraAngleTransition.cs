using UnityEngine;

namespace Codetective.CameraViews
{
    /// <summary>A snapshot of a blend. Retargeting creates a new blend from the actual current camera pose.</summary>
    public sealed class CameraAngleTransition
    {
        private readonly Pose from;
        private readonly Pose to;
        private readonly float fromFov;
        private readonly float toFov;
        private readonly float duration;
        private readonly AnimationCurve curve;
        private float elapsed;

        public Pose CurrentPose { get; private set; }
        public float CurrentFieldOfView { get; private set; }
        public bool IsComplete => elapsed >= duration;

        public CameraAngleTransition(Pose from, float fromFov, CameraAnglePreset target)
        {
            this.from = from;
            this.fromFov = fromFov;
            to = target.Pose;
            toFov = target.FieldOfView;
            duration = target.TransitionDuration;
            curve = target.BlendCurve != null ? new AnimationCurve(target.BlendCurve.keys) : null;
            CurrentPose = from;
            CurrentFieldOfView = fromFov;
            Step(0f);
        }

        public void Step(float deltaTime)
        {
            elapsed = Mathf.Min(duration, elapsed + Mathf.Max(0f, deltaTime));
            if (IsComplete)
            {
                CurrentPose = to;
                CurrentFieldOfView = toFov;
                return;
            }

            float t = elapsed / duration;
            float weight = Weight(t);
            CurrentPose = new Pose(Vector3.Lerp(from.position, to.position, weight),
                Quaternion.Slerp(from.rotation, to.rotation, weight));
            CurrentFieldOfView = Mathf.Lerp(fromFov, toFov, weight);
        }

        private float Weight(float t)
        {
            if (t <= 0f)
                return 0f;
            if (curve == null || curve.length < 2)
                return Mathf.SmoothStep(0f, 1f, t);

            float start = curve.Evaluate(0f);
            float range = curve.Evaluate(1f) - start;
            // Normalize authored endpoints so edits cannot produce an initial/final snap.
            return Mathf.Abs(range) < 0.00001f ? Mathf.SmoothStep(0f, 1f, t) :
                Mathf.Clamp01((curve.Evaluate(t) - start) / range);
        }
    }
}
