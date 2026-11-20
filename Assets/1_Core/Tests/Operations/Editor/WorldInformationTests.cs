using System;
using System.Linq;
using Codetective.Operations;
using NUnit.Framework;

namespace Codetective.Tests.Operations
{
    public sealed class WorldInformationTests
    {
        private static KernelRuntime World()
        {
            var runtime = new KernelRuntime(); runtime.ConfigureInitialClock(new GameDateTime(1994, 6, 3, 9, 30));
            runtime.CreateInstance("Person", "victor", "Victor", false);
            runtime.CreateInstance("Person", "daniel", "Daniel", false);
            runtime.CreateInstance("Keyword", "attendance", "행사 참석", false);
            return runtime;
        }
        [Test] public void CurrentInformationRetainsConflictingReportsWithoutChangingTruth()
        {
            KernelRuntime r = World(); r.Entity("victor").SetValue("occupation", Datum.Text("실제 직업"));
            r.Observe(new ObservationDefinition { subjectId = "victor", field = "occupation", value = Datum.Text("첫 주장"), source = "A", observedAt = new GameDateTime(1994, 6, 2, 14, 32) });
            r.Observe(new ObservationDefinition { subjectId = "victor", field = "occupation", value = Datum.Text("두번째 주장"), source = "B" });
            Assert.That(r.World.Find("victor").GetValue("occupation").text, Is.EqualTo("실제 직업"));
            Assert.That(r.CurrentInformation.Read("victor", "occupation").text, Is.EqualTo("두번째 주장"));
            Assert.That(r.CurrentInformation.Records.Count(o => o.field == "occupation"), Is.EqualTo(2));
            Assert.That(r.CurrentInformation.View("victor").Text(), Does.Contain("첫 주장").And.Contain("두번째 주장").And.Not.Contain("실제 직업"));
        }
        [Test] public void DelayedHistoricalObservationDoesNotOverwriteNewerObservedInformation()
        {
            KernelRuntime r = World();
            r.Observe(new ObservationDefinition { subjectId = "victor", field = "occupation", value = Datum.Text("new") });
            r.Observe(new ObservationDefinition { subjectId = "victor", field = "occupation", value = Datum.Text("old"), observedAt = new GameDateTime(1990, 1, 1) });
            Assert.That(r.LastKnownValue("victor", "occupation").text, Is.EqualTo("new"));
            ObservationRecord historical = r.Observations.Last(); Assert.That(historical.receivedAt.ToString(), Is.EqualTo("1994-06-03 09:30"));
            Assert.That(historical.observedAt.ToString(), Is.EqualTo("1990-01-01 00:00"));
        }
        [Test] public void SpeechIsAWorldFactButItsClaimDoesNotSetTruth()
        {
            KernelRuntime r = World(); r.Entity("daniel").SetValue("occupation", Datum.Text("연구원"));
            r.RecordStatement("claim", "daniel", "attendance", "저는 의사입니다.", new[] { "victor" });
            Assert.That(r.World.Find("claim").GetValue("content").text, Is.EqualTo("저는 의사입니다."));
            Assert.That(() => r.CurrentInformation.View("daniel"), Throws.TypeOf<CommandException>());
            Assert.That(r.Entity("daniel").GetValue("occupation").text, Is.EqualTo("연구원"));
            r.Discover("claim", "녹취", true);
            Assert.That(r.ViewData("claim").Kind, Is.EqualTo(DataViewerKind.Statement));
            Assert.That(r.ViewData("claim").Text(), Does.Contain("저는 의사입니다."));
        }
        [Test] public void ReportedSpeechCarriesSpeakerAndTimestamp()
        {
            KernelRuntime r = World();
            r.RecordStatement("claim", "daniel", "attendance", "15시까지 행사장에 있었습니다.", report: true, spokenAt: new GameDateTime(1994, 6, 2, 14, 32));
            ObservationRecord record = r.Observations.Single(o => o.kind == InformationKind.Testimony);
            Assert.That(record.sourceId, Is.EqualTo("daniel")); Assert.That(record.observedAt.ToString(), Is.EqualTo("1994-06-02 14:32"));
            Assert.That(r.ViewData("claim").Fields.Single(f => f.Code == "speaker").Value.id, Is.EqualTo("daniel"));
        }
        [Test] public void InferredInformationHasReferencesButDoesNotCreateWorldTruth()
        {
            KernelRuntime r = World(); Datum hypothesis = r.CreateInformation("guess", "가설", "다른 출입구가 있다."); r.Store("clue", hypothesis);
            Assert.That(r.World.Find("guess"), Is.Null); Assert.That(r.World.Objects.Select(e => e.Id), Does.Not.Contain("guess"));
            Assert.That(r.CurrentInformation.References.Single().value.id, Is.EqualTo("guess"));
            Assert.That(r.ViewData("guess").Text(), Does.Contain("Kernel 추론").And.Contain("다른 출입구"));
            Assert.That(() => r.CreateInformation("fake", "가상 인물", "", type: "Person"), Throws.TypeOf<CommandException>());
        }
        [Test] public void ViewerHonorsDynamicFieldAccessWithoutLeakingHistoricalValues()
        {
            KernelRuntime r = World(); Entity person = r.Entity("victor");
            person.AddVariable(new VariableDefinition { Code = "secret", Kind = DataKind.Text, DefaultValue = Datum.Text("숨겨진 내용"),
                Access = new AccessRule { Story = new StoryCondition { triggersOn = new[] { "permit" } } } });
            r.SetTrigger("permit", true); r.Discover("victor", observeFields: true);
            Assert.That(r.ViewData("victor").Text(), Does.Contain("숨겨진 내용"));
            r.SetTrigger("permit", false);
            Assert.That(r.ViewData("victor").Text(), Does.Not.Contain("숨겨진 내용"));
            Assert.That(r.Observations.Any(o => o.field == "secret"), Is.False);
        }
        [Test] public void ViewerDefinitionInheritsAndInstanceCanOverrideIt()
        {
            KernelRuntime r = World(); var type = new EntitySchema("SpecialPerson", "특수 인물", DataKind.Person, r.Schemas.Require("Person"))
                { Viewer = new DataViewerDefinition { kind = DataViewerKind.Person, title = "맞춤 프로필", fields = new[] { "occupation", "age" }, hideUnknown = false } };
            r.Schemas.Register(type); Entity p = r.CreateInstance(type.Id, "person", "이름", false); r.Discover(p.Id);
            Assert.That(r.ViewData(p.Id).Fields.Select(f => f.Code), Is.EqualTo(new[] { "occupation", "age" }));
            Assert.That(r.ViewData(p.Id).Fields.All(f => !f.Known), Is.True);
            p.Viewer = new DataViewerDefinition { kind = DataViewerKind.Generic, title = "개별 Viewer" };
            Assert.That(r.ViewData(p.Id).Title, Is.EqualTo("개별 Viewer"));
        }
        [Test] public void WorldAndInformationSurviveSnapshotIndependently()
        {
            KernelRuntime r = World(); r.RecordStatement("speech", "daniel", "attendance", "행사장에 있었다.", report: true);
            r.CreateInformation("guess", "가설", "기록이 다르다."); r.CreateInstance("Event", "arrival", "입장", false); r.RecordEvent("arrival");
            var restored = new KernelRuntime(); restored.Restore(r.Capture());
            Assert.That(restored.CurrentTime.ToString(), Is.EqualTo("1994-06-03 09:30"));
            Assert.That(restored.World.Find("guess"), Is.Null); Assert.That(restored.ViewData("guess").Text(), Does.Contain("기록이 다르다."));
            Assert.That(restored.World.OccurredEvents.Single().Id, Is.EqualTo("arrival"));
            Assert.That(restored.World.History.Last().at.ToString(), Is.EqualTo("1994-06-03 09:30"));
            Assert.That(restored.Observations.Any(o => o.kind == InformationKind.Testimony), Is.True);
        }
        [Test] public void SubEventCyclesAreRejectedBeforeMarkingAnythingOccurred()
        {
            KernelRuntime r = World(); Entity a = r.CreateInstance("Event", "a", "a", false), b = r.CreateInstance("Event", "b", "b", false);
            a.SetValue("subevents", Datum.List(r.Reference(b))); b.SetValue("subevents", Datum.List(r.Reference(a)));
            Assert.That(() => r.RecordEvent("a"), Throws.TypeOf<CommandException>()); Assert.That(r.World.OccurredEvents, Is.Empty);
        }
        [Test] public void FutureEventsCanExistButCannotBeMarkedAsAlreadyOccurred()
        {
            KernelRuntime r = World(); Entity time = r.CreateInstance("Time", "tomorrow", "내일", false), e = r.CreateInstance("Event", "future", "예정 사건", false);
            time.SetValue("year", Datum.Number(1994)); time.SetValue("month", Datum.Number(6)); time.SetValue("day", Datum.Number(4));
            time.SetValue("hour", Datum.Number(9)); time.SetValue("minute", Datum.Number(0)); e.SetValue("time", r.Reference(time));
            Assert.That(() => r.RecordEvent(e.Id), Throws.TypeOf<CommandException>()); Assert.That(r.World.OccurredEvents, Is.Empty);
        }
        [Test] public void PlaceSupportsMultipleConnectionsAndOldSingleConnectionSaves()
        {
            var r = new KernelRuntime(); Entity a = r.CreateInstance("Place", "a", "a"), b = r.CreateInstance("Place", "b", "b");
            a.SetValue("connection", Datum.List(r.Reference(b))); KernelSnapshot old = r.Capture();
            old.entities.Single(e => e.id == "a").values.Single(v => v.name == "connection").value = r.Reference(b);
            var restored = new KernelRuntime(); restored.Restore(old);
            Assert.That(restored.Entity("a").GetValue("connection").Elements.Single().id, Is.EqualTo("b"));
        }
        [Test] public void InvalidFutureObservationsAndNonFiniteSavedMinutesAreRejected()
        {
            KernelRuntime r = World();
            Assert.That(() => r.Observe(new ObservationDefinition { subjectId = "victor", observedAt = new GameDateTime(1995, 1, 1) }), Throws.TypeOf<CommandException>());
            r.Discover("victor"); KernelSnapshot saved = r.Capture(); saved.observations[0].receivedMinute = double.NaN;
            Assert.That(() => new KernelRuntime().Restore(saved), Throws.TypeOf<CommandException>());
        }
        [Test] public void UnsetInlineSerializedDatesUseCurrentTimeAndKeepDefaultViewer()
        {
            KernelRuntime r = World();
            r.Observe(new ObservationDefinition { subjectId = "victor", statement = "보고", observedAt = new GameDateTime(), aboutTime = new GameDateTime() });
            Assert.That(r.Observations.Last().observedAt.ToString(), Is.EqualTo(r.CurrentTime.ToString()));
            Assert.That(r.Observations.Last().aboutTime, Is.Null);
            KernelSnapshot save = r.Capture(); save.entities.Single(e => e.id == "victor").viewer = new DataViewerDefinition();
            var restored = new KernelRuntime(); restored.Restore(save);
            Assert.That(restored.ViewData("victor").Kind, Is.EqualTo(DataViewerKind.Person));
        }
    }
}
