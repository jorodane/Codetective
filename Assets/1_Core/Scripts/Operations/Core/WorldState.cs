using System;
using System.Collections.Generic;
using System.Linq;

namespace Codetective.Operations
{
    public sealed partial class KernelRuntime
    {
        private const double MinuteEpsilon = 0.0000001;
        public int Day { get; private set; } = 1;
        public double Minute { get; private set; } = 540;
        public bool IsDayEnded { get; private set; }
        public double Suspicion { get; private set; }
        public double Exposure { get; private set; }
        public bool CoffeeReceived { get; private set; }
        public int ConsultationIndex { get; private set; }
        public double ResponseMinutes { get; private set; }
        public ConsultationLine CurrentConsultation => HasClinicSchedule ? ScheduledConsultation : !AwaitingAssignment && ConsultationIndex < Consultation.Count ? Consultation[ConsultationIndex] : null;
        public string ClockText => $"DAY {Day:00}  {(int)Minute / 60:00}:{(int)Minute % 60:00}";
        public event Action<int> DayStarted;

        public void Tick(double minutes)
        {
            if (TerminalConversationPausesWorld || IsDayEnded || minutes <= 0 || double.IsNaN(minutes) || double.IsInfinity(minutes)) return;
            InitializeClinicDay();
            double? assignmentDelivery = AwaitingAssignment && ActiveVisit == null && NextVisit == null ? NextAssignmentMailMinute : (double?)null;
            if (assignmentDelivery <= Minute) return;
            InitializeDailyRecord(); SyncClinicClock();
            ApplyWorldSchedule();
            if (IsClinicTransitioning) return;
            double boundary = assignmentDelivery ?? NextClinicBoundary;
            double used = Math.Min(minutes, Math.Min(1080 - Minute, boundary - Minute));
            if (used <= 0) return;
            budget = 1024; depth = 0;
            double previousMinute = Math.Floor(Minute);
            foreach (ThreadAgent agent in Threads.ToArray()) TickAgent(agent, 0);
            double remaining = used;
            while (remaining > MinuteEpsilon)
            {
                double step = Threads.Where(a => a.State == AgentState.Executing && a.Delay > 0).Select(a => a.Delay).DefaultIfEmpty(remaining).Min();
                if (NextWorldBoundary > Minute) step = Math.Min(step, NextWorldBoundary - Minute);
                step = Math.Min(step, remaining); Minute += step; remaining -= step;
                ApplyWorldSchedule();
                foreach (ThreadAgent agent in Threads.ToArray()) TickAgent(agent, step);
            }
            bool routineVisit = !string.IsNullOrEmpty(ActiveVisit?.episodeId);
            if (!routineVisit && CurrentConsultation != null)
            {
                double previous = Math.Floor(ResponseMinutes / 5);
                ResponseMinutes += used;
                Suspicion += Math.Floor(ResponseMinutes / 5) - previous;
            }
            if (!AwaitingAssignment && !routineVisit)
            {
                Exposure = Math.Min(1, Exposure + used * (1 + Suspicion * 0.1) / 5400);
                if (Exposure >= 1 && Facts.Add("enemy_at_clinic")) Report("상담소 위치가 노출되었습니다. 지휘소 방어 권한을 확인하세요.");
            }
            SyncClinicClock();
            bool assignmentArrived = assignmentDelivery <= Minute + MinuteEpsilon;
            if (Math.Floor(Minute) != previousMinute || assignmentArrived) Revision++;
            if (assignmentArrived) NarrativeUpdated();
            if (Minute >= 1080)
            {
                IsDayEnded = true;
                Report("18:00 — 오늘의 업무가 끝났습니다. 날짜 단위 자동 저장을 진행합니다.");
                DayEnded?.Invoke();
            }
        }

        public void NextDay(bool deferMailUntilIntro = false)
        {
            if (!IsDayEnded) throw new CommandException("18시 이후 다음 날로 진행할 수 있습니다.");
            FinalizeDay();
            Day++;
            Minute = 540;
            IsDayEnded = false;
            ResponseMinutes = 0;
            dayPresentation = new DayPresentationState { day = Day, mailDeferred = deferMailUntilIntro };
            ApplyWorldSchedule();
            InitializeClinicDay(); InitializeDailyRecord();
            DeliverNewspaper();
            Report("DAY " + Day + " — 09:00 업무 시작. 현장 상황이 이어집니다.");
            NarrativeUpdated();
            if (!DryRun) DayStarted?.Invoke(Day);
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
            if (HasClinicSchedule ? !heardConsultationLines.Contains(index) : index > ConsultationIndex) throw new CommandException("아직 듣지 않은 진술입니다.");
            if (string.IsNullOrWhiteSpace(line.fact) || string.IsNullOrWhiteSpace(line.keyword)) throw new CommandException("기록할 상담 단서가 없습니다.");
            string statement = ObjectReferenceText.PlainText(line.sentence);
            Datum value = Datum.Ref(line.kind, line.fact, line.keyword, statement);
            if (!RecordedClues.Contains(line.fact))
            {
                CollectEvidence(value, "상담 단서");
                Observe(new ObservationDefinition { source = line.speaker, subjectId = Entity(ProfileOf(line)) != null ? ProfileOf(line) : "",
                    statement = statement, value = value });
                RecordedClues.Add(line.fact);
                Facts.Add(line.fact);
                Report("새 데이터: " + line.dataName + " / " + line.keyword + " — 관련 권한을 확인하세요.");
                NarrativeUpdated();
            }
            return NormalizeReference(value);
        }

