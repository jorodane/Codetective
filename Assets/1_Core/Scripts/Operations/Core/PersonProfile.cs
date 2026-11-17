using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Codetective.Operations
{
    public sealed class PersonProfile
    {
        public string Id, DisplayName, Name, Gender, Age, Birthday, Occupation, Family;
    }

    public sealed partial class KernelRuntime
    {
        private static readonly string[][] profileFields = {
            new[] { "legalName", "name", "이름" }, new[] { "gender", "sex", "성별" }, new[] { "age", "나이" },
            new[] { "birthDate", "birthday", "생일", "생년월일" }, new[] { "occupation", "job", "직업" }, new[] { "family", "familyRelations", "가족관계" } };

        public PersonProfile ProfileCard(string profileId)
        {
            RequireVisibleProfile(profileId);
            Entity person = Entity(profileId);
            string displayName = person?.Label ?? Consultation.FirstOrDefault(l => ProfileOf(l) == profileId)?.speaker ?? profileId;
            return new PersonProfile { Id = profileId, DisplayName = displayName,
                Name = ProfileField(person, profileFields[0], displayName), Gender = ProfileField(person, profileFields[1]),
                Age = ProfileField(person, profileFields[2]), Birthday = ProfileField(person, profileFields[3]),
                Occupation = ProfileField(person, profileFields[4]), Family = ProfileField(person, profileFields[5]) };
        }
        private string ProfileField(Entity person, string[] candidates, string fallback = "[미확인]")
        {
            if (person == null) return fallback;
            foreach (string candidate in candidates)
            {
                VariableDefinition field = person.FindVariable(candidate) ?? person.GetVariableDefinition(candidate);
                if (field == null) continue;
                if (!person.Variables.Any(v => v.Code == field.Code) || VariableDenial(person, field, null) != null) return "[접근 제한]";
                Datum value = person.GetValue(field.Code);
                if (!IsPlayerVisible(value)) return "[접근 제한]";
                return value.kind == DataKind.Empty || value.kind == DataKind.Text && string.IsNullOrWhiteSpace(value.text) ? fallback : value.ToString();
            }
            return fallback;
        }
        private void RequireVisibleProfile(string profileId)
        { if (!VisibleProfileIds.Contains(profileId)) throw new CommandException("현재 열람할 수 없는 프로필입니다."); }

        public string ProfileFindings(string profileId)
        {
            RequireVisibleProfile(profileId); Entity person = Entity(profileId); var text = new StringBuilder();
            var basic = new HashSet<string>(profileFields.SelectMany(f => f).Concat(new[] { "id" }), StringComparer.Ordinal);
            if (person != null)
                foreach (VariableDefinition field in person.Variables.Where(f => !basic.Contains(f.Code) && VariableDenial(person, f, null) == null))
                {
                    Datum value = person.GetValue(field.Code);
                    if (value.kind != DataKind.Empty && !(value.kind == DataKind.Text && string.IsNullOrWhiteSpace(value.text)) && IsPlayerVisible(value))
                        text.Append(field.Label ?? field.Code).Append(": ").Append(value).Append('\n');
                }
            foreach (ConsultationLine line in Consultation.Where(l => ProfileOf(l) == profileId && RecordedClues.Contains(l.fact)).GroupBy(l => l.fact).Select(g => g.First()))
                text.Append("● ").Append(line.keyword).Append('\n').Append(line.sentence).Append("\n\n");
            return text.Length == 0 ? "기록한 단서가 없습니다." : text.ToString().TrimEnd();
        }
        public string ProfileJournalText(string profileId, int pageIndex)
        {
            RequireVisibleProfile(profileId);
            ConsultationNote[] entries = NotesFor(profileId).ToArray();
            ConsultationNote note = pageIndex >= 0 && pageIndex < entries.Length ? entries[pageIndex] : null;
            string session = note == null ? "상담일: 미기록\n상담 시간: 미기록\n\n상담 내용\n작성된 상담 일지가 없습니다." :
                "상담일: DAY " + note.sessionDay + "\n상담 시간: " + TimeOfDay(note.startMinute) + " ~ " + TimeOfDay(note.endMinute) + "\n\n상담 내용\n" + note.text;
            return session + "\n\n기록한 단서 / 추가 정보\n" + ProfileFindings(profileId);
        }
        public static int ParseConsultationTime(string text)
        {
            string[] parts = (text ?? "").Trim().Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int hour) ||
                !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minute) || hour < 0 || hour > 24 || minute < 0 || minute > 59 || hour == 24 && minute != 0)
                throw new CommandException("상담 시간은 09:00처럼 시:분 형식으로 입력하세요.");
            return hour * 60 + minute;
        }
    }
}
