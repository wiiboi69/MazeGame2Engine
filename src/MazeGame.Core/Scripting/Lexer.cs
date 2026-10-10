using System.Globalization;
using System.Text;

namespace MazeGame.Core.Scripting;

public sealed class ScriptException : Exception
{
    public int Line { get; }
    public ScriptException(string message, int line = 0) : base(line > 0 ? $"line {line}: {message}" : message) { Line = line; }
}

public enum Tok
{
    Eof, Number, String, Ident,
    LParen, RParen, LBrace, RBrace, Comma, Semi,
    Plus, Minus, Star, Slash, Percent, Bang,
    Assign, PlusEq, MinusEq, Eq, Ne, Lt, Le, Gt, Ge, AndAnd, OrOr,
}

public readonly record struct Token(Tok Kind, string Text, double Num, int Line);

/// <summary>Turns script source text into tokens. Supports // and /* */ comments.</summary>
public static class Lexer
{
    public static List<Token> Tokenize(string src)
    {
        var list = new List<Token>();
        int i = 0, line = 1;
        while (i < src.Length)
        {
            char c = src[i];
            if (c == '\n') { line++; i++; continue; }
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '/' && i + 1 < src.Length && src[i + 1] == '/')
            {
                while (i < src.Length && src[i] != '\n') i++;
                continue;
            }
            if (c == '/' && i + 1 < src.Length && src[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < src.Length && !(src[i] == '*' && src[i + 1] == '/')) { if (src[i] == '\n') line++; i++; }
                i = Math.Min(src.Length, i + 2);
                continue;
            }
            if (char.IsDigit(c) || (c == '.' && i + 1 < src.Length && char.IsDigit(src[i + 1])))
            {
                int s = i;
                while (i < src.Length && (char.IsDigit(src[i]) || src[i] == '.')) i++;
                string t = src.Substring(s, i - s);
                if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    throw new ScriptException($"bad number '{t}'", line);
                list.Add(new Token(Tok.Number, t, d, line));
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                int s = i;
                while (i < src.Length && (char.IsLetterOrDigit(src[i]) || src[i] == '_')) i++;
                list.Add(new Token(Tok.Ident, src.Substring(s, i - s), 0, line));
                continue;
            }
            if (c == '"')
            {
                var sb = new StringBuilder();
                i++;
                while (i < src.Length && src[i] != '"')
                {
                    if (src[i] == '\n') throw new ScriptException("unterminated string", line);
                    if (src[i] == '\\' && i + 1 < src.Length)
                    {
                        i++;
                        sb.Append(src[i] switch { 'n' => '\n', 't' => '\t', _ => src[i] });
                        i++;
                        continue;
                    }
                    sb.Append(src[i++]);
                }
                if (i >= src.Length) throw new ScriptException("unterminated string", line);
                i++;
                list.Add(new Token(Tok.String, sb.ToString(), 0, line));
                continue;
            }

            char n = i + 1 < src.Length ? src[i + 1] : '\0';
            Tok kind;
            int len = 1;
            switch (c)
            {
                case '(': kind = Tok.LParen; break;
                case ')': kind = Tok.RParen; break;
                case '{': kind = Tok.LBrace; break;
                case '}': kind = Tok.RBrace; break;
                case ',': kind = Tok.Comma; break;
                case ';': kind = Tok.Semi; break;
                case '*': kind = Tok.Star; break;
                case '/': kind = Tok.Slash; break;
                case '%': kind = Tok.Percent; break;
                case '+': if (n == '=') { kind = Tok.PlusEq; len = 2; } else kind = Tok.Plus; break;
                case '-': if (n == '=') { kind = Tok.MinusEq; len = 2; } else kind = Tok.Minus; break;
                case '!': if (n == '=') { kind = Tok.Ne; len = 2; } else kind = Tok.Bang; break;
                case '=': if (n == '=') { kind = Tok.Eq; len = 2; } else kind = Tok.Assign; break;
                case '<': if (n == '=') { kind = Tok.Le; len = 2; } else kind = Tok.Lt; break;
                case '>': if (n == '=') { kind = Tok.Ge; len = 2; } else kind = Tok.Gt; break;
                case '&': if (n == '&') { kind = Tok.AndAnd; len = 2; } else throw new ScriptException("unexpected '&' (use &&)", line); break;
                case '|': if (n == '|') { kind = Tok.OrOr; len = 2; } else throw new ScriptException("unexpected '|' (use ||)", line); break;
                default: throw new ScriptException($"unexpected character '{c}'", line);
            }
            list.Add(new Token(kind, src.Substring(i, len), 0, line));
            i += len;
        }
        list.Add(new Token(Tok.Eof, "", 0, line));
        return list;
    }
}
