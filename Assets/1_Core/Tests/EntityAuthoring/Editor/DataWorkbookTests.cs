using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Codetective.DataExchange;
using Codetective.Gameplay;
using Codetective.Operations;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Codetective.Tests.Operations
{
    public sealed class DataWorkbookTests
    {
        private readonly List<ScriptableObject> allocated = new List<ScriptableObject>();
        private T Make<T>() where T : ScriptableObject { var a = ScriptableObject.CreateInstance<T>(); allocated.Add(a); return a; }
        private WorldDefinition Example()
        { WorldDefinition world = DataExampleFactory.Create(); allocated.AddRange(DataWorkbook.CollectAssets(world)); return world; }
        [TearDown] public void Cleanup() { foreach (ScriptableObject asset in allocated.Distinct()) Object.DestroyImmediate(asset); allocated.Clear(); }
        private sealed class Resources : IWorkbookAssets, IDisposable
        {
#if UNITY_5_3_OR_NEWER
            private readonly Texture2D texture = new Texture2D(2, 2);
            public readonly Sprite Photo;
            public Resources() { Photo = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero); }
            public void Dispose() { Object.DestroyImmediate(Photo); Object.DestroyImmediate(texture); }
#else
            public readonly Sprite Photo = new Sprite();
            public void Dispose() { }
#endif
            public WorkbookLocation Locate(Object asset) => asset == Photo ? new WorkbookLocation { Guid = "photo-guid", FileId = 21300000 } : new WorkbookLocation();
            public Object Resolve(string guid, long fileId) => guid == "photo-guid" && fileId == 21300000 ? Photo : null;
        }
        private static ScriptableObject New(Type type, string guid) => ScriptableObject.CreateInstance(type);
        private static void Cell(WorkbookSheet sheet, string key, string column, string value)
        {
            int index = Array.IndexOf(sheet.Rows[0], column); sheet.Rows.Skip(1).Single(row => row[0] == key)[index] = value;
        }
        [Test] public void TypedAssetsCreateCyclicReferencesWithoutDuplicatingWorldIdentities()
        {
            var w = Example(); KernelRuntime r = w.CreateRuntime();
            Assert.That(r.Entity("example_victor").GetValue("inventory").Elements.Single().id, Is.EqualTo("example_badge"));
            Assert.That(r.Entity("example_badge").GetValue("owner").id, Is.EqualTo("example_victor"));
            Assert.That(r.World.Find("example_victor").GetValue("currentPlace").id, Is.EqualTo("example_warehouse"));
            Assert.That(r.World.Find("example_hypothesis"), Is.Null);
            Assert.That(r.World.OccurredEvents.Select(e => e.Id), Is.EquivalentTo(new[] { "example_event", "example_arrival" }));
            Assert.That(r.World.History.Last(e => e.subjectId == "example_arrival").at.ToString(), Is.EqualTo("1994-06-02 14:32"));
            Assert.That(r.ViewData("example_victor").Text(), Does.Contain("행사장에서 Victor").And.Contain("불참").And.Not.Contain("창고"));
        }
        [Test] public void EveryCommonAssetHasABuiltinTypeAndDesignerOwnedMembers()
        {
            using var resources = new Resources(); var w = Example();
            var person = w.AllObjects.OfType<PersonAsset>().First();
            person.functions = new[] { new ActionDefinition { code = "custom", result = Datum.Text("개인별 응답") } };
            var photo = w.AllObjects.OfType<PhotoAsset>().Single(); photo.image = resources.Photo;
            KernelRuntime r = w.CreateRuntime(); r.Discover(photo.instanceId, observeFields: true);
            Assert.That(r.Entity(person.instanceId).Find("custom"), Is.Not.Null);
            Assert.That(r.ViewData(photo.instanceId).Kind, Is.EqualTo(DataViewerKind.Photo));
            Assert.That(r.ViewData(photo.instanceId).PhotoId, Is.EqualTo(photo.instanceId));
            Assert.That(r.World.CurrentTime.ToString(), Is.EqualTo("1994-06-03 09:00"));
        }
        [Test] public void XlsxRoundTripPreservesGraphUnicodeAndExternallyReferencedSprites()
        {
            using var resources = new Resources(); WorldDefinition w = Example();
            w.AllObjects.OfType<PhotoAsset>().Single().image = resources.Photo;
            var victor = w.AllObjects.OfType<PersonAsset>().Single(p => p.instanceId == "example_victor"); victor.description = "한글\n=literal <text>";
            XlsxWorkbook exported = DataWorkbook.Export(w, resources);
            using (var stream = new MemoryStream())
            {
                exported.Write(stream); stream.Position = 0;
                using (DataWorkbookPlan plan = DataWorkbook.Prepare(XlsxWorkbook.Read(stream), resources, New))
                {
                    WorldDefinition imported = plan.Assets.Select(a => a.Value).OfType<WorldDefinition>().Single();
                    var person = imported.AllObjects.OfType<PersonAsset>().Single(p => p.instanceId == "example_victor");
                    Assert.That(person.description, Is.EqualTo(victor.description)); Assert.That(person.inventory[0].owner, Is.SameAs(person));
                    Assert.That(person.portrait.image, Is.SameAs(resources.Photo));
                    Assert.That(imported.CreateRuntime().ViewData(person.instanceId).Text(), Does.Contain("불참"));
                }
            }
        }
        [Test] public void ImportEditsTypedColumnsAndNestedProtocolsWithoutMutatingOriginalAssets()
        {
            using var resources = new Resources(); WorldDefinition w = Example(); var person = w.AllObjects.OfType<PersonAsset>().First();
            person.functions = new[] { new ActionDefinition { code = "askExtra", parameters = new[] { new InputDefinition { name = "topic", typeName = "Keyword" } }, result = Datum.Text("원본"),
                story = new StoryCondition { triggersOn = new[] { "intro" } } } };
            XlsxWorkbook book = DataWorkbook.Export(w, resources); Cell(book.Require("Person"), person.instanceId, "age", "52");
            string[] result = book.Require("Details").Rows.Single(row => row[0] == person.instanceId && row[1] == "functions[0].result.text"); result[3] = "수정\n응답";
            using (DataWorkbookPlan plan = DataWorkbook.Prepare(book, resources, New))
            {
                var imported = (PersonAsset)plan.Assets.Single(a => a.Key == person.instanceId).Value;
                Assert.That(imported.age, Is.EqualTo(52)); Assert.That(imported.functions[0].result.text, Is.EqualTo("수정\n응답"));
                Assert.That(imported.functions[0].story.triggersOn, Is.EqualTo(new[] { "intro" }));
                Assert.That(person.age, Is.Not.EqualTo(52)); Assert.That(person.functions[0].result.text, Is.EqualTo("원본"));
            }
        }
        [Test] public void BadReferenceOrNegativeAgeAbortsPreparation()
        {
            using var resources = new Resources(); WorldDefinition w = Example(); XlsxWorkbook book = DataWorkbook.Export(w, resources);
            Cell(book.Require("Person"), "example_victor", "age", "-1");
            Assert.That(() => DataWorkbook.Prepare(book, resources, New), Throws.TypeOf<CommandException>());
            Assert.That(w.AllObjects.OfType<PersonAsset>().Single(p => p.instanceId == "example_victor").age, Is.EqualTo(48));
            Cell(book.Require("Person"), "example_victor", "age", "48"); Cell(book.Require("Person"), "example_victor", "portrait", "@missing");
            Assert.That(() => DataWorkbook.Prepare(book, resources, New), Throws.TypeOf<InvalidDataException>());
        }
        [Test] public void UnknownRowsDuplicatePathsAndIdentityOverridesAreRejected()
        {
            WorldDefinition w = Example(); using var resources = new Resources(); XlsxWorkbook book = DataWorkbook.Export(w, resources);
            book.Require("Person").Add("not-in-manifest"); Assert.That(() => DataWorkbook.Prepare(book, resources, New), Throws.TypeOf<InvalidDataException>());
            book = DataWorkbook.Export(w, resources); book.Require("Details").Add("example_victor", "instanceId", "value", "changed");
            Assert.That(() => DataWorkbook.Prepare(book, resources, New), Throws.TypeOf<InvalidDataException>());
            book = DataWorkbook.Export(w, resources); book.Require("Details").Add(book.Require("Details").Rows[1].ToArray());
            Assert.That(() => DataWorkbook.Prepare(book, resources, New), Throws.TypeOf<InvalidDataException>());
        }
        [Test] public void AssetIdentityCollisionsAndEventCyclesAreValidated()
        {
            WorldDefinition w = Example(); var persons = w.AllObjects.OfType<PersonAsset>().ToArray(); persons[1].instanceId = persons[0].instanceId;
            Assert.That(() => DataWorkbook.Export(w, new Resources()), Throws.TypeOf<InvalidDataException>());
            WorldDefinition other = Example(); var e = other.occurredEvents.Single(); e.subevents[0].subevents = new[] { e };
            Assert.That(() => other.CreateRuntime(), Throws.TypeOf<CommandException>());
        }
        [Test] public void AllObjectsIncludesInstancesReachableOnlyThroughSchemaFunctions()
        {
            var w = Make<WorldDefinition>(); w.includeClinicDefaults = false;
            var type = Make<EntitySchemaAsset>(); type.schemaId = "ExampleType";
            var photo = Make<PhotoAsset>(); photo.instanceId = "onlyFunction";
            type.functions = new[] { new ActionDefinition { code = "getPhoto", returnInstance = photo } }; w.schemas = new[] { type };
            Assert.That(w.AllObjects, Does.Contain(photo)); Assert.That(w.CreateRuntime().Entity("onlyFunction"), Is.Not.Null);
        }
        [Test] public void XlsxSupportsSparseSharedStringsBooleansAndCachedFormulas()
        {
            XNamespace s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var book = new XlsxWorkbook(); book.Add("Data").Add("text", "flag", "result");
            using (var stream = new MemoryStream())
            {
                book.Write(stream);
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, true))
                {
                    void Put(string path, string xml) { zip.GetEntry(path)?.Delete(); using (var writer = new StreamWriter(zip.CreateEntry(path).Open())) writer.Write(xml); }
                    Put("xl/sharedStrings.xml", new XElement(s + "sst", new XElement(s + "si", new XElement(s + "r", new XElement(s + "t", "한글")), new XElement(s + "r", new XElement(s + "t", " 기록")))).ToString());
                    Put("xl/worksheets/sheet1.xml", new XElement(s + "worksheet", new XElement(s + "sheetData", new XElement(s + "row",
                        new XElement(s + "c", new XAttribute("r", "A1"), new XAttribute("t", "s"), new XElement(s + "v", "0")),
                        new XElement(s + "c", new XAttribute("r", "C1"), new XAttribute("t", "b"), new XElement(s + "v", "1")),
                        new XElement(s + "c", new XAttribute("r", "D1"), new XElement(s + "f", "2+2"), new XElement(s + "v", "4"))))).ToString());
                }
                stream.Position = 0; string[] row = XlsxWorkbook.Read(stream).Require("Data").Rows.Single();
                Assert.That(row, Is.EqualTo(new[] { "한글 기록", null, "1", "4" }));
            }
        }
        [Test] public void FormulaLikeTextIsWrittenAsLiteralAndUncalculatedFormulasFail()
        {
            var book = new XlsxWorkbook(); WorkbookSheet sheet = book.Add("Data"); sheet.Add("header"); sheet.Add("=SUM(A1:A2)");
            using (var stream = new MemoryStream())
            {
                book.Write(stream); stream.Position = 0; Assert.That(XlsxWorkbook.Read(stream).Require("Data").Rows[1][0], Is.EqualTo("=SUM(A1:A2)"));
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, true))
                {
                    zip.GetEntry("xl/worksheets/sheet1.xml").Delete();
                    using (var writer = new StreamWriter(zip.CreateEntry("xl/worksheets/sheet1.xml").Open())) writer.Write("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row><c r=\"A1\"><f>2+2</f></c></row></sheetData></worksheet>");
                }
                stream.Position = 0; Assert.That(() => XlsxWorkbook.Read(stream), Throws.TypeOf<InvalidDataException>());
            }
        }
    }
}
