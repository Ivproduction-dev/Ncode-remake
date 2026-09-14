// Ncode — Russian programming language for games.
// Copyright (C) 2026 Ivproduction
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later.
// See LICENSE in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using Ncode.Core.Abstractions;

namespace Ncode.Rendering;

public sealed class AndroidGameHost : IGameHost
{
#if ANDROID
    private bool _active;
    private int _clientWidth;
    private int _clientHeight;

    public int ClientWidth => _clientWidth > 0 ? _clientWidth : 800;
    public int ClientHeight => _clientHeight > 0 ? _clientHeight : 600;

    public Action<string>? OnKeyDown { get; set; }
    public Action<string>? OnKeyUp { get; set; }
    public Action<double, double>? OnPointerDown { get; set; }
    public Func<IReadOnlyList<RenderableObject>>? GetObjectsToRender { get; set; }

    public bool IsActive => _active;

    public void CreateWindow(int width, int height, string title, bool resizable, bool fullscreen, string? iconPath)
    {
        _active = true;
        _clientWidth = width;
        _clientHeight = height;
    }

    public void Invalidate()
    {
    }

    public void CloseWindow()
    {
        _active = false;
    }

    public void WaitUntilClosed()
    {
        while (_active)
        {
            System.Threading.Thread.Sleep(50);
        }
    }
#else
    public int ClientWidth => 800;
    public int ClientHeight => 600;

    public Action<string>? OnKeyDown { get; set; }
    public Action<string>? OnKeyUp { get; set; }
    public Action<double, double>? OnPointerDown { get; set; }
    public Func<IReadOnlyList<RenderableObject>>? GetObjectsToRender { get; set; }

    public bool IsActive => false;

    public void CreateWindow(int width, int height, string title, bool resizable, bool fullscreen, string? iconPath) { }

    public void Invalidate() { }
    public void CloseWindow() { }
    public void WaitUntilClosed() { }
#endif
}
