using CelDotNet.Ast;
using CelDotNet.Lexer;

namespace CelDotNet.Parser;

/// <summary>
/// Recursive descent parser for the CEL grammar.
///
/// Grammar (simplified, see cel-spec for full grammar):
///   Expr           = ConditionalOr ["?" ConditionalOr ":" Expr]
///   ConditionalOr  = [ConditionalOr "||"] ConditionalAnd
///   ConditionalAnd = [ConditionalAnd "&&"] Relation
///   Relation       = [Relation Relop] Addition
///   Addition       = [Addition ("+" | "-")] Multiplication
///   Multiplication = [Multiplication ("*" | "/" | "%")] Unary
///   Unary          = Member | "!" {"!"} Member | "-" {"-"} Member
///   Member         = Primary { "." IDENT ["(" [ExprList] ")"] | "[" Expr "]" | "{" FieldInits "}" }
///   Primary        = IDENT ["(" [ExprList] ")"]
///                  | "(" Expr ")"
///                  | "[" [ExprList] [","] "]"
///                  | "{" [MapInits] [","] "}"
///                  | LITERAL
/// </summary>
public sealed class CelParser
{
    private readonly IReadOnlyList<Token> _tokens;
    private int _pos;

    // Well-known macro names that get special handling
    private static readonly HashSet<string> MacroNames = ["has", "all", "exists", "exists_one", "filter", "map"];

    private CelParser(IReadOnlyList<Token> tokens)
    {
        _tokens = tokens;
        _pos = 0;
    }

    /// <summary>
    /// Parses a CEL expression string into an AST.
    /// </summary>
    public static CelExpr Parse(string source)
    {
        var lexer = new CelLexer(source);
        var tokens = lexer.Tokenise();

        // Check for lexer errors
        var errors = tokens.Where(t => t.Kind == TokenKind.Error).ToList();
        if (errors.Count > 0)
        {
            var first = errors[0];
            throw new CelParseException(first.Lexeme, first.Span.Start);
        }

        var parser = new CelParser(tokens);
        var expr = parser.ParseExpr();

        if (!parser.IsAtEnd)
        {
            var unexpected = parser.Current;
            throw new CelParseException(
                $"unexpected token '{unexpected.Lexeme}'",
                unexpected.Span.Start);
        }

        return expr;
    }

    #region Token Navigation

    private Token Current => _tokens[_pos];
    private bool IsAtEnd => Current.Kind == TokenKind.Eof;

    private Token Advance()
    {
        var token = _tokens[_pos];
        if (!IsAtEnd) _pos++;
        return token;
    }

    private Token Expect(TokenKind kind, string? context = null)
    {
        if (Current.Kind != kind)
        {
            string msg = context is not null
                ? $"expected {kind} ({context}), got '{Current.Lexeme}'"
                : $"expected {kind}, got '{Current.Lexeme}'";
            throw new CelParseException(msg, Current.Span.Start);
        }
        return Advance();
    }

    private bool Check(TokenKind kind) => Current.Kind == kind;

    private bool Match(TokenKind kind)
    {
        if (Current.Kind != kind) return false;
        Advance();
        return true;
    }

    private SourceSpan SpanFrom(SourcePosition start) =>
        new(start, Current.Span.Start);

    #endregion

    #region Expression Parsing

    /// <summary>
    /// Expr = ConditionalOr ["?" ConditionalOr ":" Expr]
    /// </summary>
    private CelExpr ParseExpr()
    {
        var expr = ParseConditionalOr();

        if (Match(TokenKind.Question))
        {
            var trueExpr = ParseConditionalOr();
            Expect(TokenKind.Colon, "ternary expression");
            var falseExpr = ParseExpr();
            return new CelExpr.Conditional(SpanFrom(expr.Span.Start), expr, trueExpr, falseExpr);
        }

        return expr;
    }

