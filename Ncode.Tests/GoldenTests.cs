// Ncode — Russian programming language for games.
// Copyright (C) 2026 Ivproduction
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later.
// See LICENSE in the repository root.

using System.Diagnostics;
using System.Text;

namespace Ncode.Tests;

public sealed class GoldenTests
{
    static string FindNcodeDll()
    {
        var baseDir = AppContext.BaseDirectory;
        var direct = Path.Combine(baseDir, "Ncode.dll");
        if (File.Exists(direct)) return direct;
        var dir = new DirectoryInfo(baseDir);
        for (int i = 0; i < 6 && dir != null; i++)
        {
            foreach (var cfg in new[] { "Debug", "Release" })
            foreach (var tfm in new[] { "net8.0-windows", "net8.0" })
            {
                var cand = Path.Combine(dir.FullName, "Ncode", "bin", cfg, tfm, "Ncode.dll");
                if (File.Exists(cand)) return cand;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Ncode.dll not found");
    }

    static (int Exit, string Out, string Err) RunNcode(string code, string? stdin = null)
    {
        string ncodeDll = FindNcodeDll();
        string tmp = Path.Combine(Path.GetTempPath(), "ncode_test_" + Guid.NewGuid().ToString("N") + ".ncode");
        File.WriteAllText(tmp, code, new UTF8Encoding(false));
        try
        {
            var psi = new ProcessStartInfo("dotnet", $"\"{ncodeDll}\" \"{tmp}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdin != null,
                UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            using var proc = Process.Start(psi)!;
            if (stdin != null)
            {
                proc.StandardInput.Write(stdin);
                proc.StandardInput.Close();
            }
            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            bool exited = proc.WaitForExit(15000);
            if (!exited) { try { proc.Kill(true); } catch { } throw new TimeoutException("Ncode process timeout"); }
            return (proc.ExitCode, stdout, stderr);
        }
        finally { try { File.Delete(tmp); } catch { } }
    }

    static string Norm(string s) => s.Replace("\r\n", "\n").Trim();

    [Fact]
    public void Variables_And_Output()
    {
        var (exit, stdout, _) = RunNcode("задать x 2 + 3 * 4\nвывести x");
        Assert.Equal(0, exit);
        Assert.Equal("14", Norm(stdout));
    }

    [Fact]
    public void If_Else()
    {
        var (exit, stdout, _) = RunNcode("если 2 + 2 равно 4 то\n  вывести \"да\"\nиначе\n  вывести \"нет\"\nконец");
        Assert.Equal(0, exit);
        Assert.Equal("да", Norm(stdout));
    }

    [Fact]
    public void While_Loop()
    {
        var (exit, stdout, _) = RunNcode("задать i 1\nпока i <= 3 то\n  вывести i\n  задать i i + 1\nконец");
        Assert.Equal(0, exit);
        Assert.Equal("1\n2\n3", Norm(stdout));
    }

    [Fact]
    public void For_Loop()
    {
        var (exit, stdout, _) = RunNcode("для i от 1 до 3 то\n  вывести i\nконец");
        Assert.Equal(0, exit);
        Assert.Equal("1\n2\n3", Norm(stdout));
    }

    [Fact]
    public void Repeat_Block()
    {
        var (exit, stdout, _) = RunNcode("повтори 3 раз\n  вывести \"x\"\nконец");
        Assert.Equal(0, exit);
        Assert.Equal("x\nx\nx", Norm(stdout));
    }

    [Fact]
    public void For_Each_List()
    {
        var (exit, stdout, _) = RunNcode("создать список ф\nдобавить в ф \"a\"\nдобавить в ф \"b\"\nдля каждого x в ф то\n  вывести x\nконец");
        Assert.Equal(0, exit);
        Assert.Equal("a\nb", Norm(stdout));
    }

    [Fact]
    public void List_Insert_Delete_Join()
    {
        var (exit, stdout, _) = RunNcode("создать список ф\nдобавить в ф \"яблоко\"\nдобавить в ф \"банан\"\nвставить в ф 2 \"груша\"\nудалить из ф 1\nсклеить ф по \", \" в рез\nвывести рез");
        Assert.Equal(0, exit);
        Assert.Equal("груша, банан", Norm(stdout));
    }

    [Fact]
    public void Table_And_Json()
    {
        var code = "создать таблицу u\nзадать u[\"name\"] \"Alex\"\nзадать u[\"age\"] 30\nвывести u[\"name\"]\nзадать j в json u\nвывести j";
        var (exit, stdout, _) = RunNcode(code);
        Assert.Equal(0, exit);
        var lines = Norm(stdout).Split('\n');
        Assert.Equal("Alex", lines[0]);
        Assert.Contains("\"name\"", lines[1]);
    }

    [Fact]
    public void String_Functions()
    {
        var code = "вывести длина \"привет\"\nвывести срез \"привет\" с 2 по 4\nвывести заменить \"мир\" на \"друг\" в \"привет мир\"";
        var (exit, stdout, _) = RunNcode(code);
        Assert.Equal(0, exit);
        Assert.Equal("6\nрив\nпривет друг", Norm(stdout));
    }

    [Fact]
    public void Math_Functions()
    {
        var code = "вывести корень 16\nвывести модуль -5\nвывести максимум 5 10\nвывести остаток 10 3";
        var (exit, stdout, _) = RunNcode(code);
        Assert.Equal(0, exit);
        Assert.Equal("4\n5\n10\n1", Norm(stdout));
    }

    [Fact]
    public void Assignment_Fallback_Physics_Keywords()
    {
        var code = "задать скорость истина\nвывести скорость\nзадать угол 30 + 15\nвывести угол";
        var (exit, stdout, _) = RunNcode(code);
        Assert.Equal(0, exit);
        Assert.Equal("истина\n45", Norm(stdout));
    }

    [Fact]
    public void Stdin_Ask()
    {
        var (exit, stdout, _) = RunNcode("спросить \"как?\" в имя\nвывести \"привет, \" + имя", stdin: "Petya\n");
        Assert.Equal(0, exit);
        Assert.Contains("Petya", Norm(stdout));
    }

    [Fact]
    public void Try_Catch_Division_By_Zero()
    {
        var code = "попробовать\n  задать x 10 / 0\nпоймать\n  вывести \"поймал\"\nконец";
        var (exit, stdout, _) = RunNcode(code);
        Assert.Equal(0, exit);
        Assert.Equal("поймал", Norm(stdout));
    }

    [Fact]
    public void File_Write_Read()
    {
        string fname = "ncode_test_" + Guid.NewGuid().ToString("N") + ".txt";
        string dir = Path.GetTempPath();
        string full = Path.Combine(dir, fname);
        try
        {
            var code = $"записать \"привет\" в файл \"{full.Replace("\\", "\\\\")}\"\nпрочитать файл \"{full.Replace("\\", "\\\\")}\" в д\nвывести д\nудалить файл \"{full.Replace("\\", "\\\\")}\"";
            var (exit, stdout, stderr) = RunNcode(code);
            Assert.Equal(0, exit);
            Assert.Equal("привет", Norm(stdout));
            Assert.True(string.IsNullOrWhiteSpace(stderr) || !stderr.Contains("Ошибка"));
        }
        finally { try { File.Delete(full); } catch { } }
    }

    [Fact]
    public void Error_Missing_List_Reports_Line()
    {
        var (exit, _, stderr) = RunNcode("добавить в нету \"x\"");
        string combined = stderr + " ";
        var (exit2, stdout2, stderr2) = RunNcode("добавить в нету \"x\"");
        Assert.NotEqual(0, exit2);
        string all = stdout2 + stderr2;
        Assert.Contains("строка 1", all);
        Assert.Contains("нет такого списка", all);
    }

    [Fact]
    public void Contains_Starts_Ends_With()
    {
        var code = "вывести содержит \"привет мир\" \"мир\"\nвывести начинается \"начало\" \"на\"\nвывести заканчивается \"конец\" \"ец\"";
        var (exit, stdout, _) = RunNcode(code);
        Assert.Equal(0, exit);
        Assert.Equal("истина\nистина\nистина", Norm(stdout));
    }

    [Fact]
    public void Split_Join()
    {
        var code = "разделить \"а,б,в\" по \",\" в ч\nвывести длина ч\nсклеить ч по \"-\" в р\nвывести р";
        var (exit, stdout, _) = RunNcode(code);
        Assert.Equal(0, exit);
        Assert.Equal("3\nа-б-в", Norm(stdout));
    }

    [Fact]
    public void Type_And_Keys()
    {
        var code = "создать таблицу t\nзадать t[\"a\"] 1\nвывести тип t\nвывести тип \"hi\"\nвывести тип 42";
        var (exit, stdout, _) = RunNcode(code);
        Assert.Equal(0, exit);
        Assert.Equal("таблица\nтекст\nчисло", Norm(stdout));
    }

    [Fact]
    public void Broadcast_When()
    {
        var code = "когда получено старт\n  вывести \"ok\"\nконец\nпри запуске\n  вещать старт\nконец";
        var (exit, stdout, _) = RunNcode(code);
        Assert.Equal(0, exit);
        Assert.Equal("ok", Norm(stdout));
    }

    [Fact]
    public void Include_Loads_Other_File()
    {
        string libName = "ncode_inc_" + Guid.NewGuid().ToString("N") + ".ncode";
        string libPath = Path.Combine(Path.GetTempPath(), libName);
        File.WriteAllText(libPath, "задать из_библы 42", Encoding.UTF8);
        string mainCode = $"подключить \"{libPath.Replace("\\", "\\\\")}\"\nвывести из_библы";
        try
        {
            var (exit, stdout, stderr) = RunNcode(mainCode);
            Assert.Equal(0, exit);
            Assert.Equal("42", Norm(stdout));
            Assert.DoesNotContain("Ошибка", stderr);
        }
        finally { try { File.Delete(libPath); } catch { } }
    }
}
