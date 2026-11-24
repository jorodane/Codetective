using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Codetective.Operations;
using UnityEngine;
using UnityEngine.UI;

namespace Codetective.Terminal
{
    [DisallowMultipleComponent, RequireComponent(typeof(MonitorPage))]
    public sealed class ScheduleMonitorPage : MonoBehaviour, IMonitorPageContent
    {
        private const int Width = 1064;
        private const int TimeWidth = 212;
        private const int HourHeight = 57;
        private const int Hours = (ClinicHours.Closing - ClinicHours.Opening) / ClinicHours.MinutesPerSlot;
        private static readonly string[] Weekdays = { "일", "월", "화", "수", "목", "금", "토" };
        private static readonly Color SlotColor = new Color(0.038f, 0.061f, 0.045f);
        private static readonly Color TimeColor = new Color(0.065f, 0.105f, 0.077f);

        private KernelRuntime runtime;
        private MonitorPageHost host;
        private TerminalController terminal;
        private Font font;
        private RectTransform table, appointmentCells, calendar, calendarDays, journal;
        private Text dateLabel, monthLabel, journalText;
        private Button previousDay, nextDay, previousMonth, nextMonth;
        private DateTime firstDate, calendarMonth;
        private int selectedDay, lastDay, maxDay;
        private string tableSignature, journalAppointment;
        private int journalRevision;
        private float nextRefresh;

        public void Initialize(KernelRuntime value, Font typeface)
        {
            runtime = value; font = typeface;
            host = GetComponentInParent<MonitorPageHost>();
            terminal = host.GetComponent<TerminalController>();
            firstDate = runtime.DateAt(1, 0).Value.Date;
            maxDay = (DateTime.MaxValue.Date - firstDate).Days + 1;
            if (table == null) Build();
            selectedDay = 0; tableSignature = null;
            Refresh();
        }

        private void OnEnable()
        {
            if (runtime != null && table != null) Refresh();
        }

