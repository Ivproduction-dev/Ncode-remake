using System.Text;

namespace Ncode.Core.Lexer;

public static class Tokenizer
{
    private static readonly Dictionary<string, string> WordOpToSym = new(StringComparer.OrdinalIgnoreCase)
    {
        {"плюс", "+"}, {"сложить", "+"}, {"прибавить", "+"}, {"добавить", "+"},
        {"минус", "-"}, {"вычесть", "-"}, {"вычитать", "-"}, {"отнять", "-"},
        {"умножить", "*"}, {"умножитьна", "*"},
        {"разделить", "/"}, {"поделить", "/"}, {"делить", "/"},
        {"остаток", "%"},
    };

    public static List<Tok> Tokenize(string expr, int line)
    {
        var toks = new List<Tok>();
        int i = 0;
        while (i < expr.Length)
        {
            char c = expr[i];
            if (char.IsWhiteSpace(c) || c == ',') { i++; continue; }
            if (c == '"')
            {
                var sb = new StringBuilder();
                int j = i + 1;
                bool closed = false;
                while (j < expr.Length)
                {
                    if (expr[j] == '\\' && j + 1 < expr.Length)
                    {
                        char next = expr[j + 1];
                        if (next == '"') { sb.Append('"'); j += 2; continue; }
                        if (next == '\\') { sb.Append('\\'); j += 2; continue; }
                        if (next == 'n') { sb.Append('\n'); j += 2; continue; }
                        if (next == 't') { sb.Append('\t'); j += 2; continue; }
                        sb.Append(next);
                        j += 2;
                        continue;
                    }
                    if (expr[j] == '"')
                    {
                        closed = true;
                        j++;
                        break;
                    }
                    sb.Append(expr[j]);
                    j++;
                }
                if (!closed) throw new Exception($"строка {line}: нет закрывающей кавычки");
                toks.Add(new Tok(TokKind.Str, sb.ToString()));
                i = j;
                continue;
            }
            if (c == '.' && i + 1 < expr.Length && expr[i + 1] == '.')
            {
                toks.Add(new Tok(TokKind.Op, ".."));
                i += 2;
                continue;
            }
            if (char.IsDigit(c) || (c == '.' && i + 1 < expr.Length && char.IsDigit(expr[i + 1])))
            {
                int j = i;
                while (j < expr.Length)
                {
                    if (char.IsDigit(expr[j]))
                    {
                        j++;
                    }
                    else if (expr[j] == '.' && !(j + 1 < expr.Length && expr[j + 1] == '.'))
                    {
                        j++;
                    }
                    else if (expr[j] == ',' && j + 1 < expr.Length && char.IsDigit(expr[j + 1]))
                    {
                        j++;
                    }
                    else
                    {
                        break;
                    }
                }
                toks.Add(new Tok(TokKind.Num, expr[i..j].Replace(',', '.')));
                i = j;
                continue;
            }
            if (c == '(') { toks.Add(new Tok(TokKind.LPar, "(")); i++; continue; }
            if (c == ')') { toks.Add(new Tok(TokKind.RPar, ")")); i++; continue; }
            if ("+-*/%^".Contains(c)) { toks.Add(new Tok(TokKind.Op, c.ToString())); i++; continue; }
            if (char.IsLetter(c) || c == '_' || (c >= 0x400 && c <= 0x4FF))
            {
                int j = i;
                while (j < expr.Length && (char.IsLetterOrDigit(expr[j]) || expr[j] == '_' || (expr[j] == '.' && j + 1 < expr.Length && (char.IsLetter(expr[j + 1]) || expr[j + 1] == '_')))) j++;
                string w = expr[i..j];
                string low = w.ToLowerInvariant();
                if (low is "истина" or "истинно" or "правда" or "true") toks.Add(new Tok(TokKind.Bool, "true"));
                else if (low is "ложь" or "ложно" or "неправда" or "false") toks.Add(new Tok(TokKind.Bool, "false"));
                else if (WordOpToSym.TryGetValue(low, out var sym)) toks.Add(new Tok(TokKind.Op, sym));
                else if (low is "пробел") toks.Add(new Tok(TokKind.Str, " "));
                else if (low is "на") { }
                else toks.Add(new Tok(TokKind.Name, w));
                i = j;
                continue;
            }
            throw new Exception($"строка {line}: плохой символ '{c}' в '{expr}'");
        }
        return toks;
    }
}
