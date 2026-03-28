using CelDotNet.Lexer;
using Xunit;

namespace CelDotNet.Tests.Lexer;

public class CelLexerTests
{
    private static List<Token> Tokenise(string source) => new CelLexer(source).Tokenise();

    private static Token Single(string source)
    {
        var tokens = Tokenise(source);
        Assert.True(tokens.Count >= 2, $"Expected at least 2 tokens (token + EOF), got {tokens.Count}");
        Assert.Equal(TokenKind.Eof, tokens[^1].Kind);
        return tokens[0];
    }

    #region Simple Tokens

    [Theory]
    [InlineData("(", TokenKind.LeftParen)]
    [InlineData(")", TokenKind.RightParen)]
    [InlineData("[", TokenKind.LeftBracket)]
    [InlineData("]", TokenKind.RightBracket)]
    [InlineData("{", TokenKind.LeftBrace)]
    [InlineData("}", TokenKind.RightBrace)]
    [InlineData(",", TokenKind.Comma)]
    [InlineData(".", TokenKind.Dot)]
    [InlineData("+", TokenKind.Plus)]
    [InlineData("-", TokenKind.Minus)]
    [InlineData("*", TokenKind.Star)]
    [InlineData("/", TokenKind.Slash)]
    [InlineData("%", TokenKind.Percent)]
    [InlineData("?", TokenKind.Question)]
    [InlineData(":", TokenKind.Colon)]
    [InlineData("!", TokenKind.Bang)]
    [InlineData("==", TokenKind.EqualEqual)]
    [InlineData("!=", TokenKind.BangEqual)]
    [InlineData("<", TokenKind.LessThan)]
    [InlineData("<=", TokenKind.LessThanEqual)]
    [InlineData(">", TokenKind.GreaterThan)]
    [InlineData(">=", TokenKind.GreaterThanEqual)]
    [InlineData("&&", TokenKind.AmpersandAmpersand)]
    [InlineData("||", TokenKind.PipePipe)]
    public void Tokenises_SingleCharacterTokens(string source, TokenKind expectedKind)
    {
        var token = Single(source);
        Assert.Equal(expectedKind, token.Kind);
        Assert.Equal(source, token.Lexeme);
    }

    #endregion

    #region Integer Literals

    [Theory]
    [InlineData("0", 0L)]
    [InlineData("42", 42L)]
    [InlineData("123456789", 123456789L)]
    public void Tokenises_IntegerLiterals(string source, long expected)
    {
        var token = Single(source);
        Assert.Equal(TokenKind.IntLiteral, token.Kind);
        Assert.Equal(expected, token.Value);
    }

    [Theory]
    [InlineData("0u", 0UL)]
    [InlineData("42u", 42UL)]
    [InlineData("42U", 42UL)]
    public void Tokenises_UnsignedIntegerLiterals(string source, ulong expected)
    {
        var token = Single(source);
        Assert.Equal(TokenKind.UintLiteral, token.Kind);
        Assert.Equal(expected, token.Value);
    }

    [Theory]
    [InlineData("0x0", 0L)]
    [InlineData("0xFF", 255L)]
    [InlineData("0xDEAD", 0xDEADL)]
    public void Tokenises_HexLiterals(string source, long expected)
    {
        var token = Single(source);
        Assert.Equal(TokenKind.IntLiteral, token.Kind);
        Assert.Equal(expected, token.Value);
    }

    [Theory]
    [InlineData("0xFFu", 255UL)]
    [InlineData("0xDEADU", 0xDEADUL)]
    public void Tokenises_UnsignedHexLiterals(string source, ulong expected)
    {
        var token = Single(source);
        Assert.Equal(TokenKind.UintLiteral, token.Kind);
        Assert.Equal(expected, token.Value);
    }

    #endregion

    #region Double Literals

    [Theory]
    [InlineData("0.0", 0.0)]
    [InlineData("3.14", 3.14)]
    [InlineData("1e10", 1e10)]
    [InlineData("1.5e2", 1.5e2)]
    [InlineData("7e0", 7.0)]
    [InlineData(".5", 0.5)]
    public void Tokenises_DoubleLiterals(string source, double expected)
    {
        var token = Single(source);
        Assert.Equal(TokenKind.DoubleLiteral, token.Kind);
        Assert.Equal(expected, token.Value);
    }

    #endregion

    #region String Literals

    [Theory]
    [InlineData("\"hello\"", "hello")]
    [InlineData("'hello'", "hello")]
    [InlineData("\"\"", "")]
    [InlineData("''", "")]
    [InlineData("\"hello\\nworld\"", "hello\nworld")]
    [InlineData("\"hello\\tworld\"", "hello\tworld")]
    [InlineData("\"escaped\\\"quote\"", "escaped\"quote")]
    [InlineData("'escaped\\'quote'", "escaped'quote")]
    [InlineData("\"\\x41\"", "A")]
    [InlineData("\"\\u0041\"", "A")]
    public void Tokenises_StringLiterals(string source, string expected)
    {
        var token = Single(source);
        Assert.Equal(TokenKind.StringLiteral, token.Kind);
        Assert.Equal(expected, token.Value);
    }

    [Fact]
    public void Tokenises_TripleQuotedStrings()
    {
        var token = Single("\"\"\"hello\nworld\"\"\"");
        Assert.Equal(TokenKind.StringLiteral, token.Kind);
        Assert.Equal("hello\nworld", token.Value);
    }

