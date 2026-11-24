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
        private const int TableWidth = 650;
        private const int DetailGap = 24;
        private const int DetailWidth = Width - TableWidth - DetailGap;
        private const int TimeWidth = 112;
        private const int NameWidth = 180;
        private const int ContentWidth = TableWidth - TimeWidth;
        private const int HourHeight = 54;
        private const int CellGap = 3;
        private const int Hours = (ClinicHours.Closing - ClinicHours.Opening) / ClinicHours.MinutesPerSlot;
        private static readonly string[] Weekdays = { "일", "월", "화", "수", "목", "금", "토" };
        private static readonly Color SlotColor = new Color(0.038f, 0.061f, 0.045f);
        private static readonly Color TimeColor = new Color(0.065f, 0.105f, 0.077f);
        private static readonly Color LunchColor = new Color(0.082f, 0.071f, 0.047f);
        private static readonly Color ActiveAppointmentColor = Color.Lerp(MonitorWidgets.ButtonColor, MonitorWidgets.SelectedButtonColor, 0.4f);
        private static readonly Color SelectionBorderColor = Color.Lerp(MonitorWidgets.Foreground, MonitorWidgets.SelectedButtonColor, 0.22f);

        private KernelRuntime runtime;
        private MonitorPageHost host;
        private TerminalController terminal;
        private Font font;
        private RectTransform table, emptyCells, appointmentCells, calendar, calendarDays, appointmentDetails;
        private Text dateLabel, monthLabel, appointmentText;
        private CrtScrollRect appointmentScroll;
        private Button previousDay, nextDay, previousMonth, nextMonth;
        private readonly Button[] timeCells = new Button[Hours];
        private readonly string[] appointmentAtHour = new string[Hours];
        private readonly Dictionary<string, (Button button, Color normalColor)> appointmentButtons = new Dictionary<string, (Button, Color)>();
        private DateTime firstDate, calendarMonth;
        private int selectedDay, lastDay, maxDay;
        private string tableSignature, selectedAppointmentId;
        private int detailsRevision = -1;
        private float nextRefresh;

        public void Initialize(KernelRuntime value, Font typeface)
        {
            runtime = value; font = typeface;
            host = GetComponentInParent<MonitorPageHost>();
            terminal = host.GetComponent<TerminalController>();
            firstDate = runtime.DateAt(1, 0).Value.Date;
            maxDay = (DateTime.MaxValue.Date - firstDate).Days + 1;
            if (table == null) Build();
            selectedDay = 0; tableSignature = null; detailsRevision = -1;
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

            table = MonitorWidgets.Rect("Timetable", transform, new Vector2(28, -176), new Vector2(TableWidth, Hours * HourHeight));
            CellLabel(table, new Vector2(0, 36), new Vector2(TimeWidth - CellGap, 30), "상담 시간", 22, TextAnchor.MiddleCenter);
            CellLabel(table, new Vector2(TimeWidth + 12, 36), new Vector2(NameWidth - 24, 30), "이름", 22);
            CellLabel(table, new Vector2(TimeWidth + NameWidth + 12, 36), new Vector2(ContentWidth - NameWidth - 24, 30), "상담 제목", 22);
            for (int row = 0; row < Hours; row++)
            {
                int start = ClinicHours.Opening + row * ClinicHours.MinutesPerSlot;
                int hour = row;
                Button clock = MonitorWidgets.Button(table, font, new Vector2(0, -row * HourHeight),
                    new Vector2(TimeWidth - CellGap, HourHeight - CellGap), KernelRuntime.TimeOfDay(start),
                    () => SelectAppointment(appointmentAtHour[hour]));
                clock.name = "Time";
                clock.image.color = TimeColor;
                clock.transition = Selectable.Transition.None;
                clock.interactable = false;
                timeCells[row] = clock;
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

            appointmentDetails = MonitorWidgets.Rect("AppointmentDetails", transform, new Vector2(28 + TableWidth + DetailGap, -140),
                new Vector2(DetailWidth, table.sizeDelta.y + 36));
            CellLabel(appointmentDetails, new Vector2(0, -2), new Vector2(DetailWidth, 30), "상담 상세", 22);
            appointmentText = MonitorWidgets.ScrollText(appointmentDetails, font, new Vector2(0, -36), new Vector2(DetailWidth, table.sizeDelta.y));
            appointmentScroll = appointmentText.transform.parent.GetComponent<CrtScrollRect>();
            appointmentText.fontSize = 23;
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
                SetSelectedAppointment(null);
                ShowTable();
            }
            DateTime date = firstDate.AddDays(selectedDay - 1);
            dateLabel.text = date.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture) + " (" + Weekdays[(int)date.DayOfWeek] + ")";
            previousDay.interactable = selectedDay > 1;
            nextDay.interactable = selectedDay < maxDay;
            if (calendar.gameObject.activeSelf && lastDay != runtime.Day) DrawCalendar();
            lastDay = runtime.Day;
            if (!table.gameObject.activeSelf) return;

            AppointmentProgress[] visits = runtime.ScheduleForDay(selectedDay).Where(visit => visit.endMinute > visit.startMinute).ToArray();
            if (!visits.Any(visit => visit.id == selectedAppointmentId))
                SetSelectedAppointment(visits.FirstOrDefault()?.id);
            DrawAppointments(visits);
            if (detailsRevision != runtime.Revision) RefreshAppointment();
        }

        private void DrawAppointments(AppointmentProgress[] visits)
        {
            int latestUnavailableHour = selectedDay == runtime.Day ? (int)runtime.Minute / ClinicHours.MinutesPerSlot * ClinicHours.MinutesPerSlot :
                selectedDay < runtime.Day ? ClinicHours.Closing : 0;
            string signature = $"{selectedDay}:{latestUnavailableHour}|" + string.Join("|", visits.Select(v =>
                $"{v.id}:{v.startMinute}:{v.endMinute}:{v.phase}:{v.outcome}:{v.title}"));
            if (signature == tableSignature) return;
            tableSignature = signature;
            MonitorWidgets.ClearRows(emptyCells);
            MonitorWidgets.ClearRows(appointmentCells);
            appointmentButtons.Clear();

            // Older saves keep their real times. Snap their visible bounds to the nearest hour and give short visits a full cell.
            var bookings = visits.Select(visit =>
            {
                int first = Math.Min(Hours - 1, HourBoundary(visit.startMinute));
                return (visit: visit, first: first, end: Math.Max(first + 1, HourBoundary(visit.endMinute)), column: 0);
            }).OrderBy(booking => booking.first).ToArray();
            for (int hour = 0; hour < Hours; hour++)
            {
                var booked = bookings.Where(booking => booking.first <= hour && hour < booking.end).Take(2).ToArray();
                appointmentAtHour[hour] = booked.Length == 1 ? booked[0].visit.id : null;
                timeCells[hour].interactable = appointmentAtHour[hour] != null;
                timeCells[hour].image.raycastTarget = timeCells[hour].interactable;
            }

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
            Color color = bookableHour ? SlotColor : LunchColor;
            var slot = Panel("Slot", emptyCells, new Vector2(0, -row * HourHeight), new Vector2(ContentWidth, HourHeight - CellGap),
                color);
            string label = bookableHour ? (start > latestUnavailableHour ? "예약 가능" : "") : "점심시간";
            if (label.Length > 0)
                CellLabel(slot, new Vector2(12, 0), new Vector2(ContentWidth - 24, slot.sizeDelta.y), label, 22).color =
                    Color.Lerp(MonitorWidgets.Foreground, color, 0.38f);
        }

        private void DrawAppointmentCell(AppointmentProgress visit, Vector2 position, Vector2 size)
        {
            Button button = MonitorWidgets.Button(appointmentCells, font, position, size, visit.patientName, () => SelectAppointment(visit.id));
            button.name = visit.id;
            Color normalColor = visit.phase == VisitPhase.Waiting || visit.phase == VisitPhase.Finished ? MonitorWidgets.ButtonColor : ActiveAppointmentColor;
            button.image.color = visit.id == selectedAppointmentId ? MonitorWidgets.SelectedButtonColor : normalColor;
            appointmentButtons.Add(visit.id, (button, normalColor));
            var cell = (RectTransform)button.transform;
            bool hasTitle = !string.IsNullOrWhiteSpace(visit.title);
            float padding = Mathf.Min(12, size.x * 0.06f);
            float visitorWidth = hasTitle ? Mathf.Min(NameWidth, size.x * 0.43f) : size.x;
            var visitor = button.GetComponentInChildren<Text>(true);
            visitor.rectTransform.anchoredPosition = new Vector2(padding, 0);
            visitor.rectTransform.sizeDelta = new Vector2(visitorWidth - 2 * padding, size.y);
            visitor.alignment = TextAnchor.MiddleLeft;
            visitor.fontSize = 24;
            visitor.fontStyle = FontStyle.Bold;
            FitCell(visitor);
            if (hasTitle)
            {
                var title = CellLabel(cell, new Vector2(visitorWidth + padding, 0),
                    new Vector2(size.x - visitorWidth - 2 * padding, size.y), visit.title, 23);
                title.name = "Title";
                FitCell(title);
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
            calendar.gameObject.SetActive(false);
            table.gameObject.SetActive(true);
            appointmentDetails.gameObject.SetActive(true);
        }

        private void ToggleCalendar()
        {
            if (calendar.gameObject.activeSelf) { ShowTable(); Refresh(); return; }
            DateTime date = firstDate.AddDays(selectedDay - 1);
            calendarMonth = new DateTime(date.Year, date.Month, 1);
            appointmentDetails.gameObject.SetActive(false);
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

        private void SelectAppointment(string id)
        {
            if (id == null || !isActiveAndEnabled || !table.gameObject.activeSelf || host.SystemMenuVisible || !terminal.InteractionEnabled ||
                !appointmentButtons.ContainsKey(id)) return;
            SetSelectedAppointment(id);
            if (detailsRevision != runtime.Revision) RefreshAppointment();
        }

        private void SetSelectedAppointment(string id)
        {
            if (id == selectedAppointmentId) return;
            if (selectedAppointmentId != null && appointmentButtons.TryGetValue(selectedAppointmentId, out var previous))
                previous.button.image.color = previous.normalColor;
            selectedAppointmentId = id;
            if (id != null && appointmentButtons.TryGetValue(id, out var current))
                current.button.image.color = MonitorWidgets.SelectedButtonColor;
            detailsRevision = -1;
            appointmentScroll.StopMovement();
            appointmentText.rectTransform.anchoredPosition = Vector2.zero;
        }

        private void RefreshAppointment()
        {
            detailsRevision = runtime.Revision;
            try
            {
                appointmentText.text = selectedAppointmentId == null ? "예약이 없습니다." : runtime.AppointmentDetailsText(selectedDay, selectedAppointmentId);
            }
            catch (CommandException error) { appointmentText.text = error.Message; }
        }
    }
}
