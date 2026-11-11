using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Codetective.Operations
{
    public sealed class Expression
    {
        public Datum Literal;
        public string[] Path;
        public List<Expression> Arguments;
        public bool IsCall => Arguments != null;
    }

    public sealed class Statement
    {
        public string Assign;
        public bool Return;
        public Expression Value;
    }

    public sealed class Protocol
    {
        public string Name;
        public string Source;
        public Parameter[] Parameters;
        public List<Statement> Body;
        public string Signature => Name + "(" + string.Join(", ", Parameters.Select(p => p.Kind + " " + p.Name)) + ")";
        public IEnumerable<string> CalledProtocols => Body.Select(s => s.Value).Where(e => e.IsCall && e.Path.Length == 1)
            .Select(e => e.Path[0]).Distinct();
    }

    /// <summary>A small, bounded command language. It never evaluates C# or reflects into Unity.</summary>
    public sealed class CommandParser
    {
        private readonly List<string> tokens = new List<string>();
        private int index;

        public CommandParser(string source)
        {
            if (source == null || source.Length > 32000) throw new CommandException("명령/프로토콜의 길이가 너무 깁니다.");
            for (int i = 0; i < source.Length;)
            {
                char c = source[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
                {
                    while (i < source.Length && source[i] != '\n') i++;
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    char quote = c;
                    var text = new StringBuilder("\"");
                    i++;
                    bool closed = false;
                    while (i < source.Length)
                    {
                        c = source[i++];
                        if (c == quote) { closed = true; break; }
                        if (c == '\\' && i < source.Length)
                        {
                            char escaped = source[i++];
                            text.Append(escaped == 'n' ? '\n' : escaped == 't' ? '\t' : escaped);
                        }
                        else text.Append(c);
                    }
                    if (!closed) throw new CommandException("문자열의 따옴표를 닫으세요.");
                    tokens.Add(text.ToString());
                    continue;
                }
                if (char.IsLetter(c) || c == '_')
                {
                    int start = i++;
                    while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_')) i++;
                    tokens.Add(source.Substring(start, i - start).Normalize(NormalizationForm.FormC));
                    continue;
                }
                if (char.IsDigit(c) || (c == '-' && i + 1 < source.Length && char.IsDigit(source[i + 1])))
                {
                    int start = i++;
                    while (i < source.Length && (char.IsDigit(source[i]) || source[i] == '.')) i++;
                    tokens.Add(source.Substring(start, i - start));
                    continue;
                }
                if (".,=();{}".IndexOf(c) >= 0) { tokens.Add(c.ToString()); i++; continue; }
                throw new CommandException($"지원하지 않는 문자: {c}");
            }
            tokens.Add("");
        }

        private string Peek => tokens[index];
        private string Take() => tokens[index++];
        private bool Match(string token) { if (Peek != token) return false; index++; return true; }
        private void Require(string token) { if (!Match(token)) throw new CommandException($"'{token}'이 필요합니다."); }
        private string Name()
        {
            string name = Take();
            if (!IsIdentifier(name)) throw new CommandException("유효한 이름이 필요합니다.");
            return name;
        }

        public static bool IsIdentifier(string name) => !string.IsNullOrEmpty(name) &&
            (char.IsLetter(name[0]) || name[0] == '_') && name.All(c => char.IsLetterOrDigit(c) || c == '_');

        public static DataKind ParseKind(string name)
        {
            switch (name)
            {
                case "인물": return DataKind.Person;
                case "물건": return DataKind.Item;
                case "장소": return DataKind.Place;
                case "데이터": return DataKind.Any;
            }
            if (Enum.TryParse(name, true, out DataKind kind) && kind != DataKind.Empty) return kind;
            throw new CommandException($"알 수 없는 데이터 종류: {name}");
        }

        public Protocol ReadProtocol(string source)
        {
            if (!Match("protocol") && !Match("프로토콜")) throw new CommandException("protocol 이름(종류 입력) { ... } 형식으로 작성하세요.");
            string name = Name();
            Require("(");
            var parameters = new List<Parameter>();
            if (!Match(")"))
            {
                do
                {
                    DataKind kind = ParseKind(Name());
                    string parameter = Name();
                    if (parameters.Any(p => p.Name == parameter)) throw new CommandException("입력 이름이 중복됩니다.");
                    parameters.Add(new Parameter(parameter, kind));
                } while (Match(","));
                Require(")");
            }
            Require("{");
            var body = new List<Statement>();
            while (!Match("}"))
            {
                if (Peek == "") throw new CommandException("프로토콜의 '}'를 닫으세요.");
                body.Add(ReadStatement());
                Require(";");
                if (body.Count > 512) throw new CommandException("프로토콜은 512개 명령 이하로 작성하세요.");
            }
            if (Peek != "") throw new CommandException("프로토콜 하나씩 저장하세요.");
            return new Protocol { Name = name, Parameters = parameters.ToArray(), Body = Lower(body), Source = source };
        }

        // Turn nested calls into individual steps so every action keeps its duration,
        // waiting point and return value when a Thread runs the same authored source.
        private static List<Statement> Lower(List<Statement> source)
        {
            var result = new List<Statement>();
            int temporary = 0;
            foreach (Statement statement in source)
            {
                LowerArguments(statement.Value, result, ref temporary);
                if (statement.Return && statement.Value.IsCall)
                {
                    string name = "\u0001" + temporary++;
                    result.Add(new Statement { Assign = name, Value = statement.Value });
                    statement.Value = new Expression { Path = new[] { name } };
                }
                result.Add(statement);
            }
            if (result.Count > 1024) throw new CommandException("중첩 호출을 포함해 1024단계 이하로 작성하세요.");
            return result;
        }

        private static void LowerArguments(Expression expression, List<Statement> steps, ref int temporary)
        {
            if (!expression.IsCall) return;
            for (int i = 0; i < expression.Arguments.Count; i++)
            {
                Expression argument = expression.Arguments[i];
                LowerArguments(argument, steps, ref temporary);
                if (!argument.IsCall) continue;
                // This name cannot be authored by a player and cannot collide with a local.
                string name = "\u0001" + temporary++;
                steps.Add(new Statement { Assign = name, Value = argument });
                expression.Arguments[i] = new Expression { Path = new[] { name } };
            }
        }

        public Statement ReadSingle()
        {
            Statement statement = ReadStatement();
            Match(";");
            if (Peek != "") throw new CommandException("한 번에 한 명령만 실행하세요.");
            if (statement.Return) throw new CommandException("return은 프로토콜 안에서 사용하세요.");
            return statement;
        }

        private Statement ReadStatement()
        {
            var statement = new Statement();
            if (Match("return") || Match("반환"))
            {
                statement.Return = true;
                statement.Value = Peek == ";" ? new Expression { Literal = Datum.Empty } : ReadExpression(0);
                return statement;
            }
            Match("var");
            if (index + 1 < tokens.Count && tokens[index + 1] == "=")
            {
                statement.Assign = Name();
                Require("=");
            }
            statement.Value = ReadExpression(0);
            return statement;
        }

        private Expression ReadExpression(int depth)
        {
            if (depth > 32) throw new CommandException("호출 중첩이 너무 깊습니다.");
            if (Peek.StartsWith("\"", StringComparison.Ordinal)) return new Expression { Literal = Datum.Text(Take().Substring(1)) };
            if (double.TryParse(Peek, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            {
                if (double.IsNaN(number) || double.IsInfinity(number)) throw new CommandException("유한한 숫자만 사용할 수 있습니다.");
                Take(); return new Expression { Literal = Datum.Number(number) };
            }
            var path = new List<string> { Name() };
            while (Match(".")) path.Add(Name());
            var expression = new Expression { Path = path.ToArray() };
            if (Match("("))
            {
                expression.Arguments = new List<Expression>();
                if (!Match(")"))
                {
                    do { expression.Arguments.Add(ReadExpression(depth + 1)); } while (Match(","));
                    Require(")");
                }
            }
            return expression;
        }
    }
}
