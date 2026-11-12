using System;
using System.Collections.Generic;
using System.Linq;

namespace Codetective.Operations
{
    public sealed partial class KernelRuntime
    {
        /// <summary>Inspect types and sample inputs without invoking any world actions.</summary>
        public IReadOnlyList<Completion> CompleteProtocol(string source, int caret, string sampleArguments = "", bool composing = false)
        {
            source = source ?? "";
            caret = Math.Max(0, Math.Min(caret, source.Length));
            string before = source.Substring(0, caret);
            int start = Math.Max(Math.Max(before.LastIndexOf('\n'), before.LastIndexOf(';')), before.LastIndexOf('{')) + 1;
            int statementStart = start;
            string fragment = before.Substring(start);
            // An empty line is for indentation, not an implicit choice of the first object.
            if (string.IsNullOrWhiteSpace(fragment)) return Array.Empty<Completion>();
            int skip = fragment.Length - fragment.TrimStart().Length;
            start += skip;
            fragment = fragment.TrimStart();
            foreach (string keyword in CommandVocabulary.KeywordNames("var").Concat(CommandVocabulary.KeywordNames("return")).Select(k => k + " "))
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
                    locals[parameter.Name] = value != null && Accepts(parameter, value) ?
                        value : new Datum { kind = parameter.Kind, schemaId = parameter.SchemaId, label = parameter.Name };
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
            return Complete(fragment, composing, locals).Select(c => new Completion(c.Label,
                source.Substring(0, start) + c.Replacement + source.Substring(caret))).ToArray();
        }

        private Datum Preview(Expression expression, Dictionary<string, Datum> locals)
        {
            if (expression.Literal != null) return expression.Literal.Copy();
            string[] path = expression.Path;
            if (path.Length == 1)
            {
                if (locals.TryGetValue(path[0], out Datum local)) return PlayerValueVisible(local, locals) ? local.Copy() : Datum.Empty;
                if (data.TryGetValue(path[0], out DataSlot slot)) return PlayerValueVisible(slot.value, locals) ? slot.value.Copy() : Datum.Empty;
                if (protocols.ContainsKey(path[0])) return Datum.Ref(DataKind.Protocol, path[0], path[0]);
            }
            try
            {
                if (!expression.IsCall)
                {
                    try { return Reference(ResolveEntity(path, locals)); }
                    catch (CommandException)
                    {
                        if (path.Length < 2) return Datum.Empty;
                        Entity owner = ResolveCompletionEntity(path.Take(path.Length - 1).ToArray(), locals);
                        VariableDefinition field = owner.FindVariable(path.Last());
                        if (field != null && VariableDenial(owner, field, locals) == null)
                            return new Datum { kind = field.Kind, schemaId = field.SchemaId, label = field.Label };
                        Operation property = owner.Find(path.Last());
                        if (property != null && property.Property && OperationDenial(owner, property, locals) == null)
                            return new Datum { kind = property.ReturnKind, schemaId = property.ReturnSchemaId, label = property.Label };
                    }
                }
                if (path.Length > 1)
                {
                    Entity target = ResolveCompletionEntity(path.Take(path.Length - 1).ToArray(), locals);
                    Operation operation = target.Find(path.Last());
                    if (operation != null && OperationDenial(target, operation, locals) == null)
                        return new Datum { kind = operation.ReturnKind, schemaId = operation.ReturnSchemaId, label = operation.Label };
                }
            }
            catch (CommandException) { }
            return Datum.Empty;
        }
    }
}
