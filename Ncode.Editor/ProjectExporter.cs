using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Ncode.Editor;

public static class ProjectExporter
{
    public static async Task<bool> ExportAsync(Window owner, string projectDir, string? forcedMainFile = null)
    {
        if (string.IsNullOrEmpty(projectDir) || !Directory.Exists(projectDir))
            return false;

        string? mainFilePath = forcedMainFile;

        if (string.IsNullOrEmpty(mainFilePath))
        {
            var defaultMain = Path.Combine(projectDir, "main.ncode");
            if (File.Exists(defaultMain))
            {
                bool confirmed = await ConfirmDialog.Show(owner, "Главный файл проекта: main.ncode. Все верно?", "Экспорт проекта");
                if (confirmed)
                {
                    mainFilePath = defaultMain;
                }
                else
                {
                    mainFilePath = await PickMainFileAsync(owner, projectDir);
                }
            }
            else
            {
                mainFilePath = await PickMainFileAsync(owner, projectDir);
            }
        }

        if (string.IsNullOrEmpty(mainFilePath))
            return false;

        var saveFile = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Экспортировать проект как .nproject",
            DefaultExtension = "nproject",
            SuggestedFileName = Path.GetFileName(projectDir) + ".nproject",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Пакет проекта Ncode (*.nproject)")
                {
                    Patterns = new[] { "*.nproject" }
                }
            }
        });

        if (saveFile == null) return false;
        var targetPath = saveFile.TryGetLocalPath();
        if (string.IsNullOrEmpty(targetPath)) return false;

        if (File.Exists(targetPath))
            File.Delete(targetPath);

        await Task.Run(() =>
        {
            using var archive = ZipFile.Open(targetPath, ZipArchiveMode.Create);

            var manifest = new
            {
                name = Path.GetFileName(projectDir),
                main = Path.GetRelativePath(projectDir, mainFilePath),
                exportDate = DateTime.UtcNow.ToString("o")
            };

            var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            var manifestEntry = archive.CreateEntry("_manifest.json", CompressionLevel.Optimal);
            using (var entryStream = manifestEntry.Open())
            using (var writer = new StreamWriter(entryStream))
            {
                writer.Write(manifestJson);
            }

            var allFiles = Directory.GetFiles(projectDir, "*.*", SearchOption.AllDirectories);
            foreach (var file in allFiles)
            {
                var rel = Path.GetRelativePath(projectDir, file);
                if (rel.StartsWith(".git") || rel.StartsWith("bin") || rel.StartsWith("obj") || rel.EndsWith(".nproject"))
                    continue;

                archive.CreateEntryFromFile(file, rel, CompressionLevel.Optimal);
            }
        });

        return true;
    }

    public static async Task<string?> PickMainFileAsync(Window owner, string projectDir)
    {
        var ncodeFiles = Directory.GetFiles(projectDir, "*.ncode", SearchOption.AllDirectories).ToList();
        if (ncodeFiles.Count == 0) return null;

        var dlg = new SelectFileDialog(ncodeFiles, projectDir, "Выберите файл, который будет главным (точкой входа):");
        await dlg.ShowDialog(owner);
        return dlg.SelectedFile;
    }
}
