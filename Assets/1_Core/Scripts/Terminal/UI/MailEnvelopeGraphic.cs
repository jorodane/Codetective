using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    public sealed class MailEnvelopeGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); Rect rect = GetPixelAdjustedRect();
            Vector2 min = rect.min + rect.size * 0.04f, max = rect.max - rect.size * 0.04f;
            Vector2 leftTop = new Vector2(min.x, max.y), rightTop = max, leftBottom = min, rightBottom = new Vector2(max.x, min.y);
            float middle = (min.x + max.x) * 0.5f, fold = Mathf.Lerp(min.y, max.y, 0.44f);
            Line(mesh, leftTop, rightTop); Line(mesh, rightTop, rightBottom); Line(mesh, rightBottom, leftBottom); Line(mesh, leftBottom, leftTop);
            Line(mesh, leftTop, new Vector2(middle, fold)); Line(mesh, rightTop, new Vector2(middle, fold));
            Line(mesh, leftBottom, new Vector2(Mathf.Lerp(min.x, max.x, 0.36f), Mathf.Lerp(min.y, max.y, 0.58f)));
            Line(mesh, rightBottom, new Vector2(Mathf.Lerp(min.x, max.x, 0.64f), Mathf.Lerp(min.y, max.y, 0.58f)));
        }
        private void Line(VertexHelper mesh, Vector2 from, Vector2 to)
        {
            Vector2 direction = to - from;
            Vector2 normal = new Vector2(-direction.y, direction.x).normalized * (rectTransform.rect.height * 0.027f);
            int index = mesh.currentVertCount;
            mesh.AddVert(from - normal, color, Vector2.zero); mesh.AddVert(from + normal, color, Vector2.zero);
            mesh.AddVert(to + normal, color, Vector2.zero); mesh.AddVert(to - normal, color, Vector2.zero);
            // Use the same quad indices as uGUI so CrtScreen samples all four corners.
            mesh.AddTriangle(index, index + 1, index + 2);
            mesh.AddTriangle(index + 2, index + 3, index);
        }
    }
}
