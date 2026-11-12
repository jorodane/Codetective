using Codetective.Operations;
using UnityEngine;

namespace Codetective.Interaction
{
    [DisallowMultipleComponent]
    public sealed class SlidingDrawer : InspectionActionTarget
    {
        [SerializeField] private string drawerId;
        [SerializeField] private string inspectionAngleId = "drawer";
        [SerializeField] private Vector3 openOffset = new Vector3(0, 0, 0.34f);
        [SerializeField, Min(0.01f)] private float duration = 0.28f;
        private KernelRuntime runtime;
        private Vector3 closedPosition;
        private float amount;
        private bool interactionEnabled;
        public string Id => drawerId;
        public bool IsOpen => runtime != null && runtime.IsDrawerOpen(drawerId);
        public void Initialize(KernelRuntime value, string id)
        {
            if (runtime == null) closedPosition = transform.localPosition;
            runtime = value; drawerId = id; amount = IsOpen ? 1 : 0; Apply();
        }
        public void SetInteractionEnabled(bool value) => interactionEnabled = value;
        public override bool CanInteract(string angleId) => interactionEnabled && runtime != null && angleId == inspectionAngleId;
        public override void Interact() { if (interactionEnabled && runtime != null) runtime.SetDrawerOpen(drawerId, !IsOpen); }
        private void Update()
        {
            if (runtime == null) return;
            amount = Mathf.MoveTowards(amount, IsOpen ? 1 : 0, Time.deltaTime / Mathf.Max(0.01f, duration)); Apply();
        }
        private void Apply() => transform.localPosition = closedPosition + openOffset * Mathf.SmoothStep(0, 1, amount);
        private void OnDestroy() { if (runtime != null) transform.localPosition = closedPosition; }
    }
}
