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

        public override void ModifyMesh(VertexHelper mesh)
        {
            if (!IsActive())
                return;
            if (screen == null)
                screen = GetComponentInParent<CrtScreen>();
            if (screen != null)
                screen.BendMesh(mesh, transform);
        }
    }
}
