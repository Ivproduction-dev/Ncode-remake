// Ncode — Russian programming language for games.
// Copyright (C) 2026 Ivproduction
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later.
// See LICENSE in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Ncode.Core.Abstractions;

namespace Ncode.Audio;

public sealed class WindowsAudioPlayer : IAudioPlayer
{
#if WINDOWS
    private readonly object _lock = new();
    private readonly List<(string Key, dynamic Player)> _activePlayers = new();

    private static string NormalizeKey(string path)
    {
        try
        {
            return Path.GetFullPath(path).Trim().ToLowerInvariant();
        }
        catch
        {
            return path.Trim().ToLowerInvariant();
        }
    }

    private static int SafeGetPlayState(dynamic player)
    {
        for (int retry = 0; retry < 10; retry++)
        {
            try
            {
                return (int)player.playState;
            }
            catch (COMException)
            {
                Thread.Sleep(30);
            }
            catch
            {
                break;
            }
        }
        return -1;
    }

    public void Play(string filePath, bool waitForEnd)
    {
        Type? wmpType = Type.GetTypeFromProgID("WMPlayer.OCX");
        if (wmpType == null)
            throw new Exception("Проигрыватель Windows Media недоступен на этой системе.");

        dynamic player = Activator.CreateInstance(wmpType)!;
        player.settings.autoStart = true;
        player.URL = filePath;

        if (waitForEnd)
        {
            int timeout = 0;
            while (SafeGetPlayState(player) != 3 && timeout < 50)
            {
                Thread.Sleep(30);
                timeout++;
                int s = SafeGetPlayState(player);
                if (s == 8 || s == 1) break;
            }

            while (true)
            {
                int s = SafeGetPlayState(player);
                if (s is 3 or 6 or 7 or 9)
                {
                    Thread.Sleep(40);
                }
                else
                {
                    break;
                }
            }

            try
            {
                player.close();
                Marshal.FinalReleaseComObject(player);
            }
            catch { }
        }
        else
        {
            lock (_lock)
            {
                for (int p = _activePlayers.Count - 1; p >= 0; p--)
                {
                    try
                    {
                        dynamic ap = _activePlayers[p].Player;
                        int st = SafeGetPlayState(ap);
                        if (st is 1 or 8 or 0 or -1)
                        {
                            try { ap.close(); } catch { }
                            _activePlayers.RemoveAt(p);
                        }
                    }
                    catch { _activePlayers.RemoveAt(p); }
                }

                _activePlayers.Add((NormalizeKey(filePath), player));
            }
        }
    }

    public void Stop(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            StopAll();
            return;
        }

        string key = NormalizeKey(filePath);
        lock (_lock)
        {
            for (int i = _activePlayers.Count - 1; i >= 0; i--)
            {
                if (_activePlayers[i].Key == key)
                {
                    try
                    {
                        _activePlayers[i].Player.controls.stop();
                        _activePlayers[i].Player.close();
                        Marshal.FinalReleaseComObject(_activePlayers[i].Player);
                    }
                    catch { }
                    _activePlayers.RemoveAt(i);
                }
            }
        }
    }

    public void Pause(string? filePath)
    {
        string? key = string.IsNullOrWhiteSpace(filePath) ? null : NormalizeKey(filePath);
        lock (_lock)
        {
            foreach (var item in _activePlayers)
            {
                if (key == null || item.Key == key)
                {
                    try
                    {
                        item.Player.controls.pause();
                    }
                    catch { }
                }
            }
        }
    }

    public void Resume(string? filePath)
    {
        string? key = string.IsNullOrWhiteSpace(filePath) ? null : NormalizeKey(filePath);
        lock (_lock)
        {
            foreach (var item in _activePlayers)
            {
                if (key == null || item.Key == key)
                {
                    try
                    {
                        item.Player.controls.play();
                    }
                    catch { }
                }
            }
        }
    }

    public void SetVolume(string? filePath, int volume)
    {
        int v = Math.Clamp(volume, 0, 100);
        string? key = string.IsNullOrWhiteSpace(filePath) ? null : NormalizeKey(filePath);
        lock (_lock)
        {
            foreach (var item in _activePlayers)
            {
                if (key == null || item.Key == key)
                {
                    try
                    {
                        item.Player.settings.volume = v;
                    }
                    catch { }
                }
            }
        }
    }

    public void StopAll()
    {
        lock (_lock)
        {
            foreach (var item in _activePlayers)
            {
                try
                {
                    item.Player.controls.stop();
                    item.Player.close();
                    Marshal.FinalReleaseComObject(item.Player);
                }
                catch { }
            }
            _activePlayers.Clear();
        }
    }
#else
    public void Play(string filePath, bool waitForEnd) { }
    public void Stop(string? filePath) { }
    public void Pause(string? filePath) { }
    public void Resume(string? filePath) { }
    public void SetVolume(string? filePath, int volume) { }
    public void StopAll() { }
#endif
}
