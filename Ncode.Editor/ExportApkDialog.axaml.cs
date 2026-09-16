// Ncode — Russian programming language for games.
// Copyright (C) 2026 Ivproduction
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later.
// See LICENSE in the repository root.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Ncode.Editor;

public partial class ExportApkDialog : Window
{
    private readonly string _projectDir;
    private string? _createdOutputDir;

    public ExportApkDialog() : this("") { }

    public ExportApkDialog(string projectDir, string? defaultMainFile = null)
    {
        InitializeComponent();
        _projectDir = projectDir;

        string projName = !string.IsNullOrEmpty(projectDir) ? Path.GetFileName(projectDir) : "Игра";
        TitleBox.Text = projName;
        PackageBox.Text = "com.ivproduction." + Regex.Replace(projName.ToLowerInvariant(), @"[^a-z0-9]+", "");
        if (PackageBox.Text != null && PackageBox.Text.Length < 5) PackageBox.Text = "com.ivproduction.game";

        string defaultBuildDir = !string.IsNullOrEmpty(projectDir)
            ? Path.Combine(projectDir, "Сборка_Android")
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

            var localIcon = Directory.GetFiles(projectDir, "*.png", SearchOption.TopDirectoryOnly).FirstOrDefault(f => Path.GetFileName(f).Contains("icon", StringComparison.OrdinalIgnoreCase));
            if (localIcon != null) IconBox.Text = localIcon;
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
            Title = "Выберите иконку для Android (512x512 png)",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("PNG (*.png)") { Patterns = new[] { "*.png" } } }
        });
        if (files.Count > 0)
        {
            var path = files[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(path)) IconBox.Text = path;
        }
    }

    private async void BrowseOutputClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Папка для сохранения .apk", AllowMultiple = false });
        if (folders.Count > 0)
        {
            var path = folders[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(path)) OutputDirBox.Text = path;
        }
    }

    private async void BuildClick(object? sender, RoutedEventArgs e)
    {
        string title = (TitleBox.Text ?? "").Trim();
        if (string.IsNullOrEmpty(title)) { ShowError("Укажите название игры."); return; }
        string package = (PackageBox.Text ?? "").Trim();
        if (!Regex.IsMatch(package, @"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$")) { ShowError("Package ID неверный. Пример: com.ivproduction.mygame"); return; }
        string safeName = string.Join("_", title.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrEmpty(safeName)) safeName = "Game";
        string? selectedMain = MainFileCombo.SelectedItem as string;
        if (string.IsNullOrEmpty(selectedMain)) { ShowError("Выберите главный скрипт."); return; }
        string outBase = (OutputDirBox.Text ?? "").Trim();
        if (string.IsNullOrEmpty(outBase)) { ShowError("Укажите папку для сохранения."); return; }
        string version = (VersionBox.Text ?? "").Trim();
        if (string.IsNullOrEmpty(version)) version = "1.0";
        string orientation = ((OrientationCombo.SelectedItem as ComboBoxItem)?.Tag as string) ?? "Unspecified";
        if (orientation is not ("Portrait" or "Landscape" or "Unspecified")) orientation = "Unspecified";
        int versionCode = 1;
        var vm = Regex.Match(version, @"^(\d+)\.(\d+)(?:\.(\d+))?");
        if (vm.Success)
        {
            int.TryParse(vm.Groups[1].Value, out int maj);
            int.TryParse(vm.Groups[2].Value, out int min);
            int.TryParse(vm.Groups[3].Value, out int pat);
            versionCode = maj * 10000 + min * 100 + pat;
            if (versionCode < 1) versionCode = 1;
        }
        else if (int.TryParse(version, out int v)) versionCode = v;

        string? androidCsproj = FindAndroidCsproj();
        if (string.IsNullOrEmpty(androidCsproj) || !File.Exists(androidCsproj)) { ShowError("Не найден Ncode.Android.csproj"); return; }

        var dotnetCheck = await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo("dotnet", "--version") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true };
                using var p = Process.Start(psi);
                p?.WaitForExit(5000);
                return p?.ExitCode == 0;
            }
            catch { return false; }
        });
        if (!dotnetCheck) { ShowError("dotnet не найден. Установите .NET 8 SDK."); return; }

        BuildBtn.IsEnabled = false;
        CancelBtn.IsEnabled = false;
        OpenFolderBtn.IsVisible = false;
        BuildProgress.IsVisible = true;
        StatusCard.IsVisible = true;
        SetStatus("Подготовка ресурсов...", false);

        string iconPath = (IconBox.Text ?? "").Trim();
        string? tempZip = null;
        string? tempPublish = null;

        try
        {
            string gameOutputDir = Path.Combine(outBase, safeName + "_apk");
            Directory.CreateDirectory(gameOutputDir);
            tempZip = Path.GetTempFileName();
            tempPublish = Path.Combine(Path.GetTempPath(), "Ncode_ApkPub_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempPublish);

            await Task.Run(() =>
            {
                string stagingDir = Path.Combine(Path.GetTempPath(), "Ncode_ApkStage_" + Guid.NewGuid().ToString("N"));
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
                            continue;
                        string target = Path.Combine(stagingDir, rel);
                        string? tDir = Path.GetDirectoryName(target);
                        if (!string.IsNullOrEmpty(tDir) && !Directory.Exists(tDir)) Directory.CreateDirectory(tDir);
                        File.Copy(file, target, true);
                    }
                    string? bundledIconName = null;
                    if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                    {
                        bundledIconName = Path.GetFileName(iconPath);
                        File.Copy(iconPath, Path.Combine(stagingDir, bundledIconName), true);
                    }
                    var config = new { title = title, main = selectedMain, icon = bundledIconName, package = package, version = version };
                    string configJson = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(Path.Combine(stagingDir, "game.json"), configJson, new UTF8Encoding(false));
                    if (File.Exists(tempZip)) File.Delete(tempZip);
                    ZipFile.CreateFromDirectory(stagingDir, tempZip);
                }
                finally { try { Directory.Delete(stagingDir, true); } catch { } }
            });

            SetStatus("Сборка .apk (требует Android SDK, может занять минуты)...", false);

            string keystorePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ncode", "ncode.keystore");
            string keystorePass = "ncode123";
            string keyAlias = "ncode";
            string keyPass = "ncode123";
            bool useSigning = false;
            string signingArgs = "";

            bool needKeystore = !File.Exists(keystorePath);
            if (needKeystore)
            {
                var ksDlg = new CreateKeystoreDialog(isForAab: false, defaultDir: Path.GetDirectoryName(keystorePath));
                await ksDlg.ShowDialog(this);
                if (string.IsNullOrEmpty(ksDlg.ResultKeystorePath))
                {
                    ShowError("Keystore не создан — сборка отменена. Без подписи APK не установится как обновление.");
                    BuildBtn.IsEnabled = true; CancelBtn.IsEnabled = true; BuildProgress.IsVisible = false;
                    return;
                }
                keystorePath = ksDlg.ResultKeystorePath!;
                keystorePass = ksDlg.ResultStorePass;
                keyPass = ksDlg.ResultKeyPass;
                keyAlias = ksDlg.ResultAlias;
            }

            SetStatus("Шаг 1/2 — компиляция проекта...", false);
            await Task.Delay(400);

            SetStatus("Проверка Android SDK...", false);
            var envCheck = await CheckAndroidEnvAsync();
            if (!envCheck.WorkloadOk)
            {
                ShowError("Android workload не установлен." + envCheck.Details + "\nЗапустите install.bat от имени администратора или выполните: dotnet workload install android");
                BuildBtn.IsEnabled = true; CancelBtn.IsEnabled = true; BuildProgress.IsVisible = false;
                return;
            }
            if (!envCheck.Sdk11Ok)
            {
                ShowError("Для сборки Android нужен .NET 11 SDK (проект net11.0-android)." + envCheck.Details + "\nУстановите: https://dotnet.microsoft.com/download");
                BuildBtn.IsEnabled = true; CancelBtn.IsEnabled = true; BuildProgress.IsVisible = false;
                return;
            }

            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
            {
                try
                {
                    string androidProjDir = Path.GetDirectoryName(androidCsproj) ?? "";
                    foreach (var dpi in new[] { "mipmap-hdpi", "mipmap-mdpi", "mipmap-xhdpi", "mipmap-xxhdpi", "mipmap-xxxhdpi" })
                    {
                        string dst = Path.Combine(androidProjDir, "Resources", dpi, "appicon.png");
                        File.Copy(iconPath, dst, true);
                        string dstFg = Path.Combine(androidProjDir, "Resources", dpi, "appicon_foreground.png");
                        if (File.Exists(dstFg)) File.Copy(iconPath, dstFg, true);
                    }
                }
                catch { }
            }

            SetStatus("Шаг 2/2 — сборка .apk...", false);
            bool success = false;
            string lastStdout = "";
            string lastStderr = "";
            await Task.Run(async () =>
            {
                var stdoutSb = new StringBuilder();
                var stderrSb = new StringBuilder();
                var args = new StringBuilder();
                args.Append($"publish \"{androidCsproj}\" -c Release -f net11.0-android -p:GameBundleZip=\"{tempZip}\" -p:ApplicationId={package} -p:ApplicationVersion={versionCode} -p:ApplicationDisplayVersion={version} -p:ApplicationTitle=\"{title}\" -p:AndroidScreenOrientation={orientation} -o \"{tempPublish}\" --nologo");
                if (useSigning) args.Append(signingArgs);
                var psi = new ProcessStartInfo("dotnet", args.ToString())
                {
                    CreateNoWindow = true, UseShellExecute = false,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
                };
                using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                proc.OutputDataReceived += (_, e) => { if (e.Data != null) { lock (stdoutSb) stdoutSb.AppendLine(e.Data); Dispatcher.UIThread.Post(() => { StatusText.Text = e.Data!; }); } };
                proc.ErrorDataReceived += (_, e) => { if (e.Data != null) { lock (stderrSb) stderrSb.AppendLine(e.Data); Dispatcher.UIThread.Post(() => { StatusText.Text = e.Data!; }); } };
                try
                {
                    if (!proc.Start()) { success = false; return; }
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();
                    var exited = await Task.Run(() => proc.WaitForExit(600000));
                    if (!exited)
                    {
                        try { proc.Kill(true); } catch { }
                        lock (stderrSb) stderrSb.AppendLine("[ОШИБКА] Сборка зависла и была прервана по таймауту 10 минут. Проверьте Android SDK и workload: dotnet workload install android");
                        success = false;
                    }
                    else
                    {
                        success = proc.ExitCode == 0;
                    }
                    lock (stdoutSb) lastStdout = stdoutSb.ToString();
                    lock (stderrSb) lastStderr = stderrSb.ToString();
                }
                catch (Exception ex)
                {
                    lock (stderrSb) lastStderr = ex.Message;
                    success = false;
                }
            });
            if (!string.IsNullOrWhiteSpace(lastStdout) || !string.IsNullOrWhiteSpace(lastStderr))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    string combined = (lastStdout + "\n" + lastStderr).Trim();
                    if (!string.IsNullOrWhiteSpace(combined))
                        StatusText.Text = combined.Length > 2000 ? combined[^2000..] : combined;
                });
                await Task.Delay(100);
            }

            if (!success)
            {
                ShowError("Сборка .apk не удалась. Запустите install.bat от имени администратора — он поставит Android workload, SDK и JDK 17.\nИли вручную: dotnet workload install android\nСм. вывод выше.");
                BuildBtn.IsEnabled = true; CancelBtn.IsEnabled = true; BuildProgress.IsVisible = false;
                return;
            }

            SetStatus("Копирование .apk...", false);
            string? builtApk = null;
            bool bundleInside = false;
            await Task.Run(() =>
            {
                builtApk = Directory.GetFiles(tempPublish!, "*.apk", SearchOption.AllDirectories).FirstOrDefault();
                if (builtApk != null && File.Exists(builtApk))
                {
                    try
                    {
                        using var archive = new ZipArchive(File.OpenRead(builtApk), ZipArchiveMode.Read);
                        bundleInside = archive.Entries.Any(e => e.FullName.Equals("assets/GameBundle.zip", StringComparison.OrdinalIgnoreCase));
                    }
                    catch { }
                }
            });
            if (string.IsNullOrEmpty(builtApk) || !File.Exists(builtApk))
            {
                ShowError("Сборка прошла, но .apk файл не создан. См. вывод выше.");
                BuildBtn.IsEnabled = true; CancelBtn.IsEnabled = true; BuildProgress.IsVisible = false;
                return;
            }
            if (!bundleInside)
            {
                ShowError("APK собран, но игра внутрь не попала (нет assets/GameBundle.zip).\nПересоберите ещё раз или сообщите разработчику.");
                try { if (!string.IsNullOrEmpty(tempPublish)) Directory.Delete(tempPublish, true); } catch { }
                try { if (!string.IsNullOrEmpty(tempZip) && File.Exists(tempZip)) File.Delete(tempZip); } catch { }
                BuildBtn.IsEnabled = true; CancelBtn.IsEnabled = true; BuildProgress.IsVisible = false;
                return;
            }
            await Task.Run(() =>
            {
                string dest = Path.Combine(gameOutputDir, safeName + ".apk");
                File.Copy(builtApk!, dest, true);
                try { if (!string.IsNullOrEmpty(tempPublish)) Directory.Delete(tempPublish, true); } catch { }
                try { if (!string.IsNullOrEmpty(tempZip) && File.Exists(tempZip)) File.Delete(tempZip); } catch { }
            });

            _createdOutputDir = gameOutputDir;
            SetStatus($"Готово! APK в {gameOutputDir}", true);
            BuildProgress.IsVisible = false;
            BuildBtn.IsEnabled = true; BuildBtn.Content = "Собрать снова"; CancelBtn.IsEnabled = true; OpenFolderBtn.IsVisible = true;
        }
        catch (Exception ex)
        {
            ShowError("Ошибка сборки: " + ex.Message);
            BuildBtn.IsEnabled = true; CancelBtn.IsEnabled = true; BuildProgress.IsVisible = false;
        }
    }

    private sealed record AndroidEnvCheck(bool WorkloadOk, bool Sdk11Ok, string Details);

    private static async Task<(string Output, bool TimedOut)> RunDotnetAsync(string args, int timeoutMs)
    {
        var outSb = new StringBuilder();
        var errSb = new StringBuilder();
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo("dotnet", args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) outSb.AppendLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) errSb.AppendLine(e.Data); };
            if (!proc.Start()) return ("dotnet: не удалось запустить процесс", false);
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            using var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                await proc.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(true); } catch { }
                return (outSb.ToString() + "\n" + errSb.ToString(), true);
            }
            return (outSb.ToString() + "\n" + errSb.ToString(), false);
        }
        catch (Exception ex)
        {
            return ("dotnet: " + ex.Message, false);
        }
    }

    private static async Task<AndroidEnvCheck> CheckAndroidEnvAsync()
    {
        var (versionOut, _) = await RunDotnetAsync("--version", 20000);
        var (sdksOut, _) = await RunDotnetAsync("--list-sdks", 20000);
        var (workloadOut, workloadTimedOut) = await RunDotnetAsync("workload list", 90000);
        bool workloadOk = workloadOut.IndexOf("android", StringComparison.OrdinalIgnoreCase) >= 0;
        bool sdk11Ok = false;
        foreach (var line in (versionOut + "\n" + sdksOut).Split('\n'))
        {
            if (Regex.IsMatch(line.Trim(), @"^(1[1-9]|[2-9][0-9])\.\d+")) { sdk11Ok = true; break; }
        }
        string version = versionOut.Split('\n').FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))?.Trim() ?? "?";
        var sb = new StringBuilder();
        sb.Append("\ndotnet --version: ").Append(version);
        if (workloadTimedOut) sb.Append("\n(dotnet workload list превысил таймаут 90с)");
        sb.Append("\nУстановленные SDK:\n").Append(sdksOut.Trim());
        return new AndroidEnvCheck(workloadOk, sdk11Ok, sb.ToString());
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
            try { Process.Start(new ProcessStartInfo("explorer.exe", _createdOutputDir) { UseShellExecute = true }); } catch { }
        }
    }

    private static string? FindAndroidCsproj()
    {
        var baseDir = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(baseDir);
        for (int i = 0; i < 6 && dir != null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "Ncode.Android", "Ncode.Android.csproj");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
