using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Codetective.CameraViews
{
    /// <summary>Optional prototype controls. F1-F12 follow the Angle Set order without consuming terminal text.</summary>
    [RequireComponent(typeof(CameraAngleController))]
    public sealed class CameraAngleShortcuts : MonoBehaviour
    {
        private CameraAngleController controller;

        private void Awake() => controller = GetComponent<CameraAngleController>();

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || controller.AngleSet == null || !controller.isActiveAndEnabled)
                return;

            for (int i = 0; i < Mathf.Min(12, controller.AngleSet.Count); i++)
            {
                if (keyboard[(Key)((int)Key.F1 + i)].wasPressedThisFrame)
                {
                    controller.GoToIndex(i);
                    break;
                }
            }
#endif
        }
    }
}
