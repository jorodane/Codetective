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
        private static readonly string[] Commands = { "저장", "삭제" };
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
                return new CommandResult(false, "알 수 없는 명령어입니다. 저장 이름 / 삭제 이름");

            if (tokens.Length != 2 || tokens[1].Any(char.IsControl))
                return new CommandResult(false, $"사용법: {command} 이름 (이름에는 공백을 넣을 수 없습니다.)");

            string name = tokens[1];
            if (command == "저장")
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

        public IReadOnlyList<TerminalCompletion> Complete(string line)
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
                return Commands.Where(command => command.StartsWith(line, StringComparison.Ordinal))
                    .Select(command => new TerminalCompletion(command, command + " ")).ToArray();
            }

            if (line.Substring(0, separator) != "삭제")
                return Array.Empty<TerminalCompletion>();

            string prefix = line.Substring(separator).TrimStart();
            if (prefix.Any(char.IsWhiteSpace))
                return Array.Empty<TerminalCompletion>();

            return slots.Keys.Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal)
                .Select(name => new TerminalCompletion(name, "삭제 " + name)).ToArray();
        }

        private static string[] Tokenize(string line)
        {
            return line.Normalize(NormalizationForm.FormC)
                .Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
