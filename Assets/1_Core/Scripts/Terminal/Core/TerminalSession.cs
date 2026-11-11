using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Codetective.Terminal
{
    /// <summary>A named slot in the game's simulated memory, not an OS memory address.</summary>
    public sealed class MemorySlot
    {
        public int Id { get; }
        public string Name { get; }

        internal MemorySlot(int id, string name)
        {
            Id = id;
            Name = name;
        }
    }

    public readonly struct CommandResult
    {
        public bool Success { get; }
        public string Message { get; }

        public CommandResult(bool success, string message)
        {
            Success = success;
            Message = message;
        }
    }

    public readonly struct TerminalCompletion
    {
        public string DisplayText { get; }
        public string ReplacementText { get; }

        public TerminalCompletion(string displayText, string replacementText)
        {
            DisplayText = displayText;
            ReplacementText = replacementText;
        }
    }

    /// <summary>One play session. Allocations and completion candidates share the same source of truth.</summary>
    public sealed class TerminalSession
    {
        private static readonly string[] Commands = { "save", "free", "help" };
        public const string HelpText = "save <name>  : 메모리 공간 할당\n" +
                                       "free <name>  : 메모리 공간 해제\n" +
                                       "help         : 명령어 목록 보기\n\n" +
                                       "예: save 단서 / free 단서\n이름에는 공백을 넣을 수 없습니다.";
        private readonly Dictionary<string, MemorySlot> slots =
            new Dictionary<string, MemorySlot>(StringComparer.Ordinal);
        private int nextSlotId = 1;

        public int Count => slots.Count;
        public IReadOnlyList<MemorySlot> Memory => slots.Values.OrderBy(slot => slot.Id).ToArray();

        public CommandResult Execute(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return new CommandResult(false, string.Empty);

            if (line.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                return new CommandResult(false, "한 번에 한 줄만 입력하세요.");

            string[] tokens = Tokenize(line);
            string command = tokens[0];
            if (!Commands.Contains(command))
                return new CommandResult(false, "알 수 없는 명령어입니다. help로 명령어를 확인하세요.");

            if (command == "help")
                return tokens.Length == 1 ? new CommandResult(true, HelpText) :
                    new CommandResult(false, "사용법: help");

            if (tokens.Length != 2 || tokens[1].Any(char.IsControl))
                return new CommandResult(false, $"사용법: {command} 이름 (이름에는 공백을 넣을 수 없습니다.)");

            string name = tokens[1];
            if (command == "save")
            {
                if (slots.ContainsKey(name))
                    return new CommandResult(false, $"'{name}' 공간은 이미 할당되어 있습니다.");

                slots.Add(name, new MemorySlot(nextSlotId++, name));
                return new CommandResult(true, $"'{name}' 메모리 공간을 할당했습니다.");
            }

            if (!slots.Remove(name))
                return new CommandResult(false, $"'{name}'에 할당된 메모리가 없습니다.");

            return new CommandResult(true, $"'{name}' 메모리 공간을 해제했습니다.");
        }

        public IReadOnlyList<TerminalCompletion> Complete(string line, bool isComposing = false)
        {
            line = (line ?? string.Empty).Normalize(NormalizationForm.FormC).TrimStart();
            if (line.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                return Array.Empty<TerminalCompletion>();

            int separator = -1;
            for (int i = 0; i < line.Length; i++)
            {
                if (char.IsWhiteSpace(line[i]))
                {
                    separator = i;
                    break;
                }
            }

            if (separator < 0)
            {
                string commandPrefix = ComparisonForm(line, isComposing);
                return Commands.Where(command => ComparisonForm(command, isComposing).StartsWith(commandPrefix, StringComparison.Ordinal))
                    .Select(command => new TerminalCompletion(command, command == "help" ? command : command + " ")).ToArray();
            }

            if (line.Substring(0, separator) != "free")
                return Array.Empty<TerminalCompletion>();

            string prefix = line.Substring(separator).TrimStart();
            if (prefix.Any(char.IsWhiteSpace))
                return Array.Empty<TerminalCompletion>();

            prefix = ComparisonForm(prefix, isComposing);
            return slots.Keys.Where(name => ComparisonForm(name, isComposing).StartsWith(prefix, StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal)
                .Select(name => new TerminalCompletion(name, "free " + name)).ToArray();
        }

        private static string ComparisonForm(string value, bool isComposing)
        {
            // Compare composing Korean names as jamo: ㄷ / 다 / 단 can suggest 단서.
            // Keep committed text matching exact, including case and complete syllables.
            return isComposing ? value.Normalize(NormalizationForm.FormKD) : value;
        }

        private static string[] Tokenize(string line)
        {
            return line.Normalize(NormalizationForm.FormC)
                .Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
