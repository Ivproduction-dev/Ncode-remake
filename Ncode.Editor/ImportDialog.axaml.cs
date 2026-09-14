using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Ncode.Editor;

public partial class ImportDialog : Window
{
    private static readonly string[] Facts = new[]
    {
        "Ncode Engine поддерживает гибкую компонентную архитектуру сущностей.",
        "Скрипты .ncode компилируются на лету для максимальной скорости работы.",
        "Пакет .nproject содержит все исходники, скрипты и ассеты вашего проекта.",
        "Встроенная подсветка синтаксиса оптимизирована под ключевые слова Ncode.",
        "Горячая клавиша F5 позволяет мгновенно протестировать изменения в игре.",
        "Редактор автоматически валидирует манифест проекта при распаковке.",
        "Движок Ncode использует эффективное управление ресурсами и памятью.",
        "Все ресурсы упакованы со сжатием для экономии места на диске."
    };

    private readonly DispatcherTimer _factTimer;
    private int _factIndex;
    private readonly string _archivePath;
    private readonly string _destinationDir;

    public string? ResultProjectDir { get; private set; }
    public bool Success { get; private set; }

    public ImportDialog() : this("", "") { }

    public ImportDialog(string archivePath, string destinationDir)
    {
        InitializeComponent();
        _archivePath = archivePath;
        _destinationDir = destinationDir;

        _factTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
        _factTimer.Tick += (_, _) =>
        {
            _factIndex = (_factIndex + 1) % Facts.Length;
            FactText.Text = Facts[_factIndex];
        };

        CloseButton.Click += (_, _) => Close();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _factTimer.Start();
        _ = RunImportAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _factTimer.Stop();
        base.OnClosed(e);
    }

    private async Task RunImportAsync()
    {
        try
        {
            StatusText.Text = "Чтение архива...";
            await Task.Delay(400);

            if (!File.Exists(_archivePath))
            {
                StatusText.Text = "Файл архива не найден.";
                ImportProgress.IsIndeterminate = false;
                CloseButton.IsVisible = true;
                return;
            }

            string projName = Path.GetFileNameWithoutExtension(_archivePath);
            string targetDir = Path.Combine(_destinationDir, projName);
            int suffix = 1;
            while (Directory.Exists(targetDir))
            {
                targetDir = Path.Combine(_destinationDir, $"{projName}_{suffix++}");
            }

            Directory.CreateDirectory(targetDir);

            StatusText.Text = "Распаковка файлов проекта...";
            await Task.Run(() =>
            {
                string targetFullPath = Path.GetFullPath(targetDir);
                if (!targetFullPath.EndsWith(Path.DirectorySeparatorChar))
                    targetFullPath += Path.DirectorySeparatorChar;

                using var zip = ZipFile.OpenRead(_archivePath);
                foreach (var entry in zip.Entries)
                {
                    string fullPath = Path.GetFullPath(Path.Combine(targetDir, entry.FullName));
                    if (!fullPath.StartsWith(targetFullPath, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string? dir = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    if (!string.IsNullOrEmpty(entry.Name))
                    {
                        entry.ExtractToFile(fullPath, overwrite: true);
                    }
                }
            });

            StatusText.Text = "Проект успешно распакован!";
            ImportProgress.IsIndeterminate = false;
            ImportProgress.Value = 100;
            Success = true;
            ResultProjectDir = targetDir;

            await Task.Delay(600);
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка импорта: " + ex.Message;
            ImportProgress.IsIndeterminate = false;
            CloseButton.IsVisible = true;
        }
    }
}