    [Fact]
    public void Tokenises_RawStrings()
    {
        var token = Single("r\"hello\\nworld\"");
        Assert.Equal(TokenKind.StringLiteral, token.Kind);
        Assert.Equal("hello\\nworld", token.Value);
    }

    #endregion

    #region Bytes Literals

    [Theory]
    [InlineData("b\"\"", new byte[] { })]
    [InlineData("b\"abc\"", new byte[] { 97, 98, 99 })]
    [InlineData("b\"\\x00\\xFF\"", new byte[] { 0x00, 0xFF })]
    public void Tokenises_BytesLiterals(string source, byte[] expected)
    {
        var token = Single(source);
        Assert.Equal(TokenKind.BytesLiteral, token.Kind);
        Assert.Equal(expected, token.Value);
    }

    #endregion

    #region Keywords and Identifiers

    [Theory]
    [InlineData("true", TokenKind.BoolLiteral, true)]
    [InlineData("false", TokenKind.BoolLiteral, false)]
    [InlineData("null", TokenKind.NullLiteral, null)]
    [InlineData("in", TokenKind.In, null)]
    public void Tokenises_Keywords(string source, TokenKind expectedKind, object? expectedValue)
    {
        var token = Single(source);
        Assert.Equal(expectedKind, token.Kind);
        if (expectedValue is not null)
            Assert.Equal(expectedValue, token.Value);
    }

    [Theory]
    [InlineData("foo")]
    [InlineData("_bar")]
    [InlineData("camelCase")]
    [InlineData("PascalCase")]
    [InlineData("snake_case")]
    [InlineData("x1")]
    public void Tokenises_Identifiers(string source)
    {
        var token = Single(source);
        Assert.Equal(TokenKind.Identifier, token.Kind);
        Assert.Equal(source, token.Lexeme);
    }

    #endregion

    #region Whitespace and Comments

    [Fact]
    public void Skips_Whitespace()
    {
        var tokens = Tokenise("  42  ");
        Assert.Equal(2, tokens.Count); // IntLiteral + Eof
        Assert.Equal(TokenKind.IntLiteral, tokens[0].Kind);
    }

    [Fact]
    public void Skips_LineComments()
    {
        var tokens = Tokenise("42 // this is a comment\n + 1");
        Assert.Equal(4, tokens.Count); // 42 + 1 Eof
        Assert.Equal(TokenKind.IntLiteral, tokens[0].Kind);
        Assert.Equal(TokenKind.Plus, tokens[1].Kind);
        Assert.Equal(TokenKind.IntLiteral, tokens[2].Kind);
    }

    #endregion

    #region Complex Expressions

    [Fact]
    public void Tokenises_FilterExpression()
    {
        var tokens = Tokenise("name == 'foo' && age > 21");
        var kinds = tokens.Select(t => t.Kind).ToList();
        Assert.Equal(
        [
            TokenKind.Identifier,       // name
            TokenKind.EqualEqual,        // ==
            TokenKind.StringLiteral,     // 'foo'
            TokenKind.AmpersandAmpersand, // &&
            TokenKind.Identifier,        // age
            TokenKind.GreaterThan,       // >
            TokenKind.IntLiteral,        // 21
            TokenKind.Eof,
        ], kinds);
    }

    [Fact]
    public void Tokenises_MethodCall()
    {
        var tokens = Tokenise("name.startsWith('test')");
        var kinds = tokens.Select(t => t.Kind).ToList();
        Assert.Equal(
        [
            TokenKind.Identifier,  // name
            TokenKind.Dot,         // .
            TokenKind.Identifier,  // startsWith
            TokenKind.LeftParen,   // (
            TokenKind.StringLiteral, // 'test'
            TokenKind.RightParen,  // )
            TokenKind.Eof,
        ], kinds);
    }

    [Fact]
    public void Tokenises_ListExpression()
    {
        var tokens = Tokenise("x in [1, 2, 3]");
        var kinds = tokens.Select(t => t.Kind).ToList();
        Assert.Equal(
        [
            TokenKind.Identifier,    // x
            TokenKind.In,            // in
            TokenKind.LeftBracket,   // [
            TokenKind.IntLiteral,    // 1
            TokenKind.Comma,         // ,
            TokenKind.IntLiteral,    // 2
            TokenKind.Comma,         // ,
            TokenKind.IntLiteral,    // 3
            TokenKind.RightBracket,  // ]
            TokenKind.Eof,
        ], kinds);
    }

    #endregion

    #region Error Handling

    [Theory]
    [InlineData("=", "unexpected '=', did you mean '=='?")]
    [InlineData("&", "unexpected '&', did you mean '&&'?")]
    [InlineData("|", "unexpected '|', did you mean '||'?")]
    public void Produces_ErrorTokens_ForInvalidInput(string source, string expectedMessage)
    {
        var token = Single(source);
        Assert.Equal(TokenKind.Error, token.Kind);
        Assert.Equal(expectedMessage, token.Lexeme);
    }

    [Fact]
    public void Tracks_Position_Correctly()
    {
        var tokens = Tokenise("a\nb");
        Assert.Equal(1, tokens[0].Span.Start.Line);
        Assert.Equal(1, tokens[0].Span.Start.Column);
        Assert.Equal(2, tokens[1].Span.Start.Line);
        Assert.Equal(1, tokens[1].Span.Start.Column);
    }

    #endregion
}
