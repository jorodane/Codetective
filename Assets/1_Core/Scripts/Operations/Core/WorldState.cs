using System;
using System.Collections.Generic;
using System.Linq;

namespace Codetective.Operations
{
    public sealed partial class KernelRuntime
    {
        public int Day { get; private set; } = 1;
        public double Minute { get; private set; } = 540;
        public bool IsDayEnded { get; private set; }
        public double Suspicion { get; private set; }
        public double Exposure { get; private set; }
        public bool CoffeeReceived { get; private set; }
        public int ConsultationIndex { get; private set; }
        public double ResponseMinutes { get; private set; }
        public ConsultationLine CurrentConsultation => ConsultationIndex < Consultation.Count ? Consultation[ConsultationIndex] : null;
        public string ClockText => $"DAY {Day:00}  {(int)Minute / 60:00}:{(int)Minute % 60:00}";

        public void Tick(double minutes)
        {
            if (IsDayEnded || minutes <= 0 || double.IsNaN(minutes) || double.IsInfinity(minutes)) return;
            double used = Math.Min(minutes, 1080 - Minute);
            budget = 1024; depth = 0;
            foreach (ThreadAgent agent in Threads.ToArray()) TickAgent(agent, used);
            if (CurrentConsultation != null)
            {
                double previous = Math.Floor(ResponseMinutes / 5);
                ResponseMinutes += used;
                Suspicion += Math.Floor(ResponseMinutes / 5) - previous;
            }
            Exposure = Math.Min(1, Exposure + used * (1 + Suspicion * 0.1) / 5400);
            if (Exposure >= 1 && Facts.Add("enemy_at_clinic")) Report("상담소 위치가 노출되었습니다. 지휘소 방어 권한을 확인하세요.");
            double previousMinute = Math.Floor(Minute);
            Minute += used;
            if (Math.Floor(Minute) != previousMinute) Revision++;
            if (Minute >= 1080)
            {
                IsDayEnded = true;
                Report("18:00 — 오늘의 업무가 끝났습니다. 날짜 단위 자동 저장을 진행합니다.");
                DayEnded?.Invoke();
            }
        }

        public void NextDay()
        {
            if (!IsDayEnded) throw new CommandException("18시 이후 다음 날로 진행할 수 있습니다.");
            Day++;
            Minute = 540;
            IsDayEnded = false;
            ResponseMinutes = 0;
            Report("DAY " + Day + " — 09:00 업무 시작. 현장 상황이 이어집니다.");
        }

        public Datum HeadquartersOrder(string action)
        {
            Facts.Add(action);
            if (action == "lockDoor") Entity("hq_door")?.SetValue("locked", Datum.Boolean(true));
            if (action == "turnOffLights") Entity("hq_lights")?.SetValue("enabled", Datum.Boolean(false));
            if (!DryRun) SceneActionRequested?.Invoke("", action);
            return Datum.Text("지휘소 지시 접수: " + action);
        }

        public Datum RecordClue(int index)
        {
            if (index < 0 || index >= Consultation.Count) throw new CommandException("상담 기록을 찾을 수 없습니다.");
            ConsultationLine line = Consultation[index];
            if (index > ConsultationIndex) throw new CommandException("아직 듣지 않은 진술입니다.");
            Datum value = Datum.Ref(line.kind, line.fact, line.keyword, line.sentence);
            if (!RecordedClues.Contains(line.fact))
            {
                Store(line.dataName, value);
                RecordedClues.Add(line.fact);
                Facts.Add(line.fact);
                Report("새 데이터: " + line.dataName + " / " + line.keyword + " — 관련 권한을 확인하세요.");
            }
            return NormalizeReference(value);
        }

        public void Reply()
        {
            if (IsDayEnded) throw new CommandException("오늘의 상담 시간이 끝났습니다.");
            if (CurrentConsultation == null) return;
            ResponseMinutes = 0;
            ConsultationIndex++;
            Report(CurrentConsultation == null ? "상담이 끝났습니다. 프로필에서 진술을 다시 확인할 수 있습니다." : "상담 응답: 음, 계속 말씀해 주세요.");
        }

