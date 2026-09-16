// Ncode — Russian programming language for games.
// Copyright (C) 2026 Ivproduction
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later.
// See LICENSE in the repository root.

using global::Android.Content;
using global::Android.Graphics;
using global::Android.Views;
using Ncode.Core.Abstractions;
using Ncode.Rendering;
using System.IO;

namespace Ncode.Android;

public sealed class AndroidGameView : View
{
    private readonly object _lock = new();
    private IReadOnlyList<RenderableObject>? _objects;
    private readonly Dictionary<string, Bitmap> _bitmaps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Paint _paint = new() { AntiAlias = true, FilterBitmap = true };
    private readonly Paint _boxPaint = new() { AntiAlias = true };
    private readonly Paint _textPaint = new() { AntiAlias = true, Color = Color.Rgb(17, 17, 27) };
    private readonly Paint _hudPaint = new() { AntiAlias = true, Color = Color.White, TextSize = 26f };
    private readonly Paint _hudBgPaint = new() { Color = new Color(0, 0, 0, 160) };
    private readonly Paint _errorPaint = new() { AntiAlias = true, Color = Color.White, TextSize = 32f };
    private readonly Paint _errorBgPaint = new() { Color = new Color(180, 30, 30, 220) };
    private string? _error;
    private string? _hud;
    private int _viewW;
    private int _viewH;

    public AndroidGameView(Context ctx) : base(ctx)
    {
        _textPaint.TextSize = 28f;
        _textPaint.SetTypeface(Typeface.Create("sans-serif", TypefaceStyle.Bold));
    }

    public void SetObjects(IReadOnlyList<RenderableObject> objs)
    {
        lock (_lock) _objects = [.. objs];
    }

    public void SetError(string? message)
    {
        lock (_lock) _error = message;
        try { Post(() => Invalidate()); } catch { }
    }

    public void SetHud(string? hud)
    {
        lock (_lock) _hud = hud;
        try { Post(() => Invalidate()); } catch { }
    }

    protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
    {
        base.OnSizeChanged(w, h, oldw, oldh);
        _viewW = w; _viewH = h;
    }

    public int ViewWidth => _viewW > 0 ? _viewW : 800;
    public int ViewHeight => _viewH > 0 ? _viewH : 600;

