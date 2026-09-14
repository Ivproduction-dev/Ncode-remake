// Ncode — Russian programming language for games.
// Copyright (C) 2026 Ivproduction
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later.
// See LICENSE in the repository root.

namespace Ncode.Core.Abstractions;

public interface IClipboardService
{
    void Copy(string text);
    string Paste();
}

public static class ClipboardService
{
    public static IClipboardService Current { get; set; } = new NullClipboardService();
}

public sealed class NullClipboardService : IClipboardService
{
    private string _text = "";
    public void Copy(string text) => _text = text;
    public string Paste() => _text;
}