    /// <summary>
    /// ConditionalOr = ConditionalAnd {"||" ConditionalAnd}
    /// </summary>
    private CelExpr ParseConditionalOr()
    {
        var left = ParseConditionalAnd();

        while (Match(TokenKind.PipePipe))
        {
            var right = ParseConditionalAnd();
            left = new CelExpr.Binary(SpanFrom(left.Span.Start), BinaryOp.Or, left, right);
        }

        return left;
    }

    /// <summary>
    /// ConditionalAnd = Relation {"&&" Relation}
    /// </summary>
    private CelExpr ParseConditionalAnd()
    {
        var left = ParseRelation();

        while (Match(TokenKind.AmpersandAmpersand))
        {
            var right = ParseRelation();
            left = new CelExpr.Binary(SpanFrom(left.Span.Start), BinaryOp.And, left, right);
        }

        return left;
    }

    /// <summary>
    /// Relation = Addition {Relop Addition}
    /// Relop = "==" | "!=" | "&lt;" | "&lt;=" | ">" | ">=" | "in"
    /// </summary>
    private CelExpr ParseRelation()
    {
        var left = ParseAddition();

        while (true)
        {
            BinaryOp? op = Current.Kind switch
            {
                TokenKind.EqualEqual => BinaryOp.Equal,
                TokenKind.BangEqual => BinaryOp.NotEqual,
                TokenKind.LessThan => BinaryOp.LessThan,
                TokenKind.LessThanEqual => BinaryOp.LessThanOrEqual,
                TokenKind.GreaterThan => BinaryOp.GreaterThan,
                TokenKind.GreaterThanEqual => BinaryOp.GreaterThanOrEqual,
                TokenKind.In => BinaryOp.In,
                _ => null,
            };

            if (op is null) break;
            Advance();
            var right = ParseAddition();
            left = new CelExpr.Binary(SpanFrom(left.Span.Start), op.Value, left, right);
        }

        return left;
    }

    /// <summary>
    /// Addition = Multiplication {("+" | "-") Multiplication}
    /// </summary>
    private CelExpr ParseAddition()
    {
        var left = ParseMultiplication();

        while (true)
        {
            BinaryOp? op = Current.Kind switch
            {
                TokenKind.Plus => BinaryOp.Add,
                TokenKind.Minus => BinaryOp.Subtract,
                _ => null,
            };

            if (op is null) break;
            Advance();
            var right = ParseMultiplication();
            left = new CelExpr.Binary(SpanFrom(left.Span.Start), op.Value, left, right);
        }

        return left;
    }

    /// <summary>
    /// Multiplication = Unary {("*" | "/" | "%") Unary}
    /// </summary>
    private CelExpr ParseMultiplication()
    {
        var left = ParseUnary();

        while (true)
        {
            BinaryOp? op = Current.Kind switch
            {
                TokenKind.Star => BinaryOp.Multiply,
                TokenKind.Slash => BinaryOp.Divide,
                TokenKind.Percent => BinaryOp.Modulo,
                _ => null,
            };

            if (op is null) break;
            Advance();
            var right = ParseUnary();
            left = new CelExpr.Binary(SpanFrom(left.Span.Start), op.Value, left, right);
        }

        return left;
    }

    /// <summary>
    /// Unary = Member | "!" {"!"} Member | "-" {"-"} Member
    /// </summary>
    private CelExpr ParseUnary()
    {
        if (Check(TokenKind.Bang))
        {
            var start = Current.Span.Start;
            Advance();
            var operand = ParseUnary();
            return new CelExpr.Unary(SpanFrom(start), UnaryOp.Not, operand);
        }

        if (Check(TokenKind.Minus))
        {
            var start = Current.Span.Start;
            Advance();
            var operand = ParseUnary();
            return new CelExpr.Unary(SpanFrom(start), UnaryOp.Negate, operand);
        }

        return ParseMember();
    }

