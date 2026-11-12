using System;
using System.Linq;

namespace Codetective.Operations
{
    /// <summary>A small playable foundation scenario. Content can be extended through the same public registry.</summary>
    public static class ClinicScenario
    {
        public static KernelRuntime Create()
        {
            var runtime = new KernelRuntime();
            runtime.Authorities.Add("clinic.basic");
            EntitySchema hqType = Define(runtime, "Headquarters", "지휘소형", DataKind.Place);
            var hq = new Entity("hq", "지휘소", hqType) { Aliases = new[] { "지휘소" } };
            hqType.Operations.Add(new Operation { Code = "requestCoffee", Label = "커피 요청", Aliases = new[] { "커피요청" }, ReturnKind = DataKind.Task,
                Access = new AccessRule { Authorities = new[] { "clinic.basic" }, NeedsReadyThread = true,
                    Condition = r => !r.Threads.Any(t => t.SceneAction == "coffee") }, Invoke = c => c.Runtime.RequestCoffee() });
            hqType.Operations.Add(new Operation { Code = "coffee", Label = "전달받은 내 커피", Property = true, ReturnKind = DataKind.Item,
                Access = new AccessRule { Facts = new[] { "coffee_received" } },
                Invoke = c => Datum.Ref(DataKind.Item, "coffee", "Kernel의 커피") });
            hqType.Operations.Add(new Operation { Code = "checkComms", Label = "통신 확인", Aliases = new[] { "통신확인" }, ReturnKind = DataKind.Text,
                Invoke = c => Datum.Text(string.Join(" / ", c.Runtime.Threads.Select(t => t.Id + " " + t.State))) });
            hqType.Operations.Add(new Operation { Code = "requestSupplies", Label = "물품 요청", Aliases = new[] { "물품요청" }, ReturnKind = DataKind.Item,
                Access = new AccessRule { Facts = new[] { "coffee_received" } }, Invoke = c => Datum.Ref(DataKind.Item, "toolkit", "작업 도구") });
            foreach (string action in new[] { "lockDoor", "turnOffLights", "callPolice" })
            {
                string key = action;
                hqType.Operations.Add(new Operation { Code = key, Label = "지휘소 방어 지시", ReturnKind = DataKind.Text,
                    Access = new AccessRule { Facts = new[] { "enemy_at_clinic" } },
                    Invoke = c => c.Runtime.HeadquartersOrder(key) });
            }
            EntitySchema doorType = Define(runtime, "ClinicDoor", "상담소문형", DataKind.Item);
            doorType.Variables.Add(new VariableDefinition { Code = "locked", Label = "잠금 상태", Kind = DataKind.Boolean,
                DefaultValue = Datum.Boolean(false), Names = BuiltinSchemas.Ko("잠김") });
            var door = new Entity("hq_door", "상담소 문", doorType) { Aliases = new[] { "문" } };
            doorType.Operations.Add(new Operation { Code = "lock", Label = "문을 잠근다", Aliases = new[] { "잠근다" }, ReturnKind = DataKind.Text,
                Access = new AccessRule { Facts = new[] { "enemy_at_clinic" } }, Invoke = c => c.Runtime.HeadquartersOrder("lockDoor") });
            hq.Children.Add("door", door);
            EntitySchema lightsType = Define(runtime, "ClinicLights", "상담소조명형", DataKind.Item);
            lightsType.Variables.Add(new VariableDefinition { Code = "enabled", Label = "점등 상태", Kind = DataKind.Boolean,
                DefaultValue = Datum.Boolean(true), Names = BuiltinSchemas.Ko("켜짐") });
            var lights = new Entity("hq_lights", "상담소 조명", lightsType) { Aliases = new[] { "조명" } };
            lightsType.Operations.Add(new Operation { Code = "turnOff", Label = "조명을 끈다", Aliases = new[] { "끈다" }, ReturnKind = DataKind.Text,
                Access = new AccessRule { Facts = new[] { "enemy_at_clinic" } }, Invoke = c => c.Runtime.HeadquartersOrder("turnOffLights") });
            hq.Children.Add("lights", lights);
            ClinicCommandNames.Apply(hq);
            runtime.Register(hq);

            EntitySchema archiveType = Define(runtime, "Archive", "자료실형", DataKind.Place);
            var archive = new Entity("archive", "현장 자료실", archiveType) { Aliases = new[] { "자료실" } };
            archiveType.Operations.Add(new Operation { Code = "inspect", Label = "현장 자료 조사", Aliases = new[] { "조사" }, Minutes = 5,
                ReturnKind = DataKind.Document, Access = new AccessRule { Facts = new[] { "basement_noise" } },
                Invoke = c => { c.Runtime.Facts.Add("archive_inspected"); return Datum.Ref(DataKind.Document, "archive_record", "야간 출입 기록", "상담 진술과 대조할 수 있는 현장 자료"); } });
            archiveType.Operations.Add(new Operation { Code = "analyze", Label = "자료 분석", Aliases = new[] { "분석" }, Minutes = 8,
                Parameters = new[] { new Parameter("document", DataKind.Document) }, ReturnKind = DataKind.Analysis,
                Access = new AccessRule { Facts = new[] { "archive_inspected" }, RequiredData = DataKind.Document },
                Invoke = c => { c.Runtime.Facts.Add("record_analyzed"); return Datum.Ref(DataKind.Analysis, "record_analysis", "출입 기록 분석", c.Arguments[0].label + " 분석 완료"); } });
            ClinicCommandNames.Apply(archive);
            runtime.Register(archive);
            EntitySchema clientType = Define(runtime, "Client", "상담손님형", DataKind.Person);
            var client = new Entity("client", "상담 손님", clientType) { Aliases = new[] { "상담손님" } };
            clientType.Operations.Add(new Operation { Code = "identity", Label = "기본 프로필", Property = true, ReturnKind = DataKind.Text,
                Invoke = c => Datum.Text("상담 예약자 / 진술 확인 중") });
            clientType.Operations.Add(new Operation { Code = "showEvidence", Label = "분석 결과를 보여준다", Aliases = new[] { "증거를보여준다" },
                Parameters = new[] { new Parameter("evidence", DataKind.Analysis) }, ReturnKind = DataKind.Statement,
                Access = new AccessRule { Facts = new[] { "record_analyzed" } },
                Invoke = c => { c.Runtime.Facts.Add("testimony_crosschecked"); return Datum.Ref(DataKind.Statement, "revised_statement", "추가 진술", "분석 결과를 제시하여 새로운 진술을 확보했습니다."); } });
            ClinicCommandNames.Apply(client);
            runtime.Register(client);
            EntitySchema coffeeType = Define(runtime, "Coffee", "커피형", DataKind.Item);
            coffeeType.Access.Facts = new[] { "coffee_received" };
            var coffee = new Entity("coffee", "Kernel의 커피", coffeeType);
            coffeeType.Operations.Add(new Operation { Code = "inspect", Label = "커피 확인", ReturnKind = DataKind.Text,
                Invoke = c => Datum.Text("손님용 컵과 동일한 컵으로 전달받은 Kernel의 커피입니다.") });
            ClinicCommandNames.Apply(coffee);
            runtime.Register(coffee);
            Entity toolkit = runtime.CreateInstance("Item", "toolkit", "작업 도구", false);
            toolkit.Access.Facts = new[] { "coffee_received" };
            toolkit.SetValue("description", Datum.Text("지휘소에 요청해 전달받는 작업 도구입니다."));

            DefineThread(runtime.Schemas.Require("Thread"));
            for (int i = 1; i <= 3; i++)
            {
                string id = "thread" + i.ToString("D2");
                runtime.Threads.Add(new ThreadAgent(id));
                var entity = new Entity(id, id.ToUpperInvariant(), runtime.Schemas.Require("Thread"));
                ClinicCommandNames.Apply(entity);
                runtime.Register(entity, "Thread" + id.Substring(6));
            }
            runtime.NotifySchemaChanged();
            runtime.Consultation.Add(new ConsultationLine { speaker = "상담 손님", sentence = "회사 지하실에서 밤마다 이상한 기계음이 들려요.",
                keyword = "지하실 야간 소음", fact = "basement_noise", dataName = "basementNoise", kind = DataKind.Statement });
            runtime.Consultation.Add(new ConsultationLine { speaker = "상담 손님", sentence = "누군가 야간 출입 기록을 감추려는 것 같아요.",
                keyword = "야간 출입 기록", fact = "entry_record_mentioned", dataName = "entryRecordClue", kind = DataKind.Clue });
            runtime.SaveProtocol("protocol recon(Place target) { return target.inspect(); }");
            return runtime;
        }

