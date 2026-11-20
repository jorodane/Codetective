using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Codetective.Operations
{
    [Serializable] public sealed class GameDateTime
    {
        public int year, month, day, hour, minute;
        public GameDateTime() { }
        public GameDateTime(int year, int month, int day, int hour = 0, int minute = 0)
        { this.year = year; this.month = month; this.day = day; this.hour = hour; this.minute = minute; }
        public DateTime Value => new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        // Unity's inline serialization represents an unset optional class as an all-zero object.
        public bool IsSpecified => year != 0 || month != 0 || day != 0 || hour != 0 || minute != 0;
        public GameDateTime Copy() => IsSpecified ? new GameDateTime(year, month, day, hour, minute) : null;
        public static GameDateTime From(DateTime value) => new GameDateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute);
        public override string ToString() => Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }
    public enum InformationKind { Observation, Testimony, Inference, Synthesis }

    /// <summary>Designer/world API. Player viewers use CurrentInformation instead.</summary>
    public sealed class WorldState
    {
        private readonly KernelRuntime runtime;
        internal WorldState(KernelRuntime runtime) { this.runtime = runtime; }
        public GameDateTime CurrentTime => runtime.CurrentTime;
        public IReadOnlyList<Entity> Objects => runtime.Entities.Values.Where(e => !runtime.IsKnowledgeOnly(e.Id)).ToArray();
        public IReadOnlyList<WorldEventRecord> History => runtime.WorldHistory;
        public IReadOnlyList<Entity> OccurredEvents => runtime.OccurredEventIds.Select(runtime.Entity).Where(e => e != null).ToArray();
        public Entity Find(string id) => runtime.IsKnowledgeOnly(id) ? null : runtime.Entity(id);
    }
    public sealed class CurrentInformation
    {
        private readonly KernelRuntime runtime;
        internal CurrentInformation(KernelRuntime runtime) { this.runtime = runtime; }
        public IReadOnlyList<ObservationRecord> Records => runtime.Observations;
        public IReadOnlyList<DiscoveryRecord> Discoveries => runtime.Discoveries;
        public IReadOnlyList<EvidenceEntry> Items => runtime.Evidence;
        public IReadOnlyList<DataSlot> References => runtime.Data.Values.Where(s => runtime.IsPlayerVisible(s.value)).Select(s => new DataSlot { id = s.id, name = s.name,
            declaredType = s.declaredType, value = s.value.Copy() }).ToArray();
        public Datum Read(string id, string field) => runtime.LastKnownValue(id, field);
        public DataViewerModel View(string id) => runtime.ViewData(id);
    }
    public sealed partial class KernelRuntime
    {
        private DateTime calendarStart = new DateTime(1994, 1, 1);
        private WorldState worldView;
        private CurrentInformation informationView;
        private readonly HashSet<string> knowledgeOnly = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> occurredEvents = new HashSet<string>(StringComparer.Ordinal);
        internal IEnumerable<string> OccurredEventIds => occurredEvents;
        public WorldState World => worldView ?? (worldView = new WorldState(this));
        public CurrentInformation CurrentInformation => informationView ?? (informationView = new CurrentInformation(this));
        public GameDateTime CurrentTime => DateAt(Day, Minute);
        public GameDateTime DateAt(int day, double minute) => GameDateTime.From(calendarStart.AddDays(day - 1).AddMinutes(Math.Floor(minute)));
        public bool IsKnowledgeOnly(string id) => id != null && knowledgeOnly.Contains(id);
        public void ConfigureInitialClock(GameDateTime start)
        {
            if (start?.IsSpecified != true) return;
            DateTime value = start.Value;
            if (Day != 1 || Minute != 540 || value.TimeOfDay.TotalMinutes < 540 || value.TimeOfDay.TotalMinutes > 1080)
                throw new CommandException("시작 시각은 게임 시작 전 09:00~18:00으로 설정해야 합니다.");
            double shift = value.TimeOfDay.TotalMinutes - Minute; calendarStart = value.Date; Minute += shift;
            foreach (ObservationRecord record in observations)
            {
                record.observedMinute += shift; record.receivedMinute += shift;
                record.observedAt = DateAt(record.observedDay, record.observedMinute); record.receivedAt = DateAt(record.receivedDay, record.receivedMinute);
            }
            foreach (WorldEventRecord record in worldEvents) { record.minute += shift; record.at = DateAt(record.day, record.minute); }
        }
        public void RecordEvent(string id)
        {
            var active = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Dictionary<string, GameDateTime>(StringComparer.Ordinal);
            void Visit(string targetId)
            {
                Entity target = World.Find(targetId);
                if (target == null || !target.Schema.IsA(Schemas.Require("Event"))) throw new CommandException("실제 사건이 아닙니다: " + targetId);
                if (!active.Add(targetId)) throw new CommandException("세부사건에 순환이 있습니다: " + targetId);
                if (!pending.ContainsKey(targetId))
                {
                    GameDateTime time = EventTime(target);
                    if (time.Value > CurrentTime.Value) throw new CommandException("미래의 사건을 발생 목록에 넣을 수 없습니다: " + targetId);
                    pending.Add(targetId, time);
                    foreach (Datum child in target.GetValue("subevents").Elements) Visit(child.id);
                }
                active.Remove(targetId);
            }
            Visit(id);
            foreach (var item in pending) if (occurredEvents.Add(item.Key)) RecordIncident(item.Key, "사건 발생: " + Entity(item.Key).Label, item.Value);
        }
        private GameDateTime EventTime(Entity target)
        {
            Datum reference = target.GetValue("time");
            if (reference.kind == DataKind.Empty) return CurrentTime;
            Entity time = World.Find(reference.id) ?? throw new CommandException("사건의 발생 시각이 없습니다.");
            int Part(string field)
            {
                Datum value = time.GetValue(field);
                if (value.kind != DataKind.Number || double.IsNaN(value.number) || double.IsInfinity(value.number) || Math.Truncate(value.number) != value.number || value.number < 0 || value.number > 9999)
                    throw new CommandException("사건 시각의 값이 유효하지 않습니다: " + field);
                return (int)value.number;
            }
            return new GameDateTime(Part("year"), Part("month"), Part("day"), Part("hour"), Part("minute"));
        }
        /// <summary>Records the fact of speech. The content is a claim, never applied as a world-state write.</summary>
        public Datum RecordStatement(string id, string speakerId, string topicId, string content, string[] subjects = null,
            bool report = false, GameDateTime spokenAt = null, string source = "현장 진술")
        {
            Entity Require(string targetId, string schema) => World.Find(targetId) is Entity target && target.Schema.IsA(Schemas.Require(schema)) ? target :
                throw new CommandException("진술의 대상 자료형이 다릅니다: " + targetId);
            Entity speaker = Require(speakerId, "Person"), topic = Require(topicId, "Concept");
            Entity[] related = (subjects ?? Array.Empty<string>()).Select(s => Require(s, "Physical")).ToArray();
            GameDateTime time = spokenAt?.Copy() ?? CurrentTime;
            if (string.IsNullOrWhiteSpace(id) || id.Length > 250 || time.Value > CurrentTime.Value || Entity(id) != null || Entity(id + "__time") != null || string.IsNullOrWhiteSpace(content)) throw new CommandException("진술 ID, 내용 또는 시각이 유효하지 않습니다.");
            Entity timestamp = CreateInstance("Time", id + "__time", time.ToString(), false);
            timestamp.SetValue("year", Datum.Number(time.year)); timestamp.SetValue("month", Datum.Number(time.month)); timestamp.SetValue("day", Datum.Number(time.day));
            timestamp.SetValue("hour", Datum.Number(time.hour)); timestamp.SetValue("minute", Datum.Number(time.minute));
            Entity statement = CreateInstance("Statement", id, speaker.Label + "의 진술", false);
            statement.SetValue("speaker", Reference(speaker)); statement.SetValue("topic", Reference(topic)); statement.SetValue("time", Reference(timestamp));
            statement.SetValue("content", Datum.Text(content)); statement.SetValue("description", Datum.Text(content)); statement.SetValue("subjects", Datum.List(related.Select(Reference).ToArray()));
            RecordIncident(id, "진술: " + speaker.Label, time);
            if (report)
            {
                Discover(id, source, true); Discover(timestamp.Id, source, true, false);
                Observe(new ObservationDefinition { subjectId = id, field = "content", value = Datum.Text(content), sourceId = speakerId,
                    keywordId = topicId, source = source, statement = content, kind = InformationKind.Testimony, observedAt = time });
            }
            return Reference(statement);
        }
        public Datum CreateInformation(string id, string name, string content, InformationKind kind = InformationKind.Inference, string type = "Concept", string source = "Kernel 추론")
        {
            if (kind != InformationKind.Inference && kind != InformationKind.Synthesis) throw new CommandException("독립 정보는 추론 또는 조합 정보로 등록하세요.");
            if (Schemas.Require(type).IsA(Schemas.Require("Physical"))) throw new CommandException("추론으로 실제 인물·물건·장소를 생성할 수 없습니다.");
            Entity entity = CreateInstance(type, id, name, false); knowledgeOnly.Add(id);
            entity.SetValue("description", Datum.Text(content));
            Discover(id, source, false);
            Observe(new ObservationDefinition { subjectId = id, field = "description", value = Datum.Text(content), kind = kind, source = source });
            return Reference(entity);
        }
        private long ObservationOrder(ObservationRecord record)
        {
            DateTime time = record.observedAt != null ? record.observedAt.Value : DateAt(record.observedDay, record.observedMinute).Value;
            return time.Ticks + (long)((record.observedMinute - Math.Floor(record.observedMinute)) * TimeSpan.TicksPerMinute);
        }
        private bool ObservationVisible(ObservationRecord record)
        {
            Entity target = Entity(record.subjectId);
            if (target != null && !IsPlayerVisible(target)) return false;
            if (target != null && !string.IsNullOrEmpty(record.field) && !record.field.StartsWith("$child:", StringComparison.Ordinal))
            {
                VariableDefinition field = target.GetVariableDefinition(record.field);
                if (field == null || VariableDenial(target, field, null) != null) return false;
            }
            return IsPlayerVisible(record.value);
        }
    }
}
