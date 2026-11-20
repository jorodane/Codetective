using System;
using System.Collections.Generic;
using System.Linq;

namespace Codetective.Operations
{
    public sealed partial class KernelRuntime
    {
        private Operation ResolveOperation(Entity target, string name, Datum[] arguments, Dictionary<string, Datum> locals)
        {
            Operation[] named = target.EffectiveOperations.Where(o => !o.Property && o.Matches(name)).ToArray();
            if (named.Length == 0) throw new CommandException("존재하지 않는 호출입니다: " + name);
            Operation[] typed = named.Where(o => arguments.Length >= o.Parameters.Length && (o.Variadic || arguments.Length == o.Parameters.Length) &&
                o.Parameters.Select((p, i) => Accepts(p, arguments[i])).All(v => v)).ToArray();
            if (typed.Length == 0) throw new CommandException("일치하는 입력 자료형/개수의 행동이 없습니다: " + name);
            Operation[] permitted = typed.Where(o => OperationDenial(target, o, locals) == null).ToArray();
            if (permitted.Length == 0) { CheckOperationAccess(target, typed[0], locals); throw new CommandException("행동 권한이 없습니다."); }
            // Prefer an overload only when every input is at least as specific. Incomparable
            // signatures stay ambiguous rather than winning by a summed inheritance-depth score.
            bool MoreSpecific(Operation left, Operation right)
            {
                bool strict = !left.Variadic && right.Variadic;
                for (int i = 0; i < arguments.Length; i++)
                {
                    Parameter a = i < left.Parameters.Length ? left.Parameters[i] : new Parameter("extra", DataKind.Any);
                    Parameter b = i < right.Parameters.Length ? right.Parameters[i] : new Parameter("extra", DataKind.Any);
                    if (!InputSubtype(a, b)) return false;
                    if (!InputSubtype(b, a)) strict = true;
                }
                return strict;
            }
            Operation[] choices = permitted.Where(o => !permitted.Any(other => other != o && MoreSpecific(other, o))).ToArray();
            if (choices.Length != 1) throw new CommandException("행동 입력이 여러 오버로드와 일치합니다: " + name);
            return choices[0];
        }
        private bool InputSubtype(Parameter actual, Parameter expected)
        {
            if (expected.Kind == DataKind.Any && string.IsNullOrEmpty(expected.SchemaId)) return true;
            if (actual.Kind == DataKind.Any && string.IsNullOrEmpty(actual.SchemaId)) return false;
            bool aArray = DataTypes.IsArray(actual.SchemaId), bArray = DataTypes.IsArray(expected.SchemaId);
            if (aArray || bArray || actual.Kind == DataKind.Array || expected.Kind == DataKind.Array)
                return (aArray || actual.Kind == DataKind.Array) && (bArray || expected.Kind == DataKind.Array) &&
                    InputSubtype(aArray ? DataTypes.Parameter("item", actual.SchemaId.Substring(0, actual.SchemaId.Length - 2)) : new Parameter("item", DataKind.Any),
                        bArray ? DataTypes.Parameter("item", expected.SchemaId.Substring(0, expected.SchemaId.Length - 2)) : new Parameter("item", DataKind.Any));
            string[] aDict = DataTypes.DictionaryTypes(actual.SchemaId), bDict = DataTypes.DictionaryTypes(expected.SchemaId);
            if (aDict != null || bDict != null || actual.Kind == DataKind.Dictionary || expected.Kind == DataKind.Dictionary)
                return (aDict != null || actual.Kind == DataKind.Dictionary) && (bDict != null || expected.Kind == DataKind.Dictionary) &&
                    Enumerable.Range(0, 2).All(i => InputSubtype(aDict == null ? new Parameter("item", DataKind.Any) : DataTypes.Parameter("item", aDict[i]),
                        bDict == null ? new Parameter("item", DataKind.Any) : DataTypes.Parameter("item", bDict[i])));
            EntitySchema a = Schemas.Find(string.IsNullOrEmpty(actual.SchemaId) ? actual.Kind.ToString() : actual.SchemaId);
            EntitySchema b = Schemas.Find(string.IsNullOrEmpty(expected.SchemaId) ? expected.Kind.ToString() : expected.SchemaId);
            return a != null && b != null ? a.IsA(b) : actual.Kind == expected.Kind && actual.SchemaId == expected.SchemaId;
        }
        public Datum DispatchAction(string threadId, Entity target, string action, params Datum[] arguments)
        {
            ThreadAgent agent = Agent(threadId);
            if (agent == null || agent.State != AgentState.Ready) throw new CommandException("활동 가능한 Thread가 아닙니다: " + threadId);
            Operation operation = ResolveOperation(target, action, arguments, null);
            var parameters = operation.Parameters.Select((p, i) => p.TypeName(CommandLocale.English) + " input" + i).ToArray();
            string source = "protocol dispatched(Entity target" + (parameters.Length > 0 ? ", " + string.Join(", ", parameters) : "") +
                ") { return target." + operation.Code + "(" + string.Join(", ", arguments.Select((a, i) => "input" + i)) + "); }";
            Protocol protocol = new CommandParser(source).ReadProtocol(source);
            agent.Frames.Clear(); agent.Observations.Clear(); agent.Discoveries.Clear();
            agent.CipherTable.Clear(); foreach (var pair in data) agent.CipherTable[pair.Key] = pair.Value.value.Copy();
            agent.KnowledgeFlags.Clear(); agent.GrantedAuthorities.Clear();
            agent.Frames.Add(new ProtocolFrame { Protocol = protocol, Locals = Bind(protocol, new[] { Reference(target) }.Concat(arguments).ToArray(), agent.CipherTable) });
            agent.Delay = -1; agent.State = AgentState.Executing; agent.Message = target.Label + " / " + operation.NameFor(Language);
            Report(threadId + " / 명령 송신: " + agent.Message);
            return Datum.Ref(DataKind.Task, threadId, agent.Message);
        }
        public void RecordIncident(string subjectId, string description, GameDateTime at = null)
        {
            if (Entity(subjectId) == null || string.IsNullOrWhiteSpace(description)) throw new CommandException("세계 사건의 대상/내용이 유효하지 않습니다.");
            GameDateTime time = at?.Copy() ?? CurrentTime;
            if (time.Value > CurrentTime.Value) throw new CommandException("미래의 사건을 발생 이력에 기록할 수 없습니다.");
            worldEvents.Add(new WorldEventRecord { number = nextWorldEvent++, day = at?.IsSpecified == true ? (time.Value.Date - calendarStart).Days + 1 : Day,
                minute = at?.IsSpecified == true ? time.Value.TimeOfDay.TotalMinutes : Minute, at = time, subjectId = subjectId, field = "", description = description });
            Revision++;
        }
        internal void ApplyReports(Invocation invocation, ObservationDefinition[] reports, string[] discovered, string[] incidents)
        {
            foreach (string id in discovered) if (Entity(id) == null) throw new CommandException("발견 대상이 없습니다: " + id);
            foreach (ObservationDefinition authored in reports)
            {
                ObservationDefinition report = authored.Copy();
                if (string.IsNullOrEmpty(report.subjectId)) report.subjectId = invocation.Target.Id;
                if (string.IsNullOrEmpty(report.sourceId)) report.sourceId = invocation.Target.Id;
                if (string.IsNullOrEmpty(report.source)) report.source = invocation.Target.Label;
                Observe(report);
            }
            foreach (string id in discovered) Discover(id, invocation.AgentId ?? invocation.Target.Label);
            foreach (string incident in incidents) RecordIncident(invocation.Target.Id, incident);
        }
        internal void ValidateReports(Invocation call, ObservationDefinition[] reports, string[] discovered)
        {
            foreach (string id in discovered) if (Entity(id) == null) throw new CommandException("발견 대상이 없습니다: " + id);
            foreach (ObservationDefinition report in reports)
            {
                Entity target = string.IsNullOrEmpty(report.subjectId) ? call.Target : Entity(report.subjectId);
                if (target == null) throw new CommandException("관측 대상이 없습니다: " + report.subjectId);
                if (report.useCurrentField && target.GetVariableDefinition(report.field) == null) throw new CommandException("관측 변수가 없습니다: " + report.field);
                if (report.value == null) throw new CommandException("관측 값이 비어 있습니다.");
                if (report.value.kind == DataKind.Array || report.value.kind == DataKind.Dictionary) report.value.collection.Validate();
            }
        }
    }
}
