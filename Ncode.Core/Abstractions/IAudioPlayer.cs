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
