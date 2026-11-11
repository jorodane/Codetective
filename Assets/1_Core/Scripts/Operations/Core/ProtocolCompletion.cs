using System;
using System.Collections.Generic;
using System.Linq;

namespace Codetective.Operations
{
    public sealed partial class KernelRuntime
    {
        /// <summary>Inspect types and sample inputs without invoking any world actions.</summary>
        public IReadOnlyList<Completion> CompleteProtocol(string source, int caret, string sampleArguments = "")
        {
            source = source ?? "";
            caret = Math.Max(0, Math.Min(caret, source.Length));
            string before = source.Substring(0, caret);
            int start = Math.Max(Math.Max(before.LastIndexOf('\n'), before.LastIndexOf(';')), before.LastIndexOf('{')) + 1;
            int statementStart = start;
            string fragment = before.Substring(start);
            int skip = fragment.Length - fragment.TrimStart().Length;
            start += skip;
            fragment = fragment.TrimStart();
            foreach (string keyword in new[] { "var ", "return ", "반환 " })
                if (fragment.StartsWith(keyword, StringComparison.Ordinal)) { start += keyword.Length; fragment = fragment.Substring(keyword.Length); break; }

            var locals = new Dictionary<string, Datum>(StringComparer.Ordinal);
            try
            {
                int brace = source.IndexOf('{');
                if (brace < 0) return Array.Empty<Completion>();
                string header = source.Substring(0, brace);
                Protocol signature = new CommandParser(header + "{}").ReadProtocol(header + "{}");
                Expression call = new CommandParser("preview(" + sampleArguments + ")").ReadSingle().Value;
                for (int i = 0; i < signature.Parameters.Length; i++)
                {
                    Parameter parameter = signature.Parameters[i];
                    Datum value = i < call.Arguments.Count ? Preview(call.Arguments[i], locals) : null;
                    locals[parameter.Name] = value != null && (parameter.Kind == DataKind.Any || value.kind == parameter.Kind) ?
                        value : new Datum { kind = parameter.Kind, label = parameter.Name };
                }
                int end = Math.Max(brace + 1, statementStart);
                string prior = source.Substring(0, end) + "}";
                try
                {
                    Protocol partial = new CommandParser(prior).ReadProtocol(prior);
                    foreach (Statement statement in partial.Body)
                        if (statement.Assign != null) locals[statement.Assign] = Preview(statement.Value, locals);
                }
                catch (CommandException) { /* An unfinished prior line must not break global suggestions. */ }
            }
            catch (CommandException) { }
            return Complete(fragment, false, locals).Select(c => new Completion(c.Label,
                source.Substring(0, start) + c.Replacement + source.Substring(caret))).ToArray();
        }

        private Datum Preview(Expression expression, Dictionary<string, Datum> locals)
        {
            if (expression.Literal != null) return expression.Literal.Copy();
            string[] path = expression.Path;
            if (path.Length == 1)
            {
                if (locals.TryGetValue(path[0], out Datum local)) return local.Copy();
                if (data.TryGetValue(path[0], out DataSlot slot)) return slot.value.Copy();
                if (protocols.ContainsKey(path[0])) return Datum.Ref(DataKind.Protocol, path[0], path[0]);
            }
            try
            {
                if (!expression.IsCall)
                {
                    Entity entity = ResolveEntity(path, locals);
                    return Datum.Ref(entity.Kind, entity.Id, entity.Label);
                }
                if (path.Length > 1)
                {
                    Entity target = ResolveEntity(path.Take(path.Length - 1).ToArray(), locals);
                    Operation operation = target.Find(path.Last());
                    if (operation != null) return new Datum { kind = operation.ReturnKind, label = operation.Label };
                }
            }
            catch (CommandException) { }
            return Datum.Empty;
        }
    }
}
