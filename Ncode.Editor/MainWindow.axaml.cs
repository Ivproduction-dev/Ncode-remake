using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Ncode.Editor;

public partial class MainWindow : Window
{
    private string? projectDir;
    private readonly Dictionary<string, (TabItem tab, TextBox box, TextBlock gutter, TextBlock highlight)> open = new();
    private bool highlightOn = true;
    private Process? runProc;

    public MainWindow()
    {
        InitializeComponent();
        OpenFolderButton.Click += OpenFolderClick;
        NewProjectButton.Click += NewProjectClick;
        OpenProjectButton.Click += OpenProjectClick;
        SaveButton.Click += SaveClick;
        RunButton.Click += RunClick;
        StopButton.Click += StopClick;
        SendButton.Click += SendClick;
        ClearButton.Click += (_, _) => OutputBox.Text = "";
        FileTree.SelectionChanged += TreeSelectionChanged;
        FileTree.DoubleTapped += (_, _) => TreeSelectionChanged(null, null);
        HighlightCheck.Click += (_, _) =>
        {
            highlightOn = HighlightCheck.IsChecked == true;
            foreach (var kv in open)
            {
                kv.Value.highlight.IsVisible = highlightOn;
                kv.Value.box.Foreground = new SolidColorBrush(Color.Parse(highlightOn ? "#00FFFFFF" : "#CDD6F4"));
                RefreshHighlight(kv.Value.highlight, kv.Value.box);
            }
        };
        InputBox.KeyDown += InputKeyDown;
        EditorTabs.SelectionChanged += (_, _) => UpdateCaret();
        var startDir = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(Directory.Exists)
            ?? Environment.CurrentDirectory;
        if (Directory.Exists(startDir))
        {
            projectDir = startDir;
            StatusText.Text = projectDir;
            RefreshTree();
        }
    }

    private async void NewProjectClick(object? sender, RoutedEventArgs e)
    {
        var wiz = new NewProjectWizard();
        await wiz.ShowDialog(this);
        if (wiz.CreatedGameFile == null) return;
        projectDir = Path.GetDirectoryName(wiz.CreatedGameFile);
        StatusText.Text = projectDir;
        RefreshTree();
        OpenFile(wiz.CreatedGameFile);
    }

