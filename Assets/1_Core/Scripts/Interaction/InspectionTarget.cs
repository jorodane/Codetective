using Codetective.CameraViews;
using Codetective.Terminal;
using UnityEngine;

namespace Codetective.Interaction
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class InspectionTarget : MonoBehaviour
    {
        [Tooltip("Register this preset in the Main Camera's Angle Set as well.")]
        [SerializeField] private CameraAnglePreset closeUpAngle;
        [Tooltip("Optional root containing a terminal UI. Leave empty for ordinary objects.")]
        [SerializeField] private Transform terminalRoot;
        [SerializeField] private string displayName;
        [Tooltip("Show the name and outline when the pointer selects this target.")]
        [SerializeField] private bool showHoverFeedback = true;
        [Tooltip("The meshes belonging to this target, when the hotspot is a separate object.")]
        [SerializeField] private Transform visualRoot;

        public CameraAnglePreset Angle => closeUpAngle;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? (closeUpAngle != null ? closeUpAngle.name : name) : displayName;
        public bool ShowHoverFeedback => showHoverFeedback;
        public Transform VisualRoot => visualRoot != null ? visualRoot : transform;
        public TerminalController Terminal => terminalRoot != null ?
            terminalRoot.GetComponentInChildren<TerminalController>(true) : null;

        private void Awake() => GetComponent<BoxCollider>().isTrigger = true;
    }
}
