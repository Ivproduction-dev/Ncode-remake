// Ncode — Russian programming language for games.
// Copyright (C) 2026 Ivproduction
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later.
// See LICENSE in the repository root.

using System;
using System.Collections.Generic;

namespace Ncode.Core.Abstractions;

public class RenderableObject
{
    public string Name { get; set; } = "";
    public string SpritePath { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Scale { get; set; } = 100;
    public double Alpha { get; set; }
    public double Angle { get; set; } = 0;
    public bool Visible { get; set; } = true;
    public object? PlatformData { get; set; }
}

public interface IGameHost
{
    int ClientWidth { get; }
    int ClientHeight { get; }
    void CreateWindow(int width, int height, string title, bool resizable, bool fullscreen, string? iconPath);
    void Invalidate();
    void CloseWindow();
    bool IsActive { get; }
    void WaitUntilClosed();
    Action<string>? OnKeyDown { get; set; }
    Action<string>? OnKeyUp { get; set; }
    Action<double, double>? OnPointerDown { get; set; }
    Func<IReadOnlyList<RenderableObject>>? GetObjectsToRender { get; set; }
}

public static class GameHostService
{
    public static IGameHost Current { get; set; } = new NullGameHost();
}

public sealed class NullGameHost : IGameHost
{
    public int ClientWidth => 800;
    public int ClientHeight => 600;
    public void CreateWindow(int width, int height, string title, bool resizable, bool fullscreen, string? iconPath) { }
    public void Invalidate() { }
    public void CloseWindow() { }
    public bool IsActive => false;
    public void WaitUntilClosed() { }
    public Action<string>? OnKeyDown { get; set; }
    public Action<string>? OnKeyUp { get; set; }
    public Action<double, double>? OnPointerDown { get; set; }
    public Func<IReadOnlyList<RenderableObject>>? GetObjectsToRender { get; set; }
}