    private async void OpenProjectClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Папка проекта",
            AllowMultiple = false
        });
        if (folders.Count == 0) return;
        var dir = folders[0].TryGetLocalPath();
        if (dir == null) return;
        projectDir = dir;
        StatusText.Text = projectDir;
        RefreshTree();
        var game = Path.Combine(dir, "game.ncode");
        if (File.Exists(game))
        {
            OpenFile(game);
            return;
        }
        var all = new List<string>();
        foreach (var f in Directory.GetFiles(dir, "*.ncode", SearchOption.AllDirectories))
        {
            var parts = f.Split(Path.DirectorySeparatorChar);
            bool skip = false;
            foreach (var p in parts)
            {
                if (p is "bin" or "obj" or ".git" or ".vs") { skip = true; break; }
            }
            if (!skip) all.Add(f);
        }
        if (all.Count == 0)
        {
            StatusText.Text = "В папке нет .ncode: " + dir;
            return;
        }
        var dlg = new SelectFileDialog(all, dir);
        await dlg.ShowDialog(this);
        if (dlg.SelectedFile != null)
            OpenFile(dlg.SelectedFile);
    }

    private async void OpenFolderClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Папка проекта",
            AllowMultiple = false
        });
        if (folders.Count == 0) return;
        projectDir = folders[0].TryGetLocalPath();
        if (projectDir == null) return;
        StatusText.Text = projectDir;
        RefreshTree();
    }

    private void RefreshTree()
    {
        FileTree.Items.Clear();
        if (projectDir == null) return;
        FileTree.Items.Add(BuildNode(new DirectoryInfo(projectDir)));
    }

    private static TreeViewItem BuildNode(DirectoryInfo dir)
    {
        var node = new TreeViewItem { Header = dir.Name, Tag = dir.FullName, IsExpanded = true };
        foreach (var d in dir.GetDirectories())
        {
            if (d.Name is "bin" or "obj" or ".git" or ".vs") continue;
            node.Items.Add(BuildNode(d));
        }
        foreach (var f in dir.GetFiles("*.ncode"))
            node.Items.Add(new TreeViewItem { Header = f.Name, Tag = f.FullName });
        return node;
    }

    private void TreeSelectionChanged(object? sender, SelectionChangedEventArgs? e)
    {
        try
        {
            var sel = FileTree.SelectedItem;
            if (sel is TreeViewItem { Tag: string p } && File.Exists(p))
                OpenFile(p);
            else
                StatusText.Text = "клик: " + (sel?.GetType().Name ?? "null");
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка открытия: " + ex.Message;
        }
    }

    private void OpenFile(string path)
    {
        try
        {
            if (open.TryGetValue(path, out var entry))
            {
                EditorTabs.SelectedItem = entry.tab;
                return;
            }
            var mono = new FontFamily("JetBrains Mono,Consolas,Cascadia Code");
            var gutter = new TextBlock
            {
                FontFamily = mono,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.Parse("#6C7086")),
                TextAlignment = TextAlignment.Right,
                Padding = new Thickness(8, 6, 4, 6)
            };
            var box = new TextBox
            {
                Text = File.ReadAllText(path, Encoding.UTF8),
                FontFamily = mono,
                FontSize = 14,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.Parse(highlightOn ? "#00FFFFFF" : "#CDD6F4")),
                CaretBrush = new SolidColorBrush(Color.Parse("#F5C2E7")),
                SelectionBrush = new SolidColorBrush(Color.Parse("#B045475A")),
                SelectionForegroundBrush = new SolidColorBrush(Color.Parse("#CDD6F4")),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 6, 8, 6),
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.NoWrap
            };
            box.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            box.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            var highlight = new TextBlock
            {
                FontFamily = mono,
                FontSize = 14,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.Parse("#CDD6F4")),
                Padding = new Thickness(8, 6, 8, 6),
                TextWrapping = TextWrapping.NoWrap,
                IsHitTestVisible = false,
                IsVisible = highlightOn
            };
            var overlay = new Grid();
            overlay.Children.Add(box);
            overlay.Children.Add(highlight);
            var inner = new ScrollViewer
            {
                Content = overlay,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                IsScrollChainingEnabled = false
            };
            var grid = new Grid
            {
                Background = new SolidColorBrush(Color.Parse("#11111B")),
                ColumnDefinitions = new ColumnDefinitions("Auto,*")
            };
            grid.Children.Add(gutter);
            Grid.SetColumn(gutter, 0);
            grid.Children.Add(inner);
            Grid.SetColumn(inner, 1);
            var outer = new ScrollViewer
            {
                Content = grid,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            TabItem tab = new() { Header = Path.GetFileName(path), Content = outer, Tag = path };
            box.TextChanged += (_, _) => { tab.Header = Path.GetFileName(path) + " *"; RefreshGutter(gutter, box); RefreshHighlight(highlight, box); UpdateCaret(); };
            box.KeyUp += (_, _) => { RefreshGutter(gutter, box); UpdateCaret(); };
        open[path] = (tab, box, gutter, highlight);
        EditorTabs.Items.Add(tab);
        EditorTabs.SelectedItem = tab;
        RefreshGutter(gutter, box);
        RefreshHighlight(highlight, box);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка открытия: " + ex.Message;
        }
    }

    private static void RefreshGutter(TextBlock gutter, TextBox box)
    {
        var text = box.Text ?? "";
        int total = 1;
        foreach (char c in text)
            if (c == '\n') total++;
        int caret = Math.Min(box.CaretIndex, text.Length);
        int cur = 1;
        for (int i = 0; i < caret; i++)
            if (text[i] == '\n') cur++;
        gutter.Inlines!.Clear();
        for (int n = 1; n <= total; n++)
        {
            if (n > 1) gutter.Inlines!.Add(new Run("\n"));
            var run = new Run(n.ToString());
            if (n == cur)
            {
                run.FontWeight = FontWeight.Bold;
                run.Foreground = new SolidColorBrush(Color.Parse("#CDD6F4"));
            }
            gutter.Inlines!.Add(run);
        }
        int digits = Math.Max(2, total.ToString().Length);
        gutter.Width = 16 + digits * 8.5;
    }

    private static void RefreshHighlight(TextBlock highlight, TextBox box)
    {
        if (!highlight.IsVisible) return;
        var text = box.Text ?? "";
        highlight.Inlines!.Clear();
        int pos = 0;
        while (pos <= text.Length)
        {
            int nl = text.IndexOf('\n', pos);
            string line;
            bool last;
            if (nl < 0) { line = text[pos..]; last = true; }
            else { line = text[pos..nl]; last = false; }
            foreach (var span in NcodeColor.ColorizeLine(line))
            {
                var run = new Run(span.Text);
                run.Foreground = new SolidColorBrush(Color.Parse(span.Color));
                if (span.Bold) run.FontWeight = FontWeight.Bold;
                if (span.Italic) run.FontStyle = FontStyle.Italic;
                highlight.Inlines!.Add(run);
            }
            if (!last) highlight.Inlines!.Add(new Run("\n"));
            pos = last ? text.Length + 1 : nl + 1;
        }
    }

    private void UpdateCaret()
    {
        if (EditorTabs.SelectedItem is TabItem { Tag: string p } && open.TryGetValue(p, out var entry))
        {
            RefreshGutter(entry.gutter, entry.box);
            var text = entry.box.Text ?? "";
            int idx = Math.Min(entry.box.CaretIndex, text.Length);
            int line = 1;
            int lineStart = 0;
            for (int i = 0; i < idx; i++)
                if (text[i] == '\n') { line++; lineStart = i + 1; }
            CaretText.Text = $"Строка {line}, столбец {idx - lineStart + 1}";
            FileText.Text = Path.GetFileName(p);
        }
        else
        {
            CaretText.Text = "";
            FileText.Text = "";
        }
    }

    private void SaveClick(object? sender, RoutedEventArgs e)
    {
        if (EditorTabs.SelectedItem is TabItem tab)
            SaveTab(tab);
    }

    private void SaveTab(TabItem tab)
    {
        if (tab.Tag is not string path || !open.TryGetValue(path, out var entry)) return;
        File.WriteAllText(path, entry.box.Text ?? "", new UTF8Encoding(false));
        tab.Header = Path.GetFileName(path);
        StatusText.Text = "Сохранено: " + path;
    }

    private void RunClick(object? sender, RoutedEventArgs e)
    {
        if (EditorTabs.SelectedItem is not TabItem tab || tab.Tag is not string path) return;
        SaveTab(tab);
        StopProc();
        var dll = FindInterpreter();
        if (dll == null)
        {
            AppendOutput("Не найден Ncode.dll. Собери проект Ncode.");
            return;
        }
        OutputBox.Text = "";
        var psi = new ProcessStartInfo("dotnet", $"\"{dll}\" \"{path}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        runProc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        runProc.OutputDataReceived += (_, a) => { if (a.Data != null) AppendOutput(a.Data); };
        runProc.ErrorDataReceived += (_, a) => { if (a.Data != null) AppendOutput(a.Data); };
        runProc.Exited += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            StatusText.Text = "Код: " + (runProc?.ExitCode.ToString() ?? "?");
            RunButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            runProc = null;
        });
        try
        {
            runProc.Start();
            runProc.BeginOutputReadLine();
            runProc.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            AppendOutput("Не запустилось: " + ex.Message);
            runProc = null;
            return;
        }
        RunButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        StatusText.Text = "Работает: " + Path.GetFileName(path);
    }

    private void StopClick(object? sender, RoutedEventArgs e) => StopProc();

    private void StopProc()
    {
        try { runProc?.Kill(true); } catch { }
        runProc = null;
        RunButton.IsEnabled = true;
        StopButton.IsEnabled = false;
    }

    private void SendClick(object? sender, RoutedEventArgs e) => SendInput();

    private void InputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SendInput();
            e.Handled = true;
        }
    }

    private void SendInput()
    {
        if (runProc == null || string.IsNullOrEmpty(InputBox.Text)) return;
        try { runProc.StandardInput.WriteLine(InputBox.Text); } catch { }
        InputBox.Text = "";
    }

    private void AppendOutput(string line)
    {
        Dispatcher.UIThread.Post(() =>
        {
            OutputBox.Text += line + Environment.NewLine;
            OutputBox.CaretIndex = OutputBox.Text.Length;
        });
    }

    private static string? FindInterpreter()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 6 && dir != null; i++)
        {
            var cand = Path.Combine(dir.FullName, "Ncode", "bin", "Debug", "net8.0-windows", "Ncode.dll");
            if (File.Exists(cand)) return cand;
            cand = Path.Combine(dir.FullName, "Ncode", "bin", "Debug", "net8.0", "Ncode.dll");
            if (File.Exists(cand)) return cand;
            dir = dir.Parent;
        }
        return null;
    }

    protected override void OnClosed(EventArgs e)
    {
        StopProc();
        base.OnClosed(e);
    }
}
