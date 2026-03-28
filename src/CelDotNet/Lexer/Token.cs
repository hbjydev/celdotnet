namespace CelDotNet.Lexer;

/// <summary>
/// Represents a position in the source text.
/// </summary>
/// <param name="Offset">Zero-based character offset from the start of the source.</param>
/// <param name="Line">One-based line number.</param>
/// <param name="Column">One-based column number.</param>
public readonly record struct SourcePosition(int Offset, int Line, int Column)
{
    public override string ToString() => $"({Line}:{Column})";
}

/// <summary>
/// Represents a span of source text.
/// </summary>
/// <param name="Start">Start position (inclusive).</param>
/// <param name="End">End position (exclusive).</param>
public readonly record struct SourceSpan(SourcePosition Start, SourcePosition End)
{
    public override string ToString() => $"{Start}-{End}";
}

/// <summary>
/// A token produced by the CEL lexer.
/// </summary>
/// <param name="Kind">The kind of token.</param>
/// <param name="Lexeme">The raw source text of the token.</param>
/// <param name="Span">The source location of the token.</param>
/// <param name="Value">The parsed value for literals (long, ulong, double, string, bool, byte[], or null).</param>
public sealed record Token(
    TokenKind Kind,
    string Lexeme,
    SourceSpan Span,
    object? Value = null)
{
    public override string ToString() => $"{Kind}({Lexeme})@{Span.Start}";
}
