using System.Collections.Generic;
using System.IO;
using Codetective.CameraViews;
using Codetective.Interaction;
using UnityEditor;
using UnityEngine;

namespace Codetective.EditorTools
{
    [CustomEditor(typeof(CameraAngleController))]
    public sealed class CameraAngleControllerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var controller = (CameraAngleController)target;
            var inspection = controller.GetComponent<InspectionController>();
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("프리셋의 위치·회전은 Angle Origin 기준입니다. 비어 있으면 월드 좌표를 사용합니다.\n" +
                (inspection != null ?
                    "대상을 클릭하면 확대하고 ESC로 전체 보기에 돌아갑니다. 전체 보기에서 ESC는 메뉴를 엽니다.\n" +
                    "F1~F12는 Inspection Controller의 Enable Angle Shortcuts에서 켜고 끕니다." :
                    "재생 중 F1~F12로 목록 순서대로 전환합니다. 단축키는 Camera Angle Shortcuts를 끄면 비활성화됩니다."),
                MessageType.Info);

            CameraAngleSet set = controller.AngleSet;
            if (set == null)
            {
                EditorGUILayout.HelpBox("Angle Set 에셋을 지정하고 Angles 목록에 프리셋을 등록하세요.", MessageType.Info);
                return;
            }
            if (controller.AngleOrigin != null && (controller.AngleOrigin == controller.transform ||
                                                    controller.AngleOrigin.IsChildOf(controller.transform)))
            {
                EditorGUILayout.HelpBox("Angle Origin에는 카메라 자신이나 자식을 지정할 수 없습니다.", MessageType.Error);
                return;
            }

            if (GUILayout.Button("현재 카메라로 새 프리셋 생성 및 등록"))
                CreateAndRegister(controller, set);

            var ids = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < set.Count; i++)
            {
                CameraAnglePreset preset = set.Get(i);
                if (preset == null)
                    continue;

                if (!ids.Add(preset.Id))
                    EditorGUILayout.HelpBox($"중복된 앵글 ID: {preset.Id}", MessageType.Error);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField($"{i + 1}. {preset.name}  [{preset.Id}]", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField($"FOV {preset.FieldOfView:0.#}  /  {preset.TransitionDuration:0.##}초");
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(Application.isPlaying ? "보간 전환" : "미리보기"))
                        {
                            if (!Application.isPlaying)
                                Undo.RecordObjects(new Object[] { controller.transform, controller.ControlledCamera },
                                    "Preview Camera Angle");
                            if (Application.isPlaying && inspection != null && inspection.isActiveAndEnabled)
                                inspection.InspectAngle(preset);
                            else
                                controller.GoToIndex(i);
                            SceneView.RepaintAll();
                        }
                        if (GUILayout.Button("프리셋 선택"))
                            Selection.activeObject = preset;
                    }
                    using (new EditorGUI.DisabledScope(Application.isPlaying && controller.IsTransitioning))
                    {
                        if (GUILayout.Button("현재 카메라 위치·회전·FOV를 이 프리셋에 저장"))
                            Capture(controller, preset);
                    }
                }
            }
            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField(controller.IsTransitioning ? "시점 전환 중" : "시점 전환 대기");
                Repaint();
            }
        }

        private static void CreateAndRegister(CameraAngleController controller, CameraAngleSet set)
        {
            string directory = Path.GetDirectoryName(AssetDatabase.GetAssetPath(set));
            string path = EditorUtility.SaveFilePanelInProject("Create Camera Angle", "CameraAngle", "asset",
                "현재 카메라의 시점을 새 프리셋으로 저장합니다.", directory);
            if (string.IsNullOrEmpty(path))
                return;

            var preset = CreateInstance<CameraAnglePreset>();
            preset.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(preset, path);
            Undo.RegisterCreatedObjectUndo(preset, "Create Camera Angle");
            Capture(controller, preset);

            var serializedSet = new SerializedObject(set);
            SerializedProperty angles = serializedSet.FindProperty("angles");
            int index = angles.arraySize;
            angles.InsertArrayElementAtIndex(index);
            angles.GetArrayElementAtIndex(index).objectReferenceValue = preset;
            serializedSet.ApplyModifiedProperties();
            AssetDatabase.SaveAssetIfDirty(set);
            EditorGUIUtility.PingObject(preset);
        }

        private static void Capture(CameraAngleController controller, CameraAnglePreset preset)
        {
            Pose pose = controller.CapturePose();
            var serializedPreset = new SerializedObject(preset);
            serializedPreset.FindProperty("position").vector3Value = pose.position;
            serializedPreset.FindProperty("eulerAngles").vector3Value = pose.rotation.eulerAngles;
            serializedPreset.FindProperty("fieldOfView").floatValue = controller.ControlledCamera.fieldOfView;
            serializedPreset.ApplyModifiedProperties();
            AssetDatabase.SaveAssetIfDirty(preset);
        }
    }
}
