using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Ncode.Editor;

public partial class ExportExeDialog : Window
{
    private readonly string _projectDir;
    private string? _createdOutputDir;

    public ExportExeDialog() : this("") { }

    public ExportExeDialog(string projectDir, string? defaultMainFile = null)
    {
        InitializeComponent();
        _projectDir = projectDir;

        string projName = !string.IsNullOrEmpty(projectDir) ? Path.GetFileName(projectDir) : "Игра";
        TitleBox.Text = projName;

        string defaultBuildDir = !string.IsNullOrEmpty(projectDir)
            ? Path.Combine(projectDir, "Сборка_Windows")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Ncode_Builds");
        OutputDirBox.Text = defaultBuildDir;

        if (!string.IsNullOrEmpty(projectDir) && Directory.Exists(projectDir))
        {
            var ncodeFiles = Directory.GetFiles(projectDir, "*.ncode", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(projectDir, f))
                .ToList();

            MainFileCombo.ItemsSource = ncodeFiles;

            if (!string.IsNullOrEmpty(defaultMainFile))
            {
                string rel = Path.IsPathRooted(defaultMainFile) ? Path.GetRelativePath(projectDir, defaultMainFile) : defaultMainFile;
                MainFileCombo.SelectedItem = ncodeFiles.FirstOrDefault(f => f.Equals(rel, StringComparison.OrdinalIgnoreCase)) ?? ncodeFiles.FirstOrDefault();
            }
            else
            {
                MainFileCombo.SelectedItem = ncodeFiles.FirstOrDefault(f => f.Equals("main.ncode", StringComparison.OrdinalIgnoreCase))
                    ?? ncodeFiles.FirstOrDefault(f => f.Equals("game.ncode", StringComparison.OrdinalIgnoreCase))
                    ?? ncodeFiles.FirstOrDefault();
            }

            var localIco = Directory.GetFiles(projectDir, "*.ico", SearchOption.TopDirectoryOnly).FirstOrDefault()
                ?? Directory.GetFiles(projectDir, "*.png", SearchOption.TopDirectoryOnly).FirstOrDefault(f => Path.GetFileName(f).Contains("icon", StringComparison.OrdinalIgnoreCase));
            if (localIco != null)
            {
                IconBox.Text = localIco;
            }
        }

        BrowseIconBtn.Click += BrowseIconClick;
        BrowseOutputBtn.Click += BrowseOutputClick;
        CancelBtn.Click += (_, _) => Close();
        BuildBtn.Click += BuildClick;
        OpenFolderBtn.Click += (_, _) => OpenCreatedFolder();
    }

