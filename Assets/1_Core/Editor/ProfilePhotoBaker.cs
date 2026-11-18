#if UNITY_EDITOR
using System;
using System.IO;
using Codetective.Gameplay;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Codetective.Authoring
{
    [InitializeOnLoad]
    public static class ProfilePhotoBaker
    {
        static ProfilePhotoBaker()
        {
            EditorApplication.delayCall += CaptureMissing;
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += CaptureMissing; };
        }
        private static void CaptureMissing()
        {
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += CaptureMissing; return; }
            // Asset discovery is editor-only. Runtime lookup always follows the scenario's explicit catalog.
            foreach (string guid in AssetDatabase.FindAssets("t:ProfileCatalog"))
            {
                var catalog = AssetDatabase.LoadAssetAtPath<ProfileCatalog>(AssetDatabase.GUIDToAssetPath(guid));
                if (catalog == null) continue;
                foreach (VisualProfileAsset profile in catalog.All)
                    if (profile.captureMissingPhoto && profile.photo == null && profile.modelPrefab != null) CaptureSafely(profile);
            }
        }
        public static void CaptureSafely(VisualProfileAsset profile)
        {
            try { Capture(profile); }
            catch (Exception error) { Debug.LogWarning("프로필 사진 생성 실패 (" + profile.name + "): " + error.Message, profile); }
        }
        public static void Capture(VisualProfileAsset profile)
        {
            if (profile == null || profile.modelPrefab == null) throw new InvalidOperationException("사진을 찍을 프리팹을 지정하세요.");
            var preview = new PreviewRenderUtility(); Texture2D picture = null;
            try
            {
                GameObject model = Object.Instantiate(profile.modelPrefab); preview.AddSingleGO(model);
                model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(profile.modelEuler)); model.transform.localScale = Vector3.Scale(model.transform.localScale, profile.modelScale);
                ProfileModelUtility.Prepare(model);
                CharacterProfileAsset character = profile as CharacterProfileAsset;
                if (character != null) ProfileModelUtility.Seat(model, character, Vector3.zero);
                Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) throw new InvalidOperationException("프리팹에 표시할 메시가 없습니다.");
                Bounds bounds = renderers[0].bounds; foreach (Renderer renderer in renderers) if (renderer.enabled) bounds.Encapsulate(renderer.bounds);
                Transform head = character != null ? ProfileModelUtility.Bone(model, HumanBodyBones.Head, "Head") : null;
                Vector3 target = head != null ? head.position + character.portraitOffset : bounds.center;
                float distance = character != null ? character.portraitDistance : Mathf.Max(0.1f, bounds.extents.magnitude / Mathf.Sin(15 * Mathf.Deg2Rad) * 1.15f);
                preview.camera.fieldOfView = 30; preview.camera.nearClipPlane = 0.01f; preview.camera.farClipPlane = Mathf.Max(100, distance * 4);
                preview.camera.clearFlags = CameraClearFlags.SolidColor; preview.camera.backgroundColor = new Color(0.72f, 0.73f, 0.69f, 1);
                preview.camera.allowHDR = false;
                preview.camera.transform.position = target + new Vector3(0, character != null ? 0 : 0.25f, -1).normalized * distance;
                preview.camera.transform.LookAt(target);
                preview.lights[0].intensity = 1.2f; preview.lights[0].transform.rotation = Quaternion.Euler(25, 215, 0);
                preview.lights[1].intensity = 0.6f; preview.lights[1].transform.rotation = Quaternion.Euler(10, 45, 0);
                preview.ambientColor = new Color(0.5f, 0.5f, 0.5f);
                preview.BeginStaticPreview(new Rect(0, 0, character != null ? 350 : 512, character != null ? 450 : 512));
                preview.Render(true); picture = preview.EndStaticPreview();
                string assetPath = AssetDatabase.GetAssetPath(profile);
                string folder = Path.GetDirectoryName(assetPath).Replace('\\', '/') + "/Photos";
                Directory.CreateDirectory(folder);
                string output = folder + "/" + Path.GetFileNameWithoutExtension(assetPath) + ".png";
                File.WriteAllBytes(output, picture.EncodeToPNG()); AssetDatabase.Refresh(); AssetDatabase.ImportAsset(output, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(output);
                importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true; importer.mipmapEnabled = false; importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
                Undo.RecordObject(profile, "프로필 사진 촬영"); profile.photo = AssetDatabase.LoadAssetAtPath<Sprite>(output); EditorUtility.SetDirty(profile); AssetDatabase.SaveAssets();
            }
            finally { if (picture != null) Object.DestroyImmediate(picture); preview.Cleanup(); }
        }
    }
    [CustomEditor(typeof(VisualProfileAsset), true)]
    public sealed class VisualProfileInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector(); var profile = (VisualProfileAsset)target;
            using (new EditorGUI.DisabledScope(profile.modelPrefab == null))
                if (GUILayout.Button("프리팹으로 사진 촬영 / 갱신")) ProfilePhotoBaker.CaptureSafely(profile);
        }
    }
    [CustomEditor(typeof(ProfileCatalog))]
    public sealed class ProfileCatalogInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (GUILayout.Button("누락된 프로필 사진 생성"))
                foreach (VisualProfileAsset profile in ((ProfileCatalog)target).All)
                    if (profile.photo == null && profile.modelPrefab != null) ProfilePhotoBaker.CaptureSafely(profile);
        }
    }
}
#endif
