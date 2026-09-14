// Ncode — Russian programming language for games.
// Copyright (C) 2026 Ivproduction
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later.
// See LICENSE in the repository root.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace Ncode.Editor;

public partial class WelcomeWindow : Window
{
    public WelcomeWindow()
    {
        InitializeComponent();

        TryLoadLogo();

        MenuImport.Click += ImportProjectClick;
        MenuOpenFolder.Click += OpenFolderClick;
        CreateProjectFab.Click += CreateProjectClick;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        RefreshProjects();
    }

    private void TryLoadLogo()
    {
        try
        {
            var candidates = new[] { "icon.png", "logo.png", "ncode.png", "icon.ico" };
            var baseDir = Directory.GetCurrentDirectory();
            string? found = candidates
                .Select(c => Path.Combine(baseDir, c))
                .FirstOrDefault(File.Exists);

            if (found == null && Directory.GetParent(baseDir) != null)
            {
                var parent = Directory.GetParent(baseDir)!.FullName;
                found = candidates
                    .Select(c => Path.Combine(parent, c))
                    .FirstOrDefault(File.Exists);
            }

            if (found != null && File.Exists(found))
            {
                AppLogoImage.Source = new Bitmap(found);
                AppLogoImage.IsVisible = true;
                FallbackLogo.IsVisible = false;
            }
        }
        catch { }
    }

    private void RefreshProjects()
    {
        ProjectsPanel.Children.Clear();
        var projects = ProjectManager.Load();

        if (projects.Count == 0)
        {
            EmptyState.IsVisible = true;
            return;
        }

        EmptyState.IsVisible = false;

        foreach (var proj in projects)
        {
            var card = CreateProjectCard(proj);
            ProjectsPanel.Children.Add(card);
        }
    }

    private Border CreateProjectCard(ProjectItem project)
    {
        var card = new Border
        {
            Width = 220,
            Height = 160,
            Margin = new Thickness(10),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.Parse("#1E1E1C")),
            BorderBrush = new SolidColorBrush(Color.Parse("#30302E")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 14),
            Cursor = new Cursor(StandardCursorType.Hand)
        };

        card.PointerEntered += (_, _) =>
        {
            card.Background = new SolidColorBrush(Color.Parse("#262624"));
            card.BorderBrush = new SolidColorBrush(Color.Parse("#C96442"));
        };

        card.PointerExited += (_, _) =>
        {
            card.Background = new SolidColorBrush(Color.Parse("#1E1E1C"));
            card.BorderBrush = new SolidColorBrush(Color.Parse("#30302E"));
        };

        card.PointerPressed += (_, _) => LaunchProject(project.Path);

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto")
        };

        var topDock = new DockPanel();

        var iconBorder = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.Parse("#2D2D2A")),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var iconText = new TextBlock
        {
            Text = project.Name.Length > 0 ? project.Name.Substring(0, 1).ToUpperInvariant() : "N",
            Foreground = new SolidColorBrush(Color.Parse("#C96442")),
            FontWeight = FontWeight.Bold,
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        iconBorder.Child = iconText;
        DockPanel.SetDock(iconBorder, Dock.Left);
        topDock.Children.Add(iconBorder);

        var moreBtn = new Button
        {
            Content = "...",
            Classes = { "card-more" }
        };
        ToolTip.SetTip(moreBtn, "Опции");
        moreBtn.PointerPressed += (_, e) => e.Handled = true;
        DockPanel.SetDock(moreBtn, Dock.Right);

        var flyout = new MenuFlyout();

        var exportItem = new MenuItem { Header = "Экспортировать проект..." };
        exportItem.Click += async (_, _) =>
        {
            await ProjectExporter.ExportAsync(this, project.Path);
        };

        var explorerItem = new MenuItem { Header = "Показать в Проводнике" };
        explorerItem.Click += (_, _) =>
        {
            try
            {
                if (OperatingSystem.IsWindows())
                    Process.Start(new ProcessStartInfo("explorer.exe", project.Path) { UseShellExecute = true });
                else if (OperatingSystem.IsMacOS())
                    Process.Start(new ProcessStartInfo("open", $"\"{project.Path}\"") { UseShellExecute = true });
                else
                    Process.Start(new ProcessStartInfo("xdg-open", $"\"{project.Path}\"") { UseShellExecute = true });
            }
            catch { }
        };

        var removeItem = new MenuItem { Header = "Удалить из списка" };
        removeItem.Click += (_, _) =>
        {
            ProjectManager.Remove(project.Path);
            RefreshProjects();
        };

        flyout.Items.Add(exportItem);
        flyout.Items.Add(explorerItem);
        flyout.Items.Add(removeItem);
        moreBtn.Flyout = flyout;

        topDock.Children.Add(moreBtn);
        Grid.SetRow(topDock, 0);
        grid.Children.Add(topDock);

        var nameBlock = new TextBlock
        {
            Text = project.Name,
            FontSize = 15,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#FAF9F5")),
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 44,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetRow(nameBlock, 1);
        grid.Children.Add(nameBlock);

        var pathBlock = new TextBlock
        {
            Text = Path.GetFileName(project.Path),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.Parse("#87867F")),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetRow(pathBlock, 2);
        grid.Children.Add(pathBlock);

        card.Child = grid;
        return card;
    }

    private async void CreateProjectClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var wizard = new NewProjectWizard();
        await wizard.ShowDialog(this);
        if (!string.IsNullOrEmpty(wizard.CreatedGameFile))
        {
            var dir = Path.GetDirectoryName(wizard.CreatedGameFile);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                ProjectManager.AddOrUpdate(dir);
                LaunchProject(dir);
            }
        }
    }

    private async void OpenFolderClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Выберите папку проекта Ncode",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            var path = folders[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                ProjectManager.AddOrUpdate(path);
                LaunchProject(path);
            }
        }
    }

    private async void ImportProjectClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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
            ProjectManager.AddOrUpdate(dlg.ResultProjectDir);
            RefreshProjects();
            LaunchProject(dlg.ResultProjectDir);
        }
    }

    private void LaunchProject(string dir)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

        ProjectManager.AddOrUpdate(dir);

        var mainWin = new MainWindow(dir);
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = mainWin;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        }
        mainWin.Show();
        Close();
    }
}
