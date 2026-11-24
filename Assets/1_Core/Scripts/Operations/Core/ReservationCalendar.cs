using System;
using System.Collections.Generic;
using System.Linq;

namespace Codetective.Operations
{
    [Serializable] public sealed class ReservationRequest
    {
        public int day = 1;
        public PatientAppointment appointment = new PatientAppointment();
        public ReservationRequest Copy() => new ReservationRequest { day = day, appointment = appointment.Copy() };
    }
    [Serializable] public sealed class ConfirmedReservation
    {
        public string mailId, appointmentId, profileId, patientName;
        public int day, startMinute, endMinute;
        public ConfirmedReservation Copy() => (ConfirmedReservation)MemberwiseClone();
    }
    public sealed partial class KernelRuntime
    {
        private readonly Dictionary<string, ReservationRequest> reservationRequests = new Dictionary<string, ReservationRequest>(StringComparer.Ordinal);
        private readonly Dictionary<string, ConfirmedReservation> confirmedReservations = new Dictionary<string, ConfirmedReservation>(StringComparer.Ordinal);
        private static string ReservationKey(string mailId) => "mail:" + mailId;
        public string ReservationMailFor(string appointmentId) => appointmentId != null && appointmentId.StartsWith("mail:", StringComparison.Ordinal) ? appointmentId.Substring(5) : null;
        public int PendingReservationCount => Inbox(Mailbox.Clinic).Concat(Inbox(Mailbox.Kernel)).Count(m => m.HasReservation && string.IsNullOrEmpty(SelectedMailChoice(m.id)));
        public IReadOnlyList<AppointmentProgress> ScheduleForDay(int day)
        {
            if (day < 1) throw new CommandException("날짜는 1일부터 조회할 수 있습니다.");
            if (day == Day) { InitializeClinicDay(); return DailyAppointments.ToArray(); }
            if (day < Day) return DayRecord(day)?.visits.Select(v => v.Copy()).ToArray() ?? Array.Empty<AppointmentProgress>();
            return BuildClinicDay(day).appointments.Select(v => v.Copy()).ToArray();
        }
        private void RegisterReservation(string mailId, ReservationRequest request)
        {
            if (request.day < 1) throw new CommandException("예약 날짜가 유효하지 않습니다.");
            ValidateAppointment(request.appointment);
            ReservationRequest copy = request.Copy();
            RegisterAppointment(ReservationKey(mailId), copy.appointment); reservationRequests.Add(mailId, copy);
        }
        private string ReservationDenial(string mailId, ReservationRequest request)
        {
            if (request == null || !reservationRequests.ContainsKey(mailId)) return "예약 요청을 찾을 수 없습니다.";
            PatientAppointment wanted = request.appointment;
            if (request.day < Day || (request.day == Day && (IsDayEnded || wanted.startMinute <= Minute))) return "이미 지난 예약 시각입니다.";
            if (request.day == Day && IsClinicTransitioning) return "손님 응대가 끝난 뒤 예약을 승인하세요.";
            if (!wanted.condition.Matches(this)) return "현재는 승인할 수 없는 예약입니다.";
            if (confirmedReservations.ContainsKey(mailId)) return "이미 등록된 예약입니다.";
            return ScheduleForDay(request.day).Any(booked => wanted.startMinute < booked.endMinute && booked.startMinute < wanted.BookingEndMinute)
                ? "기존 예약 시간과 겹칩니다. 스케줄을 확인하세요." : null;
        }
        private void ConfirmReservation(string mailId, ReservationRequest request)
        {
            PatientAppointment a = request.appointment;
            var confirmed = new ConfirmedReservation { mailId = mailId, appointmentId = ReservationKey(mailId), day = request.day,
                profileId = a.profileId, patientName = a.patientName, startMinute = a.startMinute, endMinute = a.BookingEndMinute };
            confirmedReservations.Add(mailId, confirmed);
            if (request.day == Day)
            {
                InitializeClinicDay();
                if (!clinicDay.appointments.Any(v => v.id == confirmed.appointmentId))
                    clinicDay.appointments = clinicDay.appointments.Concat(new[] { ReservationProgress(confirmed) }).OrderBy(v => v.startMinute).ToArray();
            }
        }
        private AppointmentProgress ReservationProgress(ConfirmedReservation r) => new AppointmentProgress { id = r.appointmentId, profileId = r.profileId,
            patientName = r.patientName, title = appointmentDefinitions[r.appointmentId].title, startMinute = r.startMinute,
            endMinute = r.endMinute > 0 ? r.endMinute : ClinicHours.Closing,
            lineStart = appointmentLines[r.appointmentId], lineCount = appointmentDefinitions[r.appointmentId].dialogue.Length };
        private ClinicDayState BuildClinicDay(int day)
        {
            ClinicDayPlan plan = clinicPlans.TryGetValue(day, out ClinicDayPlan exact) ? exact : clinicPlans.TryGetValue(0, out ClinicDayPlan fallback) ? fallback : null;
            AppointmentProgress[] fixedVisits = plan?.appointments.Where(a => a.condition.Matches(this)).Select(a => new AppointmentProgress {
                id = plan.day + ":" + a.id, profileId = a.profileId, patientName = a.patientName, title = a.title, startMinute = a.startMinute, endMinute = a.BookingEndMinute,
                lineStart = appointmentLines[plan.day + ":" + a.id], lineCount = a.dialogue.Length }).ToArray() ?? Array.Empty<AppointmentProgress>();
            AppointmentProgress[] visits = fixedVisits.Concat(confirmedReservations.Values.Where(r => r.day == day).Select(ReservationProgress)).OrderBy(v => v.startMinute).ToArray();
            // Older saves can contain reservations with an open end or times that overlap the current plan.
            for (int i = 1; i < visits.Length; i++)
                visits[i - 1].endMinute = Math.Min(visits[i - 1].endMinute, visits[i].startMinute);
            return new ClinicDayState { day = day, subtitle = plan?.subtitle ?? "상담과 작전 사이", appointments = visits };
        }
        private void RestoreReservations(ConfirmedReservation[] saved)
        {
            confirmedReservations.Clear();
            foreach (ConfirmedReservation r in saved ?? Array.Empty<ConfirmedReservation>())
            {
                if (!reservationRequests.ContainsKey(r.mailId) || r.appointmentId != ReservationKey(r.mailId) || r.day < 1 || r.startMinute < ClinicHours.Opening || r.startMinute >= ClinicHours.Closing)
                    throw new CommandException("저장된 예약이 유효하지 않습니다.");
                confirmedReservations.Add(r.mailId, r.Copy());
            }
        }
    }
}
