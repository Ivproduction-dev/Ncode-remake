// Ncode — Russian programming language for games.
// Copyright (C) 2026 Ivproduction
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later.
// See LICENSE in the repository root.

using System.Threading.Tasks;
using Avalonia.Controls;

namespace Ncode.Editor;

public partial class ConfirmDialog : Window
{
    public bool Confirmed { get; private set; }

    public ConfirmDialog()
    {
        InitializeComponent();
        NoButton.Click += (_, _) =>
        {
            Confirmed = false;
            Close();
        };
        YesButton.Click += (_, _) =>
        {
            Confirmed = true;
            Close();
        };
    }

    public static async Task<bool> Show(Window owner, string message, string title = "Подтверждение")
    {
        var dlg = new ConfirmDialog
        {
            Title = title
        };
        dlg.MessageText.Text = message;
        await dlg.ShowDialog(owner);
        return dlg.Confirmed;
    }
}
