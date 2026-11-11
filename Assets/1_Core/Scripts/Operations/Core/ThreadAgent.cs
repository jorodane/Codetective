using System;
using System.Collections.Generic;
using System.Linq;

namespace Codetective.Operations
{
    internal sealed class ProtocolFrame
    {
        public Protocol Protocol;
        public Dictionary<string, Datum> Locals;
        public int Cursor;
        public string ReturnTarget;
    }

    public sealed class ThreadAgent
    {
        public string Id { get; }
        public AgentState State { get; internal set; }
        public string Message { get; internal set; } = "명령 대기";
        public Datum Report { get; internal set; } = Datum.Empty;
        public string Location { get; internal set; } = "비공개 대기 지점";
        internal readonly List<ProtocolFrame> Frames = new List<ProtocolFrame>();
        internal double Delay = -1;
        internal string SceneAction;
        public string ProtocolName => Frames.Count > 0 ? Frames[0].Protocol.Name : SceneAction ?? "";
        public string CurrentCommand
        {
            get
            {
                if (Frames.Count == 0) return SceneAction ?? "";
                ProtocolFrame frame = Frames.Last();
                if (frame.Cursor >= frame.Protocol.Body.Count) return "반환 준비";
                Statement statement = frame.Protocol.Body[frame.Cursor];
                return statement.Return ? "return" : statement.Value.Path == null ? "데이터 대입" : string.Join(".", statement.Value.Path);
            }
        }
        public ThreadAgent(string id) { Id = id; }
    }

    public sealed partial class KernelRuntime
    {
        public Datum StartProtocol(string id, string protocolName, Datum[] arguments)
        {
            ThreadAgent agent = Agent(id);
            if (agent == null || agent.State != AgentState.Ready) throw new CommandException("해당 Thread는 대기 상태가 아닙니다.");
            if (!protocols.TryGetValue(protocolName, out Protocol protocol)) throw new CommandException("저장된 프로토콜이 없습니다: " + protocolName);
            Dictionary<string, Datum> locals = Bind(protocol, arguments);
            agent.Frames.Clear();
            // Each running operation keeps its authored source, independently of later edits.
            Protocol snapshot = new CommandParser(protocol.Source).ReadProtocol(protocol.Source);
            agent.Frames.Add(new ProtocolFrame { Protocol = snapshot, Locals = locals });
            agent.Delay = -1;
            agent.State = AgentState.Executing;
            agent.Message = "프로토콜 수행: " + protocolName;
            Report(agent.Id + ": " + agent.Message);
            return Datum.Ref(DataKind.Task, id, id + " / " + protocolName);
        }

        public void Interrupt(string id, string reason)
        {
            ThreadAgent agent = Agent(id);
            if (agent == null || agent.State != AgentState.Executing) return;
            agent.State = AgentState.Waiting;
            agent.Message = reason;
            Report(id + " / WAITING: " + reason);
        }

        public void Disconnect(string id)
        {
            ThreadAgent agent = Agent(id);
            if (agent == null) return;
            agent.State = AgentState.Disconnected;
            agent.Message = "통신 두절";
            Report(id + ": 통신 두절");
        }

        public void Resume(string id)
        {
            ThreadAgent agent = Agent(id);
            if (agent == null || agent.State != AgentState.Waiting || agent.SceneAction != null)
                throw new CommandException("재개 가능한 프로토콜이 없습니다.");
            agent.State = AgentState.Executing;
            agent.Message = "명령 재개";
        }

        public void Recall(string id)
        {
            ThreadAgent agent = Agent(id);
            if (agent == null || agent.State == AgentState.Disconnected) throw new CommandException("Thread와 통신할 수 없습니다.");
            if (agent.SceneAction != null) throw new CommandException("현장 전달 중에는 완료 보고를 기다리세요.");
            agent.Frames.Clear();
            agent.State = AgentState.Ready;
            agent.Message = "복귀 완료";
        }

