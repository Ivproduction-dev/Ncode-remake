// Ncode — Russian programming language for games.
// Copyright (C) 2026 Ivproduction
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later.
// See LICENSE in the repository root.

using System;
using System.Collections.Generic;
#if WINDOWS
using System.Drawing;
using System.Windows.Forms;
#endif
using System.IO;
using System.Threading;
using Ncode.Core.Abstractions;

namespace Ncode.Rendering;

public sealed class WindowsFormsGameHost : IGameHost
{
#if WINDOWS
    private Form? _gameForm;
    private Thread? _uiThread;
    private readonly object _lock = new();

    public Action<string>? OnKeyDown { get; set; }
    public Action<string>? OnKeyUp { get; set; }
    public Action<double, double>? OnPointerDown { get; set; }
    public Func<IReadOnlyList<RenderableObject>>? GetObjectsToRender { get; set; }

    private int _currentW = 800;
    private int _currentH = 600;

    public int ClientWidth
    {
        get
        {
            lock (_lock)
            {
                if (_gameForm != null && !_gameForm.IsDisposed)
                    return _gameForm.ClientSize.Width;
                return _currentW;
            }
        }
    }

    public int ClientHeight
    {
        get
        {
            lock (_lock)
            {
                if (_gameForm != null && !_gameForm.IsDisposed)
                    return _gameForm.ClientSize.Height;
                return _currentH;
            }
        }
    }

    public bool IsActive
    {
        get
        {
            lock (_lock)
            {
                return _gameForm != null && !_gameForm.IsDisposed;
            }
        }
    }

