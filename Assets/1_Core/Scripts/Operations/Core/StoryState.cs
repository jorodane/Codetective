using System;
using System.Collections.Generic;
using System.Linq;

namespace Codetective.Operations
{
    public enum PlayerAccess { Inherit, Public, Internal }
    public enum QuestStatus { Any = -1, NotStarted, Active, Completed, Failed }
    [Serializable] public sealed class TriggerValue { public string id; public bool value; public TriggerValue Copy() => (TriggerValue)MemberwiseClone(); }
    [Serializable] public sealed class QuestProgress
    {
        public string id; public int stage; public QuestStatus status = QuestStatus.Active;
        public QuestProgress Copy() => (QuestProgress)MemberwiseClone();
    }
    [Serializable] public sealed class QuestRequirement
    {
        public string id; public int minimumStage; public int maximumStage = int.MaxValue; public QuestStatus status = QuestStatus.Any;
        public QuestRequirement Copy() => (QuestRequirement)MemberwiseClone();
    }
    [Serializable] public sealed class StoryCondition
    {
        public string[] triggersOn = Array.Empty<string>();
        public string[] triggersOff = Array.Empty<string>();
        public QuestRequirement[] quests = Array.Empty<QuestRequirement>();
        public bool Matches(KernelRuntime runtime) => (triggersOn ?? Array.Empty<string>()).All(runtime.GetTrigger) && (triggersOff ?? Array.Empty<string>()).All(id => !runtime.GetTrigger(id)) &&
            (quests ?? Array.Empty<QuestRequirement>()).All(q => { QuestProgress state = runtime.GetQuest(q.id); return state.stage >= q.minimumStage && state.stage <= q.maximumStage && (q.status == QuestStatus.Any || state.status == q.status); });
        public StoryCondition Copy() => new StoryCondition { triggersOn = triggersOn?.ToArray() ?? Array.Empty<string>(), triggersOff = triggersOff?.ToArray() ?? Array.Empty<string>(), quests = quests?.Select(q => q.Copy()).ToArray() ?? Array.Empty<QuestRequirement>() };
    }
    [Serializable] public sealed class StoryEffects
    {
        public TriggerValue[] triggers = Array.Empty<TriggerValue>();
        public QuestProgress[] quests = Array.Empty<QuestProgress>();
        public StoryEffects Copy() => new StoryEffects { triggers = triggers?.Select(t => t.Copy()).ToArray() ?? Array.Empty<TriggerValue>(), quests = quests?.Select(q => q.Copy()).ToArray() ?? Array.Empty<QuestProgress>() };
        public void Validate()
        {
            if ((triggers ?? Array.Empty<TriggerValue>()).Any(t => t == null || string.IsNullOrWhiteSpace(t.id)) || (quests ?? Array.Empty<QuestProgress>()).Any(q => q == null || string.IsNullOrWhiteSpace(q.id) || q.stage < 0 || q.status == QuestStatus.Any || !Enum.IsDefined(typeof(QuestStatus), q.status)))
                throw new CommandException("트리거/퀘스트 변경 값이 유효하지 않습니다.");
        }
    }
    [Serializable] public sealed class PlayerAccessOverride { public string kind; public string member; public PlayerAccess access; }

