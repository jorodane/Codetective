using System.Collections.Generic;
using System.Linq;
using Codetective.Gameplay;
using Codetective.Operations;
using NUnit.Framework;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Codetective.Tests.Operations
{
    public sealed class EntityAssetTests
    {
        private readonly List<ScriptableObject> assets = new List<ScriptableObject>();
        private T Asset<T>() where T : ScriptableObject
        { T asset = ScriptableObject.CreateInstance<T>(); assets.Add(asset); return asset; }
        [TearDown] public void Cleanup()
        { foreach (ScriptableObject asset in assets) Object.DestroyImmediate(asset); assets.Clear(); }

        [Test]
        public void AuthoredHierarchyInstantiatesInheritedFieldsAndTargetBoundFunctions()
        {
            var parent = Asset<EntitySchemaAsset>();
            parent.schemaId = "Room"; parent.displayName = "방"; parent.builtinParent = "Place";
            parent.fields = new[] { new FieldDefinition { code = "note", kind = DataKind.Text, writable = true, defaultValue = Datum.Text("default") } };
            parent.functions = new[] { new ActionDefinition { code = "write", parameters = new[] { new InputDefinition { name = "value", kind = DataKind.Text } },
                returnField = "note", fieldWrites = new[] { new FieldWriteDefinition { field = "note", argumentIndex = 0 } } } };
            var child = Asset<EntitySchemaAsset>(); child.schemaId = "Study"; child.parent = parent;
            var a = Asset<EntityInstanceAsset>(); a.instanceId = "a"; a.schema = child;
            var b = Asset<EntityInstanceAsset>(); b.instanceId = "b"; b.schema = child;
            b.initialValues = new[] { new InitialFieldDefinition { field = "note", value = Datum.Text("second") } };
            var runtime = new KernelRuntime();
            EntityAssetCompiler.Install(runtime, null, new[] { a, b });
            Assert.That(runtime.Execute("a.write(\"first\")").Value.text, Is.EqualTo("first"));
            Assert.That(runtime.Execute("b.note").Value.text, Is.EqualTo("second"));
            Assert.That(runtime.Entities["a"].Schema, Is.SameAs(runtime.Entities["b"].Schema));
            Assert.That(runtime.TestProtocol("protocol edit(Room r) { return r.write(\"sandbox\"); }", "edit(a)").Value.text, Is.EqualTo("sandbox"));
            Assert.That(runtime.Execute("a.note").Value.text, Is.EqualTo("first"));
        }

        [Test]
        public void InstanceReferencesSupportCyclesAndAreValidatedAgainstTheSchema()
        {
            var type = Asset<EntitySchemaAsset>(); type.schemaId = "Node";
            type.fields = new[] { new FieldDefinition { code = "next", kind = DataKind.Entity, referenceType = type } };
            var a = Asset<EntityInstanceAsset>(); a.instanceId = "a"; a.schema = type;
            var b = Asset<EntityInstanceAsset>(); b.instanceId = "b"; b.schema = type;
            a.initialValues = new[] { new InitialFieldDefinition { field = "next", reference = b } };
            b.initialValues = new[] { new InitialFieldDefinition { field = "next", reference = a } };
            var runtime = new KernelRuntime(); EntityAssetCompiler.Install(runtime, null, new[] { a });
            Assert.That(runtime.Execute("a.next.next.id").Value.text, Is.EqualTo("a"));
            Assert.That(runtime.Entities.Count, Is.EqualTo(2));
            b.initialValues = new[] { new InitialFieldDefinition { field = "next", value = Datum.Number(1) } };
            Assert.That(() => EntityAssetCompiler.Install(new KernelRuntime(), null, new[] { a }), Throws.TypeOf<CommandException>());
        }

        [Test]
        public void ParentCyclesDuplicateIdentitiesAndInvalidDefaultsAreRejected()
        {
            var a = Asset<EntitySchemaAsset>(); a.schemaId = "A";
            var b = Asset<EntitySchemaAsset>(); b.schemaId = "B"; b.parent = a; a.parent = b;
            Assert.That(() => EntityAssetCompiler.Install(new KernelRuntime(), new[] { a }, null), Throws.TypeOf<CommandException>());
            a.parent = null;
            a.fields = new[] { new FieldDefinition { code = "bad", kind = DataKind.Number, defaultValue = Datum.Text("wrong") } };
            Assert.That(() => EntityAssetCompiler.Install(new KernelRuntime(), new[] { a }, null), Throws.TypeOf<CommandException>());
            a.fields = new FieldDefinition[0];
            var first = Asset<EntityInstanceAsset>(); first.instanceId = "same"; first.schema = a;
            var second = Asset<EntityInstanceAsset>(); second.instanceId = "same"; second.schema = a;
            Assert.That(() => EntityAssetCompiler.Install(new KernelRuntime(), null, new[] { first, second }), Throws.TypeOf<CommandException>());
        }

        [Test]
        public void LegacyAdditionalObjectsCompileIntoSchemaBackedInstances()
        {
            var scenario = Asset<KernelScenarioDefinition>();
            scenario.additionalObjects = new[] { new EntityDefinition { code = "legacy", label = "기존 대상", kind = DataKind.Item,
                actions = new[] { new ActionDefinition { code = "check", result = Datum.Text("ok") } } } };
            KernelRuntime runtime = scenario.CreateRuntime();
            Assert.That(runtime.Execute("legacy.check()").Value.text, Is.EqualTo("ok"));
            Assert.That(runtime.Entity("legacy").Operations, Is.Empty);
            Assert.That(runtime.Entity("legacy").Schema.Parent.Id, Is.EqualTo("Item"));
        }

#if UNITY_EDITOR
        [Test]
        public void ShippedScenarioAssetsLoadInheritedMembersAndRoundTripThroughUnityJson()
        {
            var scenario = AssetDatabase.LoadAssetAtPath<KernelScenarioDefinition>("Assets/1_Core/Operations/ClinicScenario.asset");
            Assert.That(scenario, Is.Not.Null);
            KernelRuntime runtime = scenario.CreateRuntime();
            Assert.That(runtime.Entity("trainingArchive").Schema.Lineage().Select(s => s.Id),
                Is.EqualTo(new[] { "Entity", "Place", "Room", "ArchiveRoom" }));
            Assert.That(runtime.Execute("대상 = 연습자료실").Success, Is.True);
            Assert.That(runtime.Execute("대상.메모수정(\"확인 완료\")").Value.text, Is.EqualTo("확인 완료"));
            Assert.That(runtime.Execute("예비자료실.메모읽기()").Value.text, Is.EqualTo("기본 메모"));
            Assert.That(runtime.Execute("대상.연결된방.구역").Value.text, Is.EqualTo("B-01"));
            string json = JsonUtility.ToJson(runtime.Capture());
            KernelRuntime restored = scenario.CreateRuntime(); restored.Restore(JsonUtility.FromJson<KernelSnapshot>(json));
            Assert.That(restored.Execute("대상.메모읽기()").Value.text, Is.EqualTo("확인 완료"));
        }
#endif
    }
}
