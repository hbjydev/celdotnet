using System.Globalization;
using System.Text;

namespace CelDotNet.Lexer;

/// <summary>
/// Hand-written lexer for the CEL (Common Expression Language) grammar.
/// Produces a sequence of <see cref="Token"/>s from a source string.
/// </summary>
public sealed class CelLexer
{
    private readonly string _source;
    private int _pos;
    private int _line;
    private int _col;

    public CelLexer(string source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _pos = 0;
        _line = 1;
        _col = 1;
    }

    /// <summary>
    /// Tokenises the entire source string and returns all tokens including the final EOF.
    /// </summary>
    public List<Token> Tokenise()
    {
        var tokens = new List<Token>();
        while (true)
        {
            var token = NextToken();
            tokens.Add(token);
            if (token.Kind == TokenKind.Eof)
                break;
        }
        return tokens;
    }

    /// <summary>
    /// Returns the next token from the source.
    /// </summary>
    public Token NextToken()
    {
        SkipWhitespaceAndComments();

        if (IsAtEnd)
            return MakeToken(TokenKind.Eof, "", CurrentPosition);

        var start = CurrentPosition;
        char c = Advance();

        return c switch
        {
            '(' => MakeToken(TokenKind.LeftParen, "(", start),
            ')' => MakeToken(TokenKind.RightParen, ")", start),
            '[' => MakeToken(TokenKind.LeftBracket, "[", start),
            ']' => MakeToken(TokenKind.RightBracket, "]", start),
            '{' => MakeToken(TokenKind.LeftBrace, "{", start),
            '}' => MakeToken(TokenKind.RightBrace, "}", start),
            ',' => MakeToken(TokenKind.Comma, ",", start),
            '+' => MakeToken(TokenKind.Plus, "+", start),
            '*' => MakeToken(TokenKind.Star, "*", start),
            '/' => MakeToken(TokenKind.Slash, "/", start),
            '%' => MakeToken(TokenKind.Percent, "%", start),
            '?' => MakeToken(TokenKind.Question, "?", start),
            ':' => MakeToken(TokenKind.Colon, ":", start),
            '-' => MakeToken(TokenKind.Minus, "-", start),
            '.' => Peek() is >= '0' and <= '9' ? ScanNumber(start, leadingDot: true) : MakeToken(TokenKind.Dot, ".", start),
            '!' => Match('=')
                ? MakeToken(TokenKind.BangEqual, "!=", start)
                : MakeToken(TokenKind.Bang, "!", start),
            '=' => Match('=')
                ? MakeToken(TokenKind.EqualEqual, "==", start)
                : MakeError("unexpected '=', did you mean '=='?", start),
            '<' => Match('=')
                ? MakeToken(TokenKind.LessThanEqual, "<=", start)
                : MakeToken(TokenKind.LessThan, "<", start),
            '>' => Match('=')
                ? MakeToken(TokenKind.GreaterThanEqual, ">=", start)
                : MakeToken(TokenKind.GreaterThan, ">", start),
            '&' => Match('&')
                ? MakeToken(TokenKind.AmpersandAmpersand, "&&", start)
                : MakeError("unexpected '&', did you mean '&&'?", start),
            '|' => Match('|')
                ? MakeToken(TokenKind.PipePipe, "||", start)
                : MakeError("unexpected '|', did you mean '||'?", start),
            '\'' or '"' => ScanString(start, c),
            'b' or 'B' when Peek() is '\'' or '"' => ScanBytes(start),
            'r' or 'R' when Peek() is '\'' or '"' => ScanRawString(start),
            >= '0' and <= '9' => ScanNumber(start),
            _ when IsIdentStart(c) => ScanIdentifierOrKeyword(start),
            _ => MakeError($"unexpected character '{c}'", start),
        };
    }

    #region Character Scanning

    private SourcePosition CurrentPosition => new(_pos, _line, _col);

    private bool IsAtEnd => _pos >= _source.Length;

    private char Peek()
    {
        if (IsAtEnd) return '\0';
        return _source[_pos];
    }

    private char PeekNext()
    {
        if (_pos + 1 >= _source.Length) return '\0';
        return _source[_pos + 1];
    }

    private char Advance()
    {
        char c = _source[_pos++];
        if (c == '\n')
        {
            _line++;
            _col = 1;
        }
        else
        {
            _col++;
        }
        return c;
    }

    private bool Match(char expected)
    {
        if (IsAtEnd || _source[_pos] != expected)
            return false;
        Advance();
        return true;
    }

    private void SkipWhitespaceAndComments()
    {
        while (!IsAtEnd)
        {
            char c = Peek();
            if (c is ' ' or '\t' or '\r' or '\n')
            {
                Advance();
                continue;
            }
            // Single-line comment: // ...
            if (c == '/' && PeekNext() == '/')
            {
                while (!IsAtEnd && Peek() != '\n')
                    Advance();
                continue;
            }
            break;
        }
    }

