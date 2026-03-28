using System.Globalization;
using System.Text;

namespace CelDotNet.Conformance.Infrastructure;

/// <summary>
/// A minimal textproto parser that handles the subset needed for cel-spec SimpleTestFile.
/// This is NOT a general-purpose textproto parser — it only supports the structures used
/// in the conformance test suite.
/// </summary>
public static class TextProtoParser
{
    /// <summary>
    /// Parse a textproto file into a <see cref="SimpleTestFile"/>.
    /// </summary>
    public static SimpleTestFile Parse(string text)
    {
        var tokenizer = new Tokenizer(text);
        return ParseTestFile(ref tokenizer);
    }

    #region Top-level

    private static SimpleTestFile ParseTestFile(ref Tokenizer t)
    {
        string? name = null;
        string? description = null;
        var sections = new List<SimpleTestSection>();

        while (!t.AtEnd)
        {
            var field = t.ReadFieldName();
            switch (field)
            {
                case "name":
                    t.ExpectColon();
                    name = t.ReadString();
                    break;
                case "description":
                    t.ExpectColon();
                    description = t.ReadString();
                    break;
                case "section":
                    t.TryConsumeColon();
                    sections.Add(ParseSection(ref t));
                    break;
                default:
                    t.SkipField();
                    break;
            }
        }

        return new SimpleTestFile
        {
            Name = name ?? throw new FormatException("SimpleTestFile missing 'name'"),
            Description = description ?? "",
            Sections = sections,
        };
    }

    private static SimpleTestSection ParseSection(ref Tokenizer t)
    {
        string? name = null;
        string? description = null;
        var tests = new List<SimpleTest>();

        t.Expect('{');
        while (!t.TryConsume('}'))
        {
            var field = t.ReadFieldName();
            switch (field)
            {
                case "name":
                    t.ExpectColon();
                    name = t.ReadString();
                    break;
                case "description":
                    t.ExpectColon();
                    description = t.ReadConcatenatedStrings();
                    break;
                case "test":
                    t.TryConsumeColon();
                    tests.Add(ParseTest(ref t));
                    break;
                default:
                    t.TryConsumeColon();
                    t.SkipField();
                    break;
            }
        }

        return new SimpleTestSection
        {
            Name = name ?? throw new FormatException("Section missing 'name'"),
            Description = description ?? "",
            Tests = tests,
        };
    }

    private static SimpleTest ParseTest(ref Tokenizer t)
    {
        string? name = null;
        string? description = null;
        string? expr = null;
        bool disableMacros = false;
        bool disableCheck = false;
        bool checkOnly = false;
        string? container = null;
        ExpectedResult? result = null;
        var bindings = new List<Binding>();
        var typeEnv = new List<TypeDecl>();

        t.Expect('{');
        while (!t.TryConsume('}'))
        {
            var field = t.ReadFieldName();
            switch (field)
            {
                case "name":
                    t.ExpectColon();
                    name = t.ReadString();
                    break;
                case "description":
                    t.ExpectColon();
                    description = t.ReadConcatenatedStrings();
                    break;
                case "expr":
                    t.ExpectColon();
                    expr = t.ReadConcatenatedStrings();
                    break;
                case "disable_macros":
                    t.ExpectColon();
                    disableMacros = t.ReadBool();
                    break;
                case "disable_check":
                    t.ExpectColon();
                    disableCheck = t.ReadBool();
                    break;
                case "check_only":
                    t.ExpectColon();
                    checkOnly = t.ReadBool();
                    break;
                case "container":
                    t.ExpectColon();
                    container = t.ReadString();
                    break;
                case "value":
                    t.TryConsumeColon();
                    result = new ExpectedResult.Value(ParseValue(ref t));
                    break;
                case "eval_error":
                    t.TryConsumeColon();
                    result = ParseEvalError(ref t);
                    break;
                case "bindings":
                    t.TryConsumeColon();
                    bindings.Add(ParseBinding(ref t));
                    break;
                case "type_env":
                    t.TryConsumeColon();
                    typeEnv.Add(ParseTypeDecl(ref t));
                    break;
                default:
                    t.TryConsumeColon();
                    t.SkipField();
                    break;
            }
        }

        // Default result: value { bool_value: true }
        result ??= new ExpectedResult.Value(new ExpectedValue.BoolValue(true));

        return new SimpleTest
        {
            Name = name ?? throw new FormatException("Test missing 'name'"),
            Description = description ?? "",
            Expr = expr ?? throw new FormatException("Test missing 'expr'"),
            DisableMacros = disableMacros,
            DisableCheck = disableCheck,
            CheckOnly = checkOnly,
            Container = container,
            Result = result,
            Bindings = bindings,
            TypeEnv = typeEnv,
        };
    }

