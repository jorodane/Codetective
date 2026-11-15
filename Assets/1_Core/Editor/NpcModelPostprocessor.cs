using System;
using UnityEditor;
using UnityEngine;

namespace Codetective.EditorTools
{
    public sealed class NpcModelPostprocessor : AssetPostprocessor
    {
        private const string ModelFolder = "Assets/npc_casual_set_00/Mesh/";

        public override uint GetVersion() => 1;

        private void OnPostprocessModel(GameObject model)
        {
            if (!assetPath.StartsWith(ModelFolder, StringComparison.Ordinal) ||
                !assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                return;

            foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // These models are posed in the scene, outside their imported bind-pose bounds.
                // Recompute skinned bounds even when the old bounds leave a close-up camera's view.
                renderer.updateWhenOffscreen = true;
                renderer.allowOcclusionWhenDynamic = false;
            }
        }
    }
}
