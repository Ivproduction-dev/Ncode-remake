// Ncode — Russian programming language for games.
// Copyright (C) 2026 Ivproduction
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later.
// See LICENSE in the repository root.

using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;

namespace Ncode.Editor;

public partial class ProjectOptionsDialog : Window
{
    private readonly string _projectDir;
    private string? _mainFile;

    public ProjectOptionsDialog() : this("") { }

    public ProjectOptionsDialog(string projectDir)
    {
        InitializeComponent();
        _projectDir = projectDir;

        if (string.IsNullOrEmpty(_projectDir) || !Directory.Exists(_projectDir))
        {
            ProjectNameText.Text = "Не выбрано";
            ProjectSubtitleText.Text = "Проект не открыт";
            ProjectPathText.Text = "-";
            MainFileText.Text = "-";
            ExportBtn.IsEnabled = false;
            ChangeMainFileBtn.IsEnabled = false;
            ExeBuildSection.IsVisible = false;
        }
        else
        {
            ProjectNameText.Text = Path.GetFileName(_projectDir);
            ProjectPathText.Text = _projectDir;

            var main = Path.Combine(_projectDir, "main.ncode");
            if (File.Exists(main))
            {
                _mainFile = main;
            }
            else
            {
                _mainFile = Directory.GetFiles(_projectDir, "*.ncode", SearchOption.AllDirectories).FirstOrDefault();
            }

            UpdateMainFileDisplay();

            bool is2D = Is2DProject(_projectDir);
            SetProjectTypeBadge(is2D);

            ProjectSubtitleText.Text = is2D ? "2D проект — графическое окно" : "Консольный проект";

            ExeBuildSection.IsVisible = is2D;
        }

        ChangeMainFileBtn.Click += async (_, _) =>
        {
            var picked = await ProjectExporter.PickMainFileAsync(this, _projectDir);
            if (!string.IsNullOrEmpty(picked))
            {
                _mainFile = picked;
                UpdateMainFileDisplay();
            }
        };

        ExportBtn.Click += async (_, _) =>
        {
            StatusText.IsVisible = false;
            bool success = await ProjectExporter.ExportAsync(this, _projectDir, _mainFile);
            if (success)
            {
                StatusText.Foreground = new SolidColorBrush(Color.Parse("#8FA87B"));
                StatusText.Text = "Проект успешно экспортирован!";
                StatusText.IsVisible = true;
            }
        };

        ExportExeBtn.Click += async (_, _) =>
        {
            var dlg = new ExportExeDialog(_projectDir, _mainFile);
            await dlg.ShowDialog(this);
        };

        CloseButton.Click += (_, _) => Close();
    }

    private void UpdateMainFileDisplay()
    {
        if (!string.IsNullOrEmpty(_mainFile))
        {
            MainFileText.Text = Path.GetRelativePath(_projectDir, _mainFile);
        }
        else
        {
            MainFileText.Text = "Не назначен";
        }
    }

    private void SetProjectTypeBadge(bool is2D)
    {
        if (is2D)
        {
            ProjectTypeBadge.BorderBrush = new SolidColorBrush(Color.Parse("#3A5A2A"));
            ProjectTypeText.Text = "2D";
            ProjectTypeText.Foreground = new SolidColorBrush(Color.Parse("#7AB868"));
        }
        else
        {
            ProjectTypeBadge.BorderBrush = new SolidColorBrush(Color.Parse("#30302E"));
            ProjectTypeText.Text = "CON";
            ProjectTypeText.Foreground = new SolidColorBrush(Color.Parse("#5E5D59"));
        }
    }

    private static bool Is2DProject(string projectDir)
    {
        if (File.Exists(Path.Combine(projectDir, "settings.ncode")))
            return true;

        string[] keywords = ["создать окно", "нарисовать", "образ", "сцена", "спрайт", "холст", "draw", "window"];

        try
        {
            var ncodeFiles = Directory.GetFiles(projectDir, "*.ncode", SearchOption.AllDirectories);
            foreach (var file in ncodeFiles)
            {
                string content = File.ReadAllText(file);
                foreach (var kw in keywords)
                {
                    if (content.Contains(kw, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }
}
