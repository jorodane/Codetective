using System;
using System.Linq;
using System.Text;
using Codetective.Interaction;
using Codetective.Operations;
using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Gameplay
{
    public sealed class ClinicDayOverlay : MonoBehaviour
    {
        private KernelGameController game;
        private InspectionController inspection;
        private RectTransform bookingBar, summary;
        private Text status, recordText, title;
        private Button wait, finish;
        public bool SummaryVisible => summary != null && summary.gameObject.activeSelf;
        public void Initialize(KernelGameController controller, InspectionController navigation, Transform parent, Font font)
        {
            game = controller; inspection = navigation;
            bookingBar = OverlayWidgets.Rect("ClinicAppointments", parent, new Vector2(0, -66), new Vector2(850, 50), new Vector2(0.5f, 1));
            bookingBar.gameObject.AddComponent<Image>().color = new Color(0.035f, 0.07f, 0.05f, 0.97f);
            status = OverlayWidgets.Label(bookingBar, font, new Vector2(12, -7), new Vector2(590, 38), "", 18);
            wait = OverlayWidgets.Button(bookingBar, font, new Vector2(610, -6), new Vector2(228, 38), "다음 예약까지 기다리기", game.WaitForNextAppointment);
            finish = OverlayWidgets.Button(bookingBar, font, new Vector2(610, -6), new Vector2(228, 38), "상담 마치기", () =>
            { if (game.Runtime.CurrentAppointment?.phase == VisitPhase.Talking) game.Runtime.FinishConsultation(); });
            summary = OverlayWidgets.Rect("DailySummary", parent, Vector2.zero, new Vector2(940, 600), new Vector2(0.5f, 0.5f));
            summary.gameObject.AddComponent<Image>().color = new Color(0.025f, 0.06f, 0.04f, 0.99f);
            title = OverlayWidgets.Label(summary, font, new Vector2(28, -22), new Vector2(884, 76), "", 32);
            RectTransform viewport = OverlayWidgets.Rect("Records", summary, new Vector2(28, -116), new Vector2(884, 370));
            viewport.gameObject.AddComponent<Image>().color = new Color(0.04f, 0.085f, 0.06f); viewport.gameObject.AddComponent<RectMask2D>();
            recordText = OverlayWidgets.Label(viewport, font, new Vector2(12, -8), new Vector2(860, 0), "", 22);
            recordText.verticalOverflow = VerticalWrapMode.Overflow;
            recordText.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.content = recordText.rectTransform; scroll.viewport = viewport;
            scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 26;
            OverlayWidgets.Button(summary, font, new Vector2(28, -522), new Vector2(424, 48), "닫고 기록 정리", () => game.CloseDaySummary());
            OverlayWidgets.Button(summary, font, new Vector2(488, -522), new Vector2(424, 48), "다음 날 시작", game.BeginNextDay);
            summary.gameObject.SetActive(false);
        }
        private void Update()
        {
            if (game == null) return;
            KernelRuntime r = game.Runtime;
            bookingBar.gameObject.SetActive(r.HasClinicSchedule && !r.IsDayEnded && !r.AwaitingAssignment && !r.IsClinicTransitioning && !game.IsDateTransitioning &&
                inspection.Mode != InspectionMode.Menu && !inspection.ModalInputBlocked && (game.Carry == null || game.Carry.HeldItem == null) && !SummaryVisible);
            AppointmentProgress current = r.CurrentAppointment, next = r.NextAppointment;
            status.text = current != null ? current.patientName + " 님 상담 중  /  " + (next != null ? "다음 " + KernelRuntime.TimeOfDay(next.startMinute) + " " + next.patientName : "마지막 예약") :
                next != null ? "다음 예약  " + KernelRuntime.TimeOfDay(next.startMinute) + "  " + next.patientName : "오늘의 상담 예약이 모두 끝났습니다.";
            wait.gameObject.SetActive(current == null); wait.interactable = (r.CanWaitForNextAppointment || r.CanWaitUntilClosing) && !game.IsVisitPresenting;
            wait.GetComponentInChildren<Text>().text = next != null ? "다음 예약까지 기다리기" : "마감까지 기다리기";
            finish.gameObject.SetActive(current?.phase == VisitPhase.Talking);
        }
        public void ShowSummary(DailyRecord record)
        {
            title.text = "DAY " + record.day + " / 업무 정산\n" + record.subtitle;
            var text = new StringBuilder("상담 일정\n");
            foreach (AppointmentProgress visit in record.visits)
                text.Append(KernelRuntime.TimeOfDay(visit.startMinute)).Append("  ").Append(visit.patientName).Append("  ·  ")
                    .Append(visit.outcome == VisitOutcome.Completed ? "상담 완료" : visit.outcome == VisitOutcome.NextReservation ? "다음 예약으로 마무리" :
                        visit.outcome == VisitOutcome.ClosingTime ? "마감 시간으로 마무리" : visit.outcome == VisitOutcome.ReservationEnd ? "예약 시간으로 마무리" : "미진행").Append('\n');
            if (record.visits.Length == 0) text.Append("예약된 상담 없음\n");
            text.Append("\n작전·기록\n새 데이터 ").Append(record.newData.Length).Append("건  /  상담 메모 ").Append(record.notesWritten)
                .Append("건\n확인한 메일 ").Append(record.mailRead).Append("건  /  Thread 수행 완료 ").Append(record.completedThreadTasks).Append("건\n");
            if (record.newData.Length > 0) text.Append("\n확보 데이터\n").Append(string.Join("\n", record.newData));
            if (record.newAuthorities.Length > 0) text.Append("\n\n신규 작전 권한\n").Append(string.Join("\n", record.newAuthorities));
            text.Append("\n\n").Append(game.Feedback).Append("\n다음 날에도 진행 중인 Thread 작업은 이어집니다.");
            recordText.text = text.ToString(); summary.gameObject.SetActive(true); summary.SetAsLastSibling();
            Canvas.ForceUpdateCanvases(); recordText.transform.parent.GetComponent<ScrollRect>().verticalNormalizedPosition = 1;
        }
        public void HideSummary() => summary.gameObject.SetActive(false);
    }
}
