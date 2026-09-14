// Ncode — Russian programming language for games.
// Copyright (C) 2026 Ivproduction
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later.
// See LICENSE in the repository root.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Ncode.Editor;

public partial class CreateKeystoreDialog : Window
{
    public string? ResultKeystorePath { get; private set; }
    public string ResultStorePass { get; private set; } = "";
    public string ResultKeyPass { get; private set; } = "";
    public string ResultAlias { get; private set; } = "";
    public bool IsForAab { get; }

    public CreateKeystoreDialog() : this(false) { }

    public CreateKeystoreDialog(bool isForAab, string? defaultDir = null)
    {
        InitializeComponent();
        IsForAab = isForAab;

        string dir = defaultDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ncode");
        Directory.CreateDirectory(dir);
        KeystorePathBox.Text = Path.Combine(dir, "ncode.keystore");
        StorePassBox.Text = "ncode123";
        KeyPassBox.Text = "ncode123";

        if (isForAab)
        {
            SubtitleText.Text = "Для .AAB — потеря = потеря приложения в Play Store навсегда";
            WarningText.Text = "Для .AAB потеря keystore = потеря приложения в Google Play навсегда. Вы не сможете обновить игру. Сделайте бэкап keystore и паролей в надёжное место!";
        }
        else
        {
            SubtitleText.Text = "Для .APK — потеря = потеря подписи (придётся выпускать новое приложение)";
            WarningText.Text = "Если потеряете файл keystore или пароль — не сможете обновить игру. Для .APK это потеря подписи (придётся выпускать новое приложение с новым Package ID). Сделайте бэкап сразу!";
        }

        BrowseKeystoreBtn.Click += BrowseKeystoreClick;
        CancelBtn.Click += (_, _) => Close();
        CreateBtn.Click += CreateClick;
    }

    private async void BrowseKeystoreClick(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Куда сохранить keystore",
            DefaultExtension = "keystore",
            SuggestedFileName = "ncode.keystore",
            FileTypeChoices = new[] { new FilePickerFileType("Keystore") { Patterns = new[] { "*.keystore", "*.jks" } } }
        });
        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path)) KeystorePathBox.Text = path;
    }

    private async void CreateClick(object? sender, RoutedEventArgs e)
    {
        string path = (KeystorePathBox.Text ?? "").Trim();
        string alias = (AliasBox.Text ?? "").Trim();
        string storePass = StorePassBox.Text ?? "";
        string keyPass = KeyPassBox.Text ?? "";
        string cn = (CnBox.Text ?? "").Trim();
        string ou = (OuBox.Text ?? "").Trim();
        string o = (OBox.Text ?? "").Trim();
        string l = (LBox.Text ?? "").Trim();
        string st = (StBox.Text ?? "").Trim();
        string c = (CBox.Text ?? "").Trim().ToUpperInvariant();
        string validity = (ValidityBox.Text ?? "").Trim();

        if (string.IsNullOrEmpty(path)) { ShowError("Укажите куда сохранить keystore"); return; }
        if (string.IsNullOrEmpty(alias)) { ShowError("Укажите alias ключа"); return; }
        if (storePass.Length < 6) { ShowError("Пароль хранилища минимум 6 символов"); return; }
        if (keyPass.Length < 6) { ShowError("Пароль ключа минимум 6 символов"); return; }
        if (string.IsNullOrEmpty(cn) || string.IsNullOrEmpty(ou) || string.IsNullOrEmpty(o) || string.IsNullOrEmpty(l) || string.IsNullOrEmpty(st) || string.IsNullOrEmpty(c))
        { ShowError("Заполните все поля DName: CN, OU, O, L, ST, C"); return; }
        if (c.Length != 2) { ShowError("Страна C — 2 буквы, например RU"); return; }
        if (!int.TryParse(validity, out int valid) || valid <= 0) { ShowError("Срок — положительное число дней"); return; }

        if (File.Exists(path))
        {
            bool ok = await ConfirmDialog.Show(this, $"Файл уже есть:\n{path}\nПерезаписать?", "Перезаписать keystore?");
            if (!ok) return;
        }

        CreateBtn.IsEnabled = false;
        ErrorCard.IsVisible = false;

        bool created = await Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
                string dname = $"CN={cn}, OU={ou}, O={o}, L={l}, ST={st}, C={c}";
                var psi = new ProcessStartInfo("keytool", $"-genkeypair -keystore \"{path}\" -alias {alias} -keyalg RSA -keysize 2048 -validity {valid} -storepass {storePass} -keypass {keyPass} -dname \"{dname}\"")
                { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                using var proc = Process.Start(psi);
                if (proc == null) return false;
                proc.WaitForExit(20000);
                return proc.ExitCode == 0 && File.Exists(path);
            }
            catch { return false; }
        });

        if (!created)
        {
            ShowError("Не удалось создать keystore. Проверьте JDK (нужен keytool, JDK 17+) и права на запись.");
            CreateBtn.IsEnabled = true;
            return;
        }

        ResultKeystorePath = path;
        ResultStorePass = storePass;
        ResultKeyPass = keyPass;
        ResultAlias = alias;
        Close();
    }

    private void ShowError(string msg)
    {
        ErrorCard.IsVisible = true;
        ErrorText.Text = msg;
    }
}