        private static EntitySchema Define(KernelRuntime runtime, string id, string korean, DataKind kind)
        {
            var schema = new EntitySchema(id, korean, kind, runtime.Schemas.ForKind(kind)) { Names = BuiltinSchemas.Ko(korean) };
            runtime.Schemas.Register(schema);
            return schema;
        }

        private static void DefineThread(EntitySchema schema)
        {
            schema.Operations.Add(new Operation { Code = "status", Label = "현재 상태", Property = true, ReturnKind = DataKind.Text,
                Invoke = c => c.Runtime.Agent(c.Target.Id) is ThreadAgent a ? Datum.Text(a.State + " / " + a.Message) : Datum.Text("실행 컨텍스트가 없습니다.") });
            schema.Operations.Add(new Operation { Code = "report", Label = "마지막 보고", Property = true, ReturnKind = DataKind.Any,
                Access = new AccessRule { TargetCondition = (r, e) => r.Agent(e.Id) is ThreadAgent a && a.Report.kind != DataKind.Empty }, Invoke = c => c.Runtime.Agent(c.Target.Id).Report.Copy() });
            schema.Operations.Add(new Operation { Code = "run", Label = "프로토콜 배정", Aliases = new[] { "프로토콜실행" },
                Parameters = new[] { new Parameter("protocol", DataKind.Protocol) }, Variadic = true, ReturnKind = DataKind.Task,
                Access = new AccessRule { TargetCondition = (r, e) => r.Agent(e.Id)?.State == AgentState.Ready },
                Invoke = c => c.Runtime.StartProtocol(c.Target.Id, c.Arguments[0].id, c.Arguments.Skip(1).ToArray()) });
            schema.Operations.Add(new Operation { Code = "resume", Label = "예외 해결 후 재개", ReturnKind = DataKind.Text,
                Access = new AccessRule { TargetCondition = (r, e) => r.Agent(e.Id) is ThreadAgent a && a.State == AgentState.Waiting && a.SceneAction == null },
                Invoke = c => { c.Runtime.Resume(c.Target.Id); return Datum.Text("재개 지시 접수"); } });
            schema.Operations.Add(new Operation { Code = "recall", Label = "복귀 지시", Aliases = new[] { "복귀" }, ReturnKind = DataKind.Text,
                Access = new AccessRule { TargetCondition = (r, e) => r.Agent(e.Id) is ThreadAgent a && a.State != AgentState.Ready && a.State != AgentState.Disconnected && a.SceneAction == null },
                Invoke = c => { c.Runtime.Recall(c.Target.Id); return Datum.Text("복귀 지시 접수"); } });
        }
    }
}
