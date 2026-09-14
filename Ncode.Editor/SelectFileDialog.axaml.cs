using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;

namespace Ncode.Editor;

public partial class SelectFileDialog : Window
{
    public string? SelectedFile { get; private set; }

    public SelectFileDialog() : this(new List<string>(), ".") { }

    public SelectFileDialog(List<string> files, string projectDir, string prompt = "game.ncode не найден. Что открыть?")
    {
        InitializeComponent();
        PromptText.Text = prompt;
        foreach (var f in files)
            FileCombo.Items.Add(new ComboBoxItem { Content = Path.GetRelativePath(projectDir, f), Tag = f });
        if (FileCombo.Items.Count > 0)
            FileCombo.SelectedIndex = 0;
        CancelButton2.Click += (_, _) => Close();
        OpenButton.Click += (_, _) =>
        {
            if (FileCombo.SelectedItem is ComboBoxItem { Tag: string p })
                SelectedFile = p;
            Close();
        };
    }
}
