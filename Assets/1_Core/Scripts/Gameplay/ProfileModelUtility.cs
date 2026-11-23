using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Codetective.Gameplay
{
    public static class ProfileModelUtility
    {
        private static readonly HashSet<string> SeatedBones = new HashSet<string> {
            "Hips", "Spine", "Spine1", "Spine2", "Chest", "UpperChest", "Neck", "Head", "LeftShoulder", "RightShoulder",
            "LeftArm", "RightArm", "LeftForeArm", "RightForeArm", "LeftHand", "RightHand", "LeftUpLeg", "RightUpLeg",
            "LeftLeg", "RightLeg", "LeftFoot", "RightFoot", "LeftToeBase", "RightToeBase" };

        public static void Prepare(GameObject model)
        {
            foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (Rigidbody body in model.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
            foreach (Animator animator in model.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            foreach (LODGroup group in model.GetComponentsInChildren<LODGroup>(true))
            {
                LOD[] lods = group.GetLODs();
                Renderer[] best = lods.Select(l => l.renderers).FirstOrDefault(r => r != null && r.Any(item => item != null));
                if (best != null)
                    foreach (Renderer renderer in lods.SelectMany(l => l.renderers ?? new Renderer[0]).Where(r => r != null).Distinct()) renderer.enabled = best.Contains(renderer);
                group.enabled = false;
            }
            foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) renderer.updateWhenOffscreen = true;
        }
        public static Transform Bone(GameObject model, HumanBodyBones bone, string fallback)
        {
            Transform named = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == fallback);
            if (named != null) return named;
            Animator animator = model.GetComponentInChildren<Animator>(true);
            return animator != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman ? animator.GetBoneTransform(bone) : null;
        }
        public static void Dispose(Object value) { if (value != null) { if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value); } }
        public static bool CopySeatPose(GameObject model, Transform reference, Vector3 offset)
        {
            if (reference == null) return false;
            Transform referenceHips = Bone(reference.gameObject, HumanBodyBones.Hips, "Hips");
            Transform hips = Bone(model, HumanBodyBones.Hips, "Hips");
            if (referenceHips == null || hips == null) return false;

            var pose = reference.GetComponentsInChildren<Transform>(true).Where(bone => SeatedBones.Contains(bone.name))
                .GroupBy(bone => bone.name).ToDictionary(group => group.Key, group => group.First().rotation);
            foreach (Transform bone in model.GetComponentsInChildren<Transform>(true))
                if (pose.TryGetValue(bone.name, out Quaternion rotation)) bone.rotation = rotation;
            model.transform.position += referenceHips.position + offset - hips.position;
            AlignRigCopies(model);
            return true;
        }
        public static void Seat(GameObject model, CharacterProfileAsset profile, Vector3 seat)
        {
            if (profile == null || !profile.applySeatedPose) return;
            Vector3 forward = model.transform.forward;
            Aim(Bone(model, HumanBodyBones.LeftUpperLeg, "LeftUpLeg"), Bone(model, HumanBodyBones.LeftLowerLeg, "LeftLeg"), forward);
            Aim(Bone(model, HumanBodyBones.RightUpperLeg, "RightUpLeg"), Bone(model, HumanBodyBones.RightLowerLeg, "RightLeg"), forward);
            Aim(Bone(model, HumanBodyBones.LeftLowerLeg, "LeftLeg"), Bone(model, HumanBodyBones.LeftFoot, "LeftFoot"), Vector3.down);
            Aim(Bone(model, HumanBodyBones.RightLowerLeg, "RightLeg"), Bone(model, HumanBodyBones.RightFoot, "RightFoot"), Vector3.down);
            Aim(Bone(model, HumanBodyBones.LeftUpperArm, "LeftArm"), Bone(model, HumanBodyBones.LeftLowerArm, "LeftForeArm"), Vector3.down + forward * 0.3f);
            Aim(Bone(model, HumanBodyBones.RightUpperArm, "RightArm"), Bone(model, HumanBodyBones.RightLowerArm, "RightForeArm"), Vector3.down + forward * 0.3f);
            Aim(Bone(model, HumanBodyBones.LeftLowerArm, "LeftForeArm"), Bone(model, HumanBodyBones.LeftHand, "LeftHand"), forward);
            Aim(Bone(model, HumanBodyBones.RightLowerArm, "RightForeArm"), Bone(model, HumanBodyBones.RightHand, "RightHand"), forward);
            Transform hips = Bone(model, HumanBodyBones.Hips, "Hips");
            if (hips != null) model.transform.position += seat + Vector3.up * profile.seatedHipHeight - hips.position;
            AlignRigCopies(model);
        }

        private static void AlignRigCopies(GameObject model)
        {
            // Modular NPC clothing can carry a second copy of the rig; keep it on the same seated pose.
            foreach (var group in model.GetComponentsInChildren<Transform>(true).Where(t => SeatedBones.Contains(t.name)).GroupBy(t => t.name))
            {
                Transform first = group.First();
                foreach (Transform copy in group.Skip(1)) copy.SetPositionAndRotation(first.position, first.rotation);
            }
        }
        private static void Aim(Transform bone, Transform child, Vector3 direction)
        { if (bone != null && child != null && (child.position - bone.position).sqrMagnitude > 0.000001f) bone.rotation = Quaternion.FromToRotation(child.position - bone.position, direction) * bone.rotation; }
    }
}