    protected override void OnDraw(Canvas? canvas)
    {
        base.OnDraw(canvas);
        if (canvas == null) return;
        canvas.DrawColor(Color.Black);
        if (_viewW > 0 && _viewH > 0)
        {
            _boxPaint.Color = Color.Argb(40, 255, 255, 255);
            _boxPaint.StrokeWidth = 1f;
            _boxPaint.SetStyle(Paint.Style.Stroke);
            canvas.DrawLine(_viewW/2f, 0, _viewW/2f, _viewH, _boxPaint);
            canvas.DrawLine(0, _viewH/2f, _viewW, _viewH/2f, _boxPaint);
        }

        IReadOnlyList<RenderableObject>? snapshot;
        string? err; string? hud;
        lock (_lock) { snapshot = _objects; err = _error; hud = _hud; }
        if (!string.IsNullOrEmpty(err)) { DrawError(canvas, err); return; }
        if (snapshot == null || snapshot.Count == 0)
        {
            DrawHud(canvas, hud, snapshot);
            return;
        }

        foreach (var obj in snapshot)
        {
            if (!obj.Visible) continue;

            float scale = (float)(obj.Scale <= 0 ? 100.0 : obj.Scale) / 100.0f;
            float alpha = Math.Clamp(1.0f - (float)(obj.Alpha / 100.0), 0.0f, 1.0f);
            if (alpha <= 0.001f) continue;

            Bitmap? bmp = null;
            if (!string.IsNullOrEmpty(obj.SpritePath))
            {
                lock (_bitmaps)
                {
                    if (!_bitmaps.TryGetValue(obj.SpritePath, out bmp))
                    {
                        try
                        {
                            string filesDir = Context.FilesDir?.AbsolutePath ?? "";
                            string appFilesDir = global::Android.App.Application.Context.FilesDir?.AbsolutePath ?? "";
                            foreach (var path in new[] {
                                System.IO.Path.Combine(filesDir, obj.SpritePath),
                                System.IO.Path.Combine(filesDir, "NcodeGame", obj.SpritePath),
                                System.IO.Path.Combine(appFilesDir, obj.SpritePath),
                                System.IO.Path.Combine(appFilesDir, "NcodeGame", obj.SpritePath) })
                            {
                                if (File.Exists(path))
                                {
                                    var loaded = BitmapFactory.DecodeFile(path);
                                    if (loaded != null) _bitmaps[obj.SpritePath] = loaded;
                                    bmp = loaded;
                                    break;
                                }
                            }
                            if (bmp == null)
                            {
                                try
                                {
                                    using var afd = Context.Assets?.Open(obj.SpritePath);
                                    if (afd != null)
                                    {
                                        bmp = BitmapFactory.DecodeStream(afd);
                                        if (bmp != null) _bitmaps[obj.SpritePath] = bmp;
                                    }
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }
                }
            }
            if (obj.PlatformData is Bitmap pb) bmp = pb;

            float w = bmp != null ? bmp.Width * scale : Math.Max(24, 60 * scale);
            float h = bmp != null ? bmp.Height * scale : Math.Max(24, 60 * scale);

            float cxScreen = _viewW > 0 ? _viewW / 2f : 400f;
            float cyScreen = _viewH > 0 ? _viewH / 2f : 300f;
            float drawX = cxScreen + (float)obj.X - w / 2f;
            float drawY = cyScreen + (float)obj.Y - h / 2f;

            canvas.Save();
            if (Math.Abs(obj.Angle) > 1e-4)
            {
                float cx = drawX + w / 2f;
                float cy = drawY + h / 2f;
                canvas.Translate(cx, cy);
                canvas.Rotate((float)obj.Angle);
                canvas.Translate(-cx, -cy);
            }

            if (bmp != null)
            {
                var dst = new RectF(drawX, drawY, drawX + w, drawY + h);
                if (alpha >= 0.999f)
                {
                    canvas.DrawBitmap(bmp, null, dst, _paint);
                }
                else
                {
                    _paint.Alpha = (int)(alpha * 255);
                    canvas.DrawBitmap(bmp, null, dst, _paint);
                    _paint.Alpha = 255;
                }
            }
            else
            {
                int aByte = Math.Clamp((int)(alpha * 220), 0, 255);
                _boxPaint.Color = Color.Argb(aByte, 137, 180, 250);
                _boxPaint.SetStyle(Paint.Style.Fill);
                canvas.DrawRoundRect(new RectF(drawX, drawY, drawX + w, drawY + h), 12, 12, _boxPaint);
                _boxPaint.Color = Color.Argb(Math.Clamp((int)(alpha * 255), 0, 255), 205, 214, 244);
                _boxPaint.SetStyle(Paint.Style.Stroke);
                _boxPaint.StrokeWidth = 3;
                canvas.DrawRoundRect(new RectF(drawX, drawY, drawX + w, drawY + h), 12, 12, _boxPaint);
                canvas.DrawText(obj.Name, drawX + 8, drawY + 32, _textPaint);
            }
            canvas.Restore();
        }

        if (!string.IsNullOrEmpty(hud))
            DrawHudOverlay(canvas, hud);
        else if (snapshot != null && snapshot.Count > 0)
        {
            string info = $"объектов: {snapshot.Count}  {_viewW}x{_viewH}";
            DrawHudOverlay(canvas, info);
        }
    }

    private void DrawHud(Canvas canvas, string? hud, IReadOnlyList<RenderableObject>? snap)
    {
        try
        {
            int w = _viewW > 0 ? _viewW : canvas.Width;
            int h = _viewH > 0 ? _viewH : canvas.Height;
            string msg = hud ?? $"объектов: 0  {w}x{h}\nждём объекты…\n\nЕсли висит чёрным — проверь:\n• при запуске / создать объект / задать свойство\n• путь к .png (icon.png лежит рядом с .ncode)\n• размер в процентах (5 = крошечный, ставь 80-100)";
            if (snap != null && snap.Count == 0 && !string.IsNullOrEmpty(hud)) msg = hud + "\n\nобъектов: 0";
            canvas.DrawRect(0, 0, w, h, _errorBgPaint);
            float padding = 40f; float maxW = Math.Max(220f, w - padding*2); float y = padding + 54f;
            _hudPaint.TextSize = 30f;
            foreach (var raw in msg.Split('\n').Take(28))
            {
                string rest = raw; if (rest.Length==0){ y+=36f; continue; }
                while (rest.Length>0 && y < h-24)
                {
                    int n = _hudPaint.BreakText(rest, true, maxW, null); if (n<=0) n=rest.Length;
                    canvas.DrawText(rest.Substring(0,n), padding, y, _hudPaint); y+=38f; rest=rest.Substring(n);
                }
            }
        } catch { }
    }

    private void DrawHudOverlay(Canvas canvas, string text)
    {
        try
        {
            float pad = 12f; _hudPaint.TextSize = 22f;
            float tw = _hudPaint.MeasureText(text);
            canvas.DrawRect(pad-6, pad-10, tw+pad+14, pad+22, _hudBgPaint);
            canvas.DrawText(text, pad, pad+12, _hudPaint);
        } catch { }
    }

    private void DrawError(Canvas canvas, string err)
    {
        try
        {
            int w = _viewW > 0 ? _viewW : canvas.Width;
            int h = _viewH > 0 ? _viewH : canvas.Height;
            canvas.DrawRect(0, 0, w, h, _errorBgPaint);
            float padding = 40f;
            float maxW = Math.Max(200f, w - padding * 2);
            float y = padding + 40f;
            foreach (var rawLine in err.Split('\n').Take(40))
            {
                string rest = rawLine;
                if (rest.Length == 0) { y += 40f; continue; }
                while (rest.Length > 0 && y < h - 20)
                {
                    int n = _errorPaint.BreakText(rest, true, maxW, null);
                    if (n <= 0) n = rest.Length;
                    canvas.DrawText(rest.Substring(0, n), padding, y, _errorPaint);
                    y += 44f;
                    rest = rest.Substring(n);
                }
                if (y >= h - 20) break;
            }
        }
        catch { }
    }

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e?.Action == MotionEventActions.Down)
        {
            var host = global::Ncode.Core.Abstractions.GameHostService.Current as global::Ncode.Rendering.AndroidGameHost;
            float cx = _viewW > 0 ? _viewW / 2f : 400f;
            float cy = _viewH > 0 ? _viewH / 2f : 300f;
            host?.RaisePointerDown(e.GetX() - cx, e.GetY() - cy);
            return true;
        }
        return base.OnTouchEvent(e);
    }
}
