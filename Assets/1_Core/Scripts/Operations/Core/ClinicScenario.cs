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
            var hq = new Entity("hq", "지휘소", DataKind.Place) { Aliases = new[] { "지휘소" } };
            hq.Operations.Add(new Operation { Code = "requestCoffee", Label = "커피 요청", Aliases = new[] { "커피요청" }, ReturnKind = DataKind.Task,
                Access = new AccessRule { Authorities = new[] { "clinic.basic" }, NeedsReadyThread = true,
                    Condition = r => !r.Threads.Any(t => t.SceneAction == "coffee") }, Invoke = c => c.Runtime.RequestCoffee() });
            hq.Operations.Add(new Operation { Code = "coffee", Label = "전달받은 내 커피", Property = true, ReturnKind = DataKind.Item,
                Access = new AccessRule { Facts = new[] { "coffee_received" } },
                Invoke = c => Datum.Ref(DataKind.Item, "coffee", "Kernel의 커피") });
            hq.Operations.Add(new Operation { Code = "checkComms", Label = "통신 확인", Aliases = new[] { "통신확인" }, ReturnKind = DataKind.Text,
                Invoke = c => Datum.Text(string.Join(" / ", c.Runtime.Threads.Select(t => t.Id + " " + t.State))) });
            hq.Operations.Add(new Operation { Code = "requestSupplies", Label = "물품 요청", Aliases = new[] { "물품요청" }, ReturnKind = DataKind.Item,
                Access = new AccessRule { Facts = new[] { "coffee_received" } }, Invoke = c => Datum.Ref(DataKind.Item, "toolkit", "작업 도구") });
            foreach (string action in new[] { "lockDoor", "turnOffLights", "callPolice" })
            {
                string key = action;
                hq.Operations.Add(new Operation { Code = key, Label = "지휘소 방어 지시", ReturnKind = DataKind.Text,
                    Access = new AccessRule { Facts = new[] { "enemy_at_clinic" } },
                    Invoke = c => c.Runtime.HeadquartersOrder(key) });
            }
            var door = new Entity("hq_door", "상담소 문", DataKind.Item) { Aliases = new[] { "문" } };
            door.Operations.Add(new Operation { Code = "lock", Label = "문을 잠근다", Aliases = new[] { "잠근다" }, ReturnKind = DataKind.Text,
                Access = new AccessRule { Facts = new[] { "enemy_at_clinic" } }, Invoke = c => c.Runtime.HeadquartersOrder("lockDoor") });
            hq.Children.Add("door", door);
            var lights = new Entity("hq_lights", "상담소 조명", DataKind.Item) { Aliases = new[] { "조명" } };
            lights.Operations.Add(new Operation { Code = "turnOff", Label = "조명을 끈다", Aliases = new[] { "끈다" }, ReturnKind = DataKind.Text,
                Access = new AccessRule { Facts = new[] { "enemy_at_clinic" } }, Invoke = c => c.Runtime.HeadquartersOrder("turnOffLights") });
            hq.Children.Add("lights", lights);
            ClinicCommandNames.Apply(hq);
            runtime.Register(hq);

            var archive = new Entity("archive", "현장 자료실", DataKind.Place) { Aliases = new[] { "자료실" } };
            archive.Operations.Add(new Operation { Code = "inspect", Label = "현장 자료 조사", Aliases = new[] { "조사" }, Minutes = 5,
                ReturnKind = DataKind.Document, Access = new AccessRule { Facts = new[] { "basement_noise" } },
                Invoke = c => { c.Runtime.Facts.Add("archive_inspected"); return Datum.Ref(DataKind.Document, "archive_record", "야간 출입 기록", "상담 진술과 대조할 수 있는 현장 자료"); } });
            archive.Operations.Add(new Operation { Code = "analyze", Label = "자료 분석", Aliases = new[] { "분석" }, Minutes = 8,
                Parameters = new[] { new Parameter("document", DataKind.Document) }, ReturnKind = DataKind.Analysis,
                Access = new AccessRule { Facts = new[] { "archive_inspected" }, RequiredData = DataKind.Document },
                Invoke = c => { c.Runtime.Facts.Add("record_analyzed"); return Datum.Ref(DataKind.Analysis, "record_analysis", "출입 기록 분석", c.Arguments[0].label + " 분석 완료"); } });
            ClinicCommandNames.Apply(archive);
            runtime.Register(archive);
            var client = new Entity("client", "상담 손님", DataKind.Person) { Aliases = new[] { "상담손님" } };
            client.Operations.Add(new Operation { Code = "identity", Label = "기본 프로필", Property = true, ReturnKind = DataKind.Text,
                Invoke = c => Datum.Text("상담 예약자 / 진술 확인 중") });
            client.Operations.Add(new Operation { Code = "showEvidence", Label = "분석 결과를 보여준다", Aliases = new[] { "증거를보여준다" },
                Parameters = new[] { new Parameter("evidence", DataKind.Analysis) }, ReturnKind = DataKind.Statement,
                Access = new AccessRule { Facts = new[] { "record_analyzed" } },
                Invoke = c => { c.Runtime.Facts.Add("testimony_crosschecked"); return Datum.Ref(DataKind.Statement, "revised_statement", "추가 진술", "분석 결과를 제시하여 새로운 진술을 확보했습니다."); } });
            ClinicCommandNames.Apply(client);
            runtime.Register(client);
            var coffee = new Entity("coffee", "Kernel의 커피", DataKind.Item) { Access = new AccessRule { Facts = new[] { "coffee_received" } } };
            coffee.Operations.Add(new Operation { Code = "inspect", Label = "커피 확인", ReturnKind = DataKind.Text,
                Invoke = c => Datum.Text("손님용 컵과 동일한 컵으로 전달받은 Kernel의 커피입니다.") });
            ClinicCommandNames.Apply(coffee);
            runtime.Register(coffee);

            for (int i = 1; i <= 3; i++) AddThread(runtime, "thread" + i.ToString("D2"));
            runtime.Consultation.Add(new ConsultationLine { speaker = "상담 손님", sentence = "회사 지하실에서 밤마다 이상한 기계음이 들려요.",
                keyword = "지하실 야간 소음", fact = "basement_noise", dataName = "basementNoise", kind = DataKind.Statement });
            runtime.Consultation.Add(new ConsultationLine { speaker = "상담 손님", sentence = "누군가 야간 출입 기록을 감추려는 것 같아요.",
                keyword = "야간 출입 기록", fact = "entry_record_mentioned", dataName = "entryRecordClue", kind = DataKind.Clue });
            runtime.SaveProtocol("protocol recon(Place target) { return target.inspect(); }");
            return runtime;
        }

        private static void AddThread(KernelRuntime runtime, string id)
        {
            runtime.Threads.Add(new ThreadAgent(id));
            var entity = new Entity(id, id.ToUpperInvariant(), DataKind.Thread);
            entity.Operations.Add(new Operation { Code = "status", Label = "현재 상태", Property = true, ReturnKind = DataKind.Text,
                Invoke = c => Datum.Text(c.Runtime.Agent(id).State + " / " + c.Runtime.Agent(id).Message) });
            entity.Operations.Add(new Operation { Code = "report", Label = "마지막 보고", Property = true, ReturnKind = DataKind.Any,
                Access = new AccessRule { Condition = r => r.Agent(id).Report.kind != DataKind.Empty }, Invoke = c => c.Runtime.Agent(id).Report.Copy() });
            entity.Operations.Add(new Operation { Code = "run", Label = "프로토콜 배정", Aliases = new[] { "프로토콜실행" },
                Parameters = new[] { new Parameter("protocol", DataKind.Protocol) }, Variadic = true, ReturnKind = DataKind.Task,
                Access = new AccessRule { Condition = r => r.Agent(id).State == AgentState.Ready },
                Invoke = c => c.Runtime.StartProtocol(id, c.Arguments[0].id, c.Arguments.Skip(1).ToArray()) });
            entity.Operations.Add(new Operation { Code = "resume", Label = "예외 해결 후 재개", ReturnKind = DataKind.Text,
                Access = new AccessRule { Condition = r => r.Agent(id).State == AgentState.Waiting && r.Agent(id).SceneAction == null },
                Invoke = c => { c.Runtime.Resume(id); return Datum.Text("재개 지시 접수"); } });
            entity.Operations.Add(new Operation { Code = "recall", Label = "복귀 지시", Aliases = new[] { "복귀" }, ReturnKind = DataKind.Text,
                Access = new AccessRule { Condition = r => r.Agent(id).State != AgentState.Ready && r.Agent(id).State != AgentState.Disconnected && r.Agent(id).SceneAction == null },
                Invoke = c => { c.Runtime.Recall(id); return Datum.Text("복귀 지시 접수"); } });
            ClinicCommandNames.Apply(entity);
            runtime.Register(entity, "Thread" + id.Substring(6));
        }
    }
}
