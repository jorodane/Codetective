using System;
using System.Text;
using Codetective.Operations;
using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    [DisallowMultipleComponent, RequireComponent(typeof(MonitorPage))]
    public sealed class ClinicMonitorPage : MonoBehaviour
    {
        [SerializeField] private bool recordsPage;
        [SerializeField] private string consultationPageId = "clinic";
        [SerializeField] private string recordsPageId = "clinic-records";
        private KernelRuntime runtime;
        private MonitorPageHost host;
        private Text status;
        private Text body;
        private Text feedback;
        private Button clue;
        private Button reply;
        private Button alternateReply;
        private float nextRefresh;

        public void Initialize(KernelRuntime value, Font font)
        {
            runtime = value;
            host = GetComponentInParent<MonitorPageHost>();
            if (body == null) Build(font);
            Refresh();
        }
        private void Update()
        {
            if (runtime == null || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.2f;
            Refresh();
        }
        private void OnEnable() { if (runtime != null && body != null) Refresh(); }
        private void Build(Font font)
        {
            status = MonitorWidgets.Label(transform, font, new Vector2(28, -90), new Vector2(1064, 42), "", 28);
            body = MonitorWidgets.ScrollText(transform, font, new Vector2(28, -146), new Vector2(1064, recordsPage ? 410 : 290));
            if (recordsPage)
            {
                MonitorWidgets.Button(transform, font, new Vector2(28, -586), new Vector2(514, 48), "들은 진술의 단서 기록", RecordHeardClues);
                MonitorWidgets.Button(transform, font, new Vector2(578, -586), new Vector2(514, 48), "상담 화면", () => host.ShowPage(consultationPageId));
                feedback = MonitorWidgets.Label(transform, font, new Vector2(28, -648), new Vector2(1064, 36), "휠이나 드래그로 상담 기록을 볼 수 있습니다.", 22);
            }
            else
            {
                clue = MonitorWidgets.Button(transform, font, new Vector2(28, -452), new Vector2(1064, 48), "", RecordCurrentClue);
                clue.GetComponentInChildren<Text>().color = new Color(1f, 0.4f, 0.35f);
                reply = MonitorWidgets.Button(transform, font, new Vector2(28, -516), new Vector2(514, 48), "음, 계속 말씀해 주세요.", Reply);
                alternateReply = MonitorWidgets.Button(transform, font, new Vector2(578, -516), new Vector2(514, 48), "아아, 그럴 수 있겠네요.", Reply);
                feedback = MonitorWidgets.Label(transform, font, new Vector2(28, -578), new Vector2(1064, 40), "붉은 단서를 누르면 프로필과 데이터에 기록합니다.", 22);
                MonitorWidgets.Button(transform, font, new Vector2(28, -632), new Vector2(1064, 44), "상담 기록 보기", () => host.ShowPage(recordsPageId));
            }
        }
        private void Refresh()
        {
            if (recordsPage)
            {
                status.text = runtime.ClockText + " / 상담 기록";
                var text = new StringBuilder();
                for (int i = 0; i <= runtime.ConsultationIndex && i < runtime.Consultation.Count; i++)
                {
                    ConsultationLine line = runtime.Consultation[i];
                    text.Append(line.speaker).Append('\n').Append(line.sentence).Append('\n')
                        .Append(runtime.RecordedClues.Contains(line.fact) ? "[기록됨] " : "[미기록] ").Append(line.keyword).Append("\n\n");
                }
                body.text = text.Length == 0 ? "아직 상담 기록이 없습니다." : text.ToString();
                return;
            }
            ConsultationLine current = runtime.CurrentConsultation;
            status.text = runtime.ClockText + " / " + (current == null ? "상담 종료" : current.speaker);
            body.text = current == null ? "오늘의 상담이 끝났습니다.\n상담 기록에서 들은 진술과 단서를 다시 확인할 수 있습니다." :
                current.sentence + "\n\n응답 대기: " + runtime.ResponseMinutes.ToString("0.0") + "분\n응답이 늦어지면 의심도가 상승합니다.";
            clue.interactable = current != null;
            clue.GetComponentInChildren<Text>().text = current == null ? "기록할 진술 없음" : "● " + current.keyword +
                (runtime.RecordedClues.Contains(current.fact) ? "  [기록됨]" : "  [단서 기록]");
            reply.interactable = alternateReply.interactable = current != null && !runtime.IsDayEnded;
        }
        private void Act(Action action, string message)
        {
            try { action(); feedback.text = message; }
            catch (CommandException error) { feedback.text = error.Message; }
            Refresh();
        }
        private void Reply() => Act(runtime.Reply, "응답을 전달했습니다.");
        private void RecordCurrentClue() => Act(() => runtime.RecordClue(runtime.ConsultationIndex), "프로필과 데이터에 단서를 기록했습니다.");
        private void RecordHeardClues() => Act(() =>
        {
            for (int i = 0; i <= runtime.ConsultationIndex && i < runtime.Consultation.Count; i++) runtime.RecordClue(i);
        }, "들은 진술의 단서를 기록했습니다.");
    }
}
