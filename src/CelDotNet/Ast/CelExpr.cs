using CelDotNet.Lexer;

namespace CelDotNet.Ast;

/// <summary>
/// Base class for all CEL AST expression nodes.
/// Uses sealed derived records for exhaustive pattern matching.
/// </summary>
public abstract record CelExpr(SourceSpan Span)
{
    /// <summary>A literal value (int, uint, double, string, bytes, bool, null).</summary>
    public sealed record Literal(SourceSpan Span, object? Value, CelTypeKind TypeKind) : CelExpr(Span);

    /// <summary>An identifier reference (variable name).</summary>
    public sealed record Ident(SourceSpan Span, string Name) : CelExpr(Span);

    /// <summary>Field selection: expr.field</summary>
    public sealed record Select(SourceSpan Span, CelExpr Operand, string Field) : CelExpr(Span);

    /// <summary>Function call: name(args) or receiver.name(args)</summary>
    public sealed record Call(SourceSpan Span, CelExpr? Target, string Function, IReadOnlyList<CelExpr> Args) : CelExpr(Span);

    /// <summary>Index access: expr[index]</summary>
    public sealed record Index(SourceSpan Span, CelExpr Operand, CelExpr Key) : CelExpr(Span);

    /// <summary>Unary operation: -expr or !expr</summary>
    public sealed record Unary(SourceSpan Span, UnaryOp Op, CelExpr Operand) : CelExpr(Span);

    /// <summary>Binary operation: expr op expr</summary>
    public sealed record Binary(SourceSpan Span, BinaryOp Op, CelExpr Left, CelExpr Right) : CelExpr(Span);

    /// <summary>Ternary conditional: condition ? trueExpr : falseExpr</summary>
    public sealed record Conditional(SourceSpan Span, CelExpr Condition, CelExpr TrueExpr, CelExpr FalseExpr) : CelExpr(Span);

    /// <summary>List creation: [expr, expr, ...]</summary>
    public sealed record CreateList(SourceSpan Span, IReadOnlyList<CelExpr> Elements) : CelExpr(Span);

    /// <summary>Map creation: {key: value, ...}</summary>
    public sealed record CreateMap(SourceSpan Span, IReadOnlyList<MapEntry> Entries) : CelExpr(Span);

    /// <summary>Message/struct creation: TypeName{field: value, ...}</summary>
    public sealed record CreateStruct(SourceSpan Span, string TypeName, IReadOnlyList<FieldInit> Fields) : CelExpr(Span);

    /// <summary>
    /// Comprehension expression, produced by macro expansion.
    /// Represents: iterRange.macro(iterVar, body)
    /// </summary>
    public sealed record Comprehension(
        SourceSpan Span,
        string IterVar,
        CelExpr IterRange,
        string AccuVar,
        CelExpr AccuInit,
        CelExpr LoopCondition,
        CelExpr LoopStep,
        CelExpr Result) : CelExpr(Span);
}

/// <summary>A key-value entry in a map literal.</summary>
public sealed record MapEntry(CelExpr Key, CelExpr Value);

/// <summary>A field initialiser in a struct literal.</summary>
public sealed record FieldInit(string Field, CelExpr Value);
