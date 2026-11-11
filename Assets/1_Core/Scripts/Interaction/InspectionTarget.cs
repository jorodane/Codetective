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

        public CameraAnglePreset Angle => closeUpAngle;
        public TerminalController Terminal => terminalRoot != null ?
            terminalRoot.GetComponentInChildren<TerminalController>(true) : null;
    }
}