    /// <summary>
    /// Member = Primary { "." IDENT ["(" [ExprList] ")"] | "[" Expr "]" | "{" FieldInits "}" }
    /// </summary>
    private CelExpr ParseMember()
    {
        var expr = ParsePrimary();

        while (true)
        {
            if (Match(TokenKind.Dot))
            {
                var field = Expect(TokenKind.Identifier, "field access");

                // Check for method call: expr.method(args)
                if (Check(TokenKind.LeftParen))
                {
                    // Check if this is a macro call
                    if (MacroNames.Contains(field.Lexeme))
                    {
                        expr = ParseMacroCall(expr, field);
                    }
                    else
                    {
                        var args = ParseCallArgs();
                        expr = new CelExpr.Call(
                            SpanFrom(expr.Span.Start),
                            expr,
                            field.Lexeme,
                            args);
                    }
                }
                else
                {
                    expr = new CelExpr.Select(SpanFrom(expr.Span.Start), expr, field.Lexeme);
                }
            }
            else if (Match(TokenKind.LeftBracket))
            {
                var index = ParseExpr();
                Expect(TokenKind.RightBracket, "index access");
                expr = new CelExpr.Index(SpanFrom(expr.Span.Start), expr, index);
            }
            else if (Check(TokenKind.LeftBrace) && expr is CelExpr.Ident or CelExpr.Select)
            {
                // Struct creation: TypeName { field: value, ... }
                expr = ParseStructCreation(expr);
            }
            else
            {
                break;
            }
        }

        return expr;
    }

    /// <summary>
    /// Primary = IDENT ["(" [ExprList] ")"]
    ///         | "(" Expr ")"
    ///         | "[" [ExprList] [","] "]"
    ///         | "{" [MapInits] [","] "}"
    ///         | LITERAL
    /// </summary>
    private CelExpr ParsePrimary()
    {
        var token = Current;

        switch (token.Kind)
        {
            case TokenKind.Identifier:
                Advance();
                if (Check(TokenKind.LeftParen))
                {
                    // Global function call or macro
                    if (token.Lexeme == "has")
                    {
                        return ParseHasMacro(token);
                    }
                    var args = ParseCallArgs();
                    return new CelExpr.Call(SpanFrom(token.Span.Start), null, token.Lexeme, args);
                }
                return new CelExpr.Ident(token.Span, token.Lexeme);

            case TokenKind.IntLiteral:
                Advance();
                return new CelExpr.Literal(token.Span, token.Value, CelTypeKind.Int);

            case TokenKind.UintLiteral:
                Advance();
                return new CelExpr.Literal(token.Span, token.Value, CelTypeKind.Uint);

            case TokenKind.DoubleLiteral:
                Advance();
                return new CelExpr.Literal(token.Span, token.Value, CelTypeKind.Double);

            case TokenKind.StringLiteral:
                Advance();
                return new CelExpr.Literal(token.Span, token.Value, CelTypeKind.String);

            case TokenKind.BytesLiteral:
                Advance();
                return new CelExpr.Literal(token.Span, token.Value, CelTypeKind.Bytes);

            case TokenKind.BoolLiteral:
                Advance();
                return new CelExpr.Literal(token.Span, token.Value, CelTypeKind.Bool);

            case TokenKind.NullLiteral:
                Advance();
                return new CelExpr.Literal(token.Span, null, CelTypeKind.Null);

            case TokenKind.LeftParen:
                Advance();
                var grouped = ParseExpr();
                Expect(TokenKind.RightParen, "grouped expression");
                return grouped;

            case TokenKind.LeftBracket:
                return ParseListCreation();

            case TokenKind.LeftBrace:
                return ParseMapCreation();

            // Handle leading dot for qualified identifiers: .pkg.Type
            case TokenKind.Dot:
                return ParseQualifiedIdent();

            default:
                throw new CelParseException(
                    $"unexpected token '{token.Lexeme}'",
                    token.Span.Start);
        }
    }

    #endregion

    #region Complex Expression Parsing

