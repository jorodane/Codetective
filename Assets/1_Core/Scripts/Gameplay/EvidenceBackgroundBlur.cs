using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Codetective.Gameplay
{
    public sealed class EvidenceBackgroundBlur : ScriptableRendererFeature
    {
        [SerializeField] private Shader shader;
        internal static Camera View { get; set; }
        private Material material;
        private BlurPass pass;

        public override void Create()
        {
            CoreUtils.Destroy(material);
            material = shader != null && shader.isSupported ? CoreUtils.CreateEngineMaterial(shader) : null;
            pass = material != null ? new BlurPass(material) : null;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
        {
            if (pass != null && View != null && data.cameraData.camera == View) renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing) => CoreUtils.Destroy(material);

        private sealed class BlurPass : ScriptableRenderPass
        {
            private static readonly int PixelStep = Shader.PropertyToID("_RoomPixelStep");
            private readonly Material material;

            private sealed class BlurData
            {
                public TextureHandle source;
                public Material material;
                public int direction;
            }

            public BlurPass(Material material)
            {
                this.material = material;
                renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer || !resources.activeColorTexture.IsValid()) return;
                TextureHandle color = resources.activeColorTexture;
                TextureDesc target = graph.GetTextureDesc(color);
                int width = Mathf.Max(1, target.width), height = Mathf.Max(1, target.height);
                float radius = 4f * height / 1080f;
                material.SetVector(PixelStep, new Vector4(radius / width, radius / height, 0, 0));
                var scratch = new TextureDesc(Mathf.Max(1, width / 2), Mathf.Max(1, height / 2))
                {
                    name = "Blurred office intermediate",
                    colorFormat = target.colorFormat,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                var blurred = graph.CreateTexture(scratch);
                Blit(graph, "Evidence background blur horizontal", color, blurred, 0);
                Blit(graph, "Evidence background blur vertical", blurred, color, 1);
            }

            private void Blit(RenderGraph graph, string name, TextureHandle source, TextureHandle destination, int direction)
            {
                using (var builder = graph.AddRasterRenderPass<BlurData>(name, out var data))
                {
                    data.source = source; data.material = material; data.direction = direction;
                    builder.UseTexture(source, AccessFlags.Read);
                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                    builder.SetRenderFunc(static (BlurData data, RasterGraphContext context) =>
                        Blitter.BlitTexture(context.cmd, (RTHandle)data.source, new Vector4(1, 1, 0, 0), data.material, data.direction));
                }
            }
        }
    }
}
