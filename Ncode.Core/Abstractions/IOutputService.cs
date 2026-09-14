namespace Ncode.Core.Abstractions;

public interface IOutputService
{
    void Write(string text);
    void WriteLine(string text);
}
