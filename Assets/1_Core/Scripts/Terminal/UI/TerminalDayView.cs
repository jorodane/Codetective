using System;
using System.Text;
using Codetective.Operations;
using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    public sealed class TerminalDayView : MonoBehaviour
    {
        private MonitorPageHost host;
        private Text title, subtitle, record;
        private GameObject actions;
        public bool SummaryVisible { get; private set; }
        public void Initialize(MonitorPageHost owner, Action close, Action next)
        {
            host = owner; var root = (RectTransform)transform;
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.offsetMin = root.offsetMax = Vector2.zero;
            Image background = gameObject.AddComponent<Image>(); background.color = new Color(0.018f, 0.035f, 0.025f); gameObject.AddComponent<CrtMeshEffect>();
            title = MonitorWidgets.Label(transform, host.DisplayFont, new Vector2(48, -42), new Vector2(1024, 110), "", 68);
            subtitle = MonitorWidgets.Label(transform, host.DisplayFont, new Vector2(48, -170), new Vector2(1024, 80), "", 32);
            record = MonitorWidgets.ScrollText(transform, host.DisplayFont, new Vector2(48, -278), new Vector2(1024, 300));
            actions = MonitorWidgets.Rect("DayActions", transform, new Vector2(48, -610), new Vector2(1024, 56)).gameObject;
            MonitorWidgets.Button(actions.transform, host.DisplayFont, Vector2.zero, new Vector2(492, 56), "닫고 기록 정리", close);
            MonitorWidgets.Button(actions.transform, host.DisplayFont, new Vector2(532, 0), new Vector2(492, 56), "다음 날 시작", next);
            gameObject.SetActive(false);
        }
        public void ShowIntro(string day, string description)
        {
            SummaryVisible = false; Show(); title.text = day; subtitle.text = description;
            record.transform.parent.gameObject.SetActive(false); actions.SetActive(false);
        }
        public void ShowSummary(DailyRecord day)
        {
            SummaryVisible = true; Show(); title.text = "DAY " + day.day + " / 업무 정산"; subtitle.text = day.subtitle;
            record.transform.parent.gameObject.SetActive(true); actions.SetActive(true);
            var text = new StringBuilder("상담 일정\n");
            foreach (AppointmentProgress visit in day.visits)
                text.Append(KernelRuntime.TimeOfDay(visit.startMinute)).Append("  ").Append(visit.patientName).Append(" · ")
                    .Append(visit.outcome == VisitOutcome.Completed ? "상담 완료" : visit.outcome == VisitOutcome.Missed ? "미진행" : "시간에 맞춰 마무리").Append('\n');
            if (day.visits.Length == 0) text.Append("예약된 상담 없음\n");
            text.Append("\n작전·기록\n새 데이터 ").Append(day.newData.Length).Append("건 / 상담 메모 ").Append(day.notesWritten)
                .Append("건\n확인한 메일 ").Append(day.mailRead).Append("건 / Thread 수행 완료 ").Append(day.completedThreadTasks).Append("건\n");
            if (day.newData.Length > 0) text.Append("\n확보 데이터\n").Append(string.Join("\n", day.newData));
            if (day.newAuthorities.Length > 0) text.Append("\n\n신규 작전 권한\n").Append(string.Join("\n", day.newAuthorities));
            record.text = text.ToString(); Canvas.ForceUpdateCanvases(); record.transform.parent.GetComponent<ScrollRect>().verticalNormalizedPosition = 1;
        }
        private void Show() { host.SetOverlayVisible(this, true); transform.SetAsLastSibling(); gameObject.SetActive(true); }
        public void Hide() { SummaryVisible = false; gameObject.SetActive(false); if (host != null) host.SetOverlayVisible(this, false); }
        private void OnDestroy() { if (host != null) host.SetOverlayVisible(this, false); }
    }
}
