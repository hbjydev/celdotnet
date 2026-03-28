using System.Linq.Expressions;
using CelDotNet.Ast;
using CelDotNet.Checker;
using CelDotNet.Compiler;
using CelDotNet.Parser;

namespace CelDotNet;

/// <summary>
/// Main entry point for parsing and compiling CEL expressions.
/// </summary>
/// <example>
/// <code>
/// // Simple usage (no type checking)
/// var expr = CelExpression.Parse("name == 'foo' &amp;&amp; age > 21");
/// Expression&lt;Func&lt;Person, bool&gt;&gt; predicate = expr.ToExpression&lt;Person&gt;();
/// Func&lt;Person, bool&gt; compiled = expr.Compile&lt;Person&gt;();
///
/// // With environment and type checking
/// var env = new CelEnvironment()
///     .AddVariable("threshold", CelType.Int);
/// var expr = CelExpression.Parse("age > threshold", env);
/// </code>
/// </example>
public sealed class CelExpression
{
    private readonly CelExpr _ast;
    private readonly CelEnvironment? _environment;

    private CelExpression(CelExpr ast, CelEnvironment? environment = null)
    {
        _ast = ast;
        _environment = environment;
    }

    /// <summary>
    /// The parsed AST. Exposed for advanced scenarios (inspection, custom compilation, etc.).
    /// </summary>
    public CelExpr Ast => _ast;

    /// <summary>
    /// The environment used during parsing, if any.
    /// </summary>
    public CelEnvironment? Environment => _environment;

    /// <summary>
    /// Parses a CEL expression string into a <see cref="CelExpression"/>.
    /// No type checking is performed.
    /// </summary>
    /// <param name="expression">The CEL expression source text.</param>
    /// <returns>A parsed expression ready for compilation.</returns>
    /// <exception cref="CelParseException">Thrown when the expression contains syntax errors.</exception>
    public static CelExpression Parse(string expression)
    {
        var ast = CelParser.Parse(expression);
        return new CelExpression(ast);
    }

    /// <summary>
    /// Parses a CEL expression string with the given environment.
    /// If the environment has type checking enabled, the expression is type-checked
    /// against the declared variables.
    /// </summary>
    /// <param name="expression">The CEL expression source text.</param>
    /// <param name="environment">
    /// The environment containing variable declarations and type checking configuration.
    /// </param>
    /// <returns>A parsed expression ready for compilation.</returns>
    /// <exception cref="CelParseException">Thrown when the expression contains syntax errors.</exception>
    /// <exception cref="CelTypeException">Thrown when type checking is enabled and finds errors.</exception>
    public static CelExpression Parse(string expression, CelEnvironment environment)
    {
        var ast = CelParser.Parse(expression);
        var celExpr = new CelExpression(ast, environment);

        if (environment.TypeCheckingEnabled)
        {
            var result = TypeChecker.Check(ast, environment.TypeEnvironment);
            if (result.HasErrors)
                throw new CelTypeException(result.Errors);
        }

        return celExpr;
    }

    /// <summary>
    /// Runs the type checker against a target .NET type. This validates that all
    /// property references in the expression are valid for <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The target type to check against.</typeparam>
    /// <returns>The check result with inferred types and any errors.</returns>
    public CheckResult CheckTypes<T>() => TypeChecker.Check<T>(_ast);

    /// <summary>
    /// Compiles the parsed CEL expression into an expression tree targeting type <typeparamref name="T"/>.
    /// The resulting expression can be passed to EF Core or any IQueryable provider.
    /// </summary>
    /// <typeparam name="T">The target type whose properties are referenced in the CEL expression.</typeparam>
    /// <returns>An expression tree that can be used with LINQ or EF Core.</returns>
    /// <exception cref="CelException">Thrown when compilation fails (unknown fields, type mismatches, etc.).</exception>
    public Expression<Func<T, bool>> ToExpression<T>() => ExpressionCompiler.Compile<T>(_ast);

    /// <summary>
    /// Compiles the parsed CEL expression into a delegate targeting type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The target type whose properties are referenced in the CEL expression.</typeparam>
    /// <returns>A compiled delegate for in-memory evaluation.</returns>
    /// <exception cref="CelException">Thrown when compilation fails.</exception>
    public Func<T, bool> Compile<T>() => ToExpression<T>().Compile();
}
