using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Codetective.Interaction
{
    public sealed class InspectionOutlineFeature : ScriptableRendererFeature
    {
        [SerializeField] private Shader shader;
        private static Camera selectedCamera;
        private static IReadOnlyList<(Mesh mesh, Renderer renderer)> selectedMeshes;
        private static IReadOnlyList<(Mesh mesh, Renderer renderer)> selectedContents;
        private Material material;
        private OutlinePass pass;

        internal static void Show(Camera camera, IReadOnlyList<(Mesh mesh, Renderer renderer)> meshes,
            IReadOnlyList<(Mesh mesh, Renderer renderer)> contents)
        {
            selectedCamera = camera;
            selectedMeshes = meshes;
            selectedContents = contents;
        }

        internal static void Hide(IReadOnlyList<(Mesh mesh, Renderer renderer)> meshes)
        {
            if (!ReferenceEquals(selectedMeshes, meshes)) return;
            selectedCamera = null;
            selectedMeshes = null;
            selectedContents = null;
        }

        public override void Create()
        {
            CoreUtils.Destroy(material);
            pass = null;
            if (shader == null || !shader.isSupported) return;
            material = CoreUtils.CreateEngineMaterial(shader);
            pass = new OutlinePass(material);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (pass != null && selectedMeshes != null && selectedCamera == renderingData.cameraData.camera)
                renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing) => CoreUtils.Destroy(material);

        private sealed class OutlinePass : ScriptableRenderPass
        {
            private readonly Material material;
            private const int VisibleSilhouette = 0, VisibleContents = 1, FullSilhouette = 2, Outline = 3;

            private sealed class MaskData
            {
                public Material material;
                public IReadOnlyList<(Mesh mesh, Renderer renderer)> meshes;
                public IReadOnlyList<(Mesh mesh, Renderer renderer)> contents;
            }

            private sealed class OutlineData
            {
                public Material material;
                public TextureHandle mask;
            }

            public OutlinePass(Material material)
            {
                this.material = material;
                renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
                // The camera's depth attachment must also be bindable with the mask color target.
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (selectedMeshes == null || resources.isActiveTargetBackBuffer) return;

                TextureHandle color = resources.activeColorTexture;
                TextureHandle depth = resources.activeDepthTexture;
                if (!color.IsValid() || !depth.IsValid()) return;
                TextureDesc maskSettings = graph.GetTextureDesc(depth);
                maskSettings.name = "Inspection silhouette";
                bool hasContents = selectedContents != null && selectedContents.Count > 0;
                maskSettings.colorFormat = hasContents ? GraphicsFormat.R8G8B8A8_UNorm : GraphicsFormat.R8_UNorm;
                maskSettings.filterMode = FilterMode.Point;
                maskSettings.wrapMode = TextureWrapMode.Clamp;
                maskSettings.bindTextureMS = false;
                maskSettings.memoryless = RenderTextureMemoryless.None;
                maskSettings.clearBuffer = true;
                maskSettings.clearColor = Color.clear;
                TextureHandle mask = graph.CreateTexture(maskSettings);

                using (var builder = graph.AddRasterRenderPass<MaskData>("Inspection silhouette", out var data))
                {
                    data.material = material;
                    data.meshes = selectedMeshes;
                    data.contents = hasContents ? selectedContents : null;
                    builder.SetRenderAttachment(mask, 0);
                    builder.SetRenderAttachmentDepth(depth, AccessFlags.Read);
                    builder.SetRenderFunc(static (MaskData data, RasterGraphContext context) =>
                    {
                        DrawMeshes(context, data.material, data.meshes, VisibleSilhouette);
                        if (data.contents == null) return;
                        DrawMeshes(context, data.material, data.contents, VisibleContents);
                        DrawMeshes(context, data.material, data.meshes, FullSilhouette);
                    });
                }

                using (var builder = graph.AddRasterRenderPass<OutlineData>("Inspection outline", out var data))
                {
                    data.material = material;
                    data.mask = mask;
                    builder.UseTexture(mask, AccessFlags.Read);
                    builder.SetRenderAttachment(color, 0, AccessFlags.ReadWrite);
                    builder.SetRenderFunc(static (OutlineData data, RasterGraphContext context) =>
                        Blitter.BlitTexture(context.cmd, (RTHandle)data.mask, new Vector4(1, 1, 0, 0), data.material, Outline));
                }
            }

            private static void DrawMeshes(RasterGraphContext context, Material material, IReadOnlyList<(Mesh mesh, Renderer renderer)> meshes, int shaderPass)
            {
                foreach (var (mesh, renderer) in meshes)
                    for (int i = 0; i < mesh.subMeshCount; i++)
                        context.cmd.DrawMesh(mesh, renderer.localToWorldMatrix, material, i, shaderPass);
            }
        }
    }
}
