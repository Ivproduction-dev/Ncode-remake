namespace Ncode.Core.Common;

public class BreakException : Exception { }
public class ContinueException : Exception { }
public class ExitException : Exception { }

public class NcodeRuntimeException : Exception
{
    public int Line { get; }

    public NcodeRuntimeException(string message, int line) : base($"строка {line}: {message}")
    {
        Line = line;
    }
}