        public KernelSnapshot Capture()
        {
            return new KernelSnapshot
            {
                day = Day, minute = Minute, suspicion = Suspicion, exposure = Exposure,
                responseMinutes = ResponseMinutes, consultationIndex = ConsultationIndex,
                dayEnded = IsDayEnded, coffeeReceived = CoffeeReceived,
                facts = Facts.ToArray(), authorities = Authorities.ToArray(), recordedClues = RecordedClues.ToArray(),
                data = data.Values.Select(CloneSlot).ToArray(), protocols = protocols.Values.Select(p => p.Source).ToArray(),
                entities = entities.Values.Select(e => new EntitySnapshot
                {
                    id = e.Id, label = e.Label, schemaId = e.Schema.Id, aliases = e.Aliases.ToArray(), names = e.Names.ToArray(),
                    rootNames = roots.Where(r => r.Value == e).Select(r => r.Key).ToArray(), values = e.CaptureValues(),
                    children = e.Children.Select(c => new EntityChildSnapshot { name = c.Key, id = c.Value.Id }).ToArray(),
                    localVariables = e.CaptureLocalVariables(), localFunctions = e.CaptureLocalFunctions(),
                    disabledVariables = e.CaptureDisabledVariables(), disabledFunctions = e.CaptureDisabledFunctions()
                }).ToArray(),
                agents = Threads.Select(a => new AgentSnapshot
                {
                    id = a.Id, state = a.State, message = a.Message, location = a.Location, report = a.Report.Copy(), delay = a.Delay, sceneAction = a.SceneAction,
                    frames = a.Frames.Select(f => new FrameSnapshot { source = f.Protocol.Source, cursor = f.Cursor,
                        returnTarget = f.ReturnTarget, locals = f.Locals.Select(v => new DataSlot { name = v.Key, value = v.Value.Copy() }).ToArray() }).ToArray()
                }).ToArray()
            };
        }

        public void Restore(KernelSnapshot snapshot)
        {
            if (snapshot == null || snapshot.version != 1 || snapshot.day < 1 || snapshot.minute < 540 || snapshot.minute > 1080)
                throw new CommandException("지원하지 않거나 손상된 저장 데이터입니다.");
            var restoredProtocols = new Dictionary<string, Protocol>(StringComparer.Ordinal);
            foreach (string source in snapshot.protocols ?? Array.Empty<string>())
            {
                Protocol protocol = new CommandParser(source).ReadProtocol(source);
                ValidateProtocolTypes(protocol);
                restoredProtocols.Add(protocol.Name, protocol);
            }
            var restoredAgents = new List<ThreadAgent>();
            foreach (AgentSnapshot saved in snapshot.agents ?? Array.Empty<AgentSnapshot>())
            {
                var agent = new ThreadAgent(saved.id) { State = saved.state, Message = saved.message, Location = saved.location ?? "비공개 대기 지점",
                    Report = saved.report?.Copy() ?? Datum.Empty, Delay = saved.delay, SceneAction = saved.sceneAction };
                foreach (FrameSnapshot frame in saved.frames ?? Array.Empty<FrameSnapshot>())
                {
                    Protocol protocol = new CommandParser(frame.source).ReadProtocol(frame.source);
                    ValidateProtocolTypes(protocol);
                    if (frame.cursor < 0 || frame.cursor > protocol.Body.Count) throw new CommandException("진행 중 프로토콜의 저장 위치가 잘못되었습니다.");
                    agent.Frames.Add(new ProtocolFrame { Protocol = protocol, Cursor = frame.cursor, ReturnTarget = frame.returnTarget,
                        Locals = (frame.locals ?? Array.Empty<DataSlot>()).ToDictionary(s => s.name, s => s.value.Copy(), StringComparer.Ordinal) });
                }
                restoredAgents.Add(agent);
            }
            Day = snapshot.day; Minute = snapshot.minute; IsDayEnded = snapshot.dayEnded;
            RestoreEntities(snapshot.entities);
            Suspicion = snapshot.suspicion; Exposure = snapshot.exposure; ResponseMinutes = snapshot.responseMinutes;
            ConsultationIndex = Math.Max(0, Math.Min(snapshot.consultationIndex, Consultation.Count));
            CoffeeReceived = snapshot.coffeeReceived;
            Facts.Clear(); Facts.UnionWith(snapshot.facts ?? Array.Empty<string>());
            Authorities.Clear(); Authorities.UnionWith(snapshot.authorities ?? Array.Empty<string>());
            RecordedClues.Clear(); RecordedClues.UnionWith(snapshot.recordedClues ?? Array.Empty<string>());
            data.Clear();
            foreach (DataSlot slot in snapshot.data ?? Array.Empty<DataSlot>())
            {
                DataSlot restored = CloneSlot(slot); restored.value = NormalizeReference(restored.value);
                data.Add(restored.name, restored);
            }
            nextDataId = data.Count == 0 ? 1 : data.Values.Max(s => s.id) + 1;
            protocols.Clear(); foreach (var pair in restoredProtocols) protocols.Add(pair.Key, pair.Value);
            Threads.Clear(); Threads.AddRange(restoredAgents);
            Revision++;
        }

        private static DataSlot CloneSlot(DataSlot slot) => new DataSlot { id = slot.id, name = slot.name, value = slot.value.Copy() };
    }
}