    private List<CelExpr> ParseCallArgs()
    {
        Expect(TokenKind.LeftParen, "function call");
        var args = new List<CelExpr>();
        if (!Check(TokenKind.RightParen))
        {
            args.Add(ParseExpr());
            while (Match(TokenKind.Comma))
            {
                if (Check(TokenKind.RightParen)) break; // trailing comma
                args.Add(ParseExpr());
            }
        }
        Expect(TokenKind.RightParen, "function call");
        return args;
    }

    private CelExpr ParseListCreation()
    {
        var start = Expect(TokenKind.LeftBracket, "list literal").Span.Start;
        var elements = new List<CelExpr>();

        if (!Check(TokenKind.RightBracket))
        {
            elements.Add(ParseExpr());
            while (Match(TokenKind.Comma))
            {
                if (Check(TokenKind.RightBracket)) break; // trailing comma
                elements.Add(ParseExpr());
            }
        }

        Expect(TokenKind.RightBracket, "list literal");
        return new CelExpr.CreateList(SpanFrom(start), elements);
    }

    private CelExpr ParseMapCreation()
    {
        var start = Expect(TokenKind.LeftBrace, "map literal").Span.Start;
        var entries = new List<MapEntry>();

        if (!Check(TokenKind.RightBrace))
        {
            var key = ParseExpr();
            Expect(TokenKind.Colon, "map entry");
            var value = ParseExpr();
            entries.Add(new MapEntry(key, value));

            while (Match(TokenKind.Comma))
            {
                if (Check(TokenKind.RightBrace)) break; // trailing comma
                key = ParseExpr();
                Expect(TokenKind.Colon, "map entry");
                value = ParseExpr();
                entries.Add(new MapEntry(key, value));
            }
        }

        Expect(TokenKind.RightBrace, "map literal");
        return new CelExpr.CreateMap(SpanFrom(start), entries);
    }

    private CelExpr ParseStructCreation(CelExpr typeExpr)
    {
        string typeName = typeExpr switch
        {
            CelExpr.Ident ident => ident.Name,
            CelExpr.Select select => FlattenSelectToName(select),
            _ => throw new CelParseException("invalid type name in struct creation", typeExpr.Span.Start),
        };

        Expect(TokenKind.LeftBrace, "struct creation");
        var fields = new List<FieldInit>();

        if (!Check(TokenKind.RightBrace))
        {
            var fieldName = Expect(TokenKind.Identifier, "struct field name");
            Expect(TokenKind.Colon, "struct field");
            var value = ParseExpr();
            fields.Add(new FieldInit(fieldName.Lexeme, value));

            while (Match(TokenKind.Comma))
            {
                if (Check(TokenKind.RightBrace)) break;
                fieldName = Expect(TokenKind.Identifier, "struct field name");
                Expect(TokenKind.Colon, "struct field");
                value = ParseExpr();
                fields.Add(new FieldInit(fieldName.Lexeme, value));
            }
        }

        Expect(TokenKind.RightBrace, "struct creation");
        return new CelExpr.CreateStruct(SpanFrom(typeExpr.Span.Start), typeName, fields);
    }

    private CelExpr ParseQualifiedIdent()
    {
        var start = Expect(TokenKind.Dot, "qualified identifier").Span.Start;
        var name = Expect(TokenKind.Identifier, "qualified identifier");
        CelExpr expr = new CelExpr.Ident(SpanFrom(start), name.Lexeme);

        while (Match(TokenKind.Dot))
        {
            var next = Expect(TokenKind.Identifier, "qualified identifier");
            expr = new CelExpr.Select(SpanFrom(start), expr, next.Lexeme);
        }

        return expr;
    }

    private static string FlattenSelectToName(CelExpr.Select select)
    {
        var parts = new List<string> { select.Field };
        var current = select.Operand;
        while (current is CelExpr.Select inner)
        {
            parts.Add(inner.Field);
            current = inner.Operand;
        }
        if (current is CelExpr.Ident ident)
        {
            parts.Add(ident.Name);
        }
        parts.Reverse();
        return string.Join(".", parts);
    }

