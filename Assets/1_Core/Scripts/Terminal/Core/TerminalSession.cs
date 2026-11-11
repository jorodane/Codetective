using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Codetective.Operations;

namespace Codetective.Terminal
{
    /// <summary>A named slot in the game's simulated memory, not an OS memory address.</summary>
    public sealed class MemorySlot
    {
        private readonly DataSlot slot;
        public int Id => slot.id;
        public string Name => slot.name;
        public Datum Value => slot.value;

        internal MemorySlot(DataSlot slot)
        {
            this.slot = slot;
        }
        internal bool References(DataSlot value) => ReferenceEquals(slot, value);
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
        private readonly Dictionary<int, MemorySlot> wrappers = new Dictionary<int, MemorySlot>();
        public KernelRuntime Runtime { get; }
        public TerminalSession(KernelRuntime runtime = null) { Runtime = runtime ?? new KernelRuntime(); }

        public int Count => Runtime.Data.Count;
        public IReadOnlyList<MemorySlot> Memory => Runtime.Data.Values.OrderBy(slot => slot.id).Select(Wrap).ToArray();
        private MemorySlot Wrap(DataSlot slot)
        {
            if (!wrappers.TryGetValue(slot.id, out MemorySlot wrapper) || !wrapper.References(slot))
                wrappers[slot.id] = wrapper = new MemorySlot(slot);
            return wrapper;
        }

        public CommandResult Execute(string line)
        {
            ExecutionResult result = Runtime.Execute(line);
            return new CommandResult(result.Success, result.Message);
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
                    .Select(command => new TerminalCompletion(command, command == "help" ? command : command + " "))
                    .Concat(Runtime.Complete(line, isComposing).Select(c => new TerminalCompletion(c.Label, c.Replacement))).ToArray();
            }

            if (line.Substring(0, separator) != "free")
                return Runtime.Complete(line, isComposing).Select(c => new TerminalCompletion(c.Label, c.Replacement)).ToArray();

            string prefix = line.Substring(separator).TrimStart();
            if (prefix.Any(char.IsWhiteSpace))
                return Array.Empty<TerminalCompletion>();

            prefix = ComparisonForm(prefix, isComposing);
            return Runtime.Data.Keys.Where(name => ComparisonForm(name, isComposing).StartsWith(prefix, StringComparison.Ordinal))
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
