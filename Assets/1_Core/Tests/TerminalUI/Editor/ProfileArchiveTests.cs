#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using Codetective.Operations;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Codetective.Terminal.Tests.UI
{
    public sealed class ProfileArchiveTests
    {
        private GameObject root;
        private ProfileArchiveView archive;
        private KernelRuntime runtime;
        private string selected;
        private bool allowed;
        [SetUp]
        public void Setup()
        {
            root = new GameObject("Archive test", typeof(RectTransform), typeof(Canvas)); root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var view = new GameObject("ProfileArchive", typeof(RectTransform)); view.transform.SetParent(root.transform, false);
            ((RectTransform)view.transform).sizeDelta = new Vector2(250, 534);
            runtime = ClinicScenario.Create(); archive = view.AddComponent<ProfileArchiveView>();
            selected = null; allowed = true;
            archive.Initialize(runtime, Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"), id => { selected = id; return true; }, () => selected, () => allowed);
        }
        [TearDown] public void Cleanup() => Object.DestroyImmediate(root);
        private string Texts() => string.Join("\n", archive.GetComponentsInChildren<Text>().Select(t => t.text));
        [Test]
        public void TheListRequiresAnExplicitSelectionAndOnlyShowsDiscoveredPeople()
        {
            runtime.CreateInstance("Person", "hiddenVisitor", "숨겨진 인물", false);
            archive.Show(); Assert.That(archive.SelectedProfileId, Is.Null); Assert.That(archive.GetComponentInChildren<ProfileFormView>(), Is.Null);
            Assert.That(Texts(), Does.Not.Contain("숨겨진 인물")); Assert.That(archive.SelectProfile("hiddenVisitor"), Is.False);
            runtime.Discover("hiddenVisitor"); archive.Refresh(); Assert.That(Texts(), Does.Contain("숨겨진 인물"));
            Assert.That(archive.SelectProfile("hiddenVisitor"), Is.True); Assert.That(selected, Is.EqualTo("hiddenVisitor"));
            Assert.That(archive.GetComponentInChildren<ProfileFormView>(), Is.Null, "The list must leave the form on the desk sheet.");
            Assert.That(archive.GetComponentsInChildren<Button>().Any(b => b.GetComponentInChildren<Text>().text == "닫기"), Is.False);
            archive.Hide(); Assert.That(archive.SelectProfile("kernel"), Is.False);
        }
        [Test]
        public void TheListUsesAcquiredNamesAndHidesRevokedProfiles()
        {
            Entity person = runtime.CreateInstance("Person", "witness", "미확인 인물");
            person.AddVariable(new VariableDefinition { Code = "legalName", Kind = DataKind.Text, DefaultValue = Datum.Text("실제 이름") });
            archive.Show();
            Assert.That(Texts(), Does.Not.Contain("실제 이름"));
            runtime.Observe(new ObservationDefinition { subjectId = person.Id, field = "legalName", useCurrentField = true, source = "신원 기록" });
            archive.Refresh(); Assert.That(Texts(), Does.Contain("실제 이름"));
            person.SetEntityAccess(PlayerAccess.Internal); archive.Refresh();
            Assert.That(Texts(), Does.Not.Contain("실제 이름")); Assert.That(archive.SelectProfile(person.Id), Is.False);
        }
        [Test]
        public void TheListDisablesSelectionWhileTheDeskIsBusy()
        {
            archive.Show(); allowed = false; archive.Refresh();
            Assert.That(archive.SelectProfile("kernel"), Is.False); Assert.That(selected, Is.Null);
            Assert.That(archive.GetComponentsInChildren<Button>().All(b => !b.interactable), Is.True);
            allowed = true; archive.Refresh(); Assert.That(archive.SelectProfile("kernel"), Is.True);
        }
    }

    public sealed class DeskProfilePaperTests
    {
        private GameObject system, desk, sheet, stack, camera;
        private Component presenter;
        private KernelRuntime runtime;
        private static Type PresenterType => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Codetective.Gameplay.DeskProfilePresenter")).First(t => t != null);
        private object Call(string method, params object[] args) => PresenterType.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Invoke(presenter, args);
        private string Texts() => string.Join("\n", sheet.GetComponentsInChildren<Text>().Select(t => t.text));
        [SetUp]
        public void Setup()
        {
            system = new GameObject("Profile presentation"); system.SetActive(false);
            desk = new GameObject("Desk"); sheet = GameObject.CreatePrimitive(PrimitiveType.Cube); sheet.transform.SetParent(desk.transform, false);
            sheet.transform.localScale = new Vector3(0.21f, 0.002f, 0.297f); sheet.transform.localRotation = Quaternion.Euler(0, 180, 0);
            stack = GameObject.CreatePrimitive(PrimitiveType.Cube); camera = new GameObject("Camera", typeof(Camera));
            runtime = ClinicScenario.Create(); presenter = system.AddComponent(PresenterType);
            Call("Initialize", runtime, sheet.transform, stack.transform, camera.GetComponent<Camera>(), Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"), "kernel",
                (Func<string, Sprite>)(_ => null), (Func<bool>)(() => true), (Func<bool>)(() => false), (Action<string>)(_ => { }));
        }
        [TearDown]
        public void Cleanup()
        { Object.DestroyImmediate(system); Object.DestroyImmediate(desk); Object.DestroyImmediate(stack); Object.DestroyImmediate(camera); }
        [Test]
        public void TheAuthoredSheetRemainsOnTheDeskWithAWorldSpaceForm()
        {
            Assert.That(PresenterType.GetProperty("Sheet").GetValue(presenter), Is.SameAs(sheet.transform));
            Assert.That(sheet.transform.parent, Is.SameAs(desk.transform));
            Assert.That(sheet.GetComponentInChildren<Canvas>().renderMode, Is.EqualTo(RenderMode.WorldSpace));
            Assert.That(sheet.GetComponentsInChildren<Codetective.Interaction.HoldableItem>(true), Is.Empty);
            sheet.transform.position = stack.transform.position;
            Call("CancelSwap"); Assert.That(sheet.transform.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(sheet.transform.parent, Is.SameAs(desk.transform));
        }
        [Test]
        public void ThePrintedFormUsesLastKnownInformationAndClearsRevokedFiles()
        {
            Entity person = runtime.CreateInstance("Person", "witness", "미확인 인물"); person.SetValue("age", Datum.Number(39));
            Assert.That(Call("SelectProfile", person.Id), Is.True); Assert.That(Texts(), Does.Contain("나이: [미확인]").And.Not.Contain("나이: 39"));
            runtime.Observe(new ObservationDefinition { subjectId = person.Id, field = "age", useCurrentField = true, source = "신원 기록" });
            Call("Refresh"); Assert.That(Texts(), Does.Contain("나이: 39"));
            person.SetValue("age", Datum.Number(40)); Call("Refresh"); Assert.That(Texts(), Does.Contain("나이: 39").And.Not.Contain("나이: 40"));
            person.SetEntityAccess(PlayerAccess.Internal); Call("Refresh");
            Assert.That(PresenterType.GetProperty("ProfileId").GetValue(presenter), Is.Null); Assert.That(Texts(), Does.Not.Contain("나이: 39"));
        }
        [Test]
        public void KernelsSheetUsesTheSharedConsultationJournalAndPageControls()
        {
            runtime.AddConsultationNote("kernel", "첫 번째 상담 일지", 1, 540, 550);
            ConsultationNote second = runtime.AddConsultationNote("kernel", "두 번째 상담 일지", 1, 550, 560);
            Call("Refresh");
            Assert.That(Texts(), Does.Contain("이름: [데이터 말소됨]").And.Contain("첫 번째 상담 일지").And.Not.Contain("두 번째 상담 일지"));
            sheet.GetComponentsInChildren<Button>().Single(b => b.GetComponentInChildren<Text>().text == "›").onClick.Invoke();
            Assert.That(Texts(), Does.Contain("두 번째 상담 일지").And.Not.Contain("첫 번째 상담 일지"));
            runtime.UpdateConsultationNote(second.id, "추가한 상담 기록"); Call("Refresh"); Assert.That(Texts(), Does.Contain("추가한 상담 기록"));
            Assert.That(sheet.GetComponentsInChildren<ProfileFormView>().Length, Is.EqualTo(1));
        }
    }
}
#endif
