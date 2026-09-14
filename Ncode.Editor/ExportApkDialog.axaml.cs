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
            const string keystorePass = "ncode123";
            const string keyAlias = "ncode";
            bool useSigning = false;
            string signingArgs = "";
            try
            {
                if (!File.Exists(keystorePath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(keystorePath)!);
                    var ktPsi = new ProcessStartInfo("keytool", $"-genkeypair -keystore \"{keystorePath}\" -alias {keyAlias} -keyalg RSA -keysize 2048 -validity 10000 -storepass {keystorePass} -keypass {keystorePass} -dname \"CN=Ncode,O=Ivproduction,C=RU\"")
                    { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                    using var kt = Process.Start(ktPsi);
                    if (kt != null) { kt.WaitForExit(15000); try { kt.Kill(true); } catch { } }
                }
                if (File.Exists(keystorePath))
                {
                    useSigning = true;
                    signingArgs = $" -p:AndroidKeyStore=true -p:AndroidSigningKeyStore=\"{keystorePath}\" -p:AndroidSigningKeyAlias={keyAlias} -p:AndroidSigningKeyPass={keystorePass} -p:AndroidSigningStorePass={keystorePass}";
                }
            }
            catch { }

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

            bool success = await Task.Run(() =>
            {
                var args = new StringBuilder();
                args.Append($"publish \"{androidCsproj}\" -c Release -f net8.0-android -p:GameBundleZip=\"{tempZip}\" -p:ApplicationId={package} -p:ApplicationVersion={versionCode} -p:ApplicationDisplayVersion={version} -p:ApplicationTitle=\"{title}\" -o \"{tempPublish}\" --nologo");
                if (useSigning) args.Append(signingArgs);
                var psi = new ProcessStartInfo("dotnet", args.ToString())
                {
                    CreateNoWindow = true, UseShellExecute = false,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
                };
                using var proc = Process.Start(psi);
                if (proc == null) return false;
                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit();
                Dispatcher.UIThread.Post(() =>
                {
                    if (!string.IsNullOrWhiteSpace(stdout)) StatusText.Text = stdout.Length > 800 ? stdout[^800..] : stdout;
                    if (proc.ExitCode != 0 && !string.IsNullOrWhiteSpace(stderr)) StatusText.Text += "\n" + (stderr.Length > 800 ? stderr[^800..] : stderr);
                });
                return proc.ExitCode == 0;
            });

            if (!success)
            {
                ShowError("Сборка .apk не удалась. Нужен Android workload: dotnet workload install android + Android SDK. См. вывод выше.");
                BuildBtn.IsEnabled = true; CancelBtn.IsEnabled = true; BuildProgress.IsVisible = false;
                return;
            }

            SetStatus("Копирование .apk...", false);
            await Task.Run(() =>
            {
                var apk = Directory.GetFiles(tempPublish!, "*.apk", SearchOption.AllDirectories).FirstOrDefault();
                if (apk != null && File.Exists(apk))
                {
                    string dest = Path.Combine(gameOutputDir, safeName + ".apk");
                    File.Copy(apk, dest, true);
                }
                if (BuildAabCheck.IsChecked == true)
                {
                    try
                    {
                        var aabArgs = new StringBuilder();
                        aabArgs.Append($"publish \"{androidCsproj}\" -c Release -f net8.0-android -p:GameBundleZip=\"{tempZip}\" -p:ApplicationId={package} -p:ApplicationVersion={versionCode} -p:ApplicationDisplayVersion={version} -p:ApplicationTitle=\"{title}\" -p:AndroidPackageFormat=aab -o \"{tempPublish}_aab\" --nologo");
                        if (useSigning) aabArgs.Append(signingArgs);
                        var aabPsi = new ProcessStartInfo("dotnet", aabArgs.ToString()) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                        using var aabProc = Process.Start(aabPsi);
                        if (aabProc != null) { aabProc.WaitForExit(300000); }
                        var aab = Directory.GetFiles(tempPublish + "_aab", "*.aab", SearchOption.AllDirectories).FirstOrDefault();
                        if (aab != null && File.Exists(aab)) File.Copy(aab, Path.Combine(gameOutputDir, safeName + ".aab"), true);
                        try { Directory.Delete(tempPublish + "_aab", true); } catch { }
                    }
                    catch { }
                }
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
