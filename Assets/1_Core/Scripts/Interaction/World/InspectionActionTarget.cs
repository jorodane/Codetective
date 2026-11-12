using UnityEngine;

namespace Codetective.Interaction
{
    public abstract class InspectionActionTarget : MonoBehaviour
    {
        public abstract bool CanInteract(string angleId);
        public abstract void Interact();
    }
}
