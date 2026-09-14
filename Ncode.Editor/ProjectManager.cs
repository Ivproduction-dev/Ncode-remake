// Ncode — Russian programming language for games.
// Copyright (C) 2026 Ivproduction
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later.
// See LICENSE in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Ncode.Editor;

public class ProjectItem
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public DateTime LastOpened { get; set; } = DateTime.UtcNow;
}

public static class ProjectManager
{
    private static readonly string StorageFile;

    static ProjectManager()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = System.IO.Path.Combine(appData, "NcodeStudio");
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        StorageFile = System.IO.Path.Combine(dir, "projects.json");
    }

    public static List<ProjectItem> Load()
    {
        var list = new List<ProjectItem>();
        try
        {
            if (File.Exists(StorageFile))
            {
                var json = File.ReadAllText(StorageFile);
                var items = JsonSerializer.Deserialize<List<ProjectItem>>(json);
                if (items != null)
                {
                    list = items.Where(p => Directory.Exists(p.Path)).ToList();
                }
            }
        }
        catch { }

        if (list.Count == 0)
        {
            ScanDefaultProjects(list);
            Save(list);
        }

        return list.OrderByDescending(p => p.LastOpened).ToList();
    }

    private static void ScanDefaultProjects(List<ProjectItem> list)
    {
        try
        {
            var baseDir = Directory.GetCurrentDirectory();
            var dirs = Directory.GetDirectories(baseDir);
            foreach (var d in dirs)
            {
                var name = System.IO.Path.GetFileName(d);
                if (name.StartsWith(".") || name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Ncode", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Ncode.Core", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Ncode.Editor", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (Directory.GetFiles(d, "*.ncode", SearchOption.AllDirectories).Any())
                {
                    list.Add(new ProjectItem
                    {
                        Name = name,
                        Path = d,
                        LastOpened = Directory.GetLastWriteTimeUtc(d)
                    });
                }
            }
        }
        catch { }
    }

    public static void Save(List<ProjectItem> list)
    {
        try
        {
            var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(StorageFile, json);
        }
        catch { }
    }

    public static void AddOrUpdate(string path, string? name = null)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

        var list = Load();
        var fullPath = System.IO.Path.GetFullPath(path);
        var existing = list.FirstOrDefault(p => string.Equals(System.IO.Path.GetFullPath(p.Path), fullPath, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            existing.LastOpened = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(name)) existing.Name = name;
        }
        else
        {
            list.Add(new ProjectItem
            {
                Name = !string.IsNullOrEmpty(name) ? name : System.IO.Path.GetFileName(fullPath),
                Path = fullPath,
                LastOpened = DateTime.UtcNow
            });
        }

        Save(list);
    }

    public static void Remove(string path)
    {
        var list = Load();
        var fullPath = System.IO.Path.GetFullPath(path);
        list.RemoveAll(p => string.Equals(System.IO.Path.GetFullPath(p.Path), fullPath, StringComparison.OrdinalIgnoreCase));
        Save(list);
    }
}
