using System.Text;
using System.Text.RegularExpressions;

class BreakException : Exception { }
class ContinueException : Exception { }
class ExitException : Exception { }

record Line(string Text, int No);

class Program
{
    static Dictionary<string, object> Vars = new();
    static Dictionary<string, List<List<Line>>> Handlers = new(StringComparer.OrdinalIgnoreCase);
    static List<List<Line>> OnStarts = new();
    static int BroadcastDepth = 0;

    class Trigger
    {
        public string Cond = "";
        public List<Line> Body = new();
        public int No;
        public bool Last;
    }
    static List<Trigger> Triggers = new();
    static int TriggerDepth = 0;

    static Regex NameRegex = new(@"^[A-Za-zА-Яа-яЁё_][A-Za-zА-Яа-яЁё0-9_]*$");
    static Regex QuotedFull = new("^\"([^\"]*)\"$");

    static string FirstWord(string s)
    {
        s = s.Trim();
        int sp = s.IndexOfAny(new[] { ' ', '\t' });
        return (sp < 0 ? s : s[..sp]).ToLowerInvariant();
    }
    static bool StartsWithWord(string text, params string[] words)
    {
        var t = text.Trim().ToLowerInvariant();
        foreach (var w in words)
        {
            if (t == w) return true;
            if (t.StartsWith(w + " ") || t.StartsWith(w + "\t")) return true;
        }
        return false;
    }
    static bool IsIf(string t) => StartsWithWord(t, "если", "эсли");
    static bool IsWhile(string t) => StartsWithWord(t, "пока");
    static bool IsRepeat(string t) => StartsWithWord(t, "повтори", "повторить", "повторять") && !StartsWithWord(t, "вечно");
    static bool IsFor(string t) => StartsWithWord(t, "для");
    static bool IsForever(string t) => Regex.IsMatch(t.Trim(), @"^вечно\s+повторя", RegexOptions.IgnoreCase);
    static bool IsWhen(string t) => StartsWithWord(t, "когда");
    static bool IsOnStart(string t) => StartsWithWord(t, "при запуске", "при старте");
    static bool IsTrigger(string t) => StartsWithWord(t, "как только");
    static bool IsElse(string t) => StartsWithWord(t, "иначе");
    static bool IsElseIf(string t)
    {
        var m = Regex.Match(t.Trim(), @"^иначе\b", RegexOptions.IgnoreCase);
        if (!m.Success) return false;
        return StartsWithWord(t.Trim()[m.Length..].Trim(), "если", "эсли");
    }
    static bool IsEnd(string t) => StartsWithWord(t, "конец");
    static bool IsBreak(string t) => StartsWithWord(t, "остановить", "останови", "прервать", "прерви", "стоп");
    static bool IsContinue(string t) => StartsWithWord(t, "продолжить", "продолжи");
    static bool IsExit(string t) => StartsWithWord(t, "выход", "выйти", "закончить", "завершить");
    static bool IsDelete(string t) => StartsWithWord(t, "удалить", "удали");
    static bool IsClear(string t) => StartsWithWord(t, "очистить", "очисти");
    static bool IsWriteFile(string t) => StartsWithWord(t, "записать", "запиши");
    static bool IsReadFile(string t) => StartsWithWord(t, "прочитать", "прочитай");
    static bool IsInclude(string t) => StartsWithWord(t, "подключить", "подключи");
    static bool IsBroadcast(string t) => StartsWithWord(t, "вещать");
    static bool IsSet(string t) => StartsWithWord(t, "задать", "задай");
    static bool IsPrint(string t) => StartsWithWord(t, "вывести", "выведи", "напечатать", "напечатай", "печатать", "печатай", "показать", "покажи");
    static bool IsCreateList(string t) => StartsWithWord(t, "создать", "создай");
    static bool IsAdd(string t) => StartsWithWord(t, "добавить", "добавь");
    static bool IsAsk(string t) => StartsWithWord(t, "спросить", "спроси");
    static bool IsWait(string t) => StartsWithWord(t, "ждать", "жди");
    static bool IsBlockStart(string t) => IsIf(t) || IsWhile(t) || IsRepeat(t) || IsFor(t) || IsForever(t) || IsWhen(t) || IsOnStart(t) || IsTrigger(t);

    static string AfterFirstWord(string text)
    {
        text = text.Trim();
        int sp = text.IndexOfAny(new[] { ' ', '\t' });
        return sp < 0 ? "" : text[(sp + 1)..].Trim();
    }

