using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Codetective.Gameplay;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Codetective.DataExchange
{
    public sealed class ProjectWorkbookAssets : IWorkbookAssets
    {
        public WorkbookLocation Locate(Object asset)
        {
            if (asset == null || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long fileId)) return null;
            return new WorkbookLocation { Guid = guid, FileId = fileId, Path = AssetDatabase.GetAssetPath(asset) };
        }
        public Object Resolve(string guid, long fileId)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return null;
            if (fileId == 0) return AssetDatabase.LoadMainAssetAtPath(path);
            return AssetDatabase.LoadAllAssetsAtPath(path).FirstOrDefault(asset =>
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string _, out long id) && id == fileId);
        }
        public ScriptableObject Clone(Type type, string guid)
        {
            ScriptableObject original = Resolve(guid, 0) as ScriptableObject;
            if (original != null && original.GetType() != type) throw new InvalidDataException("기존 GUID의 자료형이 다릅니다: " + guid);
            ScriptableObject clone = ScriptableObject.CreateInstance(type);
            if (original != null) EditorUtility.CopySerialized(original, clone);
            clone.hideFlags = HideFlags.HideAndDontSave;
            return clone;
        }
    }

    public static class DataWorkbookEditor
    {
        [MenuItem("Tools/Codetective/Create Data Example")]
        public static void CreateExample()
        {
            WorldDefinition world = DataExampleFactory.Create();
            string folder = AssetDatabase.GenerateUniqueAssetPath("Assets/CodetectiveDataExample");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            foreach (ScriptableObject asset in DataWorkbook.CollectAssets(world))
                AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + asset.name + ".asset"));
            AssetDatabase.SaveAssets();
            using (FileStream stream = File.Create(folder + "/Example.xlsx")) DataWorkbook.Export(world, new ProjectWorkbookAssets()).Write(stream);
            AssetDatabase.Refresh(); Selection.activeObject = world;
        }
        [MenuItem("Assets/Codetective/Export Data Excel (.xlsx)")]
        private static void ExportSelected() => Export(Selection.activeObject as ScriptableObject);
        [MenuItem("Assets/Codetective/Export Data Excel (.xlsx)", true)]
        private static bool CanExport() => DataWorkbook.Supported(Selection.activeObject);
        public static void Export(ScriptableObject root)
        {
            string path = EditorUtility.SaveFilePanel("Codetective 데이터 내보내기", "", root.name + ".xlsx", "xlsx");
            if (string.IsNullOrEmpty(path)) return;
            // Build and encode before touching the destination, so an invalid graph cannot truncate an existing workbook.
            using (var stream = new MemoryStream())
            {
                DataWorkbook.Export(root, new ProjectWorkbookAssets()).Write(stream);
                File.WriteAllBytes(path, stream.ToArray());
            }
            AssetDatabase.Refresh(); Debug.Log("Codetective Excel 내보내기: " + path);
        }
        public static ScriptableObject[] Apply(DataWorkbookPlan plan, string folder)
        {
            if (folder != "Assets" && !folder.StartsWith("Assets/", StringComparison.Ordinal) || !AssetDatabase.IsValidFolder(folder))
                throw new InvalidDataException("새 에셋을 저장할 프로젝트 폴더를 선택하세요.");
            plan.Validate();
            var resources = new ProjectWorkbookAssets();
            var targets = new Dictionary<Object, Object>();
            var created = new List<string>();
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Import Codetective Excel");
            try
            {
                foreach (WorkbookAsset item in plan.Assets)
                {
                    ScriptableObject target = resources.Resolve(item.Guid, 0) as ScriptableObject;
                    if (target != null)
                    {
                        if (target.GetType() != item.Value.GetType()) throw new InvalidDataException("기존 자료형이 다릅니다: " + item.Key);
                        Undo.RegisterCompleteObjectUndo(target, "Import Codetective Excel");
                    }
                    else
                    {
                        target = ScriptableObject.CreateInstance(item.Value.GetType());
                        string name = string.IsNullOrWhiteSpace(item.Name) ? item.Type : item.Name;
                        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
                        name = name.Replace('/', '_').Replace('\\', '_');
                        string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name + ".asset");
                        AssetDatabase.CreateAsset(target, path); created.Add(path);
                        Undo.RegisterCreatedObjectUndo(target, "Import Codetective Excel");
                    }
                    targets.Add(item.Value, target);
                }
                // Every target exists before any references are copied: mutually referring people/items remain intact.
                foreach (WorkbookAsset item in plan.Assets)
                {
                    DataWorkbook.RewriteReferences(item.Value, value => value != null && targets.TryGetValue(value, out Object target) ? target : value);
                    var destination = (ScriptableObject)targets[item.Value];
                    EditorUtility.CopySerialized(item.Value, destination); destination.name = item.Name; destination.hideFlags = HideFlags.None;
                    EditorUtility.SetDirty(destination);
                }
                AssetDatabase.SaveAssets(); Undo.CollapseUndoOperations(group);
                return plan.Assets.Select(a => (ScriptableObject)targets[a.Value]).ToArray();
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                foreach (string path in created) AssetDatabase.DeleteAsset(path);
                AssetDatabase.SaveAssets(); throw;
            }
        }
    }

    public sealed class DataWorkbookWindow : EditorWindow
    {
        private ScriptableObject root;
        private string destination = "Assets", message;
        private bool failed;
        private DataWorkbookPlan pending;
        private Vector2 scroll;
        [MenuItem("Tools/Codetective/Data Excel")]
        public static void Open() => GetWindow<DataWorkbookWindow>("Codetective Excel");
        private void OnDisable() { pending?.Dispose(); pending = null; }
        private void OnGUI()
        {
            EditorGUILayout.HelpBox("World와 연결된 자료·스키마·Viewer를 .xlsx로 편집합니다. 사진과 프리팹은 프로젝트 GUID로 연결하며 파일에 복제하지 않습니다.", MessageType.Info);
            root = (ScriptableObject)EditorGUILayout.ObjectField("내보낼 자료 / World", root, typeof(ScriptableObject), false);
            using (new EditorGUI.DisabledScope(!DataWorkbook.Supported(root)))
                if (GUILayout.Button("Excel 내보내기 (.xlsx)")) Run(() => DataWorkbookEditor.Export(root));
            destination = EditorGUILayout.TextField("새 에셋 저장 폴더", destination);
            if (GUILayout.Button("Excel 읽기 / 검증")) Run(() =>
            {
                string path = EditorUtility.OpenFilePanel("Codetective 데이터 가져오기", "", "xlsx");
                if (string.IsNullOrEmpty(path)) return;
                pending?.Dispose(); pending = null;
                var resources = new ProjectWorkbookAssets();
                using (FileStream stream = File.OpenRead(path)) pending = DataWorkbook.Prepare(XlsxWorkbook.Read(stream), resources, resources.Clone);
                message = pending.Assets.Count + "개 에셋 검증 완료. 적용하면 기존 GUID를 유지해 갱신합니다.";
            });
            if (pending != null)
            {
                scroll = EditorGUILayout.BeginScrollView(scroll);
                foreach (WorkbookAsset item in pending.Assets) EditorGUILayout.LabelField(item.Type + " / " + item.Name, item.Key);
                EditorGUILayout.EndScrollView();
                if (GUILayout.Button("가져오기 적용 (" + pending.Assets.Count + "개)")) Run(() =>
                {
                    DataWorkbookPlan plan = pending; pending = null;
                    try
                    {
                        ScriptableObject[] assets = DataWorkbookEditor.Apply(plan, destination);
                        root = assets.OfType<WorldDefinition>().FirstOrDefault() ?? assets.FirstOrDefault();
                        Selection.activeObject = root; message = "데이터 가져오기를 완료했습니다.";
                    }
                    finally { plan.Dispose(); }
                });
            }
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, failed ? MessageType.Error : MessageType.Info);
        }
        private void Run(Action action)
        { try { failed = false; action(); } catch (Exception error) { failed = true; message = error.Message; Debug.LogException(error); } }
    }

    [CustomEditor(typeof(WorldDefinition))]
    public sealed class WorldDefinitionEditor : global::UnityEditor.Editor
    {
        private string result;
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (GUILayout.Button("월드 데이터 검증"))
            {
                try { var runtime = ((WorldDefinition)target).CreateRuntime(); result = "검증 완료 / 실제 대상 " + runtime.World.Objects.Count + " / 획득 정보 " + runtime.CurrentInformation.Records.Count; }
                catch (Exception error) { result = error.Message; }
            }
            if (GUILayout.Button("Excel 내보내기")) DataWorkbookEditor.Export((WorldDefinition)target);
            if (!string.IsNullOrEmpty(result)) EditorGUILayout.HelpBox(result, MessageType.Info);
        }
    }
}
