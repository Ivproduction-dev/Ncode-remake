// Ncode — Russian programming language for games.
// Copyright (C) 2026 Ivproduction
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later.
// See LICENSE in the repository root.

using System;
using Ncode.Core.Abstractions;

namespace Ncode.Platform;

public sealed class AndroidClipboard : IClipboardService
{
#if ANDROID
    public void Copy(string text)
    {
        try
        {
            var cm = (global::Android.Content.ClipboardManager?)global::Android.App.Application.Context.GetSystemService(global::Android.Content.Context.ClipboardService);
            if (cm != null)
            {
                var clip = global::Android.Content.ClipData.NewPlainText("ncode", text);
                cm.PrimaryClip = clip;
            }
        }
        catch { }
    }

    public string Paste()
    {
        try
        {
            var cm = (global::Android.Content.ClipboardManager?)global::Android.App.Application.Context.GetSystemService(global::Android.Content.Context.ClipboardService);
            if (cm != null && cm.HasPrimaryClip && cm.PrimaryClip != null && cm.PrimaryClip.ItemCount > 0)
            {
                var item = cm.PrimaryClip.GetItemAt(0);
                return item?.Text ?? "";
            }
        }
        catch { }
        return "";
    }
#else
    private string _buf = "";
    public void Copy(string text) => _buf = text;
    public string Paste() => _buf;
#endif
}