        private void TickAgent(ThreadAgent agent, double minutes)
        {
            int steps = 0;
            while (agent.State == AgentState.Executing && ++steps <= 1024)
            {
                if (agent.Frames.Count == 0) { FinishAgent(agent, Datum.Empty); return; }
                ProtocolFrame frame = agent.Frames.Last();
                if (frame.Cursor >= frame.Protocol.Body.Count) { ReturnFrame(agent, Datum.Empty); continue; }
                Statement statement = frame.Protocol.Body[frame.Cursor];
                try
                {
                    if (agent.Delay < 0)
                    {
                        agent.Delay = Duration(statement, frame.Locals);
                        Expression action = statement.Value;
                        if (action.IsCall && action.Path.Length > 1)
                        {
                            Entity target = ResolveEntity(action.Path.Take(action.Path.Length - 1).ToArray(), frame.Locals);
                            if (target.Kind == DataKind.Place) agent.Location = target.Label;
                        }
                        agent.Message = "수행 중: " + agent.CurrentCommand;
                    }
                    if (agent.Delay > minutes) { agent.Delay -= minutes; return; }
                    minutes -= agent.Delay;
                    agent.Delay = -1;
                    Expression call = statement.Value;
                    if (!statement.Return && call.IsCall && call.Path.Length == 1 && protocols.TryGetValue(call.Path[0], out Protocol child))
                    {
                        if (agent.Frames.Count >= 24) throw new CommandException("프로토콜 호출 깊이(24)를 초과했습니다.");
                        Datum[] args = call.Arguments.Select(e => Evaluate(e, frame.Locals, agent.Id)).ToArray();
                        var locals = Bind(child, args);
                        frame.Cursor++;
                        agent.Frames.Add(new ProtocolFrame { Protocol = new CommandParser(child.Source).ReadProtocol(child.Source),
                            Locals = locals, ReturnTarget = statement.Assign });
                        continue;
                    }
                    budget = 1024; depth = 0;
                    Datum value = ExecuteStatement(statement, frame.Locals, agent.Id);
                    frame.Cursor++;
                    if (statement.Return) ReturnFrame(agent, value);
                }
                catch (CommandException error)
                {
                    agent.Delay = 0; // Retry this step after the commander resolves the exception.
                    agent.State = AgentState.Waiting;
                    agent.Message = error.Message;
                    Report(agent.Id + " / WAITING: " + error.Message);
                    return;
                }
            }
            if (steps > 1024 && agent.State == AgentState.Executing)
                Interrupt(agent.Id, "단계 예산을 초과했습니다. 프로토콜을 확인하세요.");
        }

        private void ReturnFrame(ThreadAgent agent, Datum value)
        {
            ProtocolFrame finished = agent.Frames.Last();
            agent.Frames.RemoveAt(agent.Frames.Count - 1);
            if (agent.Frames.Count == 0) FinishAgent(agent, value);
            else if (finished.ReturnTarget != null) agent.Frames.Last().Locals[finished.ReturnTarget] = value.Copy();
        }

        private void FinishAgent(ThreadAgent agent, Datum value)
        {
            agent.Frames.Clear();
            agent.State = AgentState.Ready;
            agent.Report = value.Copy();
            agent.Message = "수행 완료: " + value;
            if (value.kind != DataKind.Empty)
            {
                string name = agent.Id + "_report";
                int suffix = 2;
                while (roots.ContainsKey(name) || protocols.ContainsKey(name)) name = agent.Id + "_report_" + suffix++;
                Store(name, value);
            }
            Report(agent.Id + ": " + agent.Message);
        }

        public Datum RequestCoffee()
        {
            if (Threads.Any(t => t.SceneAction == "coffee")) throw new CommandException("커피 전달이 진행 중입니다.");
            ThreadAgent agent = Threads.FirstOrDefault(t => t.State == AgentState.Ready);
            if (agent == null) throw new CommandException("활동 가능한 Thread가 없습니다.");
            agent.SceneAction = "coffee";
            agent.State = AgentState.Waiting;
            agent.Message = "보안 절차에 따라 커피 전달 중";
            if (DryRun) CompleteCoffee(agent.Id);
            else SceneActionRequested?.Invoke(agent.Id, "coffee");
            return Datum.Ref(DataKind.Task, agent.Id, "커피 전달 요청 / " + agent.Id);
        }

        public void CompleteCoffee(string agentId)
        {
            ThreadAgent agent = Agent(agentId);
            if (agent == null || agent.SceneAction != "coffee") return;
            agent.SceneAction = null;
            CoffeeReceived = true;
            Facts.Add("coffee_received");
            FinishAgent(agent, Datum.Ref(DataKind.Item, "coffee", "Kernel의 커피", "손님용과 같은 컵으로 전달받은 커피"));
        }

        public void RetrySceneActions()
        {
            foreach (ThreadAgent agent in Threads)
                if (agent.SceneAction != null) SceneActionRequested?.Invoke(agent.Id, agent.SceneAction);
        }
    }
}
