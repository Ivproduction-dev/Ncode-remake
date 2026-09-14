namespace Ncode.Core.Abstractions;

public interface IDialogService
{
    void ShowMessage(string text);
    void ShowError(string text);
    bool AskYesNo(string text);
}

public static class DialogService
{
    public static IDialogService Current { get; set; } = new NullDialogService();
}

public sealed class NullDialogService : IDialogService
{
    public void ShowMessage(string text) => Console.WriteLine("[сообщение] " + text);
    public void ShowError(string text) => Console.WriteLine("[ошибка] " + text);
    public bool AskYesNo(string text)
    {
        Console.Write("[да или нет] " + text + " (да/нет): ");
        var r = Console.ReadLine()?.Trim().ToLowerInvariant();
        return r is "да" or "y" or "yes" or "д";
    }
}
