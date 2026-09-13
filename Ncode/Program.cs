using System.Text;
using System.Text.RegularExpressions;

class BreakException : Exception { }

record Line(string Text, int No);

class Program
{
    static Dictionary<string, object> Vars = new();
    static Dictionary<string, List<List<Line>>> Handlers = new(StringComparer.OrdinalIgnoreCase);
    static int BroadcastDepth = 0;

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
    static bool IsElse(string t) => StartsWithWord(t, "иначе");
    static bool IsEnd(string t) => StartsWithWord(t, "конец");
    static bool IsBreak(string t) => StartsWithWord(t, "остановить", "останови", "прервать", "прерви", "выйти", "выход", "стоп");
    static bool IsBroadcast(string t) => StartsWithWord(t, "вещать");
    static bool IsSet(string t) => StartsWithWord(t, "задать", "задай");
    static bool IsPrint(string t) => StartsWithWord(t, "вывести", "выведи", "напечатать", "напечатай", "печатать", "печатай", "показать", "покажи");
    static bool IsBlockStart(string t) => IsIf(t) || IsWhile(t) || IsRepeat(t) || IsFor(t) || IsForever(t) || IsWhen(t);

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
        return v.ToString() ?? "";
    }
    static bool IsTrue(object v)
    {
        if (v is bool b) return b;
        if (v is int i) return i != 0;
        if (v is double d) return Math.Abs(d) > 1e-9;
        if (v is string s) return s.Length > 0;
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
            if (char.IsDigit(c) || (c == '.' && i + 1 < expr.Length && char.IsDigit(expr[i + 1])))
            {
                int j = i;
                while (j < expr.Length && (char.IsDigit(expr[j]) || expr[j] == '.' || expr[j] == ',')) j++;
                toks.Add(new Tok(TokKind.Num, expr[i..j].Replace(',', '.')));
                i = j; continue;
            }
            if (c == '(') { toks.Add(new Tok(TokKind.LPar, "(")); i++; continue; }
            if (c == ')') { toks.Add(new Tok(TokKind.RPar, ")")); i++; continue; }
            if ("+-*/%".Contains(c)) { toks.Add(new Tok(TokKind.Op, c.ToString())); i++; continue; }
            if (char.IsLetter(c) || c == '_' || c >= 0x400 && c <= 0x4FF)
            {
                int j = i;
                while (j < expr.Length && (char.IsLetterOrDigit(expr[j]) || expr[j] == '_')) j++;
                string w = expr[i..j];
                string low = w.ToLowerInvariant();
                if (low is "истина" or "истинно" or "правда" or "true") toks.Add(new Tok(TokKind.Bool, "true"));
                else if (low is "ложь" or "ложно" or "неправда" or "false") toks.Add(new Tok(TokKind.Bool, "false"));
                else if (WordOpToSym.TryGetValue(low, out var sym)) toks.Add(new Tok(TokKind.Op, sym));
                else if (low is "на") { }
                else toks.Add(new Tok(TokKind.Name, w));
                i = j; continue;
            }
            throw new Exception($"строка {line}: плохой символ '{c}' в '{expr}'");
        }
        return toks;
    }

    static object EvalArith(string expr, int line)
    {
        expr = expr.Trim();
        if (expr == "") throw new Exception($"строка {line}: пустое выражение");
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

    static int Prec(string op) => op is "*" or "/" or "%" ? 2 : 1;

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
                while (st.Count > 0 && st.Peek().Kind == TokKind.Op && Prec(st.Peek().Val) >= Prec(op))
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
        if (a is string || b is string)
        {
            if (op == "+") return Fmt(a) + Fmt(b);
            throw new Exception($"строка {line}: со строками можно только +");
        }
        double x = ToNum(a, line), y = ToNum(b, line);
        return op switch
        {
            "+" => NormNum(x + y),
            "-" => NormNum(x - y),
            "*" => NormNum(x * y),
            "/" => y == 0 ? throw new Exception($"строка {line}: деление на ноль") : NormNum(x / y),
            "%" => y == 0 ? throw new Exception($"строка {line}: деление на ноль") : NormNum(x % y),
            _ => throw new Exception($"строка {line}: не знаю действие '{op}'"),
        };
    }

    static readonly string[] CmpWords = { "больше или равно", "меньше или равно", "не равно", "неравно", "больше", "меньше", "равно" };
    static readonly string[] CmpSyms = { ">=", "<=", "!=", "<>", "==", ">", "<", "=" };

    static bool TryFindCmp(string expr, out string op, out string left, out string right)
    {
        op = ""; left = ""; right = "";
        bool inQ = false;
        string low = expr.ToLowerInvariant();
        for (int i = 0; i < expr.Length; i++)
        {
            if (expr[i] == '"') { inQ = !inQ; continue; }
            if (inQ) continue;
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

    static bool EvalCondition(string expr, int line)
    {
        expr = expr.Trim();
        if (expr == "") throw new Exception($"строка {line}: пустое условие");
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
        expr = expr.Trim();
        if (TryFindCmp(expr, out _, out _, out _)) return EvalCondition(expr, line);
        return EvalArith(expr, line);
    }

    static string ExtractIfCond(string text, int line)
    {
        string t = Regex.Replace(text.Trim(), @"^(если|эсли)\b", "", RegexOptions.IgnoreCase).Trim();
        t = StripTrailingWord(t, "то", "тогда");
        if (t == "") throw new Exception($"строка {line}: после 'если' надо условие. Пример: если возраст > 10 то");
        return t;
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

    static (int end, int elseIdx) FindBlock(List<Line> code, int start)
    {
        int depth = 0, elseIdx = -1;
        for (int j = start + 1; j < code.Count; j++)
        {
            string t = code[j].Text;
            if (IsBlockStart(t)) depth++;
            else if (IsEnd(t))
            {
                if (depth == 0) return (j, elseIdx);
                depth--;
            }
            else if (IsElse(t) && depth == 0 && IsIf(code[start].Text))
            {
                if (elseIdx >= 0) throw new Exception($"строка {code[j].No}: два 'иначе' в одном 'если'");
                elseIdx = j;
            }
        }
        throw new Exception($"строка {code[start].No}: нет 'конец' для '{code[start].Text}'");
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
            string text = code[i].Text;
            int no = code[i].No;
            if (IsWhen(text)) { var (e, _) = FindBlock(code, i); i = e + 1; continue; }
            else if (IsIf(text))
            {
                var (e, el) = FindBlock(code, i);
                bool c = EvalCondition(ExtractIfCond(text, no), no);
                if (c) ExecRange(code, i + 1, el >= 0 ? el : e);
                else if (el >= 0) ExecRange(code, el + 1, e);
                i = e + 1; continue;
            }
            else if (IsWhile(text))
            {
                var (e, _) = FindBlock(code, i);
                string c = ExtractWhileCond(text, no);
                int guard = 0;
                while (EvalCondition(c, no))
                {
                    try { ExecRange(code, i + 1, e); } catch (BreakException) { break; }
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
                    try { ExecRange(code, i + 1, e); } catch (BreakException) { break; }
                    if (++guard > 10_000_000) throw new Exception($"строка {no}: 'вечно повторять' без 'остановить'");
                }
                i = e + 1; continue;
            }
            else if (IsRepeat(text))
            {
                var (e, _) = FindBlock(code, i);
                int n = EvalRepeatCount(text, no);
                for (int k = 0; k < n; k++)
                { try { ExecRange(code, i + 1, e); } catch (BreakException) { break; } }
                i = e + 1; continue;
            }
            else if (IsFor(text))
            {
                var (e, _) = FindBlock(code, i);
                var (name, a, b, s) = ParseFor(text, no);
                if (s > 0) { for (double v = a; v <= b + 1e-9; v += s) { Vars[name] = NormNum(v); try { ExecRange(code, i + 1, e); } catch (BreakException) { break; } } }
                else { for (double v = a; v >= b - 1e-9; v += s) { Vars[name] = NormNum(v); try { ExecRange(code, i + 1, e); } catch (BreakException) { break; } } }
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
            else throw new Exception($"строка {no}: не знаю команду '{text}'. Знаю: задать, вывести, если, пока, повтори, для, вечно повторять, когда, вещать");
        }
    }

    static void RunFile(string path)
    {
        Vars.Clear(); Handlers.Clear(); BroadcastDepth = 0;
        var raw = File.ReadAllLines(path, Encoding.UTF8);
        var code = new List<Line>();
        for (int i = 0; i < raw.Length; i++)
        {
            string t = raw[i].Trim();
            if (t == "" || t.StartsWith("#") || t.StartsWith("//")) continue;
            code.Add(new Line(t, i + 1));
        }
        CollectHandlers(code);
        ExecRange(code, 0, code.Count);
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
        catch (Exception ex) { Console.WriteLine("Ошибка: " + ex.Message); return 1; }
    }
}
