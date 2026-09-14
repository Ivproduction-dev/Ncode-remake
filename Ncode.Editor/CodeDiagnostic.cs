namespace Ncode.Editor;

public enum DiagnosticSeverity { Warning, Error }

public record CodeDiagnostic(string File, int Line, string Message, DiagnosticSeverity Severity);
