#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using Codetective.DataExchange;
using Codetective.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Codetective.Tests.Operations
{
    public sealed class DataWorkbookEditorTests
    {
        private string folder;
        private WorldDefinition world;
        [SetUp] public void Setup()
        {
            string name = "__CodetectiveWorkbookTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name); folder = "Assets/" + name;
            world = DataExampleFactory.Create();
            foreach (ScriptableObject asset in DataWorkbook.CollectAssets(world)) AssetDatabase.CreateAsset(asset, folder + "/" + asset.name + ".asset");
            AssetDatabase.SaveAssets();
        }
        [TearDown] public void Cleanup() { if (folder != null) AssetDatabase.DeleteAsset(folder); }
        [Test] public void ApplyingEditedWorkbookPreservesExistingGuidsAndCyclicLinks()
        {
            var resources = new ProjectWorkbookAssets();
            PersonAsset person = world.AllObjects.OfType<PersonAsset>().Single(p => p.instanceId == "example_victor");
            string guid = resources.Locate(person).Guid;
            XlsxWorkbook book = DataWorkbook.Export(world, resources); WorkbookSheet sheet = book.Require("Person");
            sheet.Rows.Single(row => row[0] == person.instanceId)[Array.IndexOf(sheet.Rows[0], "age")] = "51";
            using (DataWorkbookPlan plan = DataWorkbook.Prepare(book, resources, resources.Clone))
            {
                Assert.That(person.age, Is.EqualTo(48));
                DataWorkbookEditor.Apply(plan, folder);
            }
            Assert.That(resources.Locate(person).Guid, Is.EqualTo(guid)); Assert.That(person.age, Is.EqualTo(51));
            Assert.That(person.inventory.Single().owner, Is.SameAs(person));
            Assert.That(world.CreateRuntime().World.Find(person.instanceId).GetValue("age").number, Is.EqualTo(51));
        }
        [Test] public void InvalidWorkbookLeavesPersistentAssetsUntouched()
        {
            var resources = new ProjectWorkbookAssets(); XlsxWorkbook book = DataWorkbook.Export(world, resources);
            string path = AssetDatabase.GetAssetPath(world.AllObjects.OfType<PersonAsset>().First());
            string before = File.ReadAllText(path);
            book.Require("Details").Add("example_victor", "initialValues[0].reference.displayName", "value", "잘못된 수정");
            Assert.That(() => DataWorkbook.Prepare(book, resources, resources.Clone), Throws.TypeOf<InvalidDataException>());
            Assert.That(File.ReadAllText(path), Is.EqualTo(before));
        }
        [Test] public void CheckedInExampleHasWorkingUnityReferencesAndOptionalTimestamps()
        {
            var example = AssetDatabase.LoadAssetAtPath<WorldDefinition>("Assets/1_Core/Data/Examples/CommonData/ExampleWorld.asset");
            Assert.That(example, Is.Not.Null);
            var runtime = example.CreateRuntime();
            Assert.That(runtime.World.CurrentTime.ToString(), Is.EqualTo("1994-06-03 09:00"));
            Assert.That(runtime.World.Find("example_victor").GetValue("currentPlace").id, Is.EqualTo("example_warehouse"));
            Assert.That(runtime.ViewData("example_victor").Text(), Does.Contain("불참").And.Not.Contain("창고"));
        }
    }
}
#endif
