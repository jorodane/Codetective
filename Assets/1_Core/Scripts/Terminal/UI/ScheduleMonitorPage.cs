using System;
using System.Linq;
using Codetective.Operations;
using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    [DisallowMultipleComponent, RequireComponent(typeof(MonitorPage))]
    public sealed class ScheduleMonitorPage : MonoBehaviour, IMonitorPageContent
    {
        private KernelRuntime runtime;
        private MonitorPageHost host;
        private Font font;
        private RectTransform rows;
        private Text status, detail, feedback;
        private Button previous, source, wait, finish;
        private int selectedDay;
        private string selectedId, signature;
        private float nextRefresh;
        public void Initialize(KernelRuntime value, Font typeface)
        {
            runtime = value; font = typeface; host = GetComponentInParent<MonitorPageHost>();
            if (rows == null) Build();
            selectedDay = host.SelectedScheduleDay > 0 ? host.SelectedScheduleDay : runtime.Day; Refresh();
        }
        private void OnEnable()
        {
            if (runtime == null || rows == null) return;
            selectedDay = host.SelectedScheduleDay > 0 ? host.SelectedScheduleDay : runtime.Day; Refresh();
        }
        private void Update()
        {
            if (runtime == null || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.2f;
            if (host.SelectedScheduleDay > 0) selectedDay = host.SelectedScheduleDay;
            Refresh();
        }
        public void ShowDay(int day)
        {
            selectedDay = Math.Max(1, day); host.SelectedScheduleDay = selectedDay; selectedId = null; Refresh();
        }
        private void Build()
        {
            status = MonitorWidgets.Label(transform, font, new Vector2(28, -90), new Vector2(580, 44), "", 28);
            previous = MonitorWidgets.Button(transform, font, new Vector2(640, -90), new Vector2(140, 44), "이전 날", () => ShowDay(selectedDay - 1));
            MonitorWidgets.Button(transform, font, new Vector2(796, -90), new Vector2(140, 44), "오늘", () => ShowDay(runtime.Day));
            MonitorWidgets.Button(transform, font, new Vector2(952, -90), new Vector2(140, 44), "다음 날", () => ShowDay(selectedDay == int.MaxValue ? selectedDay : selectedDay + 1));
            RectTransform viewport = MonitorWidgets.Rect("Schedule", transform, new Vector2(28, -154), new Vector2(650, 390));
            viewport.gameObject.AddComponent<Image>().color = new Color(0.02f, 0.04f, 0.03f);
            viewport.gameObject.AddComponent<CrtMeshEffect>(); viewport.gameObject.AddComponent<RectMask2D>();
            rows = MonitorWidgets.Rect("Appointments", viewport, Vector2.zero, new Vector2(650, 390));
            var scroll = viewport.gameObject.AddComponent<CrtScrollRect>(); scroll.content = rows; scroll.viewport = viewport;
            scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 30;
            detail = MonitorWidgets.ScrollText(transform, font, new Vector2(702, -154), new Vector2(390, 390));
            MonitorWidgets.Button(transform, font, new Vector2(28, -562), new Vector2(650, 44), "예약 문의 메일 확인", OpenClinicMail);
            source = MonitorWidgets.Button(transform, font, new Vector2(702, -562), new Vector2(390, 44), "이 예약의 문의 메일", OpenSourceMail);
            wait = MonitorWidgets.Button(transform, font, new Vector2(28, -620), new Vector2(650, 44), "다음 예약까지 기다리기", () =>
            { if (CanAct && (runtime.CanWaitForNextAppointment || runtime.CanWaitUntilClosing)) host.WaitForAppointment?.Invoke(); });
            finish = MonitorWidgets.Button(transform, font, new Vector2(702, -620), new Vector2(390, 44), "상담 마치기", () =>
            { if (CanAct && runtime.CurrentAppointment?.phase == VisitPhase.Talking) host.FinishConsultation?.Invoke(); });
            feedback = MonitorWidgets.Label(transform, font, new Vector2(28, -672), new Vector2(1064, 26), "", 20);
        }
        private bool CanAct => isActiveAndEnabled && selectedDay == runtime.Day && !host.SystemMenuVisible && host.GetComponent<TerminalController>().InteractionEnabled &&
            (host.ClinicActionsAllowed?.Invoke() ?? false);
        private void Refresh()
        {
            AppointmentProgress[] visits = runtime.ScheduleForDay(selectedDay).ToArray();
            previous.interactable = selectedDay > 1;
            status.text = "DAY " + selectedDay + " / 상담 스케줄" + (selectedDay == runtime.Day ? " · 오늘" : "");
            if (!visits.Any(v => v.id == selectedId)) selectedId = visits.FirstOrDefault()?.id;
            string next = selectedDay + "|" + selectedId + "|" + string.Join("|", visits.Select(v => v.id + v.startMinute + ":" + v.endMinute + v.phase + ":" + v.outcome));
            if (next != signature)
            {
                signature = next;
                for (int i = rows.childCount - 1; i >= 0; i--)
                {
                    GameObject child = rows.GetChild(i).gameObject; child.SetActive(false);
                    if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
                }
                for (int i = 0; i < visits.Length; i++)
                {
                    AppointmentProgress visit = visits[i];
                    Button button = MonitorWidgets.Button(rows, font, new Vector2(0, -i * 104), new Vector2(650, 96),
                        KernelRuntime.TimeOfDay(visit.startMinute) + " ~ " + KernelRuntime.TimeOfDay(visit.endMinute) + "   " + visit.patientName + "\n" + VisitStatus(visit),
                        () => { selectedId = visit.id; signature = null; Refresh(); });
                    if (visit.id == selectedId) button.image.color = new Color(0.16f, 0.32f, 0.23f);
                }
                rows.sizeDelta = new Vector2(650, Math.Max(390, visits.Length * 104));
            }
            AppointmentProgress selected = visits.FirstOrDefault(v => v.id == selectedId);
            string mailId = runtime.ReservationMailFor(selectedId);
            source.interactable = mailId != null;
            detail.text = selected == null ? "확정된 상담 예약이 없습니다.\n\n문의 메일을 승인하면 여기에 표시됩니다." :
                selected.patientName + "\n\nDAY " + selectedDay + "\n" + KernelRuntime.TimeOfDay(selected.startMinute) + " ~ " + KernelRuntime.TimeOfDay(selected.endMinute) +
                "\n\n" + VisitStatus(selected) + "\n\n" + (mailId != null ? "문의 메일 승인으로 등록된 예약입니다." : "기존에 확정된 예약입니다.");
            feedback.text = "확정 " + visits.Length + "건 · 승인 대기 " + runtime.PendingReservationCount + "건 / 예약 문의는 메일에서 승인하세요.";
            wait.GetComponentInChildren<Text>().text = runtime.NextAppointment != null ? "다음 예약까지 기다리기" : "마감까지 기다리기";
            wait.interactable = CanAct && host.WaitForAppointment != null && (runtime.CanWaitForNextAppointment || runtime.CanWaitUntilClosing);
            finish.interactable = CanAct && host.FinishConsultation != null && runtime.CurrentAppointment?.phase == VisitPhase.Talking;
        }
        private static string VisitStatus(AppointmentProgress visit) => visit.phase == VisitPhase.Waiting ? "예약 확정" : visit.phase == VisitPhase.Arriving ? "입실 중" :
            visit.phase == VisitPhase.Talking ? "상담 중" : visit.phase == VisitPhase.Closing ? "상담 마무리" : visit.phase == VisitPhase.Departing ? "퇴실 중" :
            visit.outcome == VisitOutcome.Completed ? "상담 완료" : visit.outcome == VisitOutcome.Missed ? "미진행" : "예약 시간으로 종료";
        private void OpenClinicMail()
        {
            host.GetComponentInChildren<MailMonitorPage>(true)?.ShowMailbox(Mailbox.Clinic); host.ShowPage("mail");
        }
        private void OpenSourceMail()
        {
            string id = runtime.ReservationMailFor(selectedId); if (id == null) return;
            host.GetComponentInChildren<MailMonitorPage>(true)?.ShowMessage(id); host.ShowPage("mail");
        }
    }
}