    #endregion

    #region Values

    private static ExpectedValue ParseValue(ref Tokenizer t)
    {
        t.Expect('{');

        if (t.TryConsume('}'))
            return new ExpectedValue.BoolValue(true); // empty value = true (default)

        var field = t.ReadFieldName();
        t.TryConsumeColon();
        var value = ParseValueField(field, ref t);
        t.TryConsume('}');
        return value;
    }

    private static ExpectedValue ParseValueField(string field, ref Tokenizer t)
    {
        switch (field)
        {
            case "int64_value":
                return new ExpectedValue.Int64Value(t.ReadInt64());
            case "uint64_value":
                return new ExpectedValue.Uint64Value(t.ReadUInt64());
            case "double_value":
                return new ExpectedValue.DoubleValue(t.ReadDouble());
            case "string_value":
                return new ExpectedValue.StringValue(t.ReadProtoString());
            case "bool_value":
                return new ExpectedValue.BoolValue(t.ReadBool());
            case "bytes_value":
                return new ExpectedValue.BytesValue(t.ReadProtoBytes());
            case "null_value":
                t.ReadIdentifier(); // consume "NULL_VALUE" or "0"
                return new ExpectedValue.NullValue();
            case "list_value":
                return ParseListValue(ref t);
            case "map_value":
                return ParseMapValue(ref t);
            case "type_value":
                return new ExpectedValue.TypeValue(t.ReadProtoString());
            case "object_value":
                // Protobuf Any — skip the nested structure, extract type URL for skip-detection
                return ParseObjectValue(ref t);
            default:
                throw new FormatException($"Unknown value field: {field}");
        }
    }

    private static ExpectedValue.ListValue ParseListValue(ref Tokenizer t)
    {
        var values = new List<ExpectedValue>();
        t.Expect('{');
        while (!t.TryConsume('}'))
        {
            var field = t.ReadFieldName();
            if (field != "values")
                throw new FormatException($"Expected 'values' in list_value, got '{field}'");
            t.TryConsumeColon();
            values.Add(ParseValue(ref t));
        }
        return new ExpectedValue.ListValue(values);
    }

    private static ExpectedValue.MapValue ParseMapValue(ref Tokenizer t)
    {
        var entries = new List<MapEntry>();
        t.Expect('{');
        while (!t.TryConsume('}'))
        {
            var field = t.ReadFieldName();
            if (field != "entries")
                throw new FormatException($"Expected 'entries' in map_value, got '{field}'");
            t.TryConsumeColon();
            entries.Add(ParseMapEntry(ref t));
        }
        return new ExpectedValue.MapValue(entries);
    }

    private static MapEntry ParseMapEntry(ref Tokenizer t)
    {
        ExpectedValue? key = null;
        ExpectedValue? value = null;

        t.Expect('{');
        while (!t.TryConsume('}'))
        {
            var field = t.ReadFieldName();
            t.TryConsumeColon();
            switch (field)
            {
                case "key":
                    key = ParseValue(ref t);
                    break;
                case "value":
                    value = ParseValue(ref t);
                    break;
                default:
                    t.SkipField();
                    break;
            }
        }

        return new MapEntry(
            key ?? throw new FormatException("MapEntry missing 'key'"),
            value ?? throw new FormatException("MapEntry missing 'value'"));
    }

