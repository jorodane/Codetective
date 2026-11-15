using System;
using System.Linq;

namespace Codetective.Operations
{
    public enum MailChoiceResult { Selected, Approved, Declined }
    [Serializable] public sealed class MailChoice
    {
        public string id;
        public string label;
        public MailChoiceResult result;
        public bool approvesReservation;
        public StoryCondition condition = new StoryCondition();
        public string[] facts = Array.Empty<string>();
        public string[] authorities = Array.Empty<string>();
        public StoryEffects effects = new StoryEffects();
        public MailChoice Copy() => new MailChoice { id = id, label = label, result = result, approvesReservation = approvesReservation,
            condition = condition?.Copy() ?? new StoryCondition(), facts = facts.ToArray(), authorities = authorities.ToArray(), effects = effects?.Copy() ?? new StoryEffects() };
    }

    public sealed partial class KernelRuntime
    {
        public string SelectedMailChoice(string id) => mailProgress.TryGetValue(id, out MailProgress state) ? state.choiceId : null;
        public string SelectedMailChoiceLabel(string id) => mailProgress.TryGetValue(id, out MailProgress state) ? state.choiceLabel : null;
        public bool IsMailDeclined(string id) => mailProgress.TryGetValue(id, out MailProgress state) && !string.IsNullOrEmpty(state.choiceId) && state.choiceResult == MailChoiceResult.Declined;
        public string MailChoiceDenial(string mailId, string choiceId)
        {
            if (mailId == null || !mail.TryGetValue(mailId, out StoryMail message) || !MailAvailable(message)) return "아직 도착하지 않은 메일입니다.";
            MailChoice choice = message.choices.FirstOrDefault(c => c.id == choiceId);
            if (choice == null) return "존재하지 않는 선택지입니다.";
            if (!IsMailRead(mailId)) return "메일 내용을 먼저 확인하세요.";
            if (!string.IsNullOrEmpty(SelectedMailChoice(mailId))) return "이미 답변한 메일입니다.";
            if (!choice.condition.Matches(this)) return "현재는 선택할 수 없는 응답입니다.";
            return choice.approvesReservation ? ReservationDenial(mailId, message.reservation) : null;
        }
        public void ChooseMail(string mailId, string choiceId)
        {
            StoryMail message = RequireMail(mailId);
            if (!string.IsNullOrEmpty(SelectedMailChoice(mailId)))
            {
                if (SelectedMailChoice(mailId) == choiceId) return;
                throw new CommandException("이미 답변한 메일입니다.");
            }
            string denial = MailChoiceDenial(mailId, choiceId); if (denial != null) throw new CommandException(denial);
            MailChoice choice = message.choices.First(c => c.id == choiceId);
            choice.effects.Validate(); // No effects or reply state are changed until the booking is valid.
            if (choice.approvesReservation) ConfirmReservation(mailId, message.reservation);
            MailProgress state = mailProgress[mailId];
            state.choiceId = choice.id; state.choiceLabel = choice.label; state.choiceResult = choice.result; state.accepted = choice.result == MailChoiceResult.Approved;
            Facts.UnionWith(choice.facts); Authorities.UnionWith(choice.authorities); ApplyStoryEffects(choice.effects);
            Report("메일 답변: " + message.subject + " / " + choice.label); NarrativeUpdated();
        }
        private static void ValidateMailChoices(StoryMail message)
        {
            MailChoice[] choices = message.choices ?? Array.Empty<MailChoice>();
            if ((message.requiresAcceptance && choices.Length > 0) || choices.Any(c => c == null || !CommandParser.IsIdentifier(c.id) || string.IsNullOrWhiteSpace(c.label) ||
                !Enum.IsDefined(typeof(MailChoiceResult), c.result) || (c.approvesReservation && (message.reservation == null || c.result != MailChoiceResult.Approved))) ||
                choices.Select(c => c.id).Distinct().Count() != choices.Length)
                throw new CommandException("메일 선택지의 ID/응답/예약 연결을 확인하세요.");
            foreach (MailChoice choice in choices) choice.effects?.Validate();
            if (message.reservation != null && (message.reservation.day < message.day || !choices.Any(c => c.approvesReservation)))
                throw new CommandException("예약 문의에는 도착일 이후의 예약 날짜와 승인 선택지가 필요합니다.");
        }
    }
}
