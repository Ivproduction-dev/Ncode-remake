namespace Ncode.Core.Lexer;

public enum TokKind { Num, Str, Name, Op, LPar, RPar, Bool }

public record Tok(TokKind Kind, string Val);
