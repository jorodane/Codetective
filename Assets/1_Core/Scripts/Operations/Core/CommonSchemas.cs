using System;
using System.Linq;

namespace Codetective.Operations
{
    public static partial class BuiltinSchemas
    {
        private static void PopulateCommon(SchemaRegistry r)
        {
            void Field(string type, string code, string name, DataKind kind, string reference = "", Datum value = null)
                => r.Require(type).Variables.Add(new VariableDefinition { Code = code, Label = name, Kind = kind, SchemaId = reference,
                    DefaultValue = value ?? Datum.Empty, Names = Ko(name) });
            void Action(string type, string code, string name, params Parameter[] parameters)
            {
                var operation = new Operation { Code = code, Label = name, Names = Ko(name), Parameters = parameters,
                    Minutes = 1, DispatchToThread = true, ReturnKind = DataKind.Any, Invoke = c => c.Runtime.StandardAction(c, code) };
                operation.Access.TargetCondition = (world, target) => code == "look" || code == "enter" && parameters.Length == 0 ||
                    target.EffectiveOperations.Any(o => o.Key == operation.Key && !ReferenceEquals(o, operation));
                r.Require(type).Operations.Add(operation);
            }
            Parameter P(string name, string type) => DataTypes.Parameter(name, type);
            Field("Physical", "portrait", "대표사진", DataKind.Photo, "Photo");
            Field("Physical", "history", "기록", DataKind.Array, "Event[]", Datum.List());
            Field("Physical", "relations", "관계대상", DataKind.Dictionary, "Dict<Physical,Relationship[]>", Datum.Dict());
            Action("Physical", "look", "살펴보기");
            Field("Keyword", "topic", "소재", DataKind.Entity, "Concept");
            Field("Keyword", "relations", "관계대상", DataKind.Dictionary, "Dict<Physical,Relationship[]>", Datum.Dict());
            foreach (string part in new[] { "year", "month", "day", "hour", "minute" })
                Field("Time", part, part == "year" ? "년" : part == "month" ? "월" : part == "day" ? "일" : part == "hour" ? "시" : "분", DataKind.Number);
            Field("Photo", "image", "이미지", DataKind.Text); Field("Photo", "time", "시간", DataKind.Time, "Time");
            Field("Relationship", "category", "구분", DataKind.Text);
            Field("Place", "items", "물품목록", DataKind.Array, "Item[]", Datum.List());
            Field("Place", "people", "인물목록", DataKind.Array, "Person[]", Datum.List());
            Field("Place", "connection", "연결된장소", DataKind.Array, "Place[]", Datum.List());
            Action("Place", "enter", "진입하기"); Action("Place", "enter", "진입하기", P("password", "Text")); Action("Place", "enter", "진입하기", P("key", "Physical"));
            Field("Event", "subjects", "관련대상", DataKind.Dictionary, "Dict<Physical,Relationship[]>", Datum.Dict());
            Field("Event", "photo", "현장사진", DataKind.Photo, "Photo"); Field("Event", "time", "발생시각", DataKind.Time, "Time");
            Field("Event", "place", "발생장소", DataKind.Place, "Place"); Field("Event", "content", "내용", DataKind.Text); Field("Event", "note", "비고", DataKind.Text);
            Field("Event", "subevents", "세부사건", DataKind.Array, "Event[]", Datum.List());
            Field("Statement", "speaker", "화자", DataKind.Person, "Person");
            Field("Statement", "topic", "소재", DataKind.Entity, "Concept");
            Field("Statement", "time", "진술시각", DataKind.Time, "Time");
            Field("Statement", "content", "내용", DataKind.Text);
            Field("Statement", "subjects", "관련대상", DataKind.Array, "Physical[]", Datum.List());
            Field("Item", "owner", "소유자", DataKind.Person, "Person");
            Action("Item", "disassemble", "분해"); Action("Item", "disassemble", "분해하기", P("tool", "Tool"));
            Action("Item", "combine", "조합하기", P("other", "Physical")); Action("Item", "combine", "조합하기", P("other", "Physical"), P("tool", "Tool"));
            Action("Tool", "use", "사용하기"); Action("Tool", "use", "사용하기", P("target", "Physical")); Action("Tool", "use", "사용하기", P("targets", "Physical[]"));
            Field("Person", "gender", "성별", DataKind.Text); Field("Person", "age", "연령", DataKind.Number);
            Field("Person", "occupation", "직업", DataKind.Text); Field("Person", "inventory", "소지품", DataKind.Array, "Item[]", Datum.List());
            Field("Person", "topics", "질문소재", DataKind.Array, "Keyword[]", Datum.List());
            Action("Person", "talk", "대화하기"); Action("Person", "ask", "질문하기", P("topic", "Keyword"));
            Action("Person", "present", "제시하기", P("evidence", "Physical")); Action("Person", "askEvent", "사건질문", P("event", "Event"));
            Action("Person", "checkRelationship", "관계확인", P("other", "Physical")); Action("Person", "checkRelationship", "관계확인", P("first", "Physical"), P("second", "Physical"));
            Action("Person", "alibi", "알리바이", P("time", "Time")); Action("Person", "alibi", "알리바이", P("time", "Time"), P("witness", "Person"));
            Field("Organization", "representative", "대표자", DataKind.Person, "Person");
            Field("Organization", "members", "구성원", DataKind.Dictionary, "Dict<Person,Text>", Datum.Dict());
            Field("Organization", "places", "장소목록", DataKind.Dictionary, "Dict<Place,Text>", Datum.Dict());
            foreach (var pair in new[] { ("Entity", DataViewerKind.Generic), ("Concept", DataViewerKind.Concept), ("Physical", DataViewerKind.Item),
                ("Person", DataViewerKind.Person), ("Place", DataViewerKind.Place), ("Item", DataViewerKind.Item), ("Tool", DataViewerKind.Tool),
                ("Event", DataViewerKind.Event), ("Statement", DataViewerKind.Statement), ("Photo", DataViewerKind.Photo), ("Keyword", DataViewerKind.Keyword),
                ("Organization", DataViewerKind.Organization), ("Time", DataViewerKind.Time), ("Relationship", DataViewerKind.Relationship) })
                r.Require(pair.Item1).Viewer = new DataViewerDefinition { kind = pair.Item2 };
            r.Require("Thread").Operations.Add(new Operation { Code = "move", Names = Ko("이동"), Parameters = new[] { P("place", "Place") },
                ReturnKind = DataKind.Task, Invoke = c => c.Runtime.DispatchAction(c.Target.Id, c.Runtime.Entity(c.Arguments[0].id), "enter") });
            r.Require("Thread").Operations.Add(new Operation { Code = "ask", Names = Ko("질문하기"), Parameters = new[] { P("person", "Person"), P("topic", "Keyword") },
                ReturnKind = DataKind.Task, Invoke = c => c.Runtime.DispatchAction(c.Target.Id, c.Runtime.Entity(c.Arguments[0].id), "ask", c.Arguments[1]) });
        }
    }
    public sealed partial class KernelRuntime
    {
        internal Datum StandardAction(Invocation call, string action)
        {
            if (action == "look")
            {
                foreach (VariableDefinition field in call.Target.Variables.Where(f => VariableDenial(call.Target, f, null) == null))
                    Observe(new ObservationDefinition { subjectId = call.Target.Id, field = field.Code, useCurrentField = true, sourceId = call.AgentId ?? "", source = call.AgentId ?? "현장 조사" });
                foreach (Entity child in call.Target.Children.Values) Discover(child.Id, call.AgentId);
                ObserveChildren(call.Target, call.AgentId ?? "현장 조사");
                return Reference(call.Target);
            }
            if (action == "enter")
            {
                // Password/key variants require an authored implementation; never silently bypass a lock.
                if (call.Arguments.Length > 0) throw new CommandException("이 장소의 진입 조건/결과를 설정해야 합니다.");
                ThreadAgent agent = Agent(call.AgentId);
                if (agent != null) agent.Location = call.Target.Id;
                RecordIncident(call.Target.Id, "Thread 진입: " + (call.AgentId ?? "현장 조사"));
                return Datum.Empty;
            }
            throw new CommandException("대상의 행동 결과를 작성해야 합니다: " + action);
        }
    }
}
