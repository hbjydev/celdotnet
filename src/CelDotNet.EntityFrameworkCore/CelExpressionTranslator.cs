using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using CelDotNet.Compiler;
using LinqExpression = System.Linq.Expressions.Expression;

namespace CelDotNet.EntityFrameworkCore;

/// <summary>
/// An <see cref="ExpressionVisitor"/> that rewrites CEL expression trees into forms
/// that EF Core can translate to SQL. Specifically, it replaces calls to
/// <see cref="CelFunctions"/> static helpers with inline property access / arithmetic
/// that EF Core's query pipeline understands.
/// </summary>
internal sealed class CelExpressionTranslator : ExpressionVisitor
{
    /// <summary>
    /// Map from CelFunctions timestamp method → (DateTimeOffset property name, offset from CLR value).
    /// CEL uses 0-based months/days, so we subtract 1 from Month, Day, and DayOfYear.
    /// </summary>
    private static readonly Dictionary<MethodInfo, (string Property, int Offset)> TimestampPropertyMap = new()
    {
        [CelFunctions.TimestampGetFullYear] = (nameof(DateTimeOffset.Year), 0),
        [CelFunctions.TimestampGetMonth] = (nameof(DateTimeOffset.Month), -1),
        [CelFunctions.TimestampGetDayOfMonth] = (nameof(DateTimeOffset.Day), -1),
        [CelFunctions.TimestampGetDayOfYear] = (nameof(DateTimeOffset.DayOfYear), -1),
        [CelFunctions.TimestampGetHours] = (nameof(DateTimeOffset.Hour), 0),
        [CelFunctions.TimestampGetMinutes] = (nameof(DateTimeOffset.Minute), 0),
        [CelFunctions.TimestampGetSeconds] = (nameof(DateTimeOffset.Second), 0),
        [CelFunctions.TimestampGetMilliseconds] = (nameof(DateTimeOffset.Millisecond), 0),
    };

    /// <summary>
    /// Translates a CEL expression tree into an EF Core-compatible form.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="expression">The expression tree produced by the CEL compiler.</param>
    /// <returns>An EF Core-translatable expression tree.</returns>
    public static Expression<Func<T, bool>> Translate<T>(Expression<Func<T, bool>> expression)
    {
        var translator = new CelExpressionTranslator();
        return (Expression<Func<T, bool>>)translator.Visit(expression);
    }

    protected override LinqExpression VisitMethodCall(MethodCallExpression node)
    {
        var method = node.Method;

        // --- Timestamp member helpers → property access ---
        if (TimestampPropertyMap.TryGetValue(method, out var mapping))
        {
            return RewriteTimestampProperty(node.Arguments[0], mapping.Property, mapping.Offset);
        }

        // --- DayOfWeek: special case (returns enum, need cast to int) ---
        if (method == CelFunctions.TimestampGetDayOfWeek)
        {
            var target = Visit(node.Arguments[0]);
            var dayOfWeek = LinqExpression.Property(target, nameof(DateTimeOffset.DayOfWeek));
            return LinqExpression.Convert(dayOfWeek, typeof(int));
        }

        // --- ParseTimestamp / ParseDuration: evaluate constant args, reject non-constant ---
        if (method == CelFunctions.ParseTimestampMethod)
        {
            return EvaluateOrThrow(node, "timestamp()");
        }

        if (method == CelFunctions.ParseDurationMethod)
        {
            return EvaluateOrThrow(node, "duration()");
        }

        // --- Size() runtime fallback: not translatable ---
        if (method == CelFunctions.SizeMethod)
        {
            throw new CelTranslationException(
                "size() on this collection type cannot be translated to SQL. " +
                "Use a collection with a .Count property (ICollection<T>) or a string.");
        }

        // --- Regex.IsMatch: let it through, EF Core can translate for supported providers ---
        // (SQL Server → LIKE/PATINDEX, PostgreSQL → ~, SQLite → custom function)

        return base.VisitMethodCall(node);
    }

    /// <summary>
    /// Rewrites a CelFunctions.GetXxx(target) call into a direct property access on
    /// <see cref="DateTimeOffset"/>, optionally adjusted by an offset for CEL's 0-based indexing.
    /// </summary>
    private LinqExpression RewriteTimestampProperty(LinqExpression targetArg, string propertyName, int offset)
    {
        var target = Visit(targetArg);
        LinqExpression propertyAccess = LinqExpression.Property(target, propertyName);

        if (offset != 0)
        {
            propertyAccess = LinqExpression.Add(propertyAccess, LinqExpression.Constant(offset));
        }

        return propertyAccess;
    }

    /// <summary>
    /// If all arguments are constants (or can be reduced to constants), evaluates the method
    /// call at translate-time and returns a <see cref="ConstantExpression"/>. Otherwise, throws
    /// <see cref="CelTranslationException"/> because the function cannot be translated to SQL.
    /// </summary>
    private LinqExpression EvaluateOrThrow(MethodCallExpression node, string functionName)
    {
        // Try to extract constant arguments
        var args = new object?[node.Arguments.Count];
        for (int i = 0; i < node.Arguments.Count; i++)
        {
            var visited = Visit(node.Arguments[i]);
            if (visited is ConstantExpression constant)
            {
                args[i] = constant.Value;
            }
            else
            {
                throw new CelTranslationException(
                    $"{functionName} with non-constant arguments cannot be translated to SQL. " +
                    $"Only literal values like timestamp(\"2023-01-01T00:00:00Z\") are supported in EF Core queries.");
            }
        }

        // Evaluate the method with the constant args
        try
        {
            var result = node.Method.Invoke(null, args);
            return LinqExpression.Constant(result, node.Method.ReturnType);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw new CelTranslationException(
                $"{functionName} evaluation failed: {ex.InnerException.Message}");
        }
    }
}
