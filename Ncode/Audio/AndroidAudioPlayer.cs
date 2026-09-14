// Ncode — Russian programming language for games.
// Copyright (C) 2026 Ivproduction
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later.
// See LICENSE in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Ncode.Core.Abstractions;

namespace Ncode.Audio;

public sealed class AndroidAudioPlayer : IAudioPlayer
{
#if ANDROID
    private readonly object _lock = new();
    private readonly List<(string Key, global::Android.Media.MediaPlayer Player)> _activePlayers = new();

    private static string NormalizeKey(string path)
    {
        return path.Trim().TrimStart('/', '\\').ToLowerInvariant();
    }

    public void Play(string filePath, bool waitForEnd)
    {
        try
        {
            var player = new global::Android.Media.MediaPlayer();

            if (File.Exists(filePath))
            {
                player.SetDataSource(filePath);
            }
            else
            {
                var context = global::Android.App.Application.Context;
                string assetName = filePath.TrimStart('/', '\\');
                using var afd = context.Assets?.OpenFd(assetName);
                if (afd != null)
                {
                    player.SetDataSource(afd.FileDescriptor, afd.StartOffset, afd.Length);
                }
                else
                {
                    Console.WriteLine("[звук не найден в APK: " + filePath + "]");
                    return;
                }
            }

            player.Prepare();

            if (waitForEnd)
            {
                using var resetEvent = new ManualResetEventSlim(false);
                player.Completion += (s, e) =>
                {
                    try { resetEvent.Set(); } catch { }
                };
                player.Start();
                resetEvent.Wait();

                try { player.Stop(); player.Release(); } catch { }
            }
            else
            {
                string key = NormalizeKey(filePath);
                lock (_lock)
                {
                    for (int i = _activePlayers.Count - 1; i >= 0; i--)
                    {
                        var p = _activePlayers[i].Player;
                        if (!p.IsPlaying)
                        {
                            try { p.Release(); } catch { }
                            _activePlayers.RemoveAt(i);
                        }
                    }

                    player.Completion += (s, e) =>
                    {
                        lock (_lock)
                        {
                            for (int i = _activePlayers.Count - 1; i >= 0; i--)
                            {
                                if (_activePlayers[i].Player == player)
                                {
                                    _activePlayers.RemoveAt(i);
                                    break;
                                }
                            }
                            try { player.Release(); } catch { }
                        }
                    };

                    _activePlayers.Add((key, player));
                }
                player.Start();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ошибка звука '{filePath}': {ex.Message}]");
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
                        if (_activePlayers[i].Player.IsPlaying)
                            _activePlayers[i].Player.Stop();
                        _activePlayers[i].Player.Release();
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
                        if (item.Player.IsPlaying)
                            item.Player.Pause();
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
                        if (!item.Player.IsPlaying)
                            item.Player.Start();
                    }
                    catch { }
                }
            }
        }
    }

    public void SetVolume(string? filePath, int volume)
    {
        float v = Math.Clamp(volume, 0, 100) / 100f;
        string? key = string.IsNullOrWhiteSpace(filePath) ? null : NormalizeKey(filePath);
        lock (_lock)
        {
            foreach (var item in _activePlayers)
            {
                if (key == null || item.Key == key)
                {
                    try
                    {
                        item.Player.SetVolume(v, v);
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
                    if (item.Player.IsPlaying) item.Player.Stop();
                    item.Player.Release();
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
