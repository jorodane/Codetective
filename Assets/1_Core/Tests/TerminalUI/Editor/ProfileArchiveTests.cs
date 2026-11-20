#if UNITY_EDITOR
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
        [SetUp]
        public void Setup()
        {
            root = new GameObject("Archive test", typeof(RectTransform), typeof(Canvas)); root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var view = new GameObject("ProfileArchive", typeof(RectTransform)); view.transform.SetParent(root.transform, false);
            ((RectTransform)view.transform).sizeDelta = new Vector2(1160, 630);
            runtime = ClinicScenario.Create(); archive = view.AddComponent<ProfileArchiveView>();
            archive.Initialize(runtime, Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"), null, null, () => archive.Hide());
        }
        [TearDown] public void Cleanup() => Object.DestroyImmediate(root);
        private string Texts() => string.Join("\n", archive.GetComponentsInChildren<Text>().Select(t => t.text));
        private Button Button(string label) => archive.GetComponentsInChildren<Button>().Single(b => b.GetComponentInChildren<Text>().text == label);
        [Test]
        public void TheListRequiresAnExplicitSelectionAndOnlyShowsDiscoveredPeople()
        {
            runtime.CreateInstance("Person", "hiddenVisitor", "숨겨진 인물", false);
            archive.Show(); Assert.That(archive.SelectedProfileId, Is.Null); Assert.That(archive.GetComponentInChildren<ProfileFormView>(), Is.Null);
            Assert.That(Texts(), Does.Not.Contain("숨겨진 인물")); Assert.That(archive.SelectProfile("hiddenVisitor"), Is.False);
            runtime.Discover("hiddenVisitor"); archive.Refresh(); Assert.That(Texts(), Does.Contain("숨겨진 인물"));
            Assert.That(archive.SelectProfile("hiddenVisitor"), Is.True); Assert.That(archive.GetComponentInChildren<ProfileFormView>(), Is.Not.Null);
            Button("닫기").onClick.Invoke(); Assert.That(archive.IsVisible, Is.False); Assert.That(archive.SelectProfile("kernel"), Is.False);
        }
        [Test]
        public void AnOpenProfileFillsOnlyFromAcquiredInformationAndClearsWhenAccessIsRevoked()
        {
            Entity person = runtime.CreateInstance("Person", "witness", "미확인 인물");
            person.AddVariable(new VariableDefinition { Code = "legalName", Kind = DataKind.Text, DefaultValue = Datum.Text("실제 이름") });
            person.SetValue("age", Datum.Number(39));
            archive.Show(); archive.SelectProfile(person.Id);
            Assert.That(Texts(), Does.Contain("나이: [미확인]").And.Not.Contain("실제 이름").And.Not.Contain("나이: 39"));
            runtime.Observe(new ObservationDefinition { subjectId = person.Id, field = "legalName", useCurrentField = true, source = "신원 기록" });
            runtime.Observe(new ObservationDefinition { subjectId = person.Id, field = "age", useCurrentField = true, source = "신원 기록" });
            runtime.Observe(new ObservationDefinition { subjectId = person.Id, statement = "기록한 증언", source = "현장 조사", value = Datum.Text("증언") });
            archive.Refresh(); Assert.That(Texts(), Does.Contain("이름: 실제 이름").And.Contain("나이: 39").And.Contain("기록한 증언"));
            person.SetValue("age", Datum.Number(40)); archive.Refresh(); Assert.That(Texts(), Does.Contain("나이: 39").And.Not.Contain("나이: 40"));
            person.SetEntityAccess(PlayerAccess.Internal); archive.Refresh();
            Assert.That(archive.SelectedProfileId, Is.Null); Assert.That(Texts(), Does.Not.Contain("실제 이름").And.Not.Contain("기록한 증언"));
        }
        [Test]
        public void KernelsFileUsesTheSameFormAndPagesThroughTheSharedConsultationJournal()
        {
            runtime.AddConsultationNote("kernel", "첫 번째 상담 일지", 1, 540, 550);
            ConsultationNote second = runtime.AddConsultationNote("kernel", "두 번째 상담 일지", 1, 550, 560);
            archive.Show(); archive.SelectProfile("kernel");
            Assert.That(Texts(), Does.Contain("이름: [데이터 말소됨]").And.Contain("첫 번째 상담 일지").And.Not.Contain("두 번째 상담 일지"));
            Button("다음 일지").onClick.Invoke(); Assert.That(Texts(), Does.Contain("두 번째 상담 일지").And.Not.Contain("첫 번째 상담 일지"));
            runtime.UpdateConsultationNote(second.id, "추가한 상담 기록"); archive.Refresh(); Assert.That(Texts(), Does.Contain("추가한 상담 기록"));
            Assert.That(archive.GetComponentsInChildren<ProfileFormView>().Length, Is.EqualTo(1));
        }
    }
}
#endif
