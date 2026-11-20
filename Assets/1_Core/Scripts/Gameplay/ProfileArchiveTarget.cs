using Codetective.Interaction;
using UnityEngine;

namespace Codetective.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class ProfileArchiveTarget : InspectionActionTarget
    {
        private KernelGameController game;
        public void Initialize(KernelGameController controller) => game = controller;
        public override bool CanInteract(string angleId) => angleId == "profile" && game != null && game.CanOpenProfileArchive;
        public override void Interact() { if (CanInteract("profile")) game.OpenProfileArchive(); }
    }
}
