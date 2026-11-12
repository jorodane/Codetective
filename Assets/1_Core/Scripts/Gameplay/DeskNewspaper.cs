using System.Linq;
using Codetective.Interaction;
using Codetective.Operations;
using UnityEngine;

namespace Codetective.Gameplay
{
    /// <summary>Physical morning delivery; the reader uses the edition frozen by KernelRuntime.</summary>
    public sealed class DeskNewspaper : MonoBehaviour
    {
        private GameObject paper;
        private Material paperMaterial;
        private TextMesh headline;
        public HoldableItem Item { get; private set; }
        public void Initialize(Transform anchor, Transform desk, Font font, ItemCarryController carry)
        {
            paper = new GameObject("DailyNewspaper"); paper.transform.SetParent(transform, false);
            Vector3 position = anchor != null ? anchor.position : new Vector3(-0.15f, 0.91f, -2.66f);
            if (desk != null)
            {
                Renderer[] surfaces = desk.GetComponentsInChildren<Renderer>();
                if (surfaces.Length > 0) position.y = surfaces.Max(r => r.bounds.max.y) + 0.005f;
            }
            paper.transform.SetPositionAndRotation(position, anchor != null ? anchor.rotation : Quaternion.identity);
            GameObject sheet = GameObject.CreatePrimitive(PrimitiveType.Cube); sheet.name = "FoldedNewsprint";
            sheet.transform.SetParent(paper.transform, false); sheet.transform.localScale = new Vector3(0.44f, 0.004f, 0.28f);
            paperMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            paperMaterial.color = new Color(0.79f, 0.75f, 0.61f); sheet.GetComponent<Renderer>().sharedMaterial = paperMaterial;
            if (font != null)
            {
                Print(font, "CODETECTIVE DAILY", new Vector3(-0.202f, 0.003f, 0.124f), 0.011f);
                headline = Print(font, "", new Vector3(-0.202f, 0.003f, 0.080f), 0.007f);
                Print(font, "___________________________\n\n|||||||||   |||||||||   |||||||||\n|||||||||   |||||||||   |||||||||\n|||||||||   |||||||||   |||||||||\n|||||||||   |||||||||   |||||||||", new Vector3(-0.202f, 0.003f, 0.044f), 0.008f);
            }
            Item = paper.AddComponent<HoldableItem>();
            Item.Configure("desk.newspaper", "오늘의 신문", HeldItemUse.Newspaper); carry.Register(Item);
        }
        public void Refresh(KernelRuntime runtime)
        {
            runtime.DeliverNewspaper();
            if (headline != null) headline.text = "DAY " + runtime.Day.ToString("D2") + " / MORNING EDITION" + (runtime.TodayNewspaper.read ? " / READ" : "");
            paper.SetActive(true);
        }
        private TextMesh Print(Font font, string text, Vector3 position, float size)
        {
            var go = new GameObject("Print", typeof(TextMesh)); go.transform.SetParent(paper.transform, false);
            go.transform.localPosition = position; go.transform.localRotation = Quaternion.Euler(90, 0, 0);
            TextMesh mesh = go.GetComponent<TextMesh>(); mesh.font = font; mesh.fontSize = 40; mesh.characterSize = size;
            mesh.anchor = TextAnchor.UpperLeft; mesh.color = new Color(0.10f, 0.09f, 0.06f); mesh.text = text;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material; return mesh;
        }
        private void OnDestroy() { if (paper != null) Destroy(paper); if (paperMaterial != null) Destroy(paperMaterial); }
    }
}