    private static bool IsIdentStart(char c) =>
        c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '_';

    private static bool IsIdentPart(char c) =>
        c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_';

    private static bool IsDigit(char c) => c is >= '0' and <= '9';

    private static bool IsHexDigit(char c) =>
        c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');

    #endregion

    #region Token Constructors

    private static Token MakeToken(TokenKind kind, string lexeme, SourcePosition start, object? value = null)
    {
        var end = new SourcePosition(
            start.Offset + lexeme.Length,
            start.Line,
            start.Column + lexeme.Length);
        return new Token(kind, lexeme, new SourceSpan(start, end), value);
    }

    private Token MakeTokenAtCurrent(TokenKind kind, SourcePosition start, object? value = null)
    {
        string lexeme = _source[start.Offset.._pos];
        var end = CurrentPosition;
        return new Token(kind, lexeme, new SourceSpan(start, end), value);
    }

    private Token MakeError(string message, SourcePosition start)
    {
        return new Token(TokenKind.Error, message, new SourceSpan(start, CurrentPosition));
    }

    #endregion

    #region Number Scanning

    private Token ScanNumber(SourcePosition start, bool leadingDot = false)
    {
        if (leadingDot)
        {
            // We already consumed '.', scan fractional part
            while (!IsAtEnd && IsDigit(Peek()))
                Advance();
            if (!IsAtEnd && Peek() is 'e' or 'E')
                ScanExponent();
            string doubleLexeme = _source[start.Offset.._pos];
            if (double.TryParse(doubleLexeme, NumberStyles.Float, CultureInfo.InvariantCulture, out double dv))
                return MakeTokenAtCurrent(TokenKind.DoubleLiteral, start, dv);
            return MakeError($"invalid double literal '{doubleLexeme}'", start);
        }

        // Check for hex: 0x or 0X
        if (_source[start.Offset] == '0' && !IsAtEnd && Peek() is 'x' or 'X')
        {
            Advance(); // consume 'x'
            if (IsAtEnd || !IsHexDigit(Peek()))
                return MakeError("expected hex digit after '0x'", start);
            while (!IsAtEnd && IsHexDigit(Peek()))
                Advance();
            bool isUnsignedHex = !IsAtEnd && Peek() is 'u' or 'U';
            if (isUnsignedHex) Advance();
            string hexLexeme = _source[start.Offset.._pos];
            string hexDigits = hexLexeme.Contains('x', StringComparison.OrdinalIgnoreCase)
                ? hexLexeme.Split('x', 'X')[1].TrimEnd('u', 'U')
                : hexLexeme;
            if (isUnsignedHex)
            {
                if (ulong.TryParse(hexDigits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong uhv))
                    return MakeTokenAtCurrent(TokenKind.UintLiteral, start, uhv);
                return MakeError($"invalid unsigned hex literal '{hexLexeme}'", start);
            }
            if (ulong.TryParse(hexDigits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong hv))
            {
                if (hv <= long.MaxValue)
                    return MakeTokenAtCurrent(TokenKind.IntLiteral, start, (long)hv);
                return MakeError($"hex literal '{hexLexeme}' overflows int64", start);
            }
            return MakeError($"invalid hex literal '{hexLexeme}'", start);
        }

        // Scan integer digits
        while (!IsAtEnd && IsDigit(Peek()))
            Advance();

        // Check for unsigned suffix
        if (!IsAtEnd && Peek() is 'u' or 'U')
        {
            Advance();
            string uintLexeme = _source[start.Offset.._pos];
            string digits = uintLexeme.TrimEnd('u', 'U');
            if (ulong.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong uv))
                return MakeTokenAtCurrent(TokenKind.UintLiteral, start, uv);
            return MakeError($"invalid unsigned integer literal '{uintLexeme}'", start);
        }

        // Check for double (dot or exponent)
        bool isDouble = false;
        if (!IsAtEnd && Peek() == '.' && PeekNext() != '.')
        {
            isDouble = true;
            Advance(); // consume '.'
            while (!IsAtEnd && IsDigit(Peek()))
                Advance();
        }
        if (!IsAtEnd && Peek() is 'e' or 'E')
        {
            isDouble = true;
            ScanExponent();
        }

        string numLexeme = _source[start.Offset.._pos];
        if (isDouble)
        {
            if (double.TryParse(numLexeme, NumberStyles.Float, CultureInfo.InvariantCulture, out double dval))
                return MakeTokenAtCurrent(TokenKind.DoubleLiteral, start, dval);
            return MakeError($"invalid double literal '{numLexeme}'", start);
        }

