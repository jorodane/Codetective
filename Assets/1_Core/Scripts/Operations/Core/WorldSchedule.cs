using System;
using System.Collections.Generic;
using System.Linq;

namespace Codetective.Operations
{
    [Serializable] public sealed class WorldMutation
    {
        public string subjectId, field;
        public Datum value = Datum.Empty;
        public WorldMutation Copy() => new WorldMutation { subjectId = subjectId, field = field, value = value.Copy() };
    }
    [Serializable] public sealed class WorldScheduleEntry
    {
        public string id, description;
        public int day = 1;
        public double minute = 540;
        public StoryCondition condition = new StoryCondition();
        public WorldMutation[] changes = Array.Empty<WorldMutation>();
        public StoryEffects effects = new StoryEffects();
        public WorldScheduleEntry Copy() => new WorldScheduleEntry { id = id, description = description, day = day, minute = minute,
            condition = condition?.Copy() ?? new StoryCondition(), changes = (changes ?? Array.Empty<WorldMutation>()).Select(c => c.Copy()).ToArray(), effects = effects?.Copy() ?? new StoryEffects() };
    }
    public sealed partial class KernelRuntime
    {
        private readonly Dictionary<string, WorldScheduleEntry> worldSchedule = new Dictionary<string, WorldScheduleEntry>(StringComparer.Ordinal);
        private readonly HashSet<string> completedWorldEvents = new HashSet<string>(StringComparer.Ordinal);
        public void RegisterWorldEvent(WorldScheduleEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.id) || worldSchedule.ContainsKey(entry.id) || entry.day < 1 ||
                entry.minute < 540 || entry.minute > 1080 || double.IsNaN(entry.minute) || double.IsInfinity(entry.minute))
                throw new CommandException("세계 사건의 ID/발생 시각이 유효하지 않습니다.");
            WorldScheduleEntry copy = entry.Copy();
            ValidateWorldChanges(copy);
            copy.effects.Validate(); worldSchedule.Add(copy.id, copy);
        }
        private void ValidateWorldChanges(WorldScheduleEntry entry)
        {
            foreach (WorldMutation change in entry.changes)
            {
                Entity entity = Entity(change.subjectId); VariableDefinition field = entity?.GetVariableDefinition(change.field);
                if (field == null || field.Getter != null || change.value.kind != DataKind.Empty && !Schemas.Accepts(field.Kind, field.SchemaId, change.value, Entity))
                    throw new CommandException("세계 사건의 변경 대상/자료형이 유효하지 않습니다: " + change.subjectId + "." + change.field);
            }
        }
        private double NextWorldBoundary => worldSchedule.Values.Where(e => !completedWorldEvents.Contains(e.id) && e.day == Day && e.minute > Minute)
            .Select(e => e.minute).DefaultIfEmpty(1080).Min();
        private void ApplyWorldSchedule()
        {
            foreach (WorldScheduleEntry entry in worldSchedule.Values.Where(e => !completedWorldEvents.Contains(e.id) && (e.day < Day || e.day == Day && e.minute <= Minute) && e.condition.Matches(this))
                .OrderBy(e => e.day).ThenBy(e => e.minute).ThenBy(e => e.id, StringComparer.Ordinal).ToArray())
            {
                bool prior = invokingAction; invokingAction = false;
                try
                {
                    // A world event changes truth without reporting it to Kernel.
                    // Dynamic instance members may have changed since this event was registered.
                    ValidateWorldChanges(entry);
                    foreach (WorldMutation change in entry.changes) Entity(change.subjectId).SetValue(change.field, change.value);
                    ApplyStoryEffects(entry.effects); completedWorldEvents.Add(entry.id);
                    if (entry.changes.Length > 0 && !string.IsNullOrEmpty(entry.description)) RecordIncident(entry.changes[0].subjectId, entry.description);
                }
                finally { invokingAction = prior; }
            }
        }
    }
}
