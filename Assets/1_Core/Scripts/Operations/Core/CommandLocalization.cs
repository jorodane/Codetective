using System;
using System.Collections.Generic;
using System.Linq;

namespace Codetective.Operations
{
    public enum CommandLocale { English, Korean }

    [Serializable]
    public sealed class LocalizedCommandName
    {
        public CommandLocale language;
        public string name;
        public string description;
        public LocalizedCommandName() { }
        public LocalizedCommandName(CommandLocale language, string name, string description = "")
        { this.language = language; this.name = name; this.description = description; }
    }

    /// <summary>Vocabulary is presentation. Every supported spelling remains executable in every locale.</summary>
    public static class CommandVocabulary
    {
        private static readonly Dictionary<string, string[]> Keywords = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            { "save", new[] { "저장" } }, { "free", new[] { "삭제", "해제" } }, { "help", new[] { "도움말" } },
            { "protocol", new[] { "프로토콜" } }, { "return", new[] { "반환" } }, { "var", new[] { "변수" } }
        };
        private static readonly string[] KoreanKinds =
        { "비어있음", "데이터", "숫자", "문자열", "인물", "물건", "증언", "사진", "문서", "기록", "장소", "시간", "분석", "규칙", "관계", "단서", "프로토콜", "스레드", "작업" };

        public static string Keyword(string input) => Keywords.FirstOrDefault(p => p.Key == input || p.Value.Contains(input)).Key;
        public static string KeywordName(string canonical, CommandLocale language) =>
            language == CommandLocale.Korean && Keywords.TryGetValue(canonical, out string[] names) ? names[0] : canonical;
        public static IEnumerable<string> KeywordNames(string canonical) => new[] { canonical }.Concat(Keywords[canonical]);
        public static string KindName(DataKind kind, CommandLocale language) => language == CommandLocale.Korean ? KoreanKinds[(int)kind] : kind.ToString();
        public static bool TryKind(string input, out DataKind kind)
        {
            for (int i = 1; i < KoreanKinds.Length; i++)
                if (KoreanKinds[i] == input) { kind = (DataKind)i; return true; }
            return Enum.TryParse(input, true, out kind) && Enum.IsDefined(typeof(DataKind), kind) && kind != DataKind.Empty;
        }

        public static string Name(string canonical, LocalizedCommandName[] names, CommandLocale language) =>
            names.FirstOrDefault(n => n.language == language && !string.IsNullOrWhiteSpace(n.name))?.name ?? canonical;
        public static string Description(string fallback, LocalizedCommandName[] names, CommandLocale language) =>
            names.FirstOrDefault(n => n.language == language && !string.IsNullOrWhiteSpace(n.description))?.description ?? fallback;
        public static IEnumerable<string> Names(string canonical, string[] aliases, LocalizedCommandName[] names) =>
            new[] { canonical }.Concat(aliases).Concat(names.Select(n => n.name)).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.Ordinal);
        public static string Match(string preferred, IEnumerable<string> names, string prefix, bool composing) =>
            new[] { preferred }.Concat(names).Distinct(StringComparer.Ordinal).FirstOrDefault(n => KernelRuntime.Prefix(n, prefix, composing));

        public static string ParameterName(string canonical, CommandLocale language)
        {
            if (language != CommandLocale.Korean) return canonical;
            switch (canonical)
            {
                case "document": return "문서";
                case "evidence": return "증거";
                case "protocol": return "프로토콜";
                default: return canonical;
            }
        }
    }

    public sealed partial class KernelRuntime
    {
        private CommandLocale language;
        public CommandLocale Language
        {
            get => language;
            set { if (language != value) { language = value; Revision++; } }
        }
        public string KindName(DataKind kind) => CommandVocabulary.KindName(kind, Language);
        public string ObjectName(string id) => Entity(id)?.NameFor(Language) ?? id;
        public string ExampleArguments => ObjectName("archive");
        public string ExampleProtocol => Language == CommandLocale.Korean ?
            "프로토콜 정찰(장소 대상)\n{\n    반환 대상.조사();\n}" :
            "protocol recon(Place target)\n{\n    return target.inspect();\n}";
        public string HelpForLanguage => Language == CommandLocale.Korean ?
            "저장 <이름> : 데이터 공간 할당\n삭제 <이름> : 데이터 공간 해제\n도움말 : 명령어 목록\n\n" +
            "대상. → 현재 권한 목록\n이름 = 대상.명령(입력);\n" +
            "예: 지휘소.커피요청();\n문서 = 자료실.조사();\n스레드01.프로토콜실행(정찰, 자료실);\n\n" + ExampleProtocol : Help;
        public string StartupGuide => Language == CommandLocale.Korean ?
            "지휘소. 로 권한 목록을 확인하세요.\n도움말로 명령 형식을 확인할 수 있습니다." :
            "Type hq. to view available commands.\nType help to see command syntax.";
    }
}