        if (long.TryParse(numLexeme, NumberStyles.Integer, CultureInfo.InvariantCulture, out long lval))
            return MakeTokenAtCurrent(TokenKind.IntLiteral, start, lval);
        return MakeError($"invalid integer literal '{numLexeme}'", start);
    }

    private void ScanExponent()
    {
        Advance(); // consume 'e' or 'E'
        if (!IsAtEnd && Peek() is '+' or '-')
            Advance();
        while (!IsAtEnd && IsDigit(Peek()))
            Advance();
    }

    #endregion

    #region String Scanning

    private Token ScanString(SourcePosition start, char quote)
    {
        // Check for triple-quoted strings
        bool isTriple = false;
        if (!IsAtEnd && Peek() == quote)
        {
            if (_pos + 1 < _source.Length && _source[_pos + 1] == quote)
            {
                isTriple = true;
                Advance(); // consume second quote
                Advance(); // consume third quote
            }
            else
            {
                // Empty string: '' or ""
                Advance(); // consume closing quote
                return MakeTokenAtCurrent(TokenKind.StringLiteral, start, "");
            }
        }

        var sb = new StringBuilder();
        while (!IsAtEnd)
        {
            if (isTriple)
            {
                if (Peek() == quote && _pos + 2 < _source.Length &&
                    _source[_pos + 1] == quote && _source[_pos + 2] == quote)
                {
                    Advance(); Advance(); Advance();
                    return MakeTokenAtCurrent(TokenKind.StringLiteral, start, sb.ToString());
                }
            }
            else
            {
                if (Peek() == quote)
                {
                    Advance();
                    return MakeTokenAtCurrent(TokenKind.StringLiteral, start, sb.ToString());
                }
                if (Peek() == '\n')
                    return MakeError("unterminated string literal", start);
            }

            if (Peek() == '\\')
            {
                Advance(); // consume backslash
                if (IsAtEnd)
                    return MakeError("unterminated escape sequence", start);
                char escaped = Advance();
                switch (escaped)
                {
                    case 'a': sb.Append('\a'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'v': sb.Append('\v'); break;
                    case '\\': sb.Append('\\'); break;
                    case '\'': sb.Append('\''); break;
                    case '"': sb.Append('"'); break;
                    case '`': sb.Append('`'); break;
                    case '0': sb.Append('\0'); break;
                    case 'x':
                    case 'X':
                        if (!TryScanHexEscape(2, out char hexChar))
                            return MakeError("invalid hex escape", start);
                        sb.Append(hexChar);
                        break;
                    case 'u':
                        if (!TryScanHexEscape(4, out char uChar))
                            return MakeError("invalid unicode escape", start);
                        sb.Append(uChar);
                        break;
                    case 'U':
                        if (!TryScanHexEscape(8, out int codePoint))
                            return MakeError("invalid unicode escape", start);
                        sb.Append(char.ConvertFromUtf32(codePoint));
                        break;
                    default:
                        if (escaped is >= '0' and <= '7')
                        {
                            if (!TryScanOctalEscape(escaped, out byte octalByte))
                                return MakeError("invalid octal escape", start);
                            sb.Append((char)octalByte);
                        }
                        else
                        {
                            return MakeError($"invalid escape character '\\{escaped}'", start);
                        }
                        break;
                }
            }
            else
            {
                sb.Append(Advance());
            }
        }

        return MakeError("unterminated string literal", start);
    }

    private Token ScanRawString(SourcePosition start)
    {
        Advance(); // consume the opening quote
        char quote = _source[_pos - 1];

        // Check for triple-quoted raw strings
        bool isTriple = false;
        if (!IsAtEnd && Peek() == quote)
        {
            if (_pos + 1 < _source.Length && _source[_pos + 1] == quote)
            {
                isTriple = true;
                Advance(); Advance();
            }
            else
            {
                Advance(); // empty raw string
                return MakeTokenAtCurrent(TokenKind.StringLiteral, start, "");
            }
        }

        var sb = new StringBuilder();
        while (!IsAtEnd)
        {
            if (isTriple)
            {
                if (Peek() == quote && _pos + 2 < _source.Length &&
                    _source[_pos + 1] == quote && _source[_pos + 2] == quote)
                {
                    Advance(); Advance(); Advance();
                    return MakeTokenAtCurrent(TokenKind.StringLiteral, start, sb.ToString());
                }
            }
            else
            {
                if (Peek() == quote)
                {
                    Advance();
                    return MakeTokenAtCurrent(TokenKind.StringLiteral, start, sb.ToString());
                }
                if (Peek() == '\n')
                    return MakeError("unterminated raw string literal", start);
            }
            // Raw strings: no escape processing, backslash is literal
            sb.Append(Advance());
        }

        return MakeError("unterminated raw string literal", start);
    }

    private Token ScanBytes(SourcePosition start)
    {
        char quote = Advance(); // consume the quote after 'b'/'B'

        // Check for triple-quoted bytes
        bool isTriple = false;
        if (!IsAtEnd && Peek() == quote)
        {
            if (_pos + 1 < _source.Length && _source[_pos + 1] == quote)
            {
                isTriple = true;
                Advance(); Advance();
            }
            else
            {
                Advance(); // empty bytes literal
                return MakeTokenAtCurrent(TokenKind.BytesLiteral, start, Array.Empty<byte>());
            }
        }

        var bytes = new List<byte>();
        while (!IsAtEnd)
        {
            if (isTriple)
            {
                if (Peek() == quote && _pos + 2 < _source.Length &&
                    _source[_pos + 1] == quote && _source[_pos + 2] == quote)
                {
                    Advance(); Advance(); Advance();
                    return MakeTokenAtCurrent(TokenKind.BytesLiteral, start, bytes.ToArray());
                }
            }
            else
            {
                if (Peek() == quote)
                {
                    Advance();
                    return MakeTokenAtCurrent(TokenKind.BytesLiteral, start, bytes.ToArray());
                }
                if (Peek() == '\n')
                    return MakeError("unterminated bytes literal", start);
            }

            if (Peek() == '\\')
            {
                Advance();
                if (IsAtEnd)
                    return MakeError("unterminated escape in bytes literal", start);
                char escaped = Advance();
                switch (escaped)
                {
                    case 'a': bytes.Add((byte)'\a'); break;
                    case 'b': bytes.Add((byte)'\b'); break;
                    case 'f': bytes.Add((byte)'\f'); break;
                    case 'n': bytes.Add((byte)'\n'); break;
                    case 'r': bytes.Add((byte)'\r'); break;
                    case 't': bytes.Add((byte)'\t'); break;
                    case 'v': bytes.Add((byte)'\v'); break;
                    case '\\': bytes.Add((byte)'\\'); break;
                    case '\'': bytes.Add((byte)'\''); break;
                    case '"': bytes.Add((byte)'"'); break;
                    case '0': bytes.Add(0); break;
                    case 'x' or 'X':
                        if (!TryScanHexEscape(2, out char hc))
                            return MakeError("invalid hex escape in bytes literal", start);
                        bytes.Add((byte)hc);
                        break;
                    default:
                        if (escaped is >= '0' and <= '7')
                        {
                            if (!TryScanOctalEscape(escaped, out byte ob))
                                return MakeError("invalid octal escape in bytes literal", start);
                            bytes.Add(ob);
                        }
                        else
                        {
                            return MakeError($"invalid escape '\\{escaped}' in bytes literal", start);
                        }
                        break;
                }
            }
            else
            {
                char ch = Advance();
                if (ch > 255)
                    return MakeError("non-ASCII character in bytes literal", start);
                bytes.Add((byte)ch);
            }
        }

        return MakeError("unterminated bytes literal", start);
    }

    private bool TryScanHexEscape(int digits, out char result)
    {
        result = '\0';
        int value = 0;
        for (int i = 0; i < digits; i++)
        {
            if (IsAtEnd || !IsHexDigit(Peek()))
                return false;
            value = value * 16 + HexValue(Advance());
        }
        result = (char)value;
        return true;
    }

    private bool TryScanHexEscape(int digits, out int result)
    {
        result = 0;
        for (int i = 0; i < digits; i++)
        {
            if (IsAtEnd || !IsHexDigit(Peek()))
                return false;
            result = result * 16 + HexValue(Advance());
        }
        return true;
    }

    private bool TryScanOctalEscape(char first, out byte result)
    {
        result = 0;
        int value = first - '0';
        for (int i = 0; i < 2; i++)
        {
            if (IsAtEnd || Peek() is not (>= '0' and <= '7'))
                break;
            value = value * 8 + (Advance() - '0');
        }
        if (value > 255)
            return false;
        result = (byte)value;
        return true;
    }

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => 0,
    };

    #endregion

    #region Identifier / Keyword Scanning

    private Token ScanIdentifierOrKeyword(SourcePosition start)
    {
        while (!IsAtEnd && IsIdentPart(Peek()))
            Advance();

        string text = _source[start.Offset.._pos];

        return text switch
        {
            "true" => MakeTokenAtCurrent(TokenKind.BoolLiteral, start, true),
            "false" => MakeTokenAtCurrent(TokenKind.BoolLiteral, start, false),
            "null" => MakeTokenAtCurrent(TokenKind.NullLiteral, start),
            "in" => MakeTokenAtCurrent(TokenKind.In, start),
            _ => MakeTokenAtCurrent(TokenKind.Identifier, start),
        };
    }

    #endregion
}