    private async void BrowseIconClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выберите файл иконки для игры",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Иконка игры (*.ico, *.png)")
                {
                    Patterns = new[] { "*.ico", "*.png" }
                }
            }
        });

        if (files.Count > 0)
        {
            var path = files[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
            {
                IconBox.Text = path;
            }
        }
    }

    private async void BrowseOutputClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Выберите папку для сохранения сборки",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            var path = folders[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
            {
                OutputDirBox.Text = path;
            }
        }
    }

    private async void BuildClick(object? sender, RoutedEventArgs e)
    {
        string title = (TitleBox.Text ?? "").Trim();
        if (string.IsNullOrEmpty(title))
        {
            ShowError("Укажите название игры.");
            return;
        }

        string safeName = string.Join("_", title.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrEmpty(safeName)) safeName = "Game";

        string? selectedMain = MainFileCombo.SelectedItem as string;
        if (string.IsNullOrEmpty(selectedMain))
        {
            ShowError("Выберите главный скрипт запуска.");
            return;
        }

        string outBase = (OutputDirBox.Text ?? "").Trim();
        if (string.IsNullOrEmpty(outBase))
        {
            ShowError("Укажите папку для сохранения.");
            return;
        }

        string? ncodeCsproj = FindNcodeCsproj();
        if (string.IsNullOrEmpty(ncodeCsproj) || !File.Exists(ncodeCsproj))
        {
            ShowError("Не найден файл проекта Ncode.csproj для компиляции.");
            return;
        }

        BuildBtn.IsEnabled = false;
        CancelBtn.IsEnabled = false;
        OpenFolderBtn.IsVisible = false;
        BuildProgress.IsVisible = true;
        StatusCard.IsVisible = true;
        SetStatus("Подготовка ресурсов и упаковка в архив...", false);

        string iconPath = (IconBox.Text ?? "").Trim();
        bool resizable = ResizableCheck.IsChecked == true;
        bool fullscreen = FullscreenCheck.IsChecked == true;

        string? tempZip = null;
        string? tempPublish = null;

        try
        {
            string gameOutputDir = Path.Combine(outBase, safeName);
            Directory.CreateDirectory(gameOutputDir);

            tempZip = Path.GetTempFileName();
            tempPublish = Path.Combine(Path.GetTempPath(), "Ncode_Pub_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempPublish);

            await Task.Run(() =>
            {
                string stagingDir = Path.Combine(Path.GetTempPath(), "Ncode_Stage_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stagingDir);

                try
                {
                    var allFiles = Directory.GetFiles(_projectDir, "*.*", SearchOption.AllDirectories);
                    foreach (var file in allFiles)
                    {
                        string rel = Path.GetRelativePath(_projectDir, file);
                        if (rel.StartsWith(".git", StringComparison.OrdinalIgnoreCase) ||
                            rel.StartsWith("bin", StringComparison.OrdinalIgnoreCase) ||
                            rel.StartsWith("obj", StringComparison.OrdinalIgnoreCase) ||
                            rel.StartsWith("Build", StringComparison.OrdinalIgnoreCase) ||
                            rel.StartsWith("Сборка", StringComparison.OrdinalIgnoreCase) ||
                            rel.EndsWith(".nproject", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string target = Path.Combine(stagingDir, rel);
                        string? tDir = Path.GetDirectoryName(target);
                        if (!string.IsNullOrEmpty(tDir) && !Directory.Exists(tDir))
                        {
                            Directory.CreateDirectory(tDir);
                        }
                        File.Copy(file, target, true);
                    }

                    string? bundledIconName = null;
                    if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                    {
                        bundledIconName = Path.GetFileName(iconPath);
                        File.Copy(iconPath, Path.Combine(stagingDir, bundledIconName), true);
                    }

                    var config = new
                    {
                        title = title,
                        main = selectedMain,
                        resizable = resizable,
                        fullscreen = fullscreen,
                        icon = bundledIconName
                    };
                    string configJson = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(Path.Combine(stagingDir, "game.json"), configJson, new UTF8Encoding(false));

                    if (File.Exists(tempZip)) File.Delete(tempZip);
                    ZipFile.CreateFromDirectory(stagingDir, tempZip);
                }
                finally
                {
                    try { Directory.Delete(stagingDir, true); } catch { }
                }
            });

            SetStatus("Компиляция автономного исполняемого модуля...", false);

            bool success = await Task.Run(() =>
            {
                var cmdArgs = new StringBuilder();
                cmdArgs.Append($"publish \"{ncodeCsproj}\" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:GameOutputType=WinExe -p:GameTargetName={safeName} -o \"{tempPublish}\" --nologo");

                if (!string.IsNullOrEmpty(tempZip) && File.Exists(tempZip))
                {
                    cmdArgs.Append($" -p:GameBundleZip=\"{tempZip}\"");
                }

                if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath) && iconPath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                {
                    cmdArgs.Append($" -p:AppIcon=\"{iconPath}\"");
                }

                var psi = new ProcessStartInfo("dotnet", cmdArgs.ToString())
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using var proc = Process.Start(psi);
                if (proc == null) return false;
                var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                var stderrTask = proc.StandardError.ReadToEndAsync();
                proc.WaitForExit();
                Task.WaitAll(stdoutTask, stderrTask);
                return proc.ExitCode == 0;
            });

            if (!success)
            {
                ShowError("Не удалось скомпилировать исполняемый файл. Проверьте .NET SDK.");
                BuildBtn.IsEnabled = true;
                CancelBtn.IsEnabled = true;
                BuildProgress.IsVisible = false;
                return;
            }

            SetStatus("Копирование готового файла...", false);

            await Task.Run(() =>
            {
                string sourceExe = Path.Combine(tempPublish!, safeName + ".exe");
                if (!File.Exists(sourceExe))
                    sourceExe = Directory.GetFiles(tempPublish!, "*.exe").FirstOrDefault() ?? sourceExe;

                string destExe = Path.Combine(gameOutputDir, safeName + ".exe");
                if (File.Exists(sourceExe))
                    File.Copy(sourceExe, destExe, true);

                try { if (!string.IsNullOrEmpty(tempPublish)) Directory.Delete(tempPublish, true); } catch { }
                try { if (!string.IsNullOrEmpty(tempZip) && File.Exists(tempZip)) File.Delete(tempZip); } catch { }
            });

            _createdOutputDir = gameOutputDir;
            SetStatus($"Готово! Файл {safeName}.exe содержит все ресурсы внутри себя.", true);
            BuildProgress.IsVisible = false;
            BuildBtn.IsEnabled = true;
            BuildBtn.Content = "Собрать снова";
            CancelBtn.IsEnabled = true;
            OpenFolderBtn.IsVisible = true;
        }
        catch (Exception ex)
        {
            ShowError("Ошибка сборки: " + ex.Message);
            BuildBtn.IsEnabled = true;
            CancelBtn.IsEnabled = true;
            BuildProgress.IsVisible = false;
        }
    }

    private void SetStatus(string message, bool success)
    {
        StatusCard.IsVisible = true;
        StatusText.Text = message;
        if (success)
        {
            StatusCard.Background = new SolidColorBrush(Color.Parse("#192819"));
            StatusCard.BorderBrush = new SolidColorBrush(Color.Parse("#788F69"));
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#8FA87B"));
        }
        else
        {
            StatusCard.Background = new SolidColorBrush(Color.Parse("#1E1E1C"));
            StatusCard.BorderBrush = new SolidColorBrush(Color.Parse("#30302E"));
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#FAF9F5"));
        }
    }

    private void ShowError(string message)
    {
        StatusCard.IsVisible = true;
        StatusCard.Background = new SolidColorBrush(Color.Parse("#2A1B1B"));
        StatusCard.BorderBrush = new SolidColorBrush(Color.Parse("#B53333"));
        StatusText.Foreground = new SolidColorBrush(Color.Parse("#FAF9F5"));
        StatusText.Text = message;
    }

    private void OpenCreatedFolder()
    {
        if (!string.IsNullOrEmpty(_createdOutputDir) && Directory.Exists(_createdOutputDir))
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", _createdOutputDir) { UseShellExecute = true });
            }
            catch { }
        }
    }

    private static string? FindNcodeCsproj()
    {
        var baseDir = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(baseDir);
        for (int i = 0; i < 6 && dir != null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "Ncode", "Ncode.csproj");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