    /// <summary>
    /// Parse a protobuf Any (object_value). We extract the type URL and skip the rest.
    /// Structure: object_value { [type.googleapis.com/...] { fields... } }
    /// </summary>
    private static ExpectedValue.ObjectValue ParseObjectValue(ref Tokenizer t)
    {
        t.Expect('{');
        // Read the type URL field: [type.googleapis.com/google.protobuf.Duration]
        var typeUrl = "unknown";
        t.SkipWhitespace();
        if (t.PeekChar() == '[')
        {
            t.Consume('[');
            var sb = new System.Text.StringBuilder();
            while (t.PeekChar() != ']')
                sb.Append(t.ReadChar());
            t.Consume(']');
            typeUrl = sb.ToString();
        }
        // Skip the nested message body
        t.SkipField(); // consumes the { ... } inner message
        t.TryConsume('}'); // close the outer object_value
        return new ExpectedValue.ObjectValue(typeUrl);
    }

    private static ExpectedResult.EvalError ParseEvalError(ref Tokenizer t)
    {
        string? message = null;
        t.Expect('{');
        while (!t.TryConsume('}'))
        {
            var field = t.ReadFieldName();
            t.TryConsumeColon();
            switch (field)
            {
                case "errors":
                    t.Expect('{');
                    while (!t.TryConsume('}'))
                    {
                        var errField = t.ReadFieldName();
                        t.TryConsumeColon();
                        if (errField == "message")
                            message = t.ReadString();
                        else
                            t.SkipField();
                    }
                    break;
                default:
                    t.SkipField();
                    break;
            }
        }
        return new ExpectedResult.EvalError(message);
    }

    private static Binding ParseBinding(ref Tokenizer t)
    {
        string? key = null;
        ExpectedValue? value = null;

        t.Expect('{');
        while (!t.TryConsume('}'))
        {
            var field = t.ReadFieldName();
            t.TryConsumeColon();
            switch (field)
            {
                case "key":
                    key = t.ReadString();
                    break;
                case "value":
                    // bindings value is wrapped: value: { value: { int64_value: 123 } }
                    t.Expect('{');
                    while (!t.TryConsume('}'))
                    {
                        var innerField = t.ReadFieldName();
                        t.TryConsumeColon();
                        if (innerField == "value")
                            value = ParseValue(ref t);
                        else
                            t.SkipField();
                    }
                    break;
                default:
                    t.SkipField();
                    break;
            }
        }

        return new Binding(
            key ?? throw new FormatException("Binding missing 'key'"),
            value ?? throw new FormatException("Binding missing 'value'"));
    }

    private static TypeDecl ParseTypeDecl(ref Tokenizer t)
    {
        string? name = null;
        string? primitiveType = null;

        t.Expect('{');
        while (!t.TryConsume('}'))
        {
            var field = t.ReadFieldName();
            switch (field)
            {
                case "name":
                    t.ExpectColon();
                    name = t.ReadString();
                    break;
                case "ident":
                    t.TryConsumeColon();
                    primitiveType = ParseIdentType(ref t);
                    break;
                default:
                    t.TryConsumeColon();
                    t.SkipField();
                    break;
            }
        }

        return new TypeDecl(
            name ?? throw new FormatException("TypeDecl missing 'name'"),
            primitiveType ?? "DYN");
    }

    private static string ParseIdentType(ref Tokenizer t)
    {
        string primitiveType = "DYN";
        t.Expect('{');
        while (!t.TryConsume('}'))
        {
            var field = t.ReadFieldName();
            t.TryConsumeColon();
            if (field == "type")
            {
                t.Expect('{');
                while (!t.TryConsume('}'))
                {
                    var typeField = t.ReadFieldName();
                    t.TryConsumeColon();
                    if (typeField == "primitive")
                        primitiveType = t.ReadIdentifier();
                    else
                        t.SkipField();
                }
            }
            else
            {
                t.SkipField();
            }
        }
        return primitiveType;
    }

