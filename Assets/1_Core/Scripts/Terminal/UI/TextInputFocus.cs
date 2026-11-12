using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    public static class TextInputFocus
    {
        public static bool IsActive
        {
            get
            {
                GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                // Selection precedes activation. Protect readonly inputs and the activation frame too.
                if (selected != null && selected.GetComponentInParent<InputField>() != null) return true;
                foreach (InputField field in Object.FindObjectsOfType<InputField>()) if (field.isFocused) return true;
                return false;
            }
        }
    }
}
