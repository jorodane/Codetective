#if UNITY_EDITOR
using System;
using Codetective.Gameplay;
using Codetective.Operations;
using UnityEditor;
using UnityEngine;

namespace Codetective.Editor
{
    [CustomEditor(typeof(EntitySchemaAsset))]
    public sealed class EntitySchemaAssetEditor : UnityEditor.Editor
    {
        private string preview;
        private bool failed;
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("Parent의 변수·함수·접근 조건을 상속합니다. 개체 포함 관계는 Instance의 Children으로 별도 설정합니다.", MessageType.Info);
            DrawDefaultInspector();
            if (GUILayout.Button("상속 멤버 확인 / 설정 검증"))
            {
                try
                {
                    var runtime = ClinicScenario.Create();
                    var asset = (EntitySchemaAsset)target;
                    EntityAssetCompiler.Install(runtime, new[] { asset }, null);
                    EntitySchema schema = runtime.Schemas.Require(asset.schemaId);
                    var text = new System.Text.StringBuilder();
                    foreach (EntitySchema ancestor in schema.Lineage())
                    {
                        text.AppendLine(ancestor.Id);
                        foreach (VariableDefinition field in ancestor.Variables)
                            text.AppendLine("  " + field.Code + " : " + (string.IsNullOrEmpty(field.SchemaId) ? field.Kind.ToString() : field.SchemaId) +
                                (field.Writable ? " (쓰기 가능)" : " (읽기 전용)") + (field.Overrides ? " [재정의]" : ""));
                        foreach (Operation function in ancestor.Operations) text.AppendLine("  " + function.Signature + (function.Overrides ? " [재정의]" : ""));
                    }
                    preview = text.ToString(); failed = false;
                }
                catch (Exception error) { preview = error.Message; failed = true; }
            }
            if (!string.IsNullOrEmpty(preview)) EditorGUILayout.HelpBox(preview, failed ? MessageType.Error : MessageType.Info);
        }
    }

    [CustomEditor(typeof(EntityInstanceAsset))]
    public sealed class EntityInstanceAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("이 에셋은 게임 시스템이 생성하는 개체입니다. Scenario의 Instances에 등록하세요.\nFields / Functions는 이 인스턴스만의 멤버입니다. Initial Values는 상속 변수도 설정합니다. Disabled 목록과 함수의 Member Changes로 대화·소지품의 사용 가능 상태를 바꿀 수 있습니다. 플레이어는 기존 개체의 참조만 만듭니다.", MessageType.Info);
            DrawDefaultInspector();
        }
    }
}
#endif
