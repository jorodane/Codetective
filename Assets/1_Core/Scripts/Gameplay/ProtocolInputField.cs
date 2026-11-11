using Codetective.Terminal;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Codetective.Gameplay
{
    /// <summary>Expose visible IME text for suggestions while keeping Enter as a newline in the editor.</summary>
    public sealed class ProtocolInputField : InputField
    {
        public TerminalCompletionInput CompletionInput
        {
            get
            {
                EventSystem system = EventSystem.current;
                string composition = isFocused && system != null && system.currentSelectedGameObject == gameObject && system.currentInputModule != null ?
                    system.currentInputModule.input.compositionString : "";
                return new TerminalCompletionInput(text, composition, m_CaretPosition, m_CaretSelectPosition);
            }
        }
    }
}
