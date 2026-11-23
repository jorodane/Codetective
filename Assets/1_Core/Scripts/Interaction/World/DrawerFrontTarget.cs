using UnityEngine;

namespace Codetective.Interaction
{
    /// <summary>Front-face control that stays selectable when placing an item.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(BoxCollider))]
    public sealed class DrawerFrontTarget : InspectionActionTarget
    {
        private SlidingDrawer drawer;
        public SlidingDrawer Drawer => drawer;
        public void Initialize(SlidingDrawer owner, Bounds front)
        {
            drawer = owner;
            BoxCollider collider = GetComponent<BoxCollider>();
            if (collider == null) collider = gameObject.AddComponent<BoxCollider>();
            collider.center = front.center; collider.size = front.size; collider.isTrigger = true;
            if (drawer != null) drawer.FrontFace = collider;
        }
        public override bool CanInteract(string angleId) => drawer != null && drawer.CanInteract(angleId);
        public override void Interact() { if (drawer != null) drawer.Interact(); }
    }
}
