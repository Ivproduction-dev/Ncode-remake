// Ncode — Russian programming language for games.
// Copyright (C) 2026 Ivproduction
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later.
// See LICENSE in the repository root.

using System;
using System.IO;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Ncode.Editor;

public partial class NewProjectWizard : Window
{
    public string? CreatedGameFile { get; private set; }

    public NewProjectWizard()
    {
        InitializeComponent();

        var defaultDocs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NcodeProjects");
        DirBox.Text = Directory.Exists(defaultDocs) ? defaultDocs : Environment.CurrentDirectory;

        CancelButton.Click += (_, _) => Close();
        NextButton.Click += (_, _) => GoStep2();
        NoButton.Click += (_, _) => GoStep1();
        YesButton.Click += (_, _) => Create();
        BrowseButton.Click += BrowseClick;

        NameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { GoStep2(); e.Handled = true; }
        };
    }

    private async void BrowseClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Выберите папку для проектов",
            AllowMultiple = false
        });
        if (folders.Count > 0)
        {
            var p = folders[0].TryGetLocalPath();
            if (p != null) DirBox.Text = p;
        }
    }

    private void GoStep1()
    {
        Step2.IsVisible = false;
        Step1.IsVisible = true;
        NoButton.IsVisible = false;
        YesButton.IsVisible = false;
        NextButton.IsVisible = true;
        CancelButton.IsVisible = true;
        SubtitleText.Text = "Шаг 1 из 2: Параметры и структура проекта";
        NameBox.Focus();
    }

    private void GoStep2()
    {
        var name = (NameBox.Text ?? "").Trim();
        if (name == "" || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            ErrorText.Text = "Некорректное имя. Используйте буквы, цифры, пробелы, дефис или подчёркивание.";
            ErrorBox.IsVisible = true;
            return;
        }

        var baseDir = (DirBox.Text ?? "").Trim();
        if (baseDir == "" || !Directory.Exists(baseDir))
        {
            try { Directory.CreateDirectory(baseDir); }
            catch
            {
                ErrorText.Text = "Не удалось подготовить указанную папку для проектов.";
                ErrorBox.IsVisible = true;
                return;
            }
        }

        ErrorBox.IsVisible = false;
        PathText.Text = Path.Combine(baseDir, name);
        Step1.IsVisible = false;
        Step2.IsVisible = true;
        NextButton.IsVisible = false;
        CancelButton.IsVisible = false;
        NoButton.IsVisible = true;
        YesButton.IsVisible = true;
        SubtitleText.Text = "Шаг 2 из 2: Подтверждение параметров";
    }

    private void Create()
    {
        var name = (NameBox.Text ?? "").Trim();
        var baseDir = (DirBox.Text ?? "").Trim();
        var dir = Path.Combine(baseDir, name);

        try
        {
            Directory.CreateDirectory(dir);
            if (Is2DCheck.IsChecked == true)
            {
                var settings = Path.Combine(dir, "settings.ncode");
                if (!File.Exists(settings))
                    File.WriteAllText(settings, "задать ширина 800\nзадать высота 600\nзадать заголовок \"" + name + "\"\n", new UTF8Encoding(false));

                var main = Path.Combine(dir, "main.ncode");
                if (!File.Exists(main))
                    File.WriteAllText(main, "подключить \"settings.ncode\"\nсоздать окно ширина высота заголовок\nзапустить сцену \"game.ncode\"\n", new UTF8Encoding(false));

                var game = Path.Combine(dir, "game.ncode");
                if (!File.Exists(game))
                    File.WriteAllText(game, "при запуске\n  вывести \"Добро пожаловать в игру " + name + "!\"\nконец\n", new UTF8Encoding(false));

                CreatedGameFile = main;
            }
            else
            {
                var game = Path.Combine(dir, "game.ncode");
                if (!File.Exists(game))
                    File.WriteAllText(game, "при запуске\n  вывести \"Привет из " + name + "!\"\nконец\n", new UTF8Encoding(false));

                CreatedGameFile = game;
            }
        }
        catch { }

        Close();
    }
}
