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
        private const int NameWidth = 244;
        private const int ContentWidth = Width - TimeWidth;
        private const int HourHeight = 54;
        private const int CellGap = 3;
        private const int Hours = (ClinicHours.Closing - ClinicHours.Opening) / ClinicHours.MinutesPerSlot;
        private static readonly string[] Weekdays = { "일", "월", "화", "수", "목", "금", "토" };
        private static readonly Color SlotColor = new Color(0.038f, 0.061f, 0.045f);
        private static readonly Color TimeColor = new Color(0.065f, 0.105f, 0.077f);
        private static readonly Color LunchColor = new Color(0.082f, 0.071f, 0.047f);
        private static readonly Color SelectionBorderColor = Color.Lerp(MonitorWidgets.Foreground, MonitorWidgets.SelectedButtonColor, 0.22f);

        private KernelRuntime runtime;
        private MonitorPageHost host;
        private TerminalController terminal;
        private Font font;
        private RectTransform table, emptyCells, appointmentCells, calendar, calendarDays, appointmentDetails;
        private Text dateLabel, monthLabel, appointmentText;
        private Button previousDay, nextDay, previousMonth, nextMonth;
        private DateTime firstDate, calendarMonth;
        private int selectedDay, lastDay, maxDay;
        private string tableSignature, openAppointmentId;
        private int detailsRevision;
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
            Vector2 arrowSize = new Vector2(48, 44);
            previousDay = MonitorWidgets.Button(transform, font, new Vector2(382, -90), arrowSize, "<", () => ShowDay(selectedDay - 1));
            var date = MonitorWidgets.Button(transform, font, new Vector2(434, -90), new Vector2(252, 44), "날짜", ToggleCalendar);
            dateLabel = date.GetComponentInChildren<Text>(true); dateLabel.fontSize = 26;
            nextDay = MonitorWidgets.Button(transform, font, new Vector2(690, -90), arrowSize, ">", () => ShowDay(selectedDay + 1));

            table = MonitorWidgets.Rect("Timetable", transform, new Vector2(28, -176), new Vector2(Width, Hours * HourHeight));
            CellLabel(table, new Vector2(0, 36), new Vector2(TimeWidth - CellGap, 30), "상담 시간", 22, TextAnchor.MiddleCenter);
            CellLabel(table, new Vector2(TimeWidth + 12, 36), new Vector2(ContentWidth - 24, 30), "예약", 22);
            for (int row = 0; row < Hours; row++)
            {
                int start = ClinicHours.Opening + row * ClinicHours.MinutesPerSlot;
                bool bookableHour = ClinicHours.IsBookableHour(start);
                var clock = Panel("Time", table, new Vector2(0, -row * HourHeight), new Vector2(TimeWidth - CellGap, HourHeight - CellGap),
                    bookableHour ? TimeColor : LunchColor);
                string time = KernelRuntime.TimeOfDay(start) + " ~ " + KernelRuntime.TimeOfDay(start + ClinicHours.MinutesPerSlot);
                FitCell(CellLabel(clock, Vector2.zero, clock.sizeDelta, bookableHour ? time : time + "\n점심시간", bookableHour ? 24 : 20, TextAnchor.MiddleCenter));
            }
            Vector2 cellOrigin = new Vector2(TimeWidth, 0);
            Vector2 cellSize = new Vector2(ContentWidth, table.sizeDelta.y);
            emptyCells = MonitorWidgets.Rect("EmptySlots", table, cellOrigin, cellSize);
            appointmentCells = MonitorWidgets.Rect("Appointments", table, cellOrigin, cellSize);

            Vector2 origin = new Vector2(28, -152);
            Vector2 size = new Vector2(Width, 513);
            calendar = MonitorWidgets.Rect("Calendar", transform, origin, size);
            previousMonth = MonitorWidgets.Button(calendar, font, new Vector2(390, 0), arrowSize, "<<", () => MoveMonth(-1));
            monthLabel = CellLabel(calendar, new Vector2(442, 0), new Vector2(180, 44), "", 26, TextAnchor.MiddleCenter);
            nextMonth = MonitorWidgets.Button(calendar, font, new Vector2(626, 0), arrowSize, ">>", () => MoveMonth(1));
            for (int col = 0; col < Weekdays.Length; col++)
                CellLabel(calendar, new Vector2(col * 152, -56), new Vector2(146, 33), Weekdays[col], 22, TextAnchor.MiddleCenter);
            calendarDays = MonitorWidgets.Rect("CalendarDays", calendar, new Vector2(0, -94), new Vector2(Width, 408));
            calendar.gameObject.SetActive(false);

            appointmentDetails = MonitorWidgets.Rect("AppointmentDetails", transform, origin, size);
            CellLabel(appointmentDetails, Vector2.zero, new Vector2(860, 44), "상담 내용", 28);
            MonitorWidgets.Button(appointmentDetails, font, new Vector2(904, 0), new Vector2(160, 44), "닫기", ShowTable);
            appointmentText = MonitorWidgets.ScrollText(appointmentDetails, font, new Vector2(0, -60), new Vector2(Width, size.y - 60));
            appointmentDetails.gameObject.SetActive(false);
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
            dateLabel.text = date.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture) + " (" + Weekdays[(int)date.DayOfWeek] + ")";
            previousDay.interactable = selectedDay > 1;
            nextDay.interactable = selectedDay < maxDay;
            if (calendar.gameObject.activeSelf && lastDay != runtime.Day) DrawCalendar();
            lastDay = runtime.Day;
            if (table.gameObject.activeSelf) DrawAppointments();
            if (appointmentDetails.gameObject.activeSelf && detailsRevision != runtime.Revision) RefreshAppointment();
        }

        private void DrawAppointments()
        {
            var visits = runtime.ScheduleForDay(selectedDay);
            int latestUnavailableHour = selectedDay == runtime.Day ? (int)runtime.Minute / ClinicHours.MinutesPerSlot * ClinicHours.MinutesPerSlot :
                selectedDay < runtime.Day ? ClinicHours.Closing : 0;
            string signature = $"{selectedDay}:{latestUnavailableHour}|" + string.Join("|", visits.Select(v =>
                $"{v.id}:{v.startMinute}:{v.endMinute}:{v.phase}:{v.outcome}:{v.title}"));
            if (signature == tableSignature) return;
            tableSignature = signature;
            MonitorWidgets.ClearRows(emptyCells);
            MonitorWidgets.ClearRows(appointmentCells);

            // Older saves keep their real times. Snap their visible bounds to the nearest hour and give short visits a full cell.
            var bookings = visits.Where(visit => visit.endMinute > visit.startMinute).Select(visit =>
            {
                int first = Math.Min(Hours - 1, HourBoundary(visit.startMinute));
                return (visit: visit, first: first, end: Math.Max(first + 1, HourBoundary(visit.endMinute)), column: 0);
            }).OrderBy(booking => booking.first).ToArray();

            int row = 0;
            var columnEnds = new List<int>();
            for (int i = 0; i < bookings.Length;)
            {
                while (row < bookings[i].first) DrawEmptySlot(row++, latestUnavailableHour);

                // When older visits map to the same hour, show them side by side without covering either cell.
                columnEnds.Clear();
                int groupEnd = row;
                int next = i;
                do
                {
                    var booking = bookings[next];
                    int column = columnEnds.FindIndex(end => end <= booking.first);
                    if (column < 0) { column = columnEnds.Count; columnEnds.Add(booking.end); }
                    else columnEnds[column] = booking.end;
                    bookings[next++].column = column;
                    groupEnd = Math.Max(groupEnd, booking.end);
                }
                while (next < bookings.Length && bookings[next].first < groupEnd);

                float columnWidth = (ContentWidth + CellGap) / (float)columnEnds.Count;
                for (; i < next; i++)
                {
                    var (visit, first, end, column) = bookings[i];
                    Vector2 position = new Vector2(column * columnWidth, -first * HourHeight);
                    Vector2 size = new Vector2(columnWidth - CellGap, (end - first) * HourHeight - CellGap);
                    DrawAppointmentCell(visit, position, size);
                }
                row = groupEnd;
            }
            while (row < Hours) DrawEmptySlot(row++, latestUnavailableHour);
        }

        private static int HourBoundary(int minute)
        {
            int offset = Mathf.Clamp(minute, ClinicHours.Opening, ClinicHours.Closing) - ClinicHours.Opening;
            return (offset + ClinicHours.MinutesPerSlot / 2) / ClinicHours.MinutesPerSlot;
        }

        private void DrawEmptySlot(int row, int latestUnavailableHour)
        {
            int start = ClinicHours.Opening + row * ClinicHours.MinutesPerSlot;
            bool bookableHour = ClinicHours.IsBookableHour(start);
            var slot = Panel("Slot", emptyCells, new Vector2(0, -row * HourHeight), new Vector2(ContentWidth, HourHeight - CellGap),
                bookableHour ? SlotColor : LunchColor);
            if (bookableHour && start > latestUnavailableHour)
                CellLabel(slot, new Vector2(12, 0), new Vector2(ContentWidth - 24, slot.sizeDelta.y), "예약 가능", 22).color =
                    Color.Lerp(MonitorWidgets.Foreground, SlotColor, 0.38f);
        }

        private void DrawAppointmentCell(AppointmentProgress visit, Vector2 position, Vector2 size)
        {
            Button button = MonitorWidgets.Button(appointmentCells, font, position, size, visit.patientName, () => OpenAppointment(visit.id));
            button.name = visit.id;
            if (visit.phase != VisitPhase.Waiting && visit.phase != VisitPhase.Finished)
                button.image.color = MonitorWidgets.SelectedButtonColor;
            var cell = (RectTransform)button.transform;
            bool hasTitle = !string.IsNullOrWhiteSpace(visit.title);
            float padding = Mathf.Min(12, size.x * 0.06f);
            float arrowWidth = Mathf.Min(42, size.x * 0.18f);
            float visitorWidth = hasTitle ? Mathf.Min(NameWidth, size.x * 0.43f) : size.x - arrowWidth;
            var visitor = button.GetComponentInChildren<Text>(true);
            visitor.rectTransform.anchoredPosition = new Vector2(padding, 0);
            visitor.rectTransform.sizeDelta = new Vector2(visitorWidth - 2 * padding, size.y);
            visitor.alignment = TextAnchor.MiddleLeft;
            visitor.fontSize = 26;
            visitor.fontStyle = FontStyle.Bold;
            FitCell(visitor);
            if (hasTitle)
            {
                var title = CellLabel(cell, new Vector2(visitorWidth + padding, 0),
                    new Vector2(size.x - visitorWidth - arrowWidth - 2 * padding, size.y), visit.title, 24);
                title.name = "Title";
                FitCell(title);
            }
            FitCell(CellLabel(cell, new Vector2(size.x - arrowWidth, 0), new Vector2(arrowWidth - padding, size.y), "›", 24, TextAnchor.MiddleRight));
        }

        private static void FitCell(Text label)
        {
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 9;
            label.resizeTextMaxSize = label.fontSize;
        }

        private void ShowTable()
        {
            openAppointmentId = null;
            appointmentDetails.gameObject.SetActive(false);
            calendar.gameObject.SetActive(false);
            table.gameObject.SetActive(true);
            if (selectedDay > 0) DrawAppointments();
        }

        private void ToggleCalendar()
        {
            if (calendar.gameObject.activeSelf) { ShowTable(); return; }
            DateTime date = firstDate.AddDays(selectedDay - 1);
            calendarMonth = new DateTime(date.Year, date.Month, 1);
            appointmentDetails.gameObject.SetActive(false); openAppointmentId = null;
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
                if (day == runtime.Day) button.image.color = MonitorWidgets.SelectedButtonColor;
                if (day == selectedDay)
                {
                    var outline = button.gameObject.AddComponent<Outline>();
                    outline.effectColor = SelectionBorderColor;
                    outline.effectDistance = new Vector2(2, -2);
                    outline.useGraphicAlpha = false;
                }
            }
        }

        private void OpenAppointment(string id)
        {
            if (!isActiveAndEnabled || host.SystemMenuVisible || !terminal.InteractionEnabled) return;
            openAppointmentId = id;
            if (!RefreshAppointment()) return;
            CrtScrollRect scroll = appointmentText.GetComponentInParent<CrtScrollRect>(true);
            scroll.StopMovement();
            appointmentText.rectTransform.anchoredPosition = Vector2.zero;
            table.gameObject.SetActive(false);
            appointmentDetails.gameObject.SetActive(true);
        }

        private bool RefreshAppointment()
        {
            detailsRevision = runtime.Revision;
            try { appointmentText.text = runtime.AppointmentDetailsText(selectedDay, openAppointmentId); return true; }
            catch (CommandException) { ShowTable(); return false; }
        }
    }
}
