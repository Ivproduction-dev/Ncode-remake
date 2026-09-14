// Ncode — Russian programming language for games.
// Copyright (C) 2026 Ivproduction
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later.
// See LICENSE in the repository root.

namespace Ncode.Core.Abstractions;

public interface IAudioPlayer
{
    void Play(string filePath, bool waitForEnd);
    void Stop(string? filePath);
    void Pause(string? filePath);
    void Resume(string? filePath);
    void SetVolume(string? filePath, int volume);
    void StopAll();
}

public static class AudioService
{
    public static IAudioPlayer Current { get; set; } = new NullAudioPlayer();
}

public sealed class NullAudioPlayer : IAudioPlayer
{
    public void Play(string filePath, bool waitForEnd) { }
    public void Stop(string? filePath) { }
    public void Pause(string? filePath) { }
    public void Resume(string? filePath) { }
    public void SetVolume(string? filePath, int volume) { }
    public void StopAll() { }
}
