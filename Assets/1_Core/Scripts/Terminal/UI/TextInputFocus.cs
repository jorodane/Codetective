using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    public static class TextInputFocus
    {
        private static readonly HashSet<InputField> inputs = new HashSet<InputField>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry() => inputs.Clear();

        // CompletingInputField registers itself. Other field types can use the same explicit route.
        public static void Register(InputField input) { if (input != null) inputs.Add(input); }
        public static void Unregister(InputField input) => inputs.Remove(input);

        public static void ReleaseAll()
        {
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            InputField selectedInput = selected != null ? selected.GetComponentInParent<InputField>() : null;
            foreach (InputField input in new List<InputField>(inputs)) Release(input);
            if (selectedInput != null && !inputs.Contains(selectedInput)) Release(selectedInput);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }
        private static void Release(InputField input)
        {
            if (input == null) return;
            if (input is CompletingInputField completing) completing.ReleaseFocus(); else input.DeactivateInputField();
        }
        public static bool IsActive
        {
            get
            {
                GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                // Selection precedes activation. Protect readonly inputs and the activation frame too.
                if (selected != null && selected.GetComponentInParent<InputField>() != null) return true;
                foreach (InputField field in inputs) if (field != null && field.isActiveAndEnabled && field.isFocused) return true;
                return false;
            }
        }
    }
}