    static string StripTrailingWord(string s, params string[] words)
    {
        s = s.Trim();
        foreach (var w in words)
        {
            var m = Regex.Match(s, @"^(.*)[\s\t]+" + Regex.Escape(w) + @"[\s\t]*$", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value.Trim();
        }
        return s;
    }

    static string Fmt(object v)
    {
        if (v is bool b) return b ? "истина" : "ложь";
        if (v is double d) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (v is List<object> l) return "[" + string.Join(", ", l.Select(Fmt)) + "]";
        return v.ToString() ?? "";
    }
    static bool IsTrue(object v)
    {
        if (v is bool b) return b;
        if (v is int i) return i != 0;
        if (v is double d) return Math.Abs(d) > 1e-9;
        if (v is string s) return s.Length > 0;
        if (v is List<object> l) return l.Count > 0;
        return v != null;
    }
    static double ToNum(object v, int line)
    {
        if (v is int i) return i;
        if (v is double d) return d;
        if (v is bool b) return b ? 1 : 0;
        throw new Exception($"строка {line}: надо число, а там '{Fmt(v)}'");
    }
    static object NormNum(double d)
    {
        if (Math.Abs(d - Math.Round(d)) < 1e-9 && d >= int.MinValue && d <= int.MaxValue)
            return (int)Math.Round(d);
        return d;
    }

    enum TokKind { Num, Str, Name, Op, LPar, RPar, Bool }
    record Tok(TokKind Kind, string Val);

    static readonly Dictionary<string, string> WordOpToSym = new(StringComparer.OrdinalIgnoreCase)
    {
        {"плюс", "+"}, {"сложить", "+"}, {"прибавить", "+"}, {"добавить", "+"},
        {"минус", "-"}, {"вычесть", "-"}, {"вычитать", "-"}, {"отнять", "-"},
        {"умножить", "*"}, {"умножитьна", "*"},
        {"разделить", "/"}, {"поделить", "/"}, {"делить", "/"},
        {"остаток", "%"},
    };

    static List<Tok> Tokenize(string expr, int line)
    {
        var toks = new List<Tok>();
        int i = 0;
        while (i < expr.Length)
        {
            char c = expr[i];
            if (char.IsWhiteSpace(c) || c == ',') { i++; continue; }
            if (c == '"')
            {
                int j = expr.IndexOf('"', i + 1);
                if (j < 0) throw new Exception($"строка {line}: нет закрывающей кавычки");
                toks.Add(new Tok(TokKind.Str, expr[(i + 1)..j]));
                i = j + 1; continue;
            }
            if (c == '.' && i + 1 < expr.Length && expr[i + 1] == '.') { toks.Add(new Tok(TokKind.Op, "..")); i += 2; continue; }
            if (char.IsDigit(c) || (c == '.' && i + 1 < expr.Length && char.IsDigit(expr[i + 1])))
            {
                int j = i;
                while (j < expr.Length && (char.IsDigit(expr[j]) || expr[j] == ',' || (expr[j] == '.' && !(j + 1 < expr.Length && expr[j + 1] == '.')))) j++;
                toks.Add(new Tok(TokKind.Num, expr[i..j].Replace(',', '.')));
                i = j; continue;
            }
            if (c == '(') { toks.Add(new Tok(TokKind.LPar, "(")); i++; continue; }
            if (c == ')') { toks.Add(new Tok(TokKind.RPar, ")")); i++; continue; }
            if ("+-*/%^".Contains(c)) { toks.Add(new Tok(TokKind.Op, c.ToString())); i++; continue; }
            if (char.IsLetter(c) || c == '_' || c >= 0x400 && c <= 0x4FF)
            {
                int j = i;
                while (j < expr.Length && (char.IsLetterOrDigit(expr[j]) || expr[j] == '_')) j++;
                string w = expr[i..j];
                string low = w.ToLowerInvariant();
                if (low is "истина" or "истинно" or "правда" or "true") toks.Add(new Tok(TokKind.Bool, "true"));
                else if (low is "ложь" or "ложно" or "неправда" or "false") toks.Add(new Tok(TokKind.Bool, "false"));
                else if (WordOpToSym.TryGetValue(low, out var sym)) toks.Add(new Tok(TokKind.Op, sym));
                else if (low is "пробел") toks.Add(new Tok(TokKind.Str, " "));
                else if (low is "на") { }
                else toks.Add(new Tok(TokKind.Name, w));
                i = j; continue;
            }
            throw new Exception($"строка {line}: плохой символ '{c}' в '{expr}'");
        }
        return toks;
    }

    static object EvalTake(string expr, int line)
    {
        string t = Regex.Replace(expr.Trim(), @"^взять\b", "", RegexOptions.IgnoreCase).Trim();
        t = Regex.Replace(t, @"^из\s+", "", RegexOptions.IgnoreCase).Trim();
        int sp = t.IndexOfAny(new[] { ' ', '\t' });
        if (sp < 0) throw new Exception($"строка {line}: надо так -> взять из фрукты 1");
        string name = t[..sp].Trim();
        string idxExpr = t[(sp + 1)..].Trim();
        if (!NameRegex.IsMatch(name)) throw new Exception($"строка {line}: плохое имя списка '{name}'");
        if (idxExpr == "") throw new Exception($"строка {line}: надо так -> взять из {name} 1");
        if (!Vars.TryGetValue(name, out var v) || v is not List<object> l)
            throw new Exception($"строка {line}: нет такого списка: {name}. Сначала: создать список {name}");
        int idx = (int)Math.Round(ToNum(EvalArith(idxExpr, line), line));
        if (idx < 1 || idx > l.Count) throw new Exception($"строка {line}: в списке '{name}' всего {l.Count}, а просят {idx} (счет с 1)");
        return l[idx - 1];
    }

    static object EvalLen(string expr, int line)
    {
        string t = Regex.Replace(expr.Trim(), @"^длина\b", "", RegexOptions.IgnoreCase).Trim();
        if (t == "") throw new Exception($"строка {line}: надо так -> длина фрукты");
        object v = EvalArith(t, line);
        if (v is List<object> l) return l.Count;
        if (v is string s) return s.Length;
        throw new Exception($"строка {line}: длина только для списка или текста");
    }

    static readonly Random Rnd = new();
    static readonly System.Diagnostics.Stopwatch ProgTime = System.Diagnostics.Stopwatch.StartNew();

    static object EvalRandom(string expr, int line)
    {
        string t = Regex.Replace(expr.Trim(), @"^случайное\b", "", RegexOptions.IgnoreCase).Trim();
        var m = Regex.Match(t, @"^от\s+(.+?)\s+до\s+(.+)$", RegexOptions.IgnoreCase);
        if (!m.Success) throw new Exception($"строка {line}: надо так -> случайное от 1 до 10");
        double a = ToNum(EvalArith(m.Groups[1].Value.Trim(), line), line);
        double b = ToNum(EvalArith(m.Groups[2].Value.Trim(), line), line);
        if (a > b) (a, b) = (b, a);
        bool ints = Math.Abs(a - Math.Round(a)) < 1e-9 && Math.Abs(b - Math.Round(b)) < 1e-9;
        if (ints) return Rnd.Next((int)a, (int)b + 1);
        return NormNum(a + Rnd.NextDouble() * (b - a));
    }

    static object EvalHas(string expr, int line)
    {
        string t = Regex.Replace(expr.Trim(), @"^есть\b", "", RegexOptions.IgnoreCase).Trim();
        int split = -1;
        bool inQ = false;
        for (int i = 0; i < t.Length; i++)
        {
            if (t[i] == '"') { inQ = !inQ; continue; }
            if (inQ) continue;
            if ((t[i] == 'в' || t[i] == 'В') && (i == 0 || !IsWordChar(t[i - 1])) && (i + 1 >= t.Length || !IsWordChar(t[i + 1])))
                split = i;
        }
        if (split < 0) throw new Exception($"строка {line}: надо так -> есть \"яблоко\" в фрукты");
        string valExpr = t[..split].Trim();
        string name = t[(split + 1)..].Trim();
        if (valExpr == "" || !NameRegex.IsMatch(name))
            throw new Exception($"строка {line}: надо так -> есть \"яблоко\" в фрукты");
        if (!Vars.TryGetValue(name, out var v) || v is not List<object> l)
            throw new Exception($"строка {line}: нет такого списка: {name}");
        object want = EvalFull(valExpr, line);
        foreach (var item in l)
        {
            if (want is int or double or bool && item is int or double or bool)
            {
                if (Math.Abs(ToNum(want, line) - ToNum(item, line)) < 1e-9) return true;
            }
            else if (Fmt(item) == Fmt(want)) return true;
        }
        return false;
    }

    static object EvalMathFn(string expr, int line)
    {
        string t = expr.Trim();
        string fn = FirstWord(t).ToLowerInvariant();
        string arg = AfterFirstWord(t);
        if (arg == "") throw new Exception($"строка {line}: надо так -> {fn} 16");
        double x = ToNum(EvalArith(arg, line), line);
        return fn switch
        {
            "корень" => x < 0 ? throw new Exception($"строка {line}: корень из отрицательного") : NormNum(Math.Sqrt(x)),
            "модуль" => NormNum(Math.Abs(x)),
            "округлить" => (int)Math.Round(x, MidpointRounding.AwayFromZero),
            _ => throw new Exception($"строка {line}: не знаю '{fn}'"),
        };
    }

    static int SplitLastStandalone(string s, string word, int line)
    {
        int found = -1;
        bool inQ = false;
        string low = s.ToLowerInvariant();
        string w = word.ToLowerInvariant();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '"') { inQ = !inQ; continue; }
            if (inQ) continue;
            if (i + w.Length <= low.Length && low.Substring(i, w.Length) == w)
            {
                bool lb = i == 0 || !IsWordChar(s[i - 1]);
                int e = i + w.Length;
                bool rb = e >= s.Length || !IsWordChar(s[e]);
                if (lb && rb) found = i;
            }
        }
        return found;
    }

    static object EvalFind(string expr, int line)
    {
        string t = Regex.Replace(expr.Trim(), @"^найти\b", "", RegexOptions.IgnoreCase).Trim();
        if (SplitFileTarget(t, line, out string what, out string where) < 0 || what == "" || where == "")
            throw new Exception($"строка {line}: надо так -> найти \"мир\" в \"привет мир\"");
        string hs = Fmt(EvalFull(where, line));
        string nd = Fmt(EvalFull(what, line));
        return hs.IndexOf(nd, StringComparison.Ordinal) + 1;
    }

    static object EvalReplace(string expr, int line)
    {
        string t = Regex.Replace(expr.Trim(), @"^заменить\b", "", RegexOptions.IgnoreCase).Trim();
        if (SplitFileTarget(t, line, out string left, out string where) < 0 || left == "" || where == "")
            throw new Exception($"строка {line}: надо так -> заменить \"мир\" на \"друг\" в \"привет мир\"");
        int ni = SplitLastStandalone(left, "на", line);
        if (ni < 0) throw new Exception($"строка {line}: надо так -> заменить \"мир\" на \"друг\" в \"привет мир\"");
        string what = left[..ni].Trim();
        string by = left[(ni + 2)..].Trim();
        if (what == "" || by == "")
            throw new Exception($"строка {line}: надо так -> заменить \"мир\" на \"друг\" в \"привет мир\"");
        return Fmt(EvalFull(where, line)).Replace(Fmt(EvalFull(what, line)), Fmt(EvalFull(by, line)));
    }

    static object EvalSlice(string expr, int line)
    {
        string t = Regex.Replace(expr.Trim(), @"^срез\b", "", RegexOptions.IgnoreCase).Trim();
        int si = SplitLastStandalone(t, "с", line);
        if (si < 0) throw new Exception($"строка {line}: надо так -> срез \"привет\" с 2 по 4");
        string textExpr = t[..si].Trim();
        string rest = t[(si + 1)..].Trim();
        int pi = SplitLastStandalone(rest, "по", line);
        if (pi < 0 || textExpr == "") throw new Exception($"строка {line}: надо так -> срез \"привет\" с 2 по 4");
        string s = Fmt(EvalFull(textExpr, line));
        int a = (int)Math.Round(ToNum(EvalArith(rest[..pi].Trim(), line), line));
        int b = (int)Math.Round(ToNum(EvalArith(rest[(pi + 2)..].Trim(), line), line));
        if (a < 1) a = 1;
        if (b > s.Length) b = s.Length;
        if (a > b) return "";
        return s.Substring(a - 1, b - a + 1);
    }

    static object EvalCase(string expr, int line)
    {
        string t = expr.Trim();
        bool up = StartsWithWord(t, "верхний");
        string arg = AfterFirstWord(t);
        if (arg == "") throw new Exception($"строка {line}: надо так -> верхний \"привет\"");
        string s = Fmt(EvalArith(arg, line));
        return up ? s.ToUpperInvariant() : s.ToLowerInvariant();
    }

    static object EvalArith(string expr, int line)
    {
        expr = expr.Trim();
        if (expr == "") throw new Exception($"строка {line}: пустое выражение");
        if (StartsWithWord(expr, "взять")) return EvalTake(expr, line);
        if (StartsWithWord(expr, "длина")) return EvalLen(expr, line);
        if (StartsWithWord(expr, "есть")) return EvalHas(expr, line);
        if (StartsWithWord(expr, "корень", "модуль", "округлить")) return EvalMathFn(expr, line);
        if (StartsWithWord(expr, "найти")) return EvalFind(expr, line);
        if (StartsWithWord(expr, "заменить")) return EvalReplace(expr, line);
        if (StartsWithWord(expr, "срез")) return EvalSlice(expr, line);
        if (StartsWithWord(expr, "верхний", "нижний")) return EvalCase(expr, line);
        if (StartsWithWord(expr, "случайное")) return EvalRandom(expr, line);
        if (expr.Trim().Equals("время", StringComparison.OrdinalIgnoreCase)) return NormNum(Math.Round(ProgTime.Elapsed.TotalSeconds, 3));
        var q = QuotedFull.Match(expr);
        if (q.Success) return q.Groups[1].Value;

        var toks = Tokenize(expr, line);
        if (toks.Count == 0) throw new Exception($"строка {line}: пустое выражение");
        if (toks.Count == 1)
        {
            var t = toks[0];
            if (t.Kind == TokKind.Num)
            {
                if (int.TryParse(t.Val, out int ii)) return ii;
                if (double.TryParse(t.Val, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double dd)) return NormNum(dd);
                throw new Exception($"строка {line}: плохое число '{t.Val}'");
            }
            if (t.Kind == TokKind.Str) return t.Val;
            if (t.Kind == TokKind.Bool) return t.Val == "true";
            if (t.Kind == TokKind.Name)
            {
                if (!Vars.TryGetValue(t.Val, out var v))
                    throw new Exception($"строка {line}: нет такой переменной: {t.Val}. Текст надо в кавычках: \"{t.Val}\"");
                return v;
            }
            throw new Exception($"строка {line}: не понимаю '{expr}'");
        }
        if (toks.Count == 3 && toks[0].Kind == TokKind.Op && toks[1].Kind != TokKind.Op && toks[2].Kind != TokKind.Op
            && toks[1].Kind != TokKind.LPar && toks[2].Kind != TokKind.LPar)
        {
            object a = SingleTokValue(toks[1], line);
            object b = SingleTokValue(toks[2], line);
            return ApplyOp(toks[0].Val, a, b, line);
        }
        var rpn = ToRpn(toks, line);
        return EvalRpn(rpn, line);
    }

    static object SingleTokValue(Tok t, int line)
    {
        if (t.Kind == TokKind.Num)
        {
            if (int.TryParse(t.Val, out int ii)) return ii;
            return double.Parse(t.Val, System.Globalization.CultureInfo.InvariantCulture);
        }
        if (t.Kind == TokKind.Str) return t.Val;
        if (t.Kind == TokKind.Bool) return t.Val == "true";
        if (t.Kind == TokKind.Name)
        {
            if (!Vars.TryGetValue(t.Val, out var v))
                throw new Exception($"строка {line}: нет такой переменной: {t.Val}");
            return v;
        }
        throw new Exception($"строка {line}: не понимаю '{t.Val}'");
    }

    static int Prec(string op) => op == ".." ? 0 : op == "^" ? 3 : op is "*" or "/" or "%" ? 2 : 1;

    static List<Tok> ToRpn(List<Tok> toks, int line)
    {
        var out_ = new List<Tok>();
        var st = new Stack<Tok>();
        Tok? prev = null;
        foreach (var t in toks)
        {
            if (t.Kind is TokKind.Num or TokKind.Str or TokKind.Name or TokKind.Bool) { out_.Add(t); prev = t; }
            else if (t.Kind == TokKind.LPar) { st.Push(t); prev = t; }
            else if (t.Kind == TokKind.RPar)
            {
                bool found = false;
                while (st.Count > 0)
                {
                    var p = st.Pop();
                    if (p.Kind == TokKind.LPar) { found = true; break; }
                    out_.Add(p);
                }
                if (!found) throw new Exception($"строка {line}: лишняя ')'");
                prev = t;
            }
            else if (t.Kind == TokKind.Op)
            {
                string op = t.Val;
                if (op == "-" && (prev == null || prev.Kind == TokKind.Op || prev.Kind == TokKind.LPar))
                {
                    out_.Add(new Tok(TokKind.Num, "0"));
                }
                while (st.Count > 0 && st.Peek().Kind == TokKind.Op && (Prec(st.Peek().Val) > Prec(op) || (Prec(st.Peek().Val) == Prec(op) && op != ".." && op != "^")))
                    out_.Add(st.Pop());
                st.Push(t); prev = t;
            }
        }
        while (st.Count > 0)
        {
            var p = st.Pop();
            if (p.Kind == TokKind.LPar) throw new Exception($"строка {line}: нет ')'");
            out_.Add(p);
        }
        return out_;
    }

    static object EvalRpn(List<Tok> rpn, int line)
    {
        var st = new Stack<object>();
        foreach (var t in rpn)
        {
            if (t.Kind == TokKind.Op)
            {
                if (st.Count < 2) throw new Exception($"строка {line}: не хватает чисел для '{t.Val}'. Текст с пробелами надо в кавычках");
                var b = st.Pop(); var a = st.Pop();
                st.Push(ApplyOp(t.Val, a, b, line));
            }
            else st.Push(SingleTokValue(t, line));
        }
        if (st.Count != 1) throw new Exception($"строка {line}: два значения подряд без действия. Текст с пробелами надо в кавычках: \"...\"");
        return st.Pop();
    }

    static object ApplyOp(string op, object a, object b, int line)
    {
        if (op == "..") return Fmt(a) + Fmt(b);
        if (a is string || b is string)
        {
            if (op == "+") return Fmt(a) + Fmt(b);
            throw new Exception($"строка {line}: со строками можно только + или ..");
        }
        double x = ToNum(a, line), y = ToNum(b, line);
        return op switch
        {
            "+" => NormNum(x + y),
            "-" => NormNum(x - y),
            "*" => NormNum(x * y),
            "/" => y == 0 ? throw new Exception($"строка {line}: деление на ноль") : NormNum(x / y),
            "%" => y == 0 ? throw new Exception($"строка {line}: деление на ноль") : NormNum(x % y),
            "^" => NormNum(Math.Pow(x, y)),
            _ => throw new Exception($"строка {line}: не знаю действие '{op}'"),
        };
    }

    static readonly string[] CmpWords = { "больше или равно", "меньше или равно", "не равно", "неравно", "больше", "меньше", "равно" };
    static readonly string[] CmpSyms = { ">=", "<=", "!=", "<>", "==", ">", "<", "=" };

    static bool TryFindCmp(string expr, out string op, out string left, out string right)
    {
        op = ""; left = ""; right = "";
        bool inQ = false;
        int depth = 0;
        string low = expr.ToLowerInvariant();
        for (int i = 0; i < expr.Length; i++)
        {
            if (expr[i] == '"') { inQ = !inQ; continue; }
            if (inQ) continue;
            if (expr[i] == '(') { depth++; continue; }
            if (expr[i] == ')') { if (depth > 0) depth--; continue; }
            if (depth != 0) continue;
            foreach (var w in CmpWords)
            {
                if (i + w.Length <= low.Length && low.Substring(i, w.Length) == w)
                {
                    bool lb = i == 0 || !(char.IsLetterOrDigit(low[i - 1]) || low[i - 1] == '_');
                    int e = i + w.Length;
                    bool rb = e >= low.Length || !(char.IsLetterOrDigit(low[e]) || low[e] == '_');
                    if (lb && rb)
                    {
                        op = w; left = expr[..i].Trim(); right = expr[e..].Trim();
                        return true;
                    }
                }
            }
            foreach (var s in CmpSyms)
            {
                if (i + s.Length <= expr.Length && expr.Substring(i, s.Length) == s)
                {
                    op = s; left = expr[..i].Trim(); right = expr[(i + s.Length)..].Trim();
                    return true;
                }
            }
        }
        return false;
    }

    static string NormCmp(string op)
    {
        op = op.ToLowerInvariant().Trim();
        if (op is "больше" or ">") return ">";
        if (op is "меньше" or "<") return "<";
        if (op is "равно" or "=" or "==") return "==";
        if (op is "не равно" or "неравно" or "!=" or "<>") return "!=";
        if (op is "больше или равно" or ">=") return ">=";
        if (op is "меньше или равно" or "<=") return "<=";
        return op;
    }

    static string StripOuterParens(string expr)
    {
        while (true)
        {
            expr = expr.Trim();
            if (expr.Length < 2 || expr[0] != '(') return expr;
            bool inQ = false;
            int depth = 0;
            bool closesAtEnd = false;
            for (int i = 0; i < expr.Length; i++)
            {
                if (expr[i] == '"') { inQ = !inQ; continue; }
                if (inQ) continue;
                if (expr[i] == '(') depth++;
                else if (expr[i] == ')')
                {
                    depth--;
                    if (depth == 0) { closesAtEnd = (i == expr.Length - 1); break; }
                }
            }
            if (!closesAtEnd) return expr;
            expr = expr[1..^1];
        }
    }

    static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    static bool TrySplitLogic(string expr, out string op, out string left, out string right)
    {
        op = ""; left = ""; right = "";
        bool inQ = false;
        int depth = 0;
        string low = expr.ToLowerInvariant();
        for (int i = 0; i < expr.Length; i++)
        {
            if (expr[i] == '"') { inQ = !inQ; continue; }
            if (inQ) continue;
            if (expr[i] == '(') { depth++; continue; }
            if (expr[i] == ')') { if (depth > 0) depth--; continue; }
            if (depth != 0) continue;
            if (i + 3 <= low.Length && low.Substring(i, 3) == "или")
            {
                bool lb = i == 0 || !IsWordChar(low[i - 1]);
                int e = i + 3;
                bool rb = e >= low.Length || !IsWordChar(low[e]);
                if (lb && rb)
                {
                    string l = expr[..i].Trim(), r = expr[e..].Trim();
                    if (l != "" && r != "") { op = "или"; left = l; right = r; return true; }
                }
            }
            if (i + 2 <= expr.Length && expr.Substring(i, 2) == "||")
            {
                string l = expr[..i].Trim(), r = expr[(i + 2)..].Trim();
                if (l != "" && r != "") { op = "или"; left = l; right = r; return true; }
            }
        }
        inQ = false; depth = 0;
        for (int i = 0; i < expr.Length; i++)
        {
            if (expr[i] == '"') { inQ = !inQ; continue; }
            if (inQ) continue;
            if (expr[i] == '(') { depth++; continue; }
            if (expr[i] == ')') { if (depth > 0) depth--; continue; }
            if (depth != 0) continue;
            if (low[i] == 'и')
            {
                bool lb = i == 0 || !IsWordChar(low[i - 1]);
                bool rb = i + 1 >= low.Length || !IsWordChar(low[i + 1]);
                if (lb && rb)
                {
                    string l = expr[..i].Trim(), r = expr[(i + 1)..].Trim();
                    if (l != "" && r != "") { op = "и"; left = l; right = r; return true; }
                }
            }
            if (i + 2 <= expr.Length && expr.Substring(i, 2) == "&&")
            {
                string l = expr[..i].Trim(), r = expr[(i + 2)..].Trim();
                if (l != "" && r != "") { op = "и"; left = l; right = r; return true; }
            }
        }
        return false;
    }

    static bool TryStripNot(string expr, out string rest)
    {
        rest = "";
        string t = expr.Trim();
        string low = t.ToLowerInvariant();
        if (low.StartsWith("не") && (t.Length == 2 || !IsWordChar(t[2])))
        {
            string r = t[2..].Trim();
            if (r != "") { rest = r; return true; }
            return false;
        }
        if (t.StartsWith("!"))
        {
            string r = t[1..].Trim();
            if (r != "") { rest = r; return true; }
            return false;
        }
        return false;
    }

    static bool EvalCondition(string expr, int line)
    {
        expr = StripOuterParens(expr.Trim());
        if (expr == "") throw new Exception($"строка {line}: пустое условие");
        if (TrySplitLogic(expr, out string lop, out string ll, out string lr))
        {
            if (lop == "или") return EvalCondition(ll, line) || EvalCondition(lr, line);
            return EvalCondition(ll, line) && EvalCondition(lr, line);
        }
        if (TryStripNot(expr, out string nr)) return !EvalCondition(nr, line);
        if (TryFindCmp(expr, out string op, out string l, out string r))
        {
            if (l == "" || r == "") throw new Exception($"строка {line}: сравнение без краев: '{expr}'");
            object a = EvalArith(l, line), b = EvalArith(r, line);
            string n = NormCmp(op);
            bool aNum = a is int or double or bool, bNum = b is int or double or bool;
            if (aNum && bNum)
            {
                double x = ToNum(a, line), y = ToNum(b, line);
                return n switch
                {
                    ">" => x > y, "<" => x < y, "==" => Math.Abs(x - y) < 1e-9,
                    "!=" => Math.Abs(x - y) >= 1e-9, ">=" => x >= y - 1e-9, "<=" => x <= y + 1e-9,
                    _ => false
                };
            }
            string sa = Fmt(a), sb = Fmt(b);
            int c = string.Compare(sa, sb, StringComparison.Ordinal);
            return n switch
            {
                "==" => sa == sb, "!=" => sa != sb, ">" => c > 0, "<" => c < 0,
                ">=" => c >= 0, "<=" => c <= 0, _ => false
            };
        }
        return IsTrue(EvalArith(expr, line));
    }

    static object EvalFull(string expr, int line)
    {
        expr = StripOuterParens(expr.Trim());
        if (TryStripNot(expr, out _)) return EvalCondition(expr, line);
        if (TrySplitLogic(expr, out _, out _, out _)) return EvalCondition(expr, line);
        if (TryFindCmp(expr, out _, out _, out _)) return EvalCondition(expr, line);
        return EvalArith(expr, line);
    }

    static string ExtractWhileCond(string text, int line)
    {
        string t = Regex.Replace(text.Trim(), @"^пока\b", "", RegexOptions.IgnoreCase).Trim();
        t = StripTrailingWord(t, "то", "тогда", "делать");
        if (t == "") throw new Exception($"строка {line}: после 'пока' надо условие. Пример: пока счет < 10");
        return t;
    }
    static int EvalRepeatCount(string text, int line)
    {
        string t = Regex.Replace(text.Trim(), @"^повтори(ть|ять)?\b", "", RegexOptions.IgnoreCase).Trim();
        t = Regex.Replace(t, @"[\s\t]+раз(а|ов)?[\s\t]*$", "", RegexOptions.IgnoreCase).Trim();
        if (t == "") throw new Exception($"строка {line}: надо так -> повтори 5 раз");
        object v = EvalArith(t, line);
        double d = ToNum(v, line);
        int n = (int)Math.Round(d);
        if (n < 0) throw new Exception($"строка {line}: повторить можно 0 и больше, а там {n}");
        return n;
    }
    static (string name, double from, double to, double step) ParseFor(string text, int line)
    {
        var m = Regex.Match(text.Trim(), @"^для\s+(\S+)\s+от\s+(.+?)\s+до\s+(.+?)(\s+шаг\s+(.+?))?(\s+то)?\s*$", RegexOptions.IgnoreCase);
        if (!m.Success) throw new Exception($"строка {line}: надо так -> для и от 1 до 10 то");
        string name = m.Groups[1].Value.Trim();
        if (!NameRegex.IsMatch(name)) throw new Exception($"строка {line}: плохое имя '{name}'");
        double a = ToNum(EvalArith(m.Groups[2].Value.Trim(), line), line);
        double b = ToNum(EvalArith(m.Groups[3].Value.Trim(), line), line);
        double s = m.Groups[5].Success ? ToNum(EvalArith(m.Groups[5].Value.Trim(), line), line) : (a <= b ? 1 : -1);
        if (Math.Abs(s) < 1e-12) throw new Exception($"строка {line}: шаг не может быть 0");
        return (name, a, b, s);
    }
    static void ExecAsk(string text, int line)
    {
        string rest = AfterFirstWord(text);
        int split = -1;
        bool inQ = false;
        for (int i = 0; i < rest.Length; i++)
        {
            if (rest[i] == '"') { inQ = !inQ; continue; }
            if (inQ) continue;
            if ((rest[i] == 'в' || rest[i] == 'В') && (i == 0 || !IsWordChar(rest[i - 1])) && (i + 1 >= rest.Length || !IsWordChar(rest[i + 1])))
                split = i;
        }
        if (split < 0) throw new Exception($"строка {line}: надо так -> спросить \"как зовут?\" в имя");
        string qExpr = rest[..split].Trim();
        string name = rest[(split + 1)..].Trim();
        if (qExpr == "" || !NameRegex.IsMatch(name))
            throw new Exception($"строка {line}: надо так -> спросить \"как зовут?\" в имя");
        object q = EvalFull(qExpr, line);
        Console.Write(Fmt(q));
        if (!Fmt(q).EndsWith(" ") && !Fmt(q).EndsWith("\n")) Console.Write(" ");
        string ans = Console.ReadLine() ?? "";
        Vars[name] = StoreValue(ans);
    }
    static readonly Dictionary<string, double> WaitUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        {"секунду", 1000}, {"секунды", 1000}, {"секунд", 1000}, {"сек", 1000},
        {"минуту", 60000}, {"минуты", 60000}, {"минут", 60000}, {"мин", 60000},
        {"час", 3600000}, {"часа", 3600000}, {"часов", 3600000},
    };

    static void ExecWait(string text, int line)
    {
        string rest = AfterFirstWord(text).Trim();
        if (rest == "") throw new Exception($"строка {line}: надо так -> ждать 1 секунду");
        double mult = 1000;
        string numPart = rest;
        var m = Regex.Match(rest, @"^(.*?)[\s\t]+(\S+)\s*$");
        if (m.Success && WaitUnits.TryGetValue(m.Groups[2].Value, out double mm))
        {
            mult = mm;
            numPart = m.Groups[1].Value.Trim();
            if (numPart == "") throw new Exception($"строка {line}: надо так -> ждать 1 секунду");
        }
        double v = ToNum(EvalArith(numPart, line), line);
        if (v < 0) throw new Exception($"строка {line}: ждать можно 0 и больше");
        Thread.Sleep((int)Math.Round(v * mult));
    }
    static object StoreValue(string ans)
    {
        ans = ans.Trim().Trim((char)0xFEFF, (char)0x200B).Trim();
        if (int.TryParse(ans, out int ii)) return ii;
        if (double.TryParse(ans.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double dd)) return NormNum(dd);
        return ans;
    }

    static int SplitFileTarget(string rest, int line, out string first, out string second)
    {
        int split = -1;
        bool inQ = false;
        for (int i = 0; i < rest.Length; i++)
        {
            if (rest[i] == '"') { inQ = !inQ; continue; }
            if (inQ) continue;
            if ((rest[i] == 'в' || rest[i] == 'В') && (i == 0 || !IsWordChar(rest[i - 1])) && (i + 1 >= rest.Length || !IsWordChar(rest[i + 1])))
                split = i;
        }
        first = ""; second = "";
        if (split < 0) return -1;
        first = rest[..split].Trim();
        second = rest[(split + 1)..].Trim();
        return split;
    }

    static void ExecWriteFile(string text, int line)
    {
        string rest = AfterFirstWord(text);
        if (SplitFileTarget(rest, line, out string valExpr, out string after) < 0)
            throw new Exception($"строка {line}: надо так -> записать \"привет\" в файл \"сейв.txt\"");
        string afterLow = after.ToLowerInvariant();
        if (!afterLow.StartsWith("файл") || (after.Length > 4 && IsWordChar(after[4])))
            throw new Exception($"строка {line}: надо так -> записать \"привет\" в файл \"сейв.txt\"");
        string pathExpr = after[4..].Trim();
        if (valExpr == "" || pathExpr == "")
            throw new Exception($"строка {line}: надо так -> записать \"привет\" в файл \"сейв.txt\"");
        object v = EvalFull(valExpr, line);
        string path = Fmt(EvalFull(pathExpr, line));
        if (System.IO.Path.GetExtension(path) == "") path += ".txt";
        try { File.WriteAllText(path, Fmt(v), new System.Text.UTF8Encoding(false)); }
        catch (Exception ex) { throw new Exception($"строка {line}: не записать '{path}': {ex.Message}"); }
    }

    static void ExecReadFile(string text, int line)
    {
        string rest = AfterFirstWord(text);
        string low = rest.ToLowerInvariant();
        if (!low.StartsWith("файл") || (rest.Length > 4 && IsWordChar(rest[4])))
            throw new Exception($"строка {line}: надо так -> прочитать файл \"сейв.txt\" в данные");
        string after = rest[4..].Trim();
        if (SplitFileTarget(after, line, out string pathExpr, out string name) < 0)
            throw new Exception($"строка {line}: надо так -> прочитать файл \"сейв.txt\" в данные");
        if (pathExpr == "" || !NameRegex.IsMatch(name))
            throw new Exception($"строка {line}: надо так -> прочитать файл \"сейв.txt\" в данные");
        string path = Fmt(EvalFull(pathExpr, line));
        if (System.IO.Path.GetExtension(path) == "") path += ".txt";
        string content;
        try { content = File.ReadAllText(path, System.Text.Encoding.UTF8); }
        catch (Exception ex) { throw new Exception($"строка {line}: не прочитать '{path}': {ex.Message}"); }
        Vars[name] = StoreValue(content);
    }

    static void ExecDelete(string text, int line)
    {
        string rest = AfterFirstWord(text);
        rest = Regex.Replace(rest, @"^из\s+", "", RegexOptions.IgnoreCase).Trim();
        int sp = rest.IndexOfAny(new[] { ' ', '\t' });
        if (sp < 0) throw new Exception($"строка {line}: надо так -> удалить из фрукты 1");
        string name = rest[..sp].Trim();
        string idxExpr = rest[(sp + 1)..].Trim();
        if (!NameRegex.IsMatch(name) || idxExpr == "")
            throw new Exception($"строка {line}: надо так -> удалить из {name} 1");
        if (!Vars.TryGetValue(name, out var v) || v is not List<object> l)
            throw new Exception($"строка {line}: нет такого списка: {name}");
        int idx = (int)Math.Round(ToNum(EvalArith(idxExpr, line), line));
        if (idx < 1 || idx > l.Count) throw new Exception($"строка {line}: в списке '{name}' всего {l.Count}, а просят {idx} (счет с 1)");
        l.RemoveAt(idx - 1);
    }

    static void ExecClear(string text, int line)
    {
        string name = AfterFirstWord(text).Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (!NameRegex.IsMatch(name))
            throw new Exception($"строка {line}: надо так -> очистить фрукты");
        if (!Vars.TryGetValue(name, out var v) || v is not List<object> l)
            throw new Exception($"строка {line}: нет такого списка: {name}");
        l.Clear();
    }
    static string ExtractEventDef(string text)
    {
        string t = Regex.Replace(text.Trim(), @"^когда\b", "", RegexOptions.IgnoreCase).Trim();
        t = Regex.Replace(t, @"^будет\s+", "", RegexOptions.IgnoreCase).Trim();
        t = Regex.Replace(t, @"^получено\s+", "", RegexOptions.IgnoreCase).Trim();
        t = t.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return t.ToLowerInvariant();
    }
    static string ExtractBroadcast(string text, int line)
    {
        string t = Regex.Replace(text.Trim(), @"^вещать\b", "", RegexOptions.IgnoreCase).Trim();
        t = Regex.Replace(t, @"^всем\s+", "", RegexOptions.IgnoreCase).Trim();
        string ev = t.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (ev == "") throw new Exception($"строка {line}: надо так -> вещать всем привет");
        return ev.ToLowerInvariant();
    }

    static (int end, int elseIdx) FindBlock(List<Line> code, int start) => FindBlockBounded(code, start, code.Count);

    static (int end, int elseIdx) FindBlockBounded(List<Line> code, int start, int bound)
    {
        int depth = 0, elseIdx = -1;
        bool elsePlain = false;
        for (int j = start + 1; j < bound; j++)
        {
            string t = code[j].Text;
            if (IsBlockStart(t)) depth++;
            else if (IsEnd(t))
            {
                if (depth == 0) return (j, elseIdx);
                depth--;
            }
            else if (IsElse(t) && depth == 0 && (IsIf(code[start].Text) || IsElseIf(code[start].Text)))
            {
                if (elseIdx < 0) { elseIdx = j; elsePlain = !IsElseIf(t); }
                else if (!IsElseIf(t) && elsePlain) throw new Exception($"строка {code[j].No}: два 'иначе' в одном 'если'");
            }
        }
        throw new Exception($"строка {code[start].No}: нет 'конец' для '{code[start].Text}'");
    }

    static string CondFromIfLine(string text, int line)
    {
        string t = Regex.Replace(text.Trim(), @"^иначе\b", "", RegexOptions.IgnoreCase).Trim();
        t = Regex.Replace(t, @"^(если|эсли)\b", "", RegexOptions.IgnoreCase).Trim();
        t = StripTrailingWord(t, "то", "тогда");
        if (t == "") throw new Exception($"строка {line}: после 'если' надо условие. Пример: если возраст > 10 то");
        return t;
    }

    static int ExecIfAt(List<Line> code, int idx, int bound)
    {
        var (end, el) = FindBlockBounded(code, idx, bound);
        int no = code[idx].No;
        if (EvalCondition(CondFromIfLine(code[idx].Text, no), no)) ExecRange(code, idx + 1, el >= 0 ? el : end);
        else if (el >= 0)
        {
            if (IsElseIf(code[el].Text)) ExecIfAt(code, el, end + 1);
            else ExecRange(code, el + 1, end);
        }
        return end + 1;
    }

    static string CondFromTriggerLine(string text, int line)
    {
        string t = Regex.Replace(text.Trim(), @"^как\s+только\b", "", RegexOptions.IgnoreCase).Trim();
        t = StripTrailingWord(t, "то", "тогда");
        if (t == "") throw new Exception($"строка {line}: надо так -> как только очки равно 10 то");
        return t;
    }

    static void CollectStarts(List<Line> code)
    {
        for (int i = 0; i < code.Count; i++)
        {
            if (IsOnStart(code[i].Text))
            {
                var (end, _) = FindBlock(code, i);
                OnStarts.Add(code.GetRange(i + 1, end - i - 1));
                i = end;
            }
        }
    }

    static void CollectTriggers(List<Line> code)
    {
        for (int i = 0; i < code.Count; i++)
        {
            if (IsTrigger(code[i].Text))
            {
                string c = CondFromTriggerLine(code[i].Text, code[i].No);
                var (end, _) = FindBlock(code, i);
                Triggers.Add(new Trigger { Cond = c, Body = code.GetRange(i + 1, end - i - 1), No = code[i].No });
                i = end;
            }
        }
    }

    static void CheckTriggers(int line)
    {
        if (Triggers.Count == 0) return;
        if (++TriggerDepth > 20) { TriggerDepth--; throw new Exception($"строка {line}: триггеры зациклились"); }
        try
        {
            foreach (var tr in Triggers)
            {
                bool now;
                try { now = EvalCondition(tr.Cond, line); }
                catch { continue; }
                if (!tr.Last && now)
                {
                    tr.Last = true;
                    ExecRange(tr.Body, 0, tr.Body.Count);
                }
                else tr.Last = now;
            }
        }
        finally { TriggerDepth--; }
    }
    static void CollectHandlers(List<Line> code)
    {
        for (int i = 0; i < code.Count; i++)
        {
            if (IsWhen(code[i].Text))
            {
                string ev = ExtractEventDef(code[i].Text);
                if (ev == "") throw new Exception($"строка {code[i].No}: надо так -> когда получено привет");
                var (end, _) = FindBlock(code, i);
                var body = code.GetRange(i + 1, end - i - 1);
                if (!Handlers.TryGetValue(ev, out var list)) Handlers[ev] = list = new();
                list.Add(body);
                i = end;
            }
        }
    }

    static void Broadcast(string ev, int line)
    {
        if (!Handlers.TryGetValue(ev, out var list)) return;
        if (++BroadcastDepth > 20) { BroadcastDepth--; throw new Exception($"строка {line}: вещание зациклилось ('{ev}')"); }
        foreach (var body in list)
            ExecRange(body, 0, body.Count);
        BroadcastDepth--;
    }

    static void ExecRange(List<Line> code, int from, int to)
    {
        int i = from;
        while (i < to)
        {
            CheckTriggers(code[i].No);
            string text = code[i].Text;
            int no = code[i].No;
            if (IsWhen(text) || IsOnStart(text) || IsTrigger(text)) { var (e, _) = FindBlock(code, i); i = e + 1; continue; }
            else if (IsIf(text)) { i = ExecIfAt(code, i, to); continue; }
            else if (IsWhile(text))
            {
                var (e, _) = FindBlock(code, i);
                string c = ExtractWhileCond(text, no);
                int guard = 0;
                while (EvalCondition(c, no))
                {
                    try { ExecRange(code, i + 1, e); } catch (BreakException) { break; } catch (ContinueException) { }
                    if (++guard > 1_000_000) throw new Exception($"строка {no}: 'пока' крутится слишком долго. Добавь 'остановить'");
                }
                i = e + 1; continue;
            }
            else if (IsForever(text))
            {
                var (e, _) = FindBlock(code, i);
                int guard = 0;
                while (true)
                {
                    try { ExecRange(code, i + 1, e); } catch (BreakException) { break; } catch (ContinueException) { }
                    if (++guard > 10_000_000) throw new Exception($"строка {no}: 'вечно повторять' без 'остановить'");
                }
                i = e + 1; continue;
            }
            else if (IsRepeat(text))
            {
                var (e, _) = FindBlock(code, i);
                int n = EvalRepeatCount(text, no);
                for (int k = 0; k < n; k++)
                { try { ExecRange(code, i + 1, e); } catch (BreakException) { break; } catch (ContinueException) { } }
                i = e + 1; continue;
            }
            else if (IsFor(text))
            {
                var (e, _) = FindBlock(code, i);
                var (name, a, b, s) = ParseFor(text, no);
                if (s > 0) { for (double v = a; v <= b + 1e-9; v += s) { Vars[name] = NormNum(v); try { ExecRange(code, i + 1, e); } catch (BreakException) { break; } catch (ContinueException) { } } }
                else { for (double v = a; v >= b - 1e-9; v += s) { Vars[name] = NormNum(v); try { ExecRange(code, i + 1, e); } catch (BreakException) { break; } catch (ContinueException) { } } }
                i = e + 1; continue;
            }
            else if (IsElse(text)) throw new Exception($"строка {no}: 'иначе' без 'если'");
            else if (IsEnd(text)) throw new Exception($"строка {no}: лишний 'конец'");
            else if (IsBreak(text)) throw new BreakException();
            else if (IsBroadcast(text)) { Broadcast(ExtractBroadcast(text, no), no); i++; continue; }
            else if (IsSet(text))
            {
                string rest = AfterFirstWord(text);
                int sp = rest.IndexOfAny(new[] { ' ', '\t' });
                if (sp < 0) throw new Exception($"строка {no}: надо так -> задать иван 5");
                string name = rest[..sp].Trim();
                string val = rest[(sp + 1)..].Trim();
                if (!NameRegex.IsMatch(name)) throw new Exception($"строка {no}: плохое имя '{name}'");
                if (val == "") throw new Exception($"строка {no}: нет значения");
                Vars[name] = EvalFull(val, no);
                i++; continue;
            }
            else if (IsPrint(text))
            {
                string rest = AfterFirstWord(text);
                if (rest == "") throw new Exception($"строка {no}: что вывести? пример: вывести иван");
                try { Console.WriteLine(Fmt(EvalFull(rest, no))); }
                catch (Exception ex) when (!ex.Message.StartsWith("строка"))
                { throw new Exception($"строка {no}: {ex.Message}"); }
                i++; continue;
            }
            else if (IsCreateList(text))
            {
                string rest = AfterFirstWord(text);
                var parts = rest.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2 || !parts[0].Equals("список", StringComparison.OrdinalIgnoreCase))
                    throw new Exception($"строка {no}: надо так -> создать список фрукты");
                if (!NameRegex.IsMatch(parts[1])) throw new Exception($"строка {no}: плохое имя '{parts[1]}'");
                Vars[parts[1]] = new List<object>();
                i++; continue;
            }
            else if (IsAdd(text))
            {
                string rest = AfterFirstWord(text);
                rest = Regex.Replace(rest, @"^в\s+", "", RegexOptions.IgnoreCase).Trim();
                int sp = rest.IndexOfAny(new[] { ' ', '\t' });
                if (sp < 0) throw new Exception($"строка {no}: надо так -> добавить в фрукты \"вишня\"");
                string name = rest[..sp].Trim();
                string valExpr = rest[(sp + 1)..].Trim();
                if (valExpr == "") throw new Exception($"строка {no}: надо так -> добавить в {name} \"вишня\"");
                if (!Vars.TryGetValue(name, out var v) || v is not List<object> l)
                    throw new Exception($"строка {no}: нет такого списка: {name}. Сначала: создать список {name}");
                l.Add(EvalFull(valExpr, no));
                i++; continue;
            }
            else if (IsAsk(text)) { ExecAsk(text, no); i++; continue; }
            else if (IsWait(text)) { ExecWait(text, no); i++; continue; }
            else if (IsDelete(text)) { ExecDelete(text, no); i++; continue; }
            else if (IsClear(text)) { ExecClear(text, no); i++; continue; }
            else if (IsWriteFile(text)) { ExecWriteFile(text, no); i++; continue; }
            else if (IsReadFile(text)) { ExecReadFile(text, no); i++; continue; }
            else if (IsContinue(text)) throw new ContinueException();
            else if (IsExit(text)) throw new ExitException();
            else throw new Exception($"строка {no}: не знаю команду '{text}'. Знаю: задать, вывести, спросить, ждать, если, иначе если, пока, повтори, для, продолжить, остановить, выход, когда, вещать, создать список, добавить, удалить, очистить, записать, прочитать, подключить, при запуске, как только");
        }
    }

    static List<Line> LoadWithIncludes(string path, HashSet<string> visited)
    {
        string full;
        try { full = System.IO.Path.GetFullPath(path); }
        catch { throw new Exception($"плохое имя файла: '{path}'"); }
        if (!visited.Add(full)) throw new Exception($"круг: файл '{path}' уже подключен");
        string[] raw;
        try { raw = File.ReadAllLines(full, System.Text.Encoding.UTF8); }
        catch (Exception ex) { throw new Exception($"не открыть '{path}': {ex.Message}"); }
        string dir = System.IO.Path.GetDirectoryName(full) ?? "";
        var code = new List<Line>();
        for (int i = 0; i < raw.Length; i++)
        {
            string t = raw[i].Trim();
            if (t == "" || t.StartsWith("#") || t.StartsWith("//")) continue;
            if (IsInclude(t))
            {
                string arg = AfterFirstWord(t).Trim();
                if (arg.StartsWith("\"") && arg.EndsWith("\"") && arg.Length >= 2) arg = arg[1..^1];
                if (arg == "") throw new Exception($"строка {i + 1}: надо так -> подключить \"библио.ncode\"");
                code.AddRange(LoadWithIncludes(System.IO.Path.Combine(dir, arg), visited));
                continue;
            }
            code.Add(new Line(t, i + 1));
        }
        return code;
    }

    static void RunFile(string path)
    {
        Vars.Clear(); Handlers.Clear(); OnStarts.Clear(); Triggers.Clear(); BroadcastDepth = 0; TriggerDepth = 0;
        var code = LoadWithIncludes(path, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        CollectHandlers(code);
        CollectStarts(code);
        CollectTriggers(code);
        foreach (var body in OnStarts)
            ExecRange(body, 0, body.Count);
        ExecRange(code, 0, code.Count);
        if (code.Count > 0) CheckTriggers(code[^1].No);
    }

    static int Main(string[] args)
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;
        string path;
        if (args.Length > 0) path = args[0];
        else if (File.Exists("test.ncode")) path = "test.ncode";
        else if (File.Exists("../test.ncode")) path = "../test.ncode";
        else path = "test.ncode";
        try { RunFile(path); return 0; }
        catch (BreakException) { Console.WriteLine("Ошибка: 'остановить' без цикла"); return 1; }
        catch (ContinueException) { Console.WriteLine("Ошибка: 'продолжить' без цикла"); return 1; }
        catch (ExitException) { return 0; }
        catch (Exception ex) { Console.WriteLine("Ошибка: " + ex.Message); return 1; }
    }
}