        private void Update()
        {
            if (runtime == null || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.2f;
            Refresh();
        }

        public void ShowDay(int day)
        {
            int target = Math.Max(1, Math.Min(maxDay, day));
            host.SelectedScheduleDay = target == runtime.Day ? 0 : target;
            ShowTable();
            Refresh();
        }

        private void Build()
        {
            previousDay = MonitorWidgets.Button(transform, font, new Vector2(28, -90), new Vector2(178, 46), "이전 날", () => ShowDay(selectedDay - 1));
            var date = MonitorWidgets.Button(transform, font, new Vector2(222, -90), new Vector2(676, 46), "날짜", ToggleCalendar);
            dateLabel = date.GetComponentInChildren<Text>(); dateLabel.fontSize = 26;
            nextDay = MonitorWidgets.Button(transform, font, new Vector2(914, -90), new Vector2(178, 46), "다음 날", () => ShowDay(selectedDay + 1));

            Vector2 origin = new Vector2(28, -152);
            Vector2 size = new Vector2(Width, Hours * HourHeight);
            table = MonitorWidgets.Rect("Timetable", transform, origin, size);
            for (int row = 0; row < Hours; row++)
            {
                int start = ClinicHours.Opening + row * ClinicHours.MinutesPerSlot;
                var clock = Panel("Time", table, new Vector2(0, -row * HourHeight), new Vector2(TimeWidth - 4, HourHeight - 3), TimeColor);
                CellLabel(clock, Vector2.zero, clock.sizeDelta, KernelRuntime.TimeOfDay(start) + " ~ " +
                    KernelRuntime.TimeOfDay(start + ClinicHours.MinutesPerSlot), 24, TextAnchor.MiddleCenter);
                var slot = Panel("Slot", table, new Vector2(TimeWidth, -row * HourHeight), new Vector2(Width - TimeWidth, HourHeight - 3), SlotColor);
                if (start == ClinicHours.LunchStart)
                    CellLabel(slot, new Vector2(16, 0), new Vector2(Width - TimeWidth - 32, HourHeight - 3), "점심시간", 22).color =
                        Color.Lerp(MonitorWidgets.Foreground, SlotColor, 0.38f);
            }
            appointmentCells = MonitorWidgets.Rect("Appointments", table, new Vector2(TimeWidth, 0), new Vector2(Width - TimeWidth, size.y));

            calendar = MonitorWidgets.Rect("Calendar", transform, origin, size);
            previousMonth = MonitorWidgets.Button(calendar, font, Vector2.zero, new Vector2(140, 44), "이전 달", () => MoveMonth(-1));
            monthLabel = CellLabel(calendar, new Vector2(146, 0), new Vector2(772, 44), "", 26, TextAnchor.MiddleCenter);
            nextMonth = MonitorWidgets.Button(calendar, font, new Vector2(924, 0), new Vector2(140, 44), "다음 달", () => MoveMonth(1));
            for (int col = 0; col < Weekdays.Length; col++)
                CellLabel(calendar, new Vector2(col * 152, -56), new Vector2(146, 33), Weekdays[col], 22, TextAnchor.MiddleCenter);
            calendarDays = MonitorWidgets.Rect("CalendarDays", calendar, new Vector2(0, -94), new Vector2(Width, 408));
            calendar.gameObject.SetActive(false);

            journal = MonitorWidgets.Rect("AppointmentJournal", transform, origin, size);
            CellLabel(journal, Vector2.zero, new Vector2(860, 44), "상담 일지", 28);
            MonitorWidgets.Button(journal, font, new Vector2(904, 0), new Vector2(160, 44), "닫기", ShowTable);
            journalText = MonitorWidgets.ScrollText(journal, font, new Vector2(0, -60), new Vector2(Width, size.y - 60));
            journal.gameObject.SetActive(false);
        }

        private static RectTransform Panel(string name, RectTransform parent, Vector2 position, Vector2 size, Color color)
        {
            RectTransform rect = MonitorWidgets.Rect(name, parent, position, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color; image.raycastTarget = false;
            rect.gameObject.AddComponent<CrtMeshEffect>();
            return rect;
        }

        private Text CellLabel(RectTransform parent, Vector2 position, Vector2 size, string value, int fontSize,
            TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            Text label = MonitorWidgets.Label(parent, font, position, size, value, fontSize);
            label.alignment = alignment;
            return label;
        }

        private void Refresh()
        {
            int target = host.SelectedScheduleDay > 0 ? host.SelectedScheduleDay : runtime.Day;
            target = Math.Max(1, Math.Min(maxDay, target));
            if (target != selectedDay)
            {
                selectedDay = target;
                ShowTable();
            }
            DateTime date = firstDate.AddDays(selectedDay - 1);
            dateLabel.text = date.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture) + " (" + Weekdays[(int)date.DayOfWeek] +
                ")  ·  DAY " + selectedDay.ToString("00") + (selectedDay == runtime.Day ? "  ·  오늘" : "");
            previousDay.interactable = selectedDay > 1;
            nextDay.interactable = selectedDay < maxDay;
            if (calendar.gameObject.activeSelf && lastDay != runtime.Day) DrawCalendar();
            lastDay = runtime.Day;
            if (table.gameObject.activeSelf) DrawAppointments();
            if (journal.gameObject.activeSelf && journalRevision != runtime.Revision) RefreshJournal();
        }

        private void DrawAppointments()
        {
            var visits = runtime.ScheduleForDay(selectedDay);
            var journalProfiles = visits.Any(v => v.HasJournal) ? new HashSet<string>(runtime.VisibleProfileIds, StringComparer.Ordinal) : null;
            string signature = selectedDay + "|" + string.Join("|", visits.Select(v =>
                $"{v.id}:{v.startMinute}:{v.endMinute}:{v.phase}:{v.outcome}:{journalProfiles?.Contains(v.profileId)}"));
            if (signature == tableSignature) return;
            tableSignature = signature;
            MonitorWidgets.ClearRows(appointmentCells);
            const int cellWidth = Width - TimeWidth;
            const int visitorWidth = 236;
            const int journalWidth = 100;
            foreach (AppointmentProgress visit in visits)
            {
                float offset = (visit.startMinute - ClinicHours.Opening) * HourHeight / (float)ClinicHours.MinutesPerSlot;
                float height = (visit.endMinute - visit.startMinute) * HourHeight / (float)ClinicHours.MinutesPerSlot;
                if (height <= 0) continue;
                Vector2 position = new Vector2(0, -offset);
                Vector2 size = new Vector2(cellWidth, height - Mathf.Min(3, height / 10));
                RectTransform cell;
                bool canOpenJournal = visit.HasJournal && journalProfiles.Contains(visit.profileId);
                if (canOpenJournal)
                {
                    var button = MonitorWidgets.Button(appointmentCells, font, position, size, "", () => OpenJournal(visit.id));
                    button.gameObject.name = visit.id;
                    cell = (RectTransform)button.transform;
                    FitCell(CellLabel(cell, new Vector2(cellWidth - journalWidth, 0), new Vector2(journalWidth - 10, size.y), "일지 ›", 20, TextAnchor.MiddleRight));
                }
                else
                {
                    Color color = visit.phase == VisitPhase.Waiting || visit.phase == VisitPhase.Finished ?
                        MonitorWidgets.ButtonColor : MonitorWidgets.SelectedButtonColor;
                    cell = Panel(visit.id, appointmentCells, position, size, color);
                }
                var visitor = CellLabel(cell, new Vector2(16, 0), new Vector2(visitorWidth - 24, size.y), visit.patientName, 26);
                visitor.fontStyle = FontStyle.Bold;
                FitCell(visitor);
                if (!string.IsNullOrWhiteSpace(visit.title))
                {
                    var title = CellLabel(cell, new Vector2(visitorWidth, 0),
                        new Vector2(cellWidth - visitorWidth - (canOpenJournal ? journalWidth + 10 : 16), size.y), visit.title, 24);
                    FitCell(title);
                }
            }
        }

        private static void FitCell(Text label)
        {
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 9;
            label.resizeTextMaxSize = label.fontSize;
        }

        private void ShowTable()
        {
            journalAppointment = null;
            journal.gameObject.SetActive(false);
            calendar.gameObject.SetActive(false);
            table.gameObject.SetActive(true);
            if (selectedDay > 0) DrawAppointments();
        }

        private void ToggleCalendar()
        {
            if (calendar.gameObject.activeSelf) { ShowTable(); return; }
            DateTime date = firstDate.AddDays(selectedDay - 1);
            calendarMonth = new DateTime(date.Year, date.Month, 1);
            journal.gameObject.SetActive(false); journalAppointment = null;
            table.gameObject.SetActive(false);
            calendar.gameObject.SetActive(true);
            DrawCalendar();
        }

        private void MoveMonth(int delta)
        {
            if (delta < 0 ? !previousMonth.interactable : !nextMonth.interactable) return;
            calendarMonth = calendarMonth.AddMonths(delta);
            DrawCalendar();
        }

        private void DrawCalendar()
        {
            previousMonth.interactable = calendarMonth > new DateTime(firstDate.Year, firstDate.Month, 1);
            nextMonth.interactable = calendarMonth.Year != DateTime.MaxValue.Year || calendarMonth.Month != 12;
            monthLabel.text = calendarMonth.Year + "년 " + calendarMonth.Month + "월";
            MonitorWidgets.ClearRows(calendarDays);
            int start = (int)calendarMonth.DayOfWeek;
            int daysInMonth = DateTime.DaysInMonth(calendarMonth.Year, calendarMonth.Month);
            for (int i = 0; i < daysInMonth; i++)
            {
                int day = (calendarMonth.AddDays(i) - firstDate).Days + 1;
                int slot = start + i;
                var button = MonitorWidgets.Button(calendarDays, font, new Vector2(slot % 7 * 152, -(slot / 7) * 68),
                    new Vector2(146, 62), (i + 1).ToString(), () => ShowDay(day));
                button.interactable = day >= 1;
                if (day == selectedDay || day == runtime.Day)
                    button.image.color = day == selectedDay ? MonitorWidgets.SelectedButtonColor : TimeColor;
                if (day == runtime.Day)
                {
                    Text label = button.GetComponentInChildren<Text>();
                    label.text += " · 오늘";
                    label.fontStyle = FontStyle.Bold;
                }
            }
        }

        private void OpenJournal(string id)
        {
            if (!isActiveAndEnabled || host.SystemMenuVisible || !terminal.InteractionEnabled) return;
            journalAppointment = id;
            if (!RefreshJournal()) return;
            CrtScrollRect scroll = journalText.GetComponentInParent<CrtScrollRect>();
            scroll.StopMovement();
            journalText.rectTransform.anchoredPosition = Vector2.zero;
            table.gameObject.SetActive(false);
            journal.gameObject.SetActive(true);
        }

        private bool RefreshJournal()
        {
            journalRevision = runtime.Revision;
            try { journalText.text = runtime.AppointmentJournalText(selectedDay, journalAppointment); return true; }
            catch (CommandException) { ShowTable(); return false; }
        }
    }
}
