using UnityEngine;
using UnityEngine.Events;

namespace Codetective.CameraViews
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class CameraAngleController : MonoBehaviour
    {
        [SerializeField] private CameraAngleSet angleSet;
        [Tooltip("Optional coordinate frame for all angle positions and rotations. Empty means world space.")]
        [SerializeField] private Transform angleOrigin;
        [SerializeField] private bool applyInitialAngleOnStart = true;
        [SerializeField] private bool useUnscaledTime = true;
        [SerializeField] private UnityEvent onTransitionStarted = new UnityEvent();
        [SerializeField] private UnityEvent onTransitionCompleted = new UnityEvent();

        private Camera controlledCamera;
        private CameraAngleTransition transition;
        private CameraAnglePreset activeAngle;

        public CameraAngleSet AngleSet => angleSet;
        public Transform AngleOrigin => angleOrigin;
        public Camera ControlledCamera => controlledCamera != null ? controlledCamera :
            controlledCamera = GetComponent<Camera>();
        public CameraAnglePreset ActiveAngle => activeAngle;
        public bool IsTransitioning => transition != null;
        public bool PauseTransitions { get; set; }

        private void Start()
        {
            if (applyInitialAngleOnStart && activeAngle == null && angleSet != null)
                TryGoToIndex(angleSet.InitialIndex, true);
        }

        private void LateUpdate()
        {
            if (transition == null || PauseTransitions)
                return;

            // Snapshot the reference: completion listeners may request another angle.
            CameraAngleTransition current = transition;
            current.Step(useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime);
            ApplyPose(current.CurrentPose, current.CurrentFieldOfView);
            if (current.IsComplete)
            {
                transition = null;
                onTransitionCompleted.Invoke();
            }
        }

        private void OnDisable() => transition = null;

        public void GoToIndex(int index)
        {
            if (!TryGoToIndex(index))
                Debug.LogWarning($"Camera angle index {index} is not registered.", this);
        }

        public void GoToId(string id)
        {
            if (angleSet == null || !TryGoToIndex(angleSet.FindId(id)))
                Debug.LogWarning($"Camera angle ID '{id}' is missing or ambiguous.", this);
        }

        public void GoToPreset(CameraAnglePreset preset)
        {
            if (preset != null && angleSet != null)
                GoToIndex(angleSet.IndexOf(preset));
        }

        public void NextAngle()
        {
            if (angleSet != null)
                GoToIndex(angleSet.NextIndex(angleSet.IndexOf(activeAngle), 1));
        }

        public void PreviousAngle()
        {
            if (angleSet != null)
                GoToIndex(angleSet.NextIndex(angleSet.IndexOf(activeAngle), -1));
        }

        public void SnapToIndex(int index) => TryGoToIndex(index, true);

        public bool TryGoToIndex(int index, bool instant = false)
        {
            CameraAnglePreset preset = angleSet != null ? angleSet.Get(index) : null;
            if (preset == null || (angleOrigin != null &&
                                  (angleOrigin == transform || angleOrigin.IsChildOf(transform))))
                return false;

            CameraAngleTransition next = new CameraAngleTransition(CapturePose(), ControlledCamera.fieldOfView, preset);
            activeAngle = preset;
            transition = next;
            if (Application.isPlaying)
                onTransitionStarted.Invoke();
            if (transition != next)
                return true; // A listener interrupted or cancelled this request.

            if (instant || !Application.isPlaying || next.IsComplete || !isActiveAndEnabled)
            {
                ApplyPose(preset.Pose, preset.FieldOfView);
                transition = null;
                if (Application.isPlaying)
                    onTransitionCompleted.Invoke();
            }
            return true;
        }

        public Pose CapturePose()
        {
            return angleOrigin == null ? new Pose(transform.position, transform.rotation) :
                new Pose(angleOrigin.InverseTransformPoint(transform.position),
                    Quaternion.Inverse(angleOrigin.rotation) * transform.rotation);
        }

        private void ApplyPose(Pose pose, float fov)
        {
            if (angleOrigin != null)
                pose = new Pose(angleOrigin.TransformPoint(pose.position), angleOrigin.rotation * pose.rotation);
            transform.SetPositionAndRotation(pose.position, pose.rotation);
            ControlledCamera.fieldOfView = fov;
        }
    }
}
