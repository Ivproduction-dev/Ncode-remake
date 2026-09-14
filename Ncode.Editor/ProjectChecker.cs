using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Ncode.Editor;

public static class ProjectChecker
{
    static readonly string[] BlockStarters = { "если", "эсли", "пока", "повтори", "повторить", "для", "вечно", "когда", "при запуске", "при старте", "как только", "каждые", "каждый", "попробовать", "поймать", "нажата", "нажата клавиша", "клавиша нажата" };
    static readonly string[] BlockEnders = { "конец", "end" };

    public static List<CodeDiagnostic> CheckProject(string projectDir)
    {
        var result = new List<CodeDiagnostic>();
        if (!Directory.Exists(projectDir)) return result;

        var files = Directory.GetFiles(projectDir, "*.ncode", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            try
            {
                var diags = CheckFile(file, projectDir);
                result.AddRange(diags);
            }
            catch { }
        }
        return result;
    }

    static string StripTrailingComment(string s)
    {
        bool inQuote = false;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '"') inQuote = !inQuote;
            else if (!inQuote)
            {
                if (s[i] == '#') return s[..i].Trim();
                if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '/') return s[..i].Trim();
            }
        }
        return s.Trim();
    }

    static List<CodeDiagnostic> CheckFile(string filePath, string projectDir)
    {
        var diags = new List<CodeDiagnostic>();
        string[] lines;
        try { lines = File.ReadAllLines(filePath, System.Text.Encoding.UTF8); }
        catch { return diags; }

        string shortName = Path.GetRelativePath(projectDir, filePath);
        int depth = 0;
        int lastOpenLine = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            string raw = lines[i];
            string trimmed = raw.Trim();
            int lineNo = i + 1;

            if (trimmed == "" || trimmed.StartsWith("#") || trimmed.StartsWith("//")) continue;

            string clean = StripTrailingComment(trimmed);
            if (clean == "") continue;

            bool isStarter = IsBlockStart(clean);
            bool isEnder = IsBlockEnd(clean);

            if (isStarter && !isEnder)
            {
                depth++;
                lastOpenLine = lineNo;
            }
            else if (isEnder && !isStarter)
            {
                if (depth <= 0)
                    diags.Add(new CodeDiagnostic(shortName, lineNo, "Лишний 'конец' без открывающего блока", DiagnosticSeverity.Error));
                else
                    depth--;
            }

            CheckIncludeOrScript(trimmed, lineNo, shortName, projectDir, diags);
            CheckUnmatchedQuotes(trimmed, lineNo, shortName, diags);
        }

        if (depth > 0)
            diags.Add(new CodeDiagnostic(shortName, lastOpenLine, $"Блок открыт, но не закрыт 'конец' (глубина: {depth})", DiagnosticSeverity.Error));

        return diags;
    }

    static bool IsBlockStart(string t)
    {
        string low = t.ToLowerInvariant();
        foreach (var s in BlockStarters)
        {
            if (low == s || low.StartsWith(s + " ") || low.StartsWith(s + "\t"))
                return true;
        }
        return false;
    }

    static bool IsBlockEnd(string t)
    {
        string low = t.ToLowerInvariant().Trim();
        foreach (var e in BlockEnders)
            if (low == e) return true;
        return false;
    }

    static void CheckIncludeOrScript(string t, int lineNo, string shortName, string projectDir, List<CodeDiagnostic> diags)
    {
        string low = t.ToLowerInvariant();
        string? refPath = null;

        if (low.StartsWith("подключить ") || low.StartsWith("подключи "))
            refPath = ExtractQuotedPath(t);
        else if (Regex.IsMatch(t, @"^запустить\s+скрипт\b", RegexOptions.IgnoreCase) ||
                 Regex.IsMatch(t, @"^запустить\s+сцену\b", RegexOptions.IgnoreCase))
            refPath = ExtractQuotedPath(t);
        else if (Regex.IsMatch(t, @"^запустить\s+\S+\.ncode", RegexOptions.IgnoreCase))
        {
            var m = Regex.Match(t, @"^запустить\s+(\S+)", RegexOptions.IgnoreCase);
            if (m.Success) refPath = m.Groups[1].Value.Trim('"');
        }

        if (refPath == null) return;
        if (Path.GetExtension(refPath) == "") refPath += ".ncode";

        var candidates = new[]
        {
            Path.Combine(projectDir, refPath),
            refPath
        };
        bool found = false;
        foreach (var c in candidates)
            if (File.Exists(c)) { found = true; break; }

        if (!found)
            diags.Add(new CodeDiagnostic(shortName, lineNo, $"Файл не найден: {refPath}", DiagnosticSeverity.Error));
    }

    static string? ExtractQuotedPath(string t)
    {
        var m = Regex.Match(t, "\"([^\"]+)\"");
        return m.Success ? m.Groups[1].Value : null;
    }

    static void CheckUnmatchedQuotes(string t, int lineNo, string shortName, List<CodeDiagnostic> diags)
    {
        int count = 0;
        foreach (char c in t) if (c == '"') count++;
        if (count % 2 != 0)
            diags.Add(new CodeDiagnostic(shortName, lineNo, "Незакрытая кавычка в строке", DiagnosticSeverity.Warning));
    }
}