    #endregion

    #region Macro Parsing

    /// <summary>
    /// Parses has(expr.field) macro.
    /// Rewritten to a Call node with function "has" and the select expression as the argument.
    /// </summary>
    private CelExpr ParseHasMacro(Token hasToken)
    {
        Expect(TokenKind.LeftParen, "has() macro");
        var arg = ParseExpr();
        Expect(TokenKind.RightParen, "has() macro");

        if (arg is not CelExpr.Select)
        {
            throw new CelParseException(
                "has() argument must be a field selection (e.g., has(x.field))",
                hasToken.Span.Start);
        }

        return new CelExpr.Call(SpanFrom(hasToken.Span.Start), null, "has", [arg]);
    }

    /// <summary>
    /// Parses receiver-style macro calls: expr.all(x, pred), expr.exists(x, pred), etc.
    /// These are expanded into Comprehension AST nodes.
    /// </summary>
    private CelExpr ParseMacroCall(CelExpr receiver, Token macroName)
    {
        Expect(TokenKind.LeftParen, $"{macroName.Lexeme}() macro");

        return macroName.Lexeme switch
        {
            "all" => ParseQuantifierMacro(receiver, macroName, QuantifierKind.All),
            "exists" => ParseQuantifierMacro(receiver, macroName, QuantifierKind.Exists),
            "exists_one" => ParseQuantifierMacro(receiver, macroName, QuantifierKind.ExistsOne),
            "filter" => ParseFilterMacro(receiver, macroName),
            "map" => ParseMapMacro(receiver, macroName),
            _ => throw new CelParseException($"unknown macro '{macroName.Lexeme}'", macroName.Span.Start),
        };
    }

    private enum QuantifierKind { All, Exists, ExistsOne }

    private CelExpr ParseQuantifierMacro(CelExpr receiver, Token macroName, QuantifierKind kind)
    {
        var iterVar = Expect(TokenKind.Identifier, $"{macroName.Lexeme}() iterator variable");
        Expect(TokenKind.Comma, $"{macroName.Lexeme}() macro");
        var predicate = ParseExpr();
        Expect(TokenKind.RightParen, $"{macroName.Lexeme}() macro");

        var span = SpanFrom(receiver.Span.Start);
        string accuVar = "__result__";

        return kind switch
        {
            // all(x, pred): acc = true, loop: acc && pred, result: acc
            QuantifierKind.All => new CelExpr.Comprehension(
                span,
                iterVar.Lexeme,
                receiver,
                accuVar,
                AccuInit: new CelExpr.Literal(span, true, CelTypeKind.Bool),
                LoopCondition: new CelExpr.Ident(span, accuVar),
                LoopStep: new CelExpr.Binary(span, BinaryOp.And,
                    new CelExpr.Ident(span, accuVar), predicate),
                Result: new CelExpr.Ident(span, accuVar)),

            // exists(x, pred): acc = false, loop: !acc, step: acc || pred, result: acc
            QuantifierKind.Exists => new CelExpr.Comprehension(
                span,
                iterVar.Lexeme,
                receiver,
                accuVar,
                AccuInit: new CelExpr.Literal(span, false, CelTypeKind.Bool),
                LoopCondition: new CelExpr.Unary(span, UnaryOp.Not,
                    new CelExpr.Ident(span, accuVar)),
                LoopStep: new CelExpr.Binary(span, BinaryOp.Or,
                    new CelExpr.Ident(span, accuVar), predicate),
                Result: new CelExpr.Ident(span, accuVar)),

            // exists_one(x, pred): acc = 0, loop: true, step: pred ? acc + 1 : acc, result: acc == 1
            QuantifierKind.ExistsOne => new CelExpr.Comprehension(
                span,
                iterVar.Lexeme,
                receiver,
                accuVar,
                AccuInit: new CelExpr.Literal(span, 0L, CelTypeKind.Int),
                LoopCondition: new CelExpr.Literal(span, true, CelTypeKind.Bool),
                LoopStep: new CelExpr.Conditional(span,
                    predicate,
                    new CelExpr.Binary(span, BinaryOp.Add,
                        new CelExpr.Ident(span, accuVar),
                        new CelExpr.Literal(span, 1L, CelTypeKind.Int)),
                    new CelExpr.Ident(span, accuVar)),
                Result: new CelExpr.Binary(span, BinaryOp.Equal,
                    new CelExpr.Ident(span, accuVar),
                    new CelExpr.Literal(span, 1L, CelTypeKind.Int))),

            _ => throw new CelParseException($"unknown quantifier '{kind}'", macroName.Span.Start),
        };
    }

