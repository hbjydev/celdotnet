namespace CelDotNet.EntityFrameworkCore;

/// <summary>
/// Extension methods for applying CEL (Common Expression Language) filters to
/// <see cref="IQueryable{T}"/> sources, enabling CEL expressions to be translated to SQL
/// via Entity Framework Core.
/// </summary>
/// <example>
/// <code>
/// // Simple filter
/// var results = await db.People
///     .WhereCel("name == 'foo' &amp;&amp; age > 21")
///     .ToListAsync();
///
/// // With environment (external variables, type checking)
/// var env = new CelEnvironment()
///     .AddVariable("min_age", typeof(int));
/// var results = await db.People
///     .WhereCel("age > min_age", env)
///     .ToListAsync();
/// </code>
/// </example>
public static class CelQueryableExtensions
{
    /// <summary>
    /// Filters an <see cref="IQueryable{T}"/> using a CEL expression string.
    /// The expression is parsed, compiled to a <see cref="System.Linq.Expressions.Expression"/> tree,
    /// optimised for EF Core translation, and applied as a <c>Where</c> clause.
    /// </summary>
    /// <typeparam name="T">The entity type being queried.</typeparam>
    /// <param name="source">The queryable source to filter.</param>
    /// <param name="expression">
    /// A CEL expression that evaluates to <c>bool</c>. Property names are resolved against
    /// <typeparamref name="T"/> using <c>[CelField]</c> attributes, exact match, or
    /// automatic <c>snake_case</c> → <c>PascalCase</c> conversion.
    /// </param>
    /// <returns>A filtered <see cref="IQueryable{T}"/>.</returns>
    /// <exception cref="CelParseException">The expression contains syntax errors.</exception>
    /// <exception cref="CelException">Compilation fails (unknown fields, type mismatches, etc.).</exception>
    /// <exception cref="CelTranslationException">
    /// The expression uses features that cannot be translated to SQL
    /// (e.g. <c>size()</c> on non-standard collections, <c>timestamp()</c> with non-constant arguments).
    /// </exception>
    public static IQueryable<T> WhereCel<T>(this IQueryable<T> source, string expression)
    {
        var celExpr = CelExpression.Parse(expression);
        var predicate = celExpr.ToExpression<T>();
        var translated = CelExpressionTranslator.Translate(predicate);
        return source.Where(translated);
    }

    /// <summary>
    /// Filters an <see cref="IQueryable{T}"/> using a CEL expression string with the given environment.
    /// If the environment has type checking enabled, the expression is validated before compilation.
    /// </summary>
    /// <typeparam name="T">The entity type being queried.</typeparam>
    /// <param name="source">The queryable source to filter.</param>
    /// <param name="expression">
    /// A CEL expression that evaluates to <c>bool</c>.
    /// </param>
    /// <param name="environment">
    /// The environment containing variable declarations and type checking configuration.
    /// </param>
    /// <returns>A filtered <see cref="IQueryable{T}"/>.</returns>
    /// <exception cref="CelParseException">The expression contains syntax errors.</exception>
    /// <exception cref="CelTypeException">Type checking is enabled and found errors.</exception>
    /// <exception cref="CelException">Compilation fails (unknown fields, type mismatches, etc.).</exception>
    /// <exception cref="CelTranslationException">
    /// The expression uses features that cannot be translated to SQL.
    /// </exception>
    public static IQueryable<T> WhereCel<T>(this IQueryable<T> source, string expression,
        CelEnvironment environment)
    {
        var celExpr = CelExpression.Parse(expression, environment);
        var predicate = celExpr.ToExpression<T>();
        var translated = CelExpressionTranslator.Translate(predicate);
        return source.Where(translated);
    }
}
