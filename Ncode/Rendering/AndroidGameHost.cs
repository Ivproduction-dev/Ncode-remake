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
    private readonly Android.Views.View? _view;
    private int _clientWidth;
    private int _clientHeight;
    private bool _active;

    public AndroidGameHost() { }
    public AndroidGameHost(Android.Views.View view) { _view = view; }

    public int ClientWidth => _view is Ncode.Android.AndroidGameView gv ? gv.ViewWidth : (_clientWidth > 0 ? _clientWidth : 800);
    public int ClientHeight => _view is Ncode.Android.AndroidGameView gv2 ? gv2.ViewHeight : (_clientHeight > 0 ? _clientHeight : 600);

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
        if (_view is Ncode.Android.AndroidGameView gv)
        {
            var objs = GetObjectsToRender?.Invoke();
            if (objs != null) gv.SetObjects(objs);
        }
    }

    public void Invalidate()
    {
        if (_view != null) _view.Post(() => _view.Invalidate());
        else if (GetObjectsToRender != null && _view is Ncode.Android.AndroidGameView gv)
        {
            var objs = GetObjectsToRender.Invoke();
            if (objs != null) gv.SetObjects(objs);
        }
    }

    public void CloseWindow() => _active = false;

    public void WaitUntilClosed()
    {
        while (_active) System.Threading.Thread.Sleep(50);
    }

    public void RaisePointerDown(double x, double y) => OnPointerDown?.Invoke(x, y);
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
