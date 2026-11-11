using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Graphic))]
    public sealed class CrtMeshEffect : BaseMeshEffect
    {
        private CrtScreen screen;
        private Matrix4x4 lastMatrix;
        private bool hasMatrix;

        protected override void OnEnable()
        {
            base.OnEnable();
            hasMatrix = false;
            Canvas.preWillRenderCanvases += RefreshMovedGraphic;
        }

        protected override void OnDisable()
        {
            Canvas.preWillRenderCanvases -= RefreshMovedGraphic;
            base.OnDisable();
        }

        private void RefreshMovedGraphic()
        {
            if (!IsActive())
                return;
            if (screen == null)
                screen = GetComponentInParent<CrtScreen>();
            if (screen == null || !screen.IsCurved)
                return;

            Matrix4x4 current = screen.GraphicToPanelMatrix(transform);
            if (hasMatrix && current == lastMatrix)
                return;
            lastMatrix = current;
            hasMatrix = true;
            // ScrollRect translates Text without rebuilding its mesh. The bend must
            // follow its new panel coordinates, not the coordinates before scrolling.
            graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper mesh)
        {
            if (!IsActive())
                return;
            if (screen == null)
                screen = GetComponentInParent<CrtScreen>();
            if (screen != null)
            {
                screen.BendMesh(mesh, transform);
                lastMatrix = screen.GraphicToPanelMatrix(transform);
                hasMatrix = true;
            }
        }
    }
}