        public void Reply()
        {
            if (IsDayEnded) throw new CommandException("오늘의 상담 시간이 끝났습니다.");
            if (CurrentConsultation?.ResponseCount != 0) return;
            AdvanceConsultation();
        }

        public void ReplyToConsultationChoice(int index)
        {
            if (IsDayEnded) throw new CommandException("오늘의 상담 시간이 끝났습니다.");
            ConsultationLine line = CurrentConsultation;
            if (line == null || index < 0 || index >= line.ResponseCount) throw new CommandException("현재 상담의 응답을 선택하세요.");
            AdvanceConsultation();
        }

        private void AdvanceConsultation()
        {
            if (HasClinicSchedule) { ReplyToAppointment(); return; }
            ResponseMinutes = 0;
            ConsultationIndex++;
            Report(CurrentConsultation == null ? "상담이 끝났습니다. 프로필에서 진술을 다시 확인할 수 있습니다." : "상담 응답: 음, 계속 말씀해 주세요.");
            NarrativeUpdated();
        }

        public KernelSnapshot Capture()
        {
            return new KernelSnapshot
            {
                contentTextVersion = 1,
                activeTerminalConversation = activeTerminalConversation, terminalConversations = terminalConversationProgress.Values.Select(p => p.Copy()).ToArray(),
                day = Day, minute = Minute, calendarStart = GameDateTime.From(calendarStart), knowledgeOnlyIds = knowledgeOnly.ToArray(), occurredEventIds = occurredEvents.ToArray(), suspicion = Suspicion, exposure = Exposure,
                responseMinutes = ResponseMinutes, consultationIndex = ConsultationIndex, consultationSeed = consultationSeed,
                dayEnded = IsDayEnded, coffeeReceived = CoffeeReceived,
                facts = Facts.ToArray(), authorities = Authorities.ToArray(), recordedClues = RecordedClues.ToArray(),
                consultationNotes = notes.Select(n => n.Copy()).ToArray(),
                nextConsultationNoteId = nextNoteId,
                mailProgress = mailProgress.Values.Select(m => m.Copy()).ToArray(),
                confirmedReservations = confirmedReservations.Values.Select(r => r.Copy()).ToArray(),
                drawers = drawers.Select(d => new DrawerState { id = d.Key, open = d.Value }).ToArray(),
                storedItems = StoredItems.ToArray(),
                triggers = triggers.Select(t => new TriggerValue { id = t.Key, value = t.Value }).ToArray(),
                quests = quests.Values.Select(q => q.Copy()).ToArray(),
                newspapers = editions.Values.Select(e => e.Copy()).ToArray(),
                clinicDay = clinicDay?.Copy(), heardConsultationLines = heardConsultationLines.ToArray(),
                dayPresentation = dayPresentation.Copy(),
                dayLedger = CopyLedger(dayLedger), dailyRecords = dailyRecords.Values.Select(r => r.Copy()).ToArray(),
                data = data.Values.Select(CloneSlot).ToArray(), nextDataId = nextDataId, protocols = protocols.Values.Select(p => p.Source).ToArray(),
                evidence = evidence.Values.Select(e => e.Copy()).ToArray(), nextEvidenceNumber = nextEvidenceNumber,
                discoveries = discoveries.Values.Select(d => d.Copy()).ToArray(), observations = observations.Select(o => o.Copy()).ToArray(),
                worldEvents = worldEvents.Select(e => e.Copy()).ToArray(),
                completedWorldEvents = completedWorldEvents.ToArray(),
                entities = entities.Values.Select(e => new EntitySnapshot
                {
                    id = e.Id, label = e.Label, labelKey = e.LabelKey, schemaId = e.Schema.Id, aliases = e.Aliases.ToArray(), names = e.Names.ToArray(),
                    rootNames = roots.Where(r => r.Value == e).Select(r => r.Key).ToArray(), values = e.CaptureValues(),
                    children = e.Children.Select(c => new EntityChildSnapshot { name = c.Key, id = c.Value.Id }).ToArray(),
                    localVariables = e.CaptureLocalVariables(), localFunctions = e.CaptureLocalFunctions(), viewer = e.Viewer?.Copy(), hasViewerOverride = e.Viewer != null,
                    disabledVariables = e.CaptureDisabledVariables(), disabledFunctions = e.CaptureDisabledFunctions(), playerAccess = e.CapturePlayerAccess()
                }).ToArray(),
                agents = Threads.Select(a => new AgentSnapshot
                {
                    id = a.Id, state = a.State, message = a.Message, location = a.Location, report = a.Report.Copy(), delay = a.Delay, sceneAction = a.SceneAction,
                    observations = a.Observations.Select(o => o.Copy()).ToArray(), discoveries = a.Discoveries.ToArray(),
                    cipherTable = a.CipherTable.Select(p => new DataSlot { name = p.Key, value = p.Value.Copy() }).ToArray(),
                    knowledgeFlags = a.KnowledgeFlags.ToArray(), grantedAuthorities = a.GrantedAuthorities.ToArray(),
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
                Protocol protocol = new CommandParser(source).ReadProtocol(source, legacyDeclarations: true);
                ValidateProtocolTypes(protocol);
                restoredProtocols.Add(protocol.Name, protocol);
            }
            var restoredAgents = new List<ThreadAgent>();
            foreach (AgentSnapshot saved in snapshot.agents ?? Array.Empty<AgentSnapshot>())
            {
                var agent = new ThreadAgent(saved.id) { State = saved.state, Message = saved.message, Location = saved.location ?? "비공개 대기 지점",
                    Report = saved.report?.Copy() ?? Datum.Empty, Delay = saved.delay, SceneAction = saved.sceneAction };
                agent.Observations.AddRange((saved.observations ?? Array.Empty<ObservationRecord>()).Select(o => o.Copy()));
                agent.Discoveries.AddRange(saved.discoveries ?? Array.Empty<string>());
                foreach (DataSlot entry in saved.cipherTable ?? Array.Empty<DataSlot>()) agent.CipherTable.Add(entry.name, entry.value.Copy());
                agent.KnowledgeFlags.UnionWith(saved.knowledgeFlags ?? Array.Empty<string>()); agent.GrantedAuthorities.UnionWith(saved.grantedAuthorities ?? Array.Empty<string>());
                foreach (FrameSnapshot frame in saved.frames ?? Array.Empty<FrameSnapshot>())
                {
                    Protocol protocol = new CommandParser(frame.source).ReadProtocol(frame.source, legacyDeclarations: true);
                    ValidateProtocolTypes(protocol);
                    if (frame.cursor < 0 || frame.cursor > protocol.Body.Count) throw new CommandException("진행 중 프로토콜의 저장 위치가 잘못되었습니다.");
                    agent.Frames.Add(new ProtocolFrame { Protocol = protocol, Cursor = frame.cursor, ReturnTarget = frame.returnTarget,
                        Locals = (frame.locals ?? Array.Empty<DataSlot>()).ToDictionary(s => s.name, s => s.value.Copy(), StringComparer.Ordinal) });
                }
                restoredAgents.Add(agent);
            }
            if (snapshot.calendarStart?.IsSpecified == true) calendarStart = snapshot.calendarStart.Value.Date;
            Day = snapshot.day; Minute = snapshot.minute; IsDayEnded = snapshot.dayEnded;
            RestoreStoryState(snapshot);
            RestoreEntities(snapshot.entities, snapshot.discoveries == null, snapshot.contentTextVersion < 1);
            Suspicion = snapshot.suspicion; Exposure = snapshot.exposure; ResponseMinutes = snapshot.responseMinutes;
            if (snapshot.consultationSeed != 0) consultationSeed = snapshot.consultationSeed;
            ConsultationIndex = Math.Max(HasClinicSchedule ? -1 : 0, Math.Min(snapshot.consultationIndex, Consultation.Count));
            CoffeeReceived = snapshot.coffeeReceived;
            Facts.Clear(); Facts.UnionWith(snapshot.facts ?? Array.Empty<string>());
            Authorities.Clear(); Authorities.UnionWith(snapshot.authorities ?? Array.Empty<string>());
            RecordedClues.Clear(); RecordedClues.UnionWith(snapshot.recordedClues ?? Array.Empty<string>());
            RestoreNarrative(snapshot);
            RestoreItemStorage(snapshot.storedItems);
            RestoreNewspapers(snapshot.newspapers);
            data.Clear();
            foreach (DataSlot slot in snapshot.data ?? Array.Empty<DataSlot>())
            {
                DataSlot restored = CloneSlot(slot); restored.value = NormalizeReference(restored.value);
                data.Add(restored.name, restored);
            }
            nextDataId = Math.Max(snapshot.nextDataId, data.Count == 0 ? 1 : data.Values.Max(s => s.id) + 1);
            protocols.Clear(); foreach (var pair in restoredProtocols) protocols.Add(pair.Key, pair.Value);
            Threads.Clear(); Threads.AddRange(restoredAgents);
            RestoreReservations(snapshot.confirmedReservations); RestoreClinic(snapshot); RestoreDailyRecord(snapshot);
            RestoreEvidence(snapshot);
            RestoreKnowledge(snapshot);
            RestoreTerminalConversations(snapshot);
            completedWorldEvents.Clear(); completedWorldEvents.UnionWith(snapshot.completedWorldEvents ?? Array.Empty<string>());
            Revision++;
        }

        private static DataSlot CloneSlot(DataSlot slot) => new DataSlot { id = slot.id, name = slot.name, value = slot.value.Copy(), declaredType = slot.declaredType };
    }
}
