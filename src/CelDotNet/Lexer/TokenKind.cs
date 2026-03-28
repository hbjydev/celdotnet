namespace CelDotNet.Lexer;

/// <summary>
/// All token kinds in the CEL grammar.
/// </summary>
public enum TokenKind
{
    // Literals
    IntLiteral,
    UintLiteral,
    DoubleLiteral,
    StringLiteral,
    BytesLiteral,
    BoolLiteral,
    NullLiteral,

    // Identifiers
    Identifier,

    // Operators - Arithmetic
    Plus,
    Minus,
    Star,
    Slash,
    Percent,

    // Operators - Comparison
    EqualEqual,
    BangEqual,
    LessThan,
    LessThanEqual,
    GreaterThan,
    GreaterThanEqual,

    // Operators - Logical
    AmpersandAmpersand,
    PipePipe,
    Bang,

    // Operators - Ternary
    Question,
    Colon,

    // Delimiters
    LeftParen,
    RightParen,
    LeftBracket,
    RightBracket,
    LeftBrace,
    RightBrace,

    // Punctuation
    Dot,
    Comma,

    // Keywords
    In,
    True,
    False,
    Null,

    // Special
    Eof,
    Error,
}