    #endregion

    #region Tokenizer

    /// <summary>
    /// Simple tokenizer for textproto format. Handles strings, numbers, identifiers,
    /// braces, colons, and comments.
    /// </summary>
    private ref struct Tokenizer
    {
        private readonly ReadOnlySpan<char> _text;
        private int _pos;

        public Tokenizer(ReadOnlySpan<char> text)
        {
            _text = text;
            _pos = 0;
        }

        public bool AtEnd
        {
            get
            {
                SkipWhitespaceAndComments();
                return _pos >= _text.Length;
            }
        }

        /// <summary>Read a field name (identifier, possibly dot-separated).</summary>
        public string ReadFieldName()
        {
            SkipWhitespaceAndComments();
            var start = _pos;
            while (_pos < _text.Length && (char.IsLetterOrDigit(_text[_pos]) || _text[_pos] == '_' || _text[_pos] == '.'))
                _pos++;
            if (_pos == start)
                throw new FormatException($"Expected field name at position {_pos}, got '{(_pos < _text.Length ? _text[_pos] : '?')}'");
            return _text[start.._pos].ToString();
        }

        /// <summary>Read an identifier (e.g., NULL_VALUE, true, false, INT64, inf, etc.).</summary>
        public string ReadIdentifier()
        {
            SkipWhitespaceAndComments();
            var start = _pos;
            // Handle numeric values too (like "0" for null_value)
            if (_pos < _text.Length && (char.IsDigit(_text[_pos]) || _text[_pos] == '-'))
            {
                if (_text[_pos] == '-') _pos++;
                while (_pos < _text.Length && (char.IsDigit(_text[_pos]) || _text[_pos] == '.' || _text[_pos] == 'e' || _text[_pos] == 'E' || _text[_pos] == '+' || _text[_pos] == '-'))
                    _pos++;
                if (_pos < _text.Length && (_text[_pos] == 'u' || _text[_pos] == 'U'))
                    _pos++;
            }
            else
            {
                while (_pos < _text.Length && (char.IsLetterOrDigit(_text[_pos]) || _text[_pos] == '_'))
                    _pos++;
            }
            if (_pos == start)
                throw new FormatException($"Expected identifier at position {_pos}");
            return _text[start.._pos].ToString();
        }

        public void ExpectColon()
        {
            SkipWhitespaceAndComments();
            if (_pos >= _text.Length || _text[_pos] != ':')
                throw new FormatException($"Expected ':' at position {_pos}");
            _pos++;
        }

        public bool TryConsumeColon()
        {
            SkipWhitespaceAndComments();
            if (_pos < _text.Length && _text[_pos] == ':')
            {
                _pos++;
                return true;
            }
            return false;
        }

        public void Expect(char c)
        {
            SkipWhitespaceAndComments();
            if (_pos >= _text.Length || _text[_pos] != c)
                throw new FormatException($"Expected '{c}' at position {_pos}, got '{(_pos < _text.Length ? _text[_pos] : '?')}'");
            _pos++;
        }

        public bool TryConsume(char c)
        {
            SkipWhitespaceAndComments();
            if (_pos < _text.Length && _text[_pos] == c)
            {
                _pos++;
                return true;
            }
            return false;
        }

        public string ReadString()
        {
            SkipWhitespaceAndComments();
            if (_pos >= _text.Length)
                throw new FormatException("Expected string, got end of input");

            char quote = _text[_pos];
            if (quote != '"' && quote != '\'')
                throw new FormatException($"Expected string at position {_pos}, got '{quote}'");

            _pos++;
            var sb = new StringBuilder();

            while (_pos < _text.Length)
            {
                if (_text[_pos] == '\\')
                {
                    _pos++;
                    if (_pos >= _text.Length)
                        throw new FormatException("Unexpected end of string escape");
                    sb.Append(ReadEscapeSequence());
                }
                else if (_text[_pos] == quote)
                {
                    _pos++;
                    return sb.ToString();
                }
                else
                {
                    sb.Append(_text[_pos++]);
                }
            }

            throw new FormatException("Unterminated string");
        }

        /// <summary>
        /// Read concatenated strings: "part1" "part2" → "part1part2"
        /// This is used for multi-line expr fields in textproto.
        /// </summary>
        public string ReadConcatenatedStrings()
        {
            var result = ReadString();
            while (true)
            {
                SkipWhitespaceAndComments();
                if (_pos < _text.Length && (_text[_pos] == '"' || _text[_pos] == '\''))
                    result += ReadString();
                else
                    break;
            }
            return result;
        }

        /// <summary>Read a protobuf string value (with \xNN and \NNN octal escapes).</summary>
        public string ReadProtoString()
        {
            SkipWhitespaceAndComments();
            if (_pos >= _text.Length)
                throw new FormatException("Expected proto string");

            char quote = _text[_pos];
            if (quote != '"' && quote != '\'')
                throw new FormatException($"Expected string at position {_pos}");

            _pos++;
            var bytes = new List<byte>();

            while (_pos < _text.Length)
            {
                if (_text[_pos] == '\\')
                {
                    _pos++;
                    if (_pos >= _text.Length)
                        throw new FormatException("Unexpected end of string escape");
                    ReadProtoEscapeBytes(bytes);
                }
                else if (_text[_pos] == quote)
                {
                    _pos++;
                    return Encoding.UTF8.GetString(bytes.ToArray());
                }
                else
                {
                    // Regular UTF-8 char
                    var c = _text[_pos++];
                    foreach (var b in Encoding.UTF8.GetBytes(new[] { c }))
                        bytes.Add(b);
                }
            }

            throw new FormatException("Unterminated string");
        }

        /// <summary>Read a protobuf bytes value.</summary>
        public byte[] ReadProtoBytes()
        {
            SkipWhitespaceAndComments();
            if (_pos >= _text.Length)
                throw new FormatException("Expected proto bytes");

            char quote = _text[_pos];
            if (quote != '"' && quote != '\'')
                throw new FormatException($"Expected string at position {_pos}");

            _pos++;
            var bytes = new List<byte>();

            while (_pos < _text.Length)
            {
                if (_text[_pos] == '\\')
                {
                    _pos++;
                    if (_pos >= _text.Length)
                        throw new FormatException("Unexpected end of bytes escape");
                    ReadProtoEscapeBytes(bytes);
                }
                else if (_text[_pos] == quote)
                {
                    _pos++;
                    return bytes.ToArray();
                }
                else
                {
                    // Raw byte from character
                    var c = _text[_pos++];
                    foreach (var b in Encoding.UTF8.GetBytes(new[] { c }))
                        bytes.Add(b);
                }
            }

            throw new FormatException("Unterminated bytes");
        }

        public bool ReadBool()
        {
            var id = ReadIdentifier();
            return id switch
            {
                "true" => true,
                "false" => false,
                _ => throw new FormatException($"Expected bool, got '{id}'"),
            };
        }

        public long ReadInt64()
        {
            SkipWhitespaceAndComments();
            var start = _pos;
            if (_pos < _text.Length && _text[_pos] == '-')
                _pos++;
            while (_pos < _text.Length && char.IsDigit(_text[_pos]))
                _pos++;
            return long.Parse(_text[start.._pos], CultureInfo.InvariantCulture);
        }

        public ulong ReadUInt64()
        {
            SkipWhitespaceAndComments();
            var start = _pos;
            while (_pos < _text.Length && char.IsDigit(_text[_pos]))
                _pos++;
            return ulong.Parse(_text[start.._pos], CultureInfo.InvariantCulture);
        }

        public double ReadDouble()
        {
            SkipWhitespaceAndComments();

            // Handle special identifiers: inf, -inf, Infinity, -Infinity, nan, NaN
            if (_pos < _text.Length)
            {
                // Check for negative special values
                if (_text[_pos] == '-')
                {
                    var ahead = _pos + 1;
                    if (ahead < _text.Length && char.IsLetter(_text[ahead]))
                    {
                        _pos++; // consume -
                        var id = ReadIdentifier();
                        return id.ToLowerInvariant() switch
                        {
                            "inf" or "infinity" => double.NegativeInfinity,
                            _ => throw new FormatException($"Unknown double literal: -{id}"),
                        };
                    }
                }

                if (char.IsLetter(_text[_pos]))
                {
                    var id = ReadIdentifier();
                    return id.ToLowerInvariant() switch
                    {
                        "inf" or "infinity" => double.PositiveInfinity,
                        "nan" => double.NaN,
                        _ => throw new FormatException($"Unknown double literal: {id}"),
                    };
                }
            }

            var start = _pos;
            if (_pos < _text.Length && _text[_pos] == '-')
                _pos++;
            while (_pos < _text.Length && (char.IsDigit(_text[_pos]) || _text[_pos] == '.' || _text[_pos] == 'e' || _text[_pos] == 'E' || _text[_pos] == '+' || _text[_pos] == '-'))
            {
                // Skip 'e' followed by sign only once
                if (_text[_pos] == 'e' || _text[_pos] == 'E')
                {
                    _pos++;
                    if (_pos < _text.Length && (_text[_pos] == '+' || _text[_pos] == '-'))
                        _pos++;
                }
                else
                {
                    _pos++;
                }
            }
            return double.Parse(_text[start.._pos], CultureInfo.InvariantCulture);
        }

        /// <summary>Skip a field value (string, message, or scalar).</summary>
        public void SkipField()
        {
            SkipWhitespaceAndComments();
            if (_pos >= _text.Length)
                return;

            if (_text[_pos] == '{')
            {
                SkipMessage();
            }
            else if (_text[_pos] == '"' || _text[_pos] == '\'')
            {
                ReadString();
            }
            else
            {
                // Skip scalar value (identifier, number, etc.)
                ReadIdentifier();
            }
        }

        /// <summary>Skip a nested message { ... } including nested messages.</summary>
        public void SkipMessage()
        {
            Expect('{');
            int depth = 1;
            while (_pos < _text.Length && depth > 0)
            {
                SkipWhitespaceAndComments();
                if (_pos >= _text.Length) break;

                if (_text[_pos] == '{')
                {
                    depth++;
                    _pos++;
                }
                else if (_text[_pos] == '}')
                {
                    depth--;
                    _pos++;
                }
                else if (_text[_pos] == '"' || _text[_pos] == '\'')
                {
                    SkipStringLiteral();
                }
                else
                {
                    _pos++;
                }
            }
        }

        private void SkipStringLiteral()
        {
            var quote = _text[_pos++];
            while (_pos < _text.Length)
            {
                if (_text[_pos] == '\\')
                {
                    _pos += 2; // skip escape
                }
                else if (_text[_pos] == quote)
                {
                    _pos++;
                    return;
                }
                else
                {
                    _pos++;
                }
            }
        }

        /// <summary>Expose whitespace skipping for external callers.</summary>
        public void SkipWhitespace() => SkipWhitespaceAndComments();

        /// <summary>Peek at the current character without advancing.</summary>
        public char PeekChar()
        {
            SkipWhitespaceAndComments();
            return _text[_pos];
        }

        /// <summary>Read and advance one character.</summary>
        public char ReadChar() => _text[_pos++];

        /// <summary>Expect and consume a specific character.</summary>
        public void Consume(char c)
        {
            SkipWhitespaceAndComments();
            if (_pos >= _text.Length || _text[_pos] != c)
                throw new FormatException($"Expected '{c}' at position {_pos}");
            _pos++;
        }

        private void SkipWhitespaceAndComments()
        {
            while (_pos < _text.Length)
            {
                if (char.IsWhiteSpace(_text[_pos]) || _text[_pos] == ',')
                {
                    _pos++;
                }
                else if (_text[_pos] == '#')
                {
                    // Skip to end of line
                    while (_pos < _text.Length && _text[_pos] != '\n')
                        _pos++;
                }
                else if (_pos + 1 < _text.Length && _text[_pos] == '/' && _text[_pos + 1] == '/')
                {
                    // C++ style comment
                    while (_pos < _text.Length && _text[_pos] != '\n')
                        _pos++;
                }
                else if (_pos + 1 < _text.Length && _text[_pos] == '/' && _text[_pos + 1] == '*')
                {
                    _pos += 2;
                    while (_pos + 1 < _text.Length && !(_text[_pos] == '*' && _text[_pos + 1] == '/'))
                        _pos++;
                    if (_pos + 1 < _text.Length)
                        _pos += 2;
                }
                else
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Read a CEL expression escape sequence from a textproto string.
        /// For unrecognised escapes (like \U for 8-digit Unicode), we preserve
        /// the backslash so the CEL lexer can handle it.
        /// </summary>
        private string ReadEscapeSequence()
        {
            var c = _text[_pos++];
            return c switch
            {
                'n' => "\n",
                'r' => "\r",
                't' => "\t",
                '\\' => "\\",
                '\'' => "'",
                '"' => "\"",
                'a' => "\a",
                'b' => "\b",
                'f' => "\f",
                'v' => "\v",
                '0' => "\0",
                'x' => ReadHexEscape(2).ToString(),
                'X' => ReadHexEscape(2).ToString(),
                _ when c >= '0' && c <= '7' => ReadOctalEscape(c).ToString(),
                // Unrecognised escape — preserve backslash for CEL lexer (e.g. \U, \u)
                _ => $"\\{c}",
            };
        }

        private void ReadProtoEscapeBytes(List<byte> bytes)
        {
            var c = _text[_pos++];
            switch (c)
            {
                case 'n': bytes.Add((byte)'\n'); break;
                case 'r': bytes.Add((byte)'\r'); break;
                case 't': bytes.Add((byte)'\t'); break;
                case '\\': bytes.Add((byte)'\\'); break;
                case '\'': bytes.Add((byte)'\''); break;
                case '"': bytes.Add((byte)'"'); break;
                case 'a': bytes.Add((byte)'\a'); break;
                case 'b': bytes.Add((byte)'\b'); break;
                case 'f': bytes.Add((byte)'\f'); break;
                case 'v': bytes.Add((byte)'\v'); break;
                case '0' or '1' or '2' or '3' or '4' or '5' or '6' or '7':
                {
                    // Octal escape: \NNN
                    int val = c - '0';
                    for (int i = 0; i < 2 && _pos < _text.Length && _text[_pos] >= '0' && _text[_pos] <= '7'; i++)
                        val = val * 8 + (_text[_pos++] - '0');
                    bytes.Add((byte)val);
                    break;
                }
                case 'x' or 'X':
                {
                    // Hex escape: \xNN
                    int val = 0;
                    for (int i = 0; i < 2 && _pos < _text.Length && IsHexDigit(_text[_pos]); i++)
                        val = val * 16 + HexVal(_text[_pos++]);
                    bytes.Add((byte)val);
                    break;
                }
                default:
                    bytes.Add((byte)c);
                    break;
            }
        }

        private char ReadHexEscape(int digits)
        {
            int val = 0;
            for (int i = 0; i < digits && _pos < _text.Length && IsHexDigit(_text[_pos]); i++)
                val = val * 16 + HexVal(_text[_pos++]);
            return (char)val;
        }

        private char ReadOctalEscape(char first)
        {
            int val = first - '0';
            for (int i = 0; i < 2 && _pos < _text.Length && _text[_pos] >= '0' && _text[_pos] <= '7'; i++)
                val = val * 8 + (_text[_pos++] - '0');
            return (char)val;
        }

        private static bool IsHexDigit(char c) =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

        private static int HexVal(char c) => c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            >= 'A' and <= 'F' => c - 'A' + 10,
            _ => throw new FormatException($"Invalid hex digit: {c}"),
        };
    }

    #endregion
}