    public void CreateWindow(int width, int height, string title, bool resizable, bool fullscreen, string? iconPath)
    {
        lock (_lock)
        {
            if (_gameForm != null && !_gameForm.IsDisposed)
            {
                try
                {
                    _gameForm.Invoke(() =>
                    {
                        _currentW = width;
                        _currentH = height;
                        _gameForm.Text = title;
                        _gameForm.ClientSize = new Size(width, height);
                    });
                }
                catch { }
                return;
            }
            _currentW = width;
            _currentH = height;
        }

        var ready = new ManualResetEventSlim(false);
        Exception? threadEx = null;

        _uiThread = new Thread(() =>
        {
            try
            {
                _gameForm = new Form
                {
                    Text = title,
                    ClientSize = new Size(width, height),
                    BackColor = Color.Black,
                    StartPosition = FormStartPosition.CenterScreen,
                    FormBorderStyle = resizable ? FormBorderStyle.Sizable : FormBorderStyle.FixedSingle,
                    MaximizeBox = resizable,
                    KeyPreview = true
                };

                if (fullscreen)
                {
                    _gameForm.WindowState = FormWindowState.Maximized;
                }

                if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                {
                    try
                    {
                        if (iconPath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                            _gameForm.Icon = new Icon(iconPath);
                        else
                        {
                            using var bmp = new Bitmap(iconPath);
                            _gameForm.Icon = Icon.FromHandle(bmp.GetHicon());
                        }
                    }
                    catch { }
                }

                typeof(Form).InvokeMember("DoubleBuffered",
                    System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    null, _gameForm, new object[] { true });

                _gameForm.Paint += (s, pe) => Render(pe.Graphics, _gameForm.ClientSize.Width, _gameForm.ClientSize.Height);
                _gameForm.Resize += (s, e) => Invalidate();
                _gameForm.MouseDown += (s, me) =>
                {
                    int cw = _gameForm.ClientSize.Width;
                    int ch = _gameForm.ClientSize.Height;
                    double cx = me.X - cw / 2.0;
                    double cy = me.Y - ch / 2.0;
                    OnPointerDown?.Invoke(cx, cy);
                };
                _gameForm.KeyDown += (s, ke) =>
                {
                    string k = ke.KeyCode switch
                    {
                        Keys.Space => "пробел",
                        Keys.Left => "стрелка влево",
                        Keys.Right => "стрелка вправо",
                        Keys.Up => "стрелка вверх",
                        Keys.Down => "стрелка вниз",
                        Keys.Return => "ввод",
                        Keys.Escape => "эскейп",
                        _ => ke.KeyCode.ToString().ToLowerInvariant()
                    };
                    OnKeyDown?.Invoke(k);
                };
                _gameForm.KeyUp += (s, ke) =>
                {
                    string k = ke.KeyCode switch
                    {
                        Keys.Space => "пробел",
                        Keys.Left => "стрелка влево",
                        Keys.Right => "стрелка вправо",
                        Keys.Up => "стрелка вверх",
                        Keys.Down => "стрелка вниз",
                        Keys.Return => "ввод",
                        Keys.Escape => "эскейп",
                        _ => ke.KeyCode.ToString().ToLowerInvariant()
                    };
                    OnKeyUp?.Invoke(k);
                };
                _gameForm.FormClosed += (s, e) => { try { Environment.Exit(0); } catch { } };

                ready.Set();
                Application.Run(_gameForm);
            }
            catch (Exception ex)
            {
                threadEx = ex;
                ready.Set();
            }
        });

        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.IsBackground = false;
        _uiThread.Start();
        ready.Wait(5000);

        if (threadEx != null) throw new Exception($"Окно не создалось: {threadEx.Message}");
    }

    public void Invalidate()
    {
        lock (_lock)
        {
            if (_gameForm != null && !_gameForm.IsDisposed)
            {
                try
                {
                    if (_gameForm.InvokeRequired)
                        _gameForm.BeginInvoke(new Action(() => { if (_gameForm != null && !_gameForm.IsDisposed) _gameForm.Invalidate(); }));
                    else
                        _gameForm.Invalidate();
                }
                catch { }
            }
        }
    }

    public void CloseWindow()
    {
        lock (_lock)
        {
            if (_gameForm != null && !_gameForm.IsDisposed)
            {
                try { _gameForm.Invoke(() => _gameForm.Close()); } catch { }
            }
        }
    }

    public void WaitUntilClosed()
    {
        while (IsActive)
        {
            Thread.Sleep(50);
        }
    }

    private void Render(Graphics g, int clientW, int clientH)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

        var objects = GetObjectsToRender?.Invoke();
        if (objects == null) return;

        foreach (var obj in objects)
        {
            if (!obj.Visible) continue;

            if (obj.PlatformData is not Image img && !string.IsNullOrEmpty(obj.SpritePath) && File.Exists(obj.SpritePath))
            {
                try
                {
                    using var stream = new MemoryStream(File.ReadAllBytes(obj.SpritePath));
                    obj.PlatformData = Image.FromStream(stream);
                }
                catch { }
            }

            img = obj.PlatformData as Image;
            float scale = (float)(obj.Scale <= 0 ? 100.0 : obj.Scale) / 100.0f;
            float alpha = Math.Clamp(1.0f - (float)(obj.Alpha / 100.0), 0.0f, 1.0f);

            float w = img != null ? img.Width * scale : Math.Max(24, (int)(60 * scale));
            float h = img != null ? img.Height * scale : Math.Max(24, (int)(60 * scale));

            float cxScreen = clientW / 2f;
            float cyScreen = clientH / 2f;
            float drawX = cxScreen + (float)obj.X - w / 2f;
            float drawY = cyScreen + (float)obj.Y - h / 2f;

            var gState = g.Save();
            if (Math.Abs(obj.Angle) > 1e-4)
            {
                float cx = drawX + w / 2f;
                float cy = drawY + h / 2f;
                g.TranslateTransform(cx, cy);
                g.RotateTransform((float)obj.Angle);
                g.TranslateTransform(-cx, -cy);
            }

            if (img != null)
            {
                if (alpha >= 0.999f)
                {
                    g.DrawImage(img, drawX, drawY, w, h);
                }
                else if (alpha > 0.001f)
                {
                    var matrix = new System.Drawing.Imaging.ColorMatrix { Matrix33 = alpha };
                    using var attr = new System.Drawing.Imaging.ImageAttributes();
                    attr.SetColorMatrix(matrix, System.Drawing.Imaging.ColorMatrixFlag.Default, System.Drawing.Imaging.ColorAdjustType.Bitmap);
                    g.DrawImage(img, new Rectangle((int)drawX, (int)drawY, (int)w, (int)h), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, attr);
                }
            }
            else
            {
                int boxW = (int)w;
                int boxH = (int)h;
                int aByte = Math.Clamp((int)(alpha * 220), 0, 255);
                using var brush = new SolidBrush(Color.FromArgb(aByte, 137, 180, 250));
                using var pen = new Pen(Color.FromArgb(Math.Clamp((int)(alpha * 255), 0, 255), 205, 214, 244), 2);
                g.FillRectangle(brush, (int)drawX, (int)drawY, boxW, boxH);
                g.DrawRectangle(pen, (int)drawX, (int)drawY, boxW, boxH);

                using var font = new Font("Segoe UI", 9f, FontStyle.Bold);
                using var textBrush = new SolidBrush(Color.FromArgb(Math.Clamp((int)(alpha * 255), 0, 255), 17, 17, 27));
                g.DrawString(obj.Name, font, textBrush, drawX + 4, drawY + 4);
            }
            g.Restore(gState);
        }
    }
#else
    public Action<string>? OnKeyDown { get; set; }
    public Action<string>? OnKeyUp { get; set; }
    public Action<double, double>? OnPointerDown { get; set; }
    public Func<IReadOnlyList<RenderableObject>>? GetObjectsToRender { get; set; }
    public int ClientWidth => 800;
    public int ClientHeight => 600;
    public bool IsActive => false;
    public void CreateWindow(int width, int height, string title, bool resizable, bool fullscreen, string? iconPath) { }
    public void Invalidate() { }
    public void CloseWindow() { }
    public void WaitUntilClosed() { }
#endif
}