    public sealed partial class KernelRuntime
    {
        private readonly Dictionary<string, bool> triggers = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly Dictionary<string, QuestProgress> quests = new Dictionary<string, QuestProgress>(StringComparer.Ordinal);
        public bool GetTrigger(string id) => id != null && triggers.TryGetValue(id, out bool value) && value;
        public QuestProgress GetQuest(string id) => id != null && quests.TryGetValue(id, out QuestProgress state) ? state.Copy() : new QuestProgress { id = id, status = QuestStatus.NotStarted };
        public void SetTrigger(string id, bool value)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new CommandException("트리거 ID가 필요합니다.");
            if (triggers.TryGetValue(id, out bool previous) && previous == value) return;
            triggers[id] = value; Revision++; NarrativeUpdated();
        }
        public void SetQuestStage(string id, int stage, QuestStatus status = QuestStatus.Active)
        {
            if (string.IsNullOrWhiteSpace(id) || stage < 0 || status == QuestStatus.Any || !Enum.IsDefined(typeof(QuestStatus), status)) throw new CommandException("퀘스트 상태가 유효하지 않습니다.");
            QuestProgress previous = GetQuest(id);
            if (previous.stage == stage && previous.status == status) return;
            quests[id] = new QuestProgress { id = id, stage = stage, status = status }; Revision++; NarrativeUpdated();
        }
        public void ApplyStoryEffects(StoryEffects effects)
        {
            if (effects == null) return;
            effects.Validate();
            foreach (TriggerValue trigger in effects.triggers ?? Array.Empty<TriggerValue>()) SetTrigger(trigger.id, trigger.value);
            foreach (QuestProgress quest in effects.quests ?? Array.Empty<QuestProgress>()) SetQuestStage(quest.id, quest.stage, quest.status);
        }
        private void RestoreStoryState(KernelSnapshot snapshot)
        {
            triggers.Clear(); quests.Clear();
            foreach (TriggerValue value in snapshot.triggers ?? Array.Empty<TriggerValue>()) triggers.Add(value.id, value.value);
            foreach (QuestProgress quest in snapshot.quests ?? Array.Empty<QuestProgress>())
            {
                new StoryEffects { quests = new[] { quest } }.Validate(); quests.Add(quest.id, quest.Copy());
            }
        }
    }

    public sealed partial class Entity
    {
        private readonly Dictionary<string, PlayerAccess> playerAccess = new Dictionary<string, PlayerAccess>(StringComparer.Ordinal);
        public void SetEntityAccess(PlayerAccess access) => SetPlayerAccess("entity", "", access);
        public void SetVariableAccess(string name, PlayerAccess access)
        {
            VariableDefinition field = GetVariableDefinition(name) ?? throw new CommandException("존재하지 않는 변수입니다: " + name);
            SetPlayerAccess("variable", field.Code, access);
        }
        public void SetFunctionAccess(string name, PlayerAccess access)
        {
            Operation function = AllOperations.FirstOrDefault(o => o.Matches(name)) ?? throw new CommandException("존재하지 않는 함수입니다: " + name);
            SetPlayerAccess("function", function.Code, access);
        }
        private void SetPlayerAccess(string kind, string member, PlayerAccess access)
        {
            if (!Enum.IsDefined(typeof(PlayerAccess), access)) throw new CommandException("공개 범위가 유효하지 않습니다.");
            string key = kind + ":" + member;
            bool changed = access == PlayerAccess.Inherit ? playerAccess.Remove(key) : !playerAccess.TryGetValue(key, out PlayerAccess old) || old != access;
            if (access != PlayerAccess.Inherit) playerAccess[key] = access;
            if (changed) ValueChanged?.Invoke();
        }
        internal bool Exposed(string kind, string member, IEnumerable<AccessRule> rules)
        {
            if (playerAccess.TryGetValue(kind + ":" + member, out PlayerAccess access)) return access != PlayerAccess.Internal;
            return rules.Select(r => r.PlayerAccess).LastOrDefault(a => a != PlayerAccess.Inherit) != PlayerAccess.Internal;
        }
        internal PlayerAccessOverride[] CapturePlayerAccess() => playerAccess.Select(p => new PlayerAccessOverride { kind = p.Key.Substring(0, p.Key.IndexOf(':')), member = p.Key.Substring(p.Key.IndexOf(':') + 1), access = p.Value }).ToArray();
        internal void RestorePlayerAccess(PlayerAccessOverride[] saved)
        {
            foreach (PlayerAccessOverride state in saved ?? Array.Empty<PlayerAccessOverride>())
                if (state.kind == "entity") SetEntityAccess(state.access);
                else if (state.kind == "variable") SetVariableAccess(state.member, state.access);
                else if (state.kind == "function") SetFunctionAccess(state.member, state.access);
                else throw new CommandException("저장된 공개 범위 대상이 유효하지 않습니다.");
        }
    }
}
