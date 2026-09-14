namespace Ncode.Core.Abstractions;

public interface IClipboardService
{
    void Copy(string text);
    string Paste();
}

public static class ClipboardService
{
    public static IClipboardService Current { get; set; } = new NullClipboardService();
}

public sealed class NullClipboardService : IClipboardService
{
    private string _text = "";
    public void Copy(string text) => _text = text;
    public string Paste() => _text;
}
