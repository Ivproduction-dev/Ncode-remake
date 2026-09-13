using System;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Ncode.Editor;

public partial class NewProjectWizard : Window
{
    public string? CreatedGameFile { get; private set; }
    private readonly string baseDir = Environment.CurrentDirectory;

    public NewProjectWizard()
    {
        InitializeComponent();
        CancelButton.Click += (_, _) => Close();
        NextButton.Click += (_, _) => GoStep2();
        NoButton.Click += (_, _) => GoStep1();
        YesButton.Click += (_, _) => Create();
        NameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { GoStep2(); e.Handled = true; }
        };
    }

    private void GoStep1()
    {
        Step2.IsVisible = false;
        Step1.IsVisible = true;
        NameBox.Focus();
    }

    private void GoStep2()
    {
        var name = (NameBox.Text ?? "").Trim();
        if (name == "" || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            ErrorText.Text = "Плохое имя. Только буквы, цифры, пробелы, _ и -";
            ErrorText.IsVisible = true;
            return;
        }
        ErrorText.IsVisible = false;
        PathText.Text = "Папка: " + Path.Combine(baseDir, name);
        Step1.IsVisible = false;
        Step2.IsVisible = true;
    }

    private void Create()
    {
        var name = (NameBox.Text ?? "").Trim();
        var dir = Path.Combine(baseDir, name);
        Directory.CreateDirectory(dir);
        var game = Path.Combine(dir, "game.ncode");
        if (!File.Exists(game))
            File.WriteAllText(game, "при запуске\n  вывести \"Это мой проект!\"\nконец\n", new UTF8Encoding(false));
        CreatedGameFile = game;
        Close();
    }
}