    private CelExpr ParseFilterMacro(CelExpr receiver, Token macroName)
    {
        var iterVar = Expect(TokenKind.Identifier, "filter() iterator variable");
        Expect(TokenKind.Comma, "filter() macro");
        var predicate = ParseExpr();
        Expect(TokenKind.RightParen, "filter() macro");

        var span = SpanFrom(receiver.Span.Start);
        string accuVar = "__result__";

        // filter(x, pred): acc = [], loop: true, step: pred ? acc + [x] : acc, result: acc
        return new CelExpr.Comprehension(
            span,
            iterVar.Lexeme,
            receiver,
            accuVar,
            AccuInit: new CelExpr.CreateList(span, []),
            LoopCondition: new CelExpr.Literal(span, true, CelTypeKind.Bool),
            LoopStep: new CelExpr.Conditional(span,
                predicate,
                new CelExpr.Binary(span, BinaryOp.Add,
                    new CelExpr.Ident(span, accuVar),
                    new CelExpr.CreateList(span, [new CelExpr.Ident(span, iterVar.Lexeme)])),
                new CelExpr.Ident(span, accuVar)),
            Result: new CelExpr.Ident(span, accuVar));
    }

    private CelExpr ParseMapMacro(CelExpr receiver, Token macroName)
    {
        var iterVar = Expect(TokenKind.Identifier, "map() iterator variable");
        Expect(TokenKind.Comma, "map() macro");

        var firstExpr = ParseExpr();

        // map() has two forms:
        // 1. e.map(x, transform)
        // 2. e.map(x, filter, transform)
        CelExpr? filterExpr = null;
        CelExpr transformExpr;

        if (Match(TokenKind.Comma))
        {
            // Three-arg form: filter, then transform
            filterExpr = firstExpr;
            transformExpr = ParseExpr();
        }
        else
        {
            transformExpr = firstExpr;
        }

        Expect(TokenKind.RightParen, "map() macro");

        var span = SpanFrom(receiver.Span.Start);
        string accuVar = "__result__";

        CelExpr loopStep;
        if (filterExpr is not null)
        {
            // With filter: step = filter ? acc + [transform] : acc
            loopStep = new CelExpr.Conditional(span,
                filterExpr,
                new CelExpr.Binary(span, BinaryOp.Add,
                    new CelExpr.Ident(span, accuVar),
                    new CelExpr.CreateList(span, [transformExpr])),
                new CelExpr.Ident(span, accuVar));
        }
        else
        {
            // Without filter: step = acc + [transform]
            loopStep = new CelExpr.Binary(span, BinaryOp.Add,
                new CelExpr.Ident(span, accuVar),
                new CelExpr.CreateList(span, [transformExpr]));
        }

        return new CelExpr.Comprehension(
            span,
            iterVar.Lexeme,
            receiver,
            accuVar,
            AccuInit: new CelExpr.CreateList(span, []),
            LoopCondition: new CelExpr.Literal(span, true, CelTypeKind.Bool),
            LoopStep: loopStep,
            Result: new CelExpr.Ident(span, accuVar));
    }

    #endregion
}
