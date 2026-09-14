// Ncode — Russian programming language for games.
// Copyright (C) 2026 Ivproduction
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later.
// See LICENSE in the repository root.

#if !ANDROID
using System.Windows.Forms;
#endif
using Ncode.Core.Abstractions;

namespace Ncode.Platform;

public sealed class WindowsDialogService : IDialogService
{
#if !ANDROID
    public void ShowMessage(string text)
    {
        MessageBox.Show(text, "Ncode", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    public void ShowError(string text)
    {
        MessageBox.Show(text, "Ncode - Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    public bool AskYesNo(string text)
    {
        var res = MessageBox.Show(text, "Ncode", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        return res == DialogResult.Yes;
    }
#else
    public void ShowMessage(string text) { }
    public void ShowError(string text) { }
    public bool AskYesNo(string text) => false;
#endif
}
