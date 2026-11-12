using Codetective.Terminal;
using UnityEngine;

namespace Codetective.Gameplay
{
    /// <summary>Share console completion gestures; an Enter after completion inserts a newline here.</summary>
    [DefaultExecutionOrder(-100)]
    public sealed class ProtocolInputField : CompletingInputField
    {
    }
}
