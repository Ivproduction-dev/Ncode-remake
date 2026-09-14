// Ncode — Russian programming language for games.
// Copyright (C) 2026 Ivproduction
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later.
// See LICENSE in the repository root.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit;

namespace Ncode.Editor;

public partial class MainWindow : Window
{
    private string? projectDir;
    private readonly Dictionary<string, (TabItem tab, TextEditor editor, TextBlock titleBlock)> open = new();
    private Process? runProc;
    private readonly StringBuilder outputBuffer = new();
    private readonly object outputLock = new();
    private bool outputFlushScheduled = false;
    private static readonly NcodeHighlighting CachedHighlighting = new();
    private DispatcherTimer? _diagTimer;
    private List<CodeDiagnostic> _currentDiags = new();

    public MainWindow() : this(null) { }

    public MainWindow(string? initialProjectDir)
    {
        InitializeComponent();

        NewFileButton.Click += (_, _) => CreateNewFile();
        NewProjectButton.Click += NewProjectClick;
        OpenFolderButton.Click += OpenFolderClick;
        SaveButton.Click += SaveClick;
        RunButton.Click += RunClick;
        StopButton.Click += StopClick;

        TreeNewFileBtn.Click += (_, _) => CreateNewFile();
        TreeRefreshBtn.Click += (_, _) => RefreshTree();

        MenuNewFile.Click += (_, _) => CreateNewFile();
        MenuNewProject.Click += NewProjectClick;
        MenuOpenFile.Click += OpenFilePickerClick;
        MenuOpenFolder.Click += OpenFolderClick;
        MenuProjectOptions.Click += ProjectOptionsClick;
        MenuImportProject.Click += ImportProjectClick;
        FlyoutImport.Click += ImportProjectClick;
        FlyoutProjectOptions.Click += ProjectOptionsClick;
        MenuSave.Click += SaveClick;
        MenuSaveAll.Click += (_, _) => SaveAllOpenTabs();
        MenuCloseTab.Click += (_, _) => CloseCurrentTab();
        MenuExit.Click += (_, _) => Close();
        MenuClearOutput.Click += (_, _) => ClearOutput();
        MenuRun.Click += RunClick;
        MenuStop.Click += StopClick;
        MenuAbout.Click += (_, _) => ShowAbout();

        WordWrapCheck.IsCheckedChanged += (_, _) =>
        {
            bool wrap = WordWrapCheck.IsChecked == true;
            MenuWordWrap.IsChecked = wrap;
            ToggleWordWrap(wrap);
        };
        MenuWordWrap.IsCheckedChanged += (_, _) =>
        {
            bool wrap = MenuWordWrap.IsChecked == true;
            WordWrapCheck.IsChecked = wrap;
            ToggleWordWrap(wrap);
        };

        HighlightCheck.IsCheckedChanged += (_, _) =>
        {
            bool on = HighlightCheck.IsChecked == true;
            MenuHighlight.IsChecked = on;
            ToggleHighlighting(on);
        };
        MenuHighlight.IsCheckedChanged += (_, _) =>
        {
            bool on = MenuHighlight.IsChecked == true;
            HighlightCheck.IsChecked = on;
            ToggleHighlighting(on);
        };

        ClearButton.Click += (_, _) => ClearOutput();
        SendButton.Click += (_, _) => SendInput();
        InputBox.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter)
            {
                SendInput();
                e.Handled = true;
            }
        };

        FileTree.SelectionChanged += TreeSelectionChanged;
        EditorTabs.SelectionChanged += (_, _) => UpdateCaret();

        DiagBadge.PointerPressed += (_, _) =>
        {
            DiagPanel.IsVisible = !DiagPanel.IsVisible;
        };
        DiagCloseBtn.Click += (_, _) =>
        {
            DiagPanel.IsVisible = false;
        };

        KeyDown += OnWindowKeyDown;

        if (!string.IsNullOrEmpty(initialProjectDir) && Directory.Exists(initialProjectDir))
        {
            SetProjectDir(initialProjectDir);
        }
        else
        {
            var startDir = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(Directory.Exists)
                ?? Environment.CurrentDirectory;
            if (Directory.Exists(startDir))
            {
                SetProjectDir(startDir);
            }
        }
        UpdatePlaceholder();
    }

    private void SetProjectDir(string dir)
    {
        projectDir = dir;
        ProjectManager.AddOrUpdate(dir);
        ProjectDirText.Text = dir;
        StatusText.Text = "Проект: " + Path.GetFileName(dir);
        RefreshTree();

        if (_diagTimer == null)
        {
            _diagTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _diagTimer.Tick += (_, _) => RunProjectDiagnostics();
            _diagTimer.Start();
        }
        RunProjectDiagnostics();

        var main = Path.Combine(dir, "main.ncode");
        var game = Path.Combine(dir, "game.ncode");
        if (File.Exists(main))
        {
            OpenFile(main);
        }
        else if (File.Exists(game))
        {
            OpenFile(game);
        }
        else
        {
            var first = Directory.GetFiles(dir, "*.ncode", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (first != null) OpenFile(first);
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (e.Key == Key.F5 && !shift)
        {
            RunProject();
            e.Handled = true;
        }
        else if ((e.Key == Key.F5 && shift) || (e.Key == Key.Cancel))
        {
            StopProject();
            e.Handled = true;
        }
        else if (ctrl && shift && e.Key == Key.S)
        {
            SaveAllOpenTabs();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.S)
        {
            SaveActiveTab();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.W)
        {
            CloseCurrentTab();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.N)
        {
            CreateNewFile();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.O)
        {
            OpenFilePicker();
            e.Handled = true;
        }
    }

    private async void NewProjectClick(object? sender, RoutedEventArgs e)
    {
        var wiz = new NewProjectWizard();
        await wiz.ShowDialog(this);
        if (wiz.CreatedGameFile == null) return;
        var dir = Path.GetDirectoryName(wiz.CreatedGameFile);
        if (dir != null)
        {
            SetProjectDir(dir);
            OpenFile(wiz.CreatedGameFile);
        }
    }

    private void OpenFilePicker() => OpenFilePickerClick(null, null);

    private async void OpenFilePickerClick(object? sender, RoutedEventArgs? e = null)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Открыть файл",
            AllowMultiple = true
        });
        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (path != null && File.Exists(path))
            {
                OpenFile(path);
            }
        }
    }

    private async void OpenFolderClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Папка проекта",
            AllowMultiple = false
        });
        if (folders.Count == 0) return;
        var dir = folders[0].TryGetLocalPath();
        if (dir == null) return;
        SetProjectDir(dir);
    }

    private async void ImportProjectClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Импортировать пакет проекта Ncode",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Пакет проекта Ncode (*.nproject)")
                {
                    Patterns = new[] { "*.nproject" }
                }
            }
        });

        if (files.Count == 0) return;
        var archivePath = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath)) return;

        var defaultParent = Path.GetDirectoryName(archivePath)
            ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        var dlg = new ImportDialog(archivePath, defaultParent);
        await dlg.ShowDialog(this);

        if (dlg.Success && !string.IsNullOrEmpty(dlg.ResultProjectDir) && Directory.Exists(dlg.ResultProjectDir))
        {
            SetProjectDir(dlg.ResultProjectDir);
            StatusText.Text = "Проект импортирован: " + Path.GetFileName(dlg.ResultProjectDir);
        }
    }

    private async void ProjectOptionsClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(projectDir) || !Directory.Exists(projectDir))
        {
            StatusText.Text = "Нет открытого проекта.";
            return;
        }

        var dlg = new ProjectOptionsDialog(projectDir);
        await dlg.ShowDialog(this);
    }

    private void RefreshTree()
    {
        FileTree.Items.Clear();
        if (projectDir == null || !Directory.Exists(projectDir)) return;
        FileTree.Items.Add(BuildNode(new DirectoryInfo(projectDir)));
    }

    private TreeViewItem BuildNode(DirectoryInfo dir)
    {
        var node = new TreeViewItem
        {
            Header = dir.Name,
            Tag = dir.FullName,
            IsExpanded = true
        };

        var menu = new ContextMenu();
        var newFileItem = new MenuItem { Header = "Новый файл..." };
        newFileItem.Click += (_, _) => CreateNewFileIn(dir.FullName);
        var openExpItem = new MenuItem { Header = "Открыть в Проводнике" };
        openExpItem.Click += (_, _) => OpenInExplorer(dir.FullName);
        menu.Items.Add(newFileItem);
        menu.Items.Add(openExpItem);
        node.ContextMenu = menu;

        try
        {
            foreach (var d in dir.GetDirectories())
            {
                if (d.Name is "bin" or "obj" or ".git" or ".vs" or ".idea") continue;
                node.Items.Add(BuildNode(d));
            }

            string[] allowedExts = [".ncode", ".txt", ".json", ".md", ".csv", ".log", ".bat"];
            foreach (var f in dir.GetFiles())
            {
                if (allowedExts.Contains(f.Extension.ToLowerInvariant()) || f.Name.EndsWith(".ncode"))
                {
                    string icon = f.Extension.ToLowerInvariant() == ".ncode" ? "[n] " : "";
                    var fileNode = new TreeViewItem
                    {
                        Header = icon + f.Name,
                        Tag = f.FullName
                    };

                    var fileMenu = new ContextMenu();
                    var openItem = new MenuItem { Header = "Открыть" };
                    openItem.Click += (_, _) => OpenFile(f.FullName);
                    var deleteItem = new MenuItem { Header = "Удалить" };
                    deleteItem.Click += (_, _) => DeleteFile(f.FullName);
                    var expItem = new MenuItem { Header = "Показать в Проводнике" };
                    expItem.Click += (_, _) => OpenInExplorer(f.FullName);

                    fileMenu.Items.Add(openItem);
                    fileMenu.Items.Add(deleteItem);
                    fileMenu.Items.Add(expItem);
                    fileNode.ContextMenu = fileMenu;

                    node.Items.Add(fileNode);
                }
            }
        }
        catch { }

        return node;
    }

    private void TreeSelectionChanged(object? sender, SelectionChangedEventArgs? e)
    {
        try
        {
            var sel = FileTree.SelectedItem;
            if (sel is TreeViewItem item && item.Tag is string p)
            {
                if (File.Exists(p))
                {
                    OpenFile(p);
                }
                else if (Directory.Exists(p))
                {
                    item.IsExpanded = !item.IsExpanded;
                }
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка: " + ex.Message;
        }
    }

    private void CreateNewFile()
    {
        string baseFolder = projectDir ?? Environment.CurrentDirectory;
        CreateNewFileIn(baseFolder);
    }

    private void CreateNewFileIn(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            int idx = 1;
            string newPath;
            do
            {
                newPath = Path.Combine(folder, $"файл_{idx}.ncode");
                idx++;
            } while (File.Exists(newPath));

            File.WriteAllText(newPath, "при запуске\n  вывести \"Привет, Ncode!\"\nконец\n", new UTF8Encoding(false));
            RefreshTree();
            OpenFile(newPath);
            StatusText.Text = "Создан: " + Path.GetFileName(newPath);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Не удалось создать: " + ex.Message;
        }
    }

    private void DeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                CloseTab(path);
                File.Delete(path);
                RefreshTree();
                StatusText.Text = "Удален: " + Path.GetFileName(path);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка удаления: " + ex.Message;
        }
    }

    private static void OpenInExplorer(string path)
    {
        try
        {
            if (File.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch { }
    }

    private void OpenFile(string path)
    {
        try
        {
            if (open.TryGetValue(path, out var entry))
            {
                EditorTabs.SelectedItem = entry.tab;
                UpdateCaret();
                return;
            }

            var editor = new TextEditor
            {
                Text = File.ReadAllText(path, Encoding.UTF8),
                FontFamily = new FontFamily("JetBrains Mono,Cascadia Code,Consolas,Courier New"),
                FontSize = 14,
                Background = new SolidColorBrush(Color.Parse("#141413")),
                Foreground = new SolidColorBrush(Color.Parse("#FAF9F5")),
                ShowLineNumbers = true,
                SyntaxHighlighting = HighlightCheck.IsChecked == true ? CachedHighlighting : null,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                WordWrap = WordWrapCheck.IsChecked == true,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
            };

            editor.TextArea.TextView.LinkTextForegroundBrush = new SolidColorBrush(Color.Parse("#D97757"));
            editor.TextArea.SelectionBrush = new SolidColorBrush(Color.Parse("#343431"));
            editor.TextArea.SelectionForeground = new SolidColorBrush(Color.Parse("#FAF9F5"));
            editor.TextArea.Caret.CaretBrush = new SolidColorBrush(Color.Parse("#C96442"));

            editor.PointerWheelChanged += (s, e) =>
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                {
                    if (e.Delta.Y > 0 && editor.FontSize < 36)
                        editor.FontSize += 1;
                    else if (e.Delta.Y < 0 && editor.FontSize > 8)
                        editor.FontSize -= 1;
                    UpdateCaret();
                    e.Handled = true;
                }
            };

            var border = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#141413")),
                Child = editor,
                Padding = new Thickness(0),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
            };

            var headerPanel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            var titleBlock = new TextBlock
            {
                Text = Path.GetFileName(path),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.Parse("#FAF9F5")),
                FontSize = 13
            };
            var closeBtn = new Button
            {
                Content = "x",
                Classes = { "tab-close" },
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            ToolTip.SetTip(closeBtn, "Закрыть вкладку (Ctrl+W)");

            headerPanel.Children.Add(titleBlock);
            headerPanel.Children.Add(closeBtn);

            var tab = new TabItem
            {
                Header = headerPanel,
                Content = border,
                Tag = path
            };

            closeBtn.Click += (_, e) =>
            {
                e.Handled = true;
                CloseTab(path);
            };

            tab.PointerPressed += (_, pe) =>
            {
                if (pe.GetCurrentPoint(tab).Properties.IsMiddleButtonPressed)
                {
                    CloseTab(path);
                    pe.Handled = true;
                }
            };

            editor.TextChanged += (_, _) =>
            {
                titleBlock.Text = Path.GetFileName(path) + " •";
                UpdateCaret();
            };
            editor.TextArea.Caret.PositionChanged += (_, _) => UpdateCaret();

            open[path] = (tab, editor, titleBlock);
            EditorTabs.Items.Add(tab);
            EditorTabs.SelectedItem = tab;

            UpdatePlaceholder();
            UpdateCaret();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка открытия: " + ex.Message;
        }
    }

    private void CloseTab(string path)
    {
        if (!open.TryGetValue(path, out var entry)) return;

        int idx = EditorTabs.Items.IndexOf(entry.tab);
        EditorTabs.Items.Remove(entry.tab);
        open.Remove(path);

        if (EditorTabs.Items.Count > 0)
        {
            int next = Math.Clamp(idx, 0, EditorTabs.Items.Count - 1);
            EditorTabs.SelectedItem = EditorTabs.Items[next];
        }
        else
        {
            EditorTabs.SelectedItem = null;
        }

        UpdatePlaceholder();
        UpdateCaret();
    }

    private void CloseCurrentTab()
    {
        if (EditorTabs.SelectedItem is TabItem { Tag: string path })
            CloseTab(path);
    }

    private void UpdatePlaceholder()
    {
        EmptyTabsPlaceholder.IsVisible = open.Count == 0;
    }

    private void UpdateCaret()
    {
        if (EditorTabs.SelectedItem is TabItem { Tag: string p } && open.TryGetValue(p, out var entry))
        {
            var caret = entry.editor.TextArea.Caret;
            CaretText.Text = $"Стр {caret.Line}, стл {caret.Column} (шрифт {entry.editor.FontSize}pt)";
            FileText.Text = Path.GetFileName(p);
        }
        else
        {
            CaretText.Text = "";
            FileText.Text = "";
        }
    }

    private void SaveActiveTab() => SaveClick(null, null);

    private void SaveClick(object? sender, RoutedEventArgs? e = null)
    {
        if (EditorTabs.SelectedItem is TabItem tab)
            SaveTab(tab);
    }

    private void SaveTab(TabItem tab)
    {
        if (tab.Tag is not string path || !open.TryGetValue(path, out var entry)) return;
        File.WriteAllText(path, entry.editor.Text ?? "", new UTF8Encoding(false));
        entry.titleBlock.Text = Path.GetFileName(path);
        StatusText.Text = "Сохранено: " + Path.GetFileName(path);
    }

    private void SaveAllOpenTabs()
    {
        foreach (var kv in open)
            SaveTab(kv.Value.tab);
    }

    private void ClearOutput()
    {
        lock (outputLock) { outputBuffer.Clear(); }
        OutputBox.Text = "";
        SetStatusBadge("ГОТОВ", "#262624", "#87867F");
    }

    private void RunProject() => RunClick(null, null);

    private void RunClick(object? sender, RoutedEventArgs? e = null)
    {
        if (EditorTabs.SelectedItem is not TabItem tab || tab.Tag is not string path)
        {
            AppendOutput("Нет активного файла для запуска. Откройте файл .ncode");
            return;
        }

        SaveAllOpenTabs();
        StopProc();

        var dll = FindInterpreter();
        if (dll == null)
        {
            AppendOutput("Не найден Ncode.dll. Соберите проект Ncode.");
            return;
        }

        ClearOutput();
        SetStatusBadge("ВЫПОЛНЯЕТСЯ", "#DEB368", "#141413");
        StatusText.Text = "Работает: " + Path.GetFileName(path);

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

        if (projectDir != null && Directory.Exists(projectDir))
        {
            psi.WorkingDirectory = projectDir;
        }

        runProc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        runProc.OutputDataReceived += (_, a) => { if (a.Data != null) AppendOutput(a.Data); };
        runProc.ErrorDataReceived += (_, a) => { if (a.Data != null) AppendOutput("[ОШИБКА] " + a.Data); };
        runProc.Exited += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            int code = runProc?.ExitCode ?? 0;
            if (code == 0)
                SetStatusBadge("ЗАВЕРШЕНО (0)", "#788F69", "#FAF9F5");
            else
                SetStatusBadge($"ОШИБКА ({code})", "#B53333", "#FAF9F5");

            StatusText.Text = $"Код завершения: {code}";
            RunButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            InputBox.IsEnabled = false;
            SendButton.IsEnabled = false;
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
            SetStatusBadge("ОШИБКА", "#B53333", "#FAF9F5");
            runProc = null;
            return;
        }

        RunButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        InputBox.IsEnabled = true;
        SendButton.IsEnabled = true;
    }

    private void StopProject() => StopClick(null, null);

    private void StopClick(object? sender, RoutedEventArgs? e = null) => StopProc();

    private void StopProc()
    {
        try { runProc?.Kill(true); } catch { }
        runProc = null;
        RunButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        InputBox.IsEnabled = false;
        SendButton.IsEnabled = false;
        SetStatusBadge("ОСТАНОВЛЕНО", "#D97757", "#141413");
        StatusText.Text = "Процесс остановлен пользователем";
    }

    private void SendInput()
    {
        if (runProc == null || string.IsNullOrEmpty(InputBox.Text)) return;
        try
        {
            runProc.StandardInput.WriteLine(InputBox.Text);
            AppendOutput("> " + InputBox.Text);
        }
        catch { }
        InputBox.Text = "";
    }

    private void AppendOutput(string line)
    {
        lock (outputLock)
        {
            outputBuffer.AppendLine(line);
            if (!outputFlushScheduled)
            {
                outputFlushScheduled = true;
                Dispatcher.UIThread.Post(FlushOutput, DispatcherPriority.Background);
            }
        }
    }

    private void FlushOutput()
    {
        string text;
        lock (outputLock)
        {
            text = outputBuffer.ToString();
            outputFlushScheduled = false;
        }
        OutputBox.Text = text;
        OutputBox.CaretIndex = text.Length;

        if (runProc != null && (text.EndsWith("? ") || text.EndsWith("? \r\n") || text.Contains("спросить")))
        {
            InputBox.Focus();
        }
    }

    private void SetStatusBadge(string text, string bgHex, string fgHex)
    {
        StatusBadge.Background = new SolidColorBrush(Color.Parse(bgHex));
        StatusBadgeText.Text = text;
        StatusBadgeText.Foreground = new SolidColorBrush(Color.Parse(fgHex));
    }

    private void ToggleWordWrap(bool enable)
    {
        foreach (var kv in open)
            kv.Value.editor.WordWrap = enable;
    }

    private void ToggleHighlighting(bool enable)
    {
        foreach (var kv in open)
            kv.Value.editor.SyntaxHighlighting = enable ? CachedHighlighting : null;
    }

    private void ShowAbout()
    {
        AppendOutput("=== Ncode Studio ===");
        AppendOutput("Русскоязычная среда разработки и игровой движок.");
        AppendOutput("Платформа: Avalonia UI + .NET 8");
        AppendOutput("Горячие клавиши: F5 (Запуск), Shift+F5 (Стоп), Ctrl+S (Сохранить), Ctrl+W (Закрыть вкладку), Ctrl+N (Новый файл)");
    }

    private static string? FindInterpreter()
    {
        var baseDir = AppContext.BaseDirectory;
        var directCand = Path.Combine(baseDir, "Ncode.dll");
        if (File.Exists(directCand)) return directCand;

        var dir = new DirectoryInfo(baseDir);
        for (int i = 0; i < 6 && dir != null; i++)
        {
            string[] configs = ["Debug", "Release"];
            string[] tfms = ["net8.0-windows", "net8.0"];
            foreach (var cfg in configs)
            {
                foreach (var tfm in tfms)
                {
                    var cand = Path.Combine(dir.FullName, "Ncode", "bin", cfg, tfm, "Ncode.dll");
                    if (File.Exists(cand)) return cand;
                }
            }
            dir = dir.Parent;
        }
        return null;
    }

    private void RunProjectDiagnostics()
    {
        var dir = projectDir;
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

        Task.Run(() =>
        {
            var diags = ProjectChecker.CheckProject(dir);
            Dispatcher.UIThread.Post(() =>
            {
                _currentDiags = diags;
                UpdateDiagUI(diags);
            });
        });
    }

    private void UpdateDiagUI(List<CodeDiagnostic> diags)
    {
        int errors = diags.Count(d => d.Severity == DiagnosticSeverity.Error);
        int warnings = diags.Count(d => d.Severity == DiagnosticSeverity.Warning);

        if (errors > 0)
        {
            DiagDot.Fill = new SolidColorBrush(Color.Parse("#B53333"));
            DiagText.Text = $"{errors} {PluralErrors(errors)}";
            DiagText.Foreground = new SolidColorBrush(Color.Parse("#FAF9F5"));
        }
        else if (warnings > 0)
        {
            DiagDot.Fill = new SolidColorBrush(Color.Parse("#D4A017"));
            DiagText.Text = $"{warnings} {PluralWarnings(warnings)}";
            DiagText.Foreground = new SolidColorBrush(Color.Parse("#FAF9F5"));
        }
        else
        {
            DiagDot.Fill = new SolidColorBrush(Color.Parse("#788F69"));
            DiagText.Text = "ОК";
            DiagText.Foreground = new SolidColorBrush(Color.Parse("#B0AEA5"));
        }

        DiagItemsPanel.Children.Clear();
        if (diags.Count == 0)
        {
            var okBox = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#141413")),
                BorderBrush = new SolidColorBrush(Color.Parse("#30302E")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(14, 12),
                Margin = new Thickness(0, 4)
            };
            var okText = new TextBlock
            {
                Text = "Ошибок не обнаружено. Структура проекта корректна.",
                Foreground = new SolidColorBrush(Color.Parse("#8FA87B")),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            okBox.Child = okText;
            DiagItemsPanel.Children.Add(okBox);
            return;
        }

        foreach (var diag in diags)
        {
            var itemBorder = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#262624")),
                BorderBrush = new SolidColorBrush(Color.Parse("#30302E")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8),
                Cursor = new Cursor(StandardCursorType.Hand)
            };

            itemBorder.PointerEntered += (_, _) =>
            {
                itemBorder.Background = new SolidColorBrush(Color.Parse("#30302E"));
                itemBorder.BorderBrush = new SolidColorBrush(Color.Parse("#D97757"));
            };
            itemBorder.PointerExited += (_, _) =>
            {
                itemBorder.Background = new SolidColorBrush(Color.Parse("#262624"));
                itemBorder.BorderBrush = new SolidColorBrush(Color.Parse("#30302E"));
            };

            var dock = new DockPanel();

            var dot = new Avalonia.Controls.Shapes.Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = diag.Severity == DiagnosticSeverity.Error
                    ? new SolidColorBrush(Color.Parse("#B53333"))
                    : new SolidColorBrush(Color.Parse("#D4A017")),
                Margin = new Thickness(0, 4, 8, 0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
            };
            DockPanel.SetDock(dot, Dock.Left);
            dock.Children.Add(dot);

            var sp = new StackPanel { Spacing = 2 };

            var locationText = new TextBlock
            {
                Text = $"{diag.File} : строка {diag.Line}",
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#D97757"))
            };
            var msgText = new TextBlock
            {
                Text = diag.Message,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#FAF9F5")),
                TextWrapping = TextWrapping.Wrap
            };

            sp.Children.Add(locationText);
            sp.Children.Add(msgText);
            dock.Children.Add(sp);

            itemBorder.Child = dock;

            itemBorder.PointerPressed += (_, _) =>
            {
                string targetPath = diag.File;
                if (projectDir != null && !Path.IsPathRooted(targetPath))
                    targetPath = Path.Combine(projectDir, targetPath);
                NavigateTo(targetPath, diag.Line);
            };

            DiagItemsPanel.Children.Add(itemBorder);
        }
    }

    private static string PluralErrors(int n)
    {
        int rem100 = n % 100;
        int rem10 = n % 10;
        if (rem100 is >= 11 and <= 19) return "ошибок";
        if (rem10 == 1) return "ошибка";
        if (rem10 is >= 2 and <= 4) return "ошибки";
        return "ошибок";
    }

    private static string PluralWarnings(int n)
    {
        int rem100 = n % 100;
        int rem10 = n % 10;
        if (rem100 is >= 11 and <= 19) return "предупреждений";
        if (rem10 == 1) return "предупреждение";
        if (rem10 is >= 2 and <= 4) return "предупреждения";
        return "предупреждений";
    }

    public void NavigateTo(string path, int line)
    {
        try
        {
            OpenFile(path);
            if (open.TryGetValue(path, out var entry))
            {
                EditorTabs.SelectedItem = entry.tab;
                if (line > 0 && line <= entry.editor.Document.LineCount)
                {
                    var docLine = entry.editor.Document.GetLineByNumber(line);
                    entry.editor.CaretOffset = docLine.Offset;
                    entry.editor.ScrollToLine(line);
                    entry.editor.Focus();
                }
            }
        }
        catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        _diagTimer?.Stop();
        StopProc();
        base.OnClosed(e);
    }
}
