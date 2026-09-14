// Ncode — Russian programming language for games.
// Copyright (C) 2026 Ivproduction
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later.
// See LICENSE in the repository root.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Ncode.Editor;

public sealed class StarfieldControl : Control
{
    private readonly List<Star> _stars = new();
    private readonly Random _rng = new();
    private readonly DispatcherTimer _timer;

    private sealed class Star
    {
        public double X, Y, Vx, Vy, Size, Phase, Alpha;
    }

    public StarfieldControl()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => { Step(); InvalidateVisual(); };
        ClipToBounds = true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Seed();
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void Seed()
    {
        _stars.Clear();
        double w = Bounds.Width > 0 ? Bounds.Width : 900;
        double h = Bounds.Height > 0 ? Bounds.Height : 600;
        for (int i = 0; i < 130; i++)
            _stars.Add(Make(w, h, scatter: true));
    }

    private Star Make(double w, double h, bool scatter = false) => new Star
    {
        X = scatter ? _rng.NextDouble() * w : -8,
        Y = _rng.NextDouble() * h,
        Vx = 0.22 + _rng.NextDouble() * 0.42,
        Vy = (_rng.NextDouble() - 0.5) * 0.14,
        Size = 1.2 + _rng.NextDouble() * 2.8,
        Phase = _rng.NextDouble() * Math.PI * 2,
        Alpha = 0.45 + _rng.NextDouble() * 0.5
    };

    private void Step()
    {
        double w = Bounds.Width > 0 ? Bounds.Width : 900;
        double h = Bounds.Height > 0 ? Bounds.Height : 600;
        for (int i = 0; i < _stars.Count; i++)
        {
            var s = _stars[i];
            s.X += s.Vx;
            s.Y += s.Vy;
            s.Phase += 0.012;
            if (s.X > w + 10 || s.Y < -10 || s.Y > h + 10)
                _stars[i] = Make(w, h);
        }
    }

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(new SolidColorBrush(Color.Parse("#070714")), new Rect(Bounds.Size));
        foreach (var s in _stars)
        {
            double hue = 190 + ((Math.Sin(s.Phase) + 1) / 2.0) * 90;
            var col = Hsv(hue, 0.50, 1.0);
            var glow = new SolidColorBrush(Color.FromArgb((byte)(s.Alpha * 38), col.R, col.G, col.B));
            var core = new SolidColorBrush(Color.FromArgb((byte)(s.Alpha * 215), col.R, col.G, col.B));
            ctx.DrawEllipse(glow, null, new Point(s.X, s.Y), s.Size * 2.4, s.Size * 2.4);
            ctx.DrawEllipse(core, null, new Point(s.X, s.Y), s.Size / 2, s.Size / 2);
        }
    }

    private static Color Hsv(double h, double s, double v)
    {
        h %= 360;
        int hi = (int)(h / 60) % 6;
        double f = h / 60 - Math.Floor(h / 60);
        double p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
        var (r, g, b) = hi switch
        {
            0 => (v, t, p), 1 => (q, v, p), 2 => (p, v, t),
            3 => (p, q, v), 4 => (t, p, v), _ => (v, p, q)
        };
        return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }
}
