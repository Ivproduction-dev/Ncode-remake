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
    private int _viewW;
    private int _viewH;

    public AndroidGameView(Context ctx) : base(ctx)
    {
        _textPaint.TextSize = 28f;
        _textPaint.SetTypeface(Typeface.Create("sans-serif", TypefaceStyle.Bold));
    }

    public void SetObjects(IReadOnlyList<RenderableObject> objs)
    {
        lock (_lock) _objects = objs;
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

        IReadOnlyList<RenderableObject>? snapshot;
        lock (_lock) snapshot = _objects;
        if (snapshot == null) return;

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
                            string path = System.IO.Path.Combine(Context.FilesDir?.AbsolutePath ?? "", obj.SpritePath);
                            if (!File.Exists(path))
                                path = System.IO.Path.Combine(global::Android.App.Application.Context.FilesDir?.AbsolutePath ?? "", obj.SpritePath);
                            if (File.Exists(path))
                            {
                                var loaded = BitmapFactory.DecodeFile(path);
                                if (loaded != null) _bitmaps[obj.SpritePath] = loaded;
                                bmp = loaded;
                            }
                            else
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

            canvas.Save();
            if (Math.Abs(obj.Angle) > 1e-4)
            {
                float cx = (float)obj.X + w / 2f;
                float cy = (float)obj.Y + h / 2f;
                canvas.Translate(cx, cy);
                canvas.Rotate((float)obj.Angle);
                canvas.Translate(-cx, -cy);
            }

            if (bmp != null)
            {
                var dst = new RectF((float)obj.X, (float)obj.Y, (float)obj.X + w, (float)obj.Y + h);
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
                canvas.DrawRoundRect(new RectF((float)obj.X, (float)obj.Y, (float)obj.X + w, (float)obj.Y + h), 12, 12, _boxPaint);
                _boxPaint.Color = Color.Argb(Math.Clamp((int)(alpha * 255), 0, 255), 205, 214, 244);
                _boxPaint.SetStyle(Paint.Style.Stroke);
                _boxPaint.StrokeWidth = 3;
                canvas.DrawRoundRect(new RectF((float)obj.X, (float)obj.Y, (float)obj.X + w, (float)obj.Y + h), 12, 12, _boxPaint);
                canvas.DrawText(obj.Name, (float)obj.X + 8, (float)obj.Y + 32, _textPaint);
            }
            canvas.Restore();
        }
    }

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e?.Action == MotionEventActions.Down)
        {
            var host = global::Ncode.Core.Abstractions.GameHostService.Current as global::Ncode.Rendering.AndroidGameHost;
            host?.RaisePointerDown(e.GetX(), e.GetY());
            return true;
        }
        return base.OnTouchEvent(e);
    }
}
