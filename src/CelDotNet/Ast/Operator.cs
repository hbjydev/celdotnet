namespace CelDotNet.Ast;

/// <summary>
/// Binary operators in CEL.
/// </summary>
public enum BinaryOp
{
    // Arithmetic
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,

    // Comparison
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,

    // Logical
    And,
    Or,

    // Membership
    In,
}

/// <summary>
/// Unary operators in CEL.
/// </summary>
public enum UnaryOp
{
    Negate,
    Not,
}
