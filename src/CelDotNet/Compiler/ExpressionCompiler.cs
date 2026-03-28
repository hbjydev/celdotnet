using System.Linq.Expressions;
using System.Reflection;
using CelDotNet.Ast;
using LinqExpression = System.Linq.Expressions.Expression;

namespace CelDotNet.Compiler;

/// <summary>
/// Compiles a CEL AST (<see cref="CelExpr"/>) into a <see cref="System.Linq.Expressions.Expression"/> tree.
/// The resulting expression can be used with EF Core / IQueryable or compiled to a delegate.
/// </summary>
internal sealed class ExpressionCompiler
{
    private readonly CompilerContext _context;

    private ExpressionCompiler(CompilerContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Compiles a CEL AST into an <see cref="Expression{TDelegate}"/> of type Func&lt;T, bool&gt;.
    /// </summary>
    public static Expression<Func<T, bool>> Compile<T>(CelExpr expr)
    {
        var context = new CompilerContext(typeof(T));
        var compiler = new ExpressionCompiler(context);
        var body = compiler.Visit(expr);

        // Ensure the body is boolean
        if (body.Type != typeof(bool))
        {
            throw new CelException(
                $"CEL expression must evaluate to bool, got {body.Type.Name}");
        }

        return LinqExpression.Lambda<Func<T, bool>>(body, context.Parameter);
    }

    /// <summary>
    /// Compiles a CEL AST into a raw <see cref="LambdaExpression"/>.
    /// The body type is not constrained to bool.
    /// </summary>
    public static LambdaExpression CompileUntyped(CelExpr expr, Type targetType)
    {
        var context = new CompilerContext(targetType);
        var compiler = new ExpressionCompiler(context);
        var body = compiler.Visit(expr);
        return LinqExpression.Lambda(body, context.Parameter);
    }

    #region Visitor Dispatch

    private LinqExpression Visit(CelExpr expr) => expr switch
    {
        CelExpr.Literal lit => VisitLiteral(lit),
        CelExpr.Ident ident => VisitIdent(ident),
        CelExpr.Select select => VisitSelect(select),
        CelExpr.Call call => VisitCall(call),
        CelExpr.Index index => VisitIndex(index),
        CelExpr.Unary unary => VisitUnary(unary),
        CelExpr.Binary binary => VisitBinary(binary),
        CelExpr.Conditional cond => VisitConditional(cond),
        CelExpr.CreateList list => VisitCreateList(list),
        CelExpr.CreateMap map => VisitCreateMap(map),
        CelExpr.Comprehension comp => VisitComprehension(comp),
        _ => throw new CelException($"unsupported AST node: {expr.GetType().Name}"),
    };

    #endregion

    #region Literals

    private LinqExpression VisitLiteral(CelExpr.Literal lit)
    {
        return lit.TypeKind switch
        {
            CelTypeKind.Bool => LinqExpression.Constant((bool)lit.Value!, typeof(bool)),
            CelTypeKind.Int => lit.Value is ulong
                ? LinqExpression.Constant(lit.Value, typeof(ulong))   // overflow literal, compiler will fold in VisitNegate
                : LinqExpression.Constant(lit.Value!, typeof(long)),
            CelTypeKind.Uint => LinqExpression.Constant(lit.Value!, typeof(ulong)),
            CelTypeKind.Double => LinqExpression.Constant(lit.Value!, typeof(double)),
            CelTypeKind.String => LinqExpression.Constant((string)lit.Value!, typeof(string)),
            CelTypeKind.Null => LinqExpression.Constant(null, typeof(object)),
            CelTypeKind.Bytes => LinqExpression.Constant((byte[])lit.Value!, typeof(byte[])),
            _ => throw new CelException($"unsupported literal type: {lit.TypeKind}"),
        };
    }

    #endregion

    #region Identifiers & Field Access

    private LinqExpression VisitIdent(CelExpr.Ident ident)
    {
        var resolved = _context.ResolveIdentifier(ident.Name);
        if (resolved is not null)
            return resolved;

        throw new CelException(
            $"unknown identifier '{ident.Name}' — not a variable or property of {_context.TargetType.Name}");
    }

    private LinqExpression VisitSelect(CelExpr.Select select)
    {
        var operand = Visit(select.Operand);
        var prop = CompilerContext.ResolveProperty(operand.Type, select.Field);

        if (prop is not null)
            return LinqExpression.Property(operand, prop);

        throw new CelException(
            $"no property '{select.Field}' on type {operand.Type.Name}");
    }

    private LinqExpression VisitIndex(CelExpr.Index index)
    {
        var operand = Visit(index.Operand);
        var key = Visit(index.Key);

        // Array indexing: arr[int]
        if (operand.Type.IsArray)
        {
            var elementType = operand.Type.GetElementType()!;
            var intKey = EnsureType(key, typeof(int));
            return LinqExpression.ArrayIndex(operand, intKey);
        }

        // Dictionary indexing
        if (operand.Type.IsGenericType && operand.Type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            var keyType = operand.Type.GetGenericArguments()[0];
            var indexer = operand.Type.GetProperty("Item")!;
            var convertedKey = EnsureType(key, keyType);
            return LinqExpression.MakeIndex(operand, indexer, [convertedKey]);
        }

        // Try to find an indexer on the type
        var genericIndexer = operand.Type.GetProperties()
            .FirstOrDefault(p => p.GetIndexParameters().Length == 1);

        if (genericIndexer is not null)
        {
            var indexParam = genericIndexer.GetIndexParameters()[0];
            var convertedKey = EnsureType(key, indexParam.ParameterType);
            return LinqExpression.MakeIndex(operand, genericIndexer, [convertedKey]);
        }

        throw new CelException(
            $"type {operand.Type.Name} does not support index access");
    }

    #endregion

    #region Unary Operations

    private LinqExpression VisitUnary(CelExpr.Unary unary)
    {
        var operand = Visit(unary.Operand);

        return unary.Op switch
        {
            UnaryOp.Not => LinqExpression.Not(EnsureType(operand, typeof(bool))),
            UnaryOp.Negate => VisitNegate(operand),
            _ => throw new CelException($"unsupported unary operator: {unary.Op}"),
        };
    }

    private static LinqExpression VisitNegate(LinqExpression operand)
    {
        // Handle INT64_MIN: -(9223372036854775808) where the literal overflows long
        // but fits in ulong. The only valid case is negating ulong(long.MaxValue + 1) → long.MinValue.
        if (operand is ConstantExpression { Value: ulong ulVal } && operand.Type == typeof(ulong))
        {
            if (ulVal == (ulong)long.MaxValue + 1)
                return LinqExpression.Constant(long.MinValue, typeof(long));
            throw new CelException("integer literal overflow");
        }

        if (operand.Type == typeof(long))
            return LinqExpression.NegateChecked(operand);
        if (operand.Type == typeof(double))
            return LinqExpression.Negate(operand);
        if (operand.Type == typeof(int))
            return LinqExpression.NegateChecked(LinqExpression.Convert(operand, typeof(long)));

        throw new CelException($"cannot negate type {operand.Type.Name}");
    }

    #endregion

    #region Binary Operations

    private LinqExpression VisitBinary(CelExpr.Binary binary)
    {
        // Special case: "in" operator
        if (binary.Op == BinaryOp.In)
            return VisitInOperator(binary);

        // Special case: logical AND/OR need lazy evaluation for CEL error semantics
        if (binary.Op == BinaryOp.And || binary.Op == BinaryOp.Or)
            return VisitLogical(binary);

        var left = Visit(binary.Left);
        var right = Visit(binary.Right);

        return binary.Op switch
        {

            // Comparison
            BinaryOp.Equal => MakeEqual(left, right),
            BinaryOp.NotEqual => LinqExpression.Not(MakeEqual(left, right)),
            BinaryOp.LessThan => MakeComparison(LinqExpression.LessThan, left, right),
            BinaryOp.LessThanOrEqual => MakeComparison(LinqExpression.LessThanOrEqual, left, right),
            BinaryOp.GreaterThan => MakeComparison(LinqExpression.GreaterThan, left, right),
            BinaryOp.GreaterThanOrEqual => MakeComparison(LinqExpression.GreaterThanOrEqual, left, right),

            // Arithmetic
            BinaryOp.Add => MakeAdd(left, right),
            BinaryOp.Subtract => MakeSubtract(left, right),
            BinaryOp.Multiply => MakeCheckedArithmetic(LinqExpression.MultiplyChecked, LinqExpression.Multiply, left, right),
            BinaryOp.Divide => MakeDivide(left, right),
            BinaryOp.Modulo => MakeModulo(left, right),

            _ => throw new CelException($"unsupported binary operator: {binary.Op}"),
        };
    }

    /// <summary>
    /// CEL logical AND/OR with commutative error handling.
    /// Both operands are wrapped in Func&lt;object&gt; so the runtime can catch errors
    /// on either side and still short-circuit.
    /// </summary>
    private LinqExpression VisitLogical(CelExpr.Binary binary)
    {
        var left = Visit(binary.Left);
        var right = Visit(binary.Right);

        // If both sides are plain bools with no potential for errors, use simple AndAlso/OrElse
        if (left.Type == typeof(bool) && right.Type == typeof(bool)
            && !MightThrow(binary.Left) && !MightThrow(binary.Right))
        {
            return binary.Op == BinaryOp.And
                ? LinqExpression.AndAlso(left, right)
                : LinqExpression.OrElse(left, right);
        }

        // Wrap each side in a Func<object> lambda for deferred evaluation with error catching
        var leftLambda = LinqExpression.Lambda<Func<object>>(EnsureType(left, typeof(object)));
        var rightLambda = LinqExpression.Lambda<Func<object>>(EnsureType(right, typeof(object)));

        var method = binary.Op == BinaryOp.And
            ? CelFunctions.LogicalAndLazyMethod
            : CelFunctions.LogicalOrLazyMethod;

        // LogicalAndLazy/LogicalOrLazy return object — unbox to bool since CEL && / || always produce bool
        return LinqExpression.Unbox(LinqExpression.Call(method, leftLambda, rightLambda), typeof(bool));
    }

    /// <summary>
    /// Heuristic: does this expression potentially throw at runtime?
    /// True for any expression containing division, modulo, function calls, or index access.
    /// </summary>
    private static bool MightThrow(CelExpr expr) => expr switch
    {
        CelExpr.Binary { Op: BinaryOp.Divide or BinaryOp.Modulo } => true,
        CelExpr.Binary b => MightThrow(b.Left) || MightThrow(b.Right),
        CelExpr.Unary u => MightThrow(u.Operand),
        CelExpr.Conditional c => MightThrow(c.Condition) || MightThrow(c.TrueExpr) || MightThrow(c.FalseExpr),
        CelExpr.Call => true,
        CelExpr.Index => true,
        _ => false,
    };

    /// <summary>
    /// CEL logical AND with short-circuit semantics.
    /// If both sides are bool, uses standard AndAlso.
    /// Otherwise, uses a runtime helper that handles non-bool operands and error propagation.
    /// </summary>
    private static LinqExpression MakeLogicalAnd(LinqExpression left, LinqExpression right)
    {
        if (left.Type == typeof(bool) && right.Type == typeof(bool))
            return LinqExpression.AndAlso(left, right);

        // Runtime helper for non-bool operands
        return LinqExpression.Call(
            CelFunctions.LogicalAndMethod,
            EnsureType(left, typeof(object)),
            EnsureType(right, typeof(object)));
    }

    /// <summary>
    /// CEL logical OR with short-circuit semantics.
    /// If both sides are bool, uses standard OrElse.
    /// Otherwise, uses a runtime helper that handles non-bool operands and error propagation.
    /// </summary>
    private static LinqExpression MakeLogicalOr(LinqExpression left, LinqExpression right)
    {
        if (left.Type == typeof(bool) && right.Type == typeof(bool))
            return LinqExpression.OrElse(left, right);

        // Runtime helper for non-bool operands
        return LinqExpression.Call(
            CelFunctions.LogicalOrMethod,
            EnsureType(left, typeof(object)),
            EnsureType(right, typeof(object)));
    }

    private LinqExpression MakeEqual(LinqExpression left, LinqExpression right)
    {
        // Handle null comparisons
        if (IsNullConstant(left) || IsNullConstant(right))
        {
            var nonNull = IsNullConstant(left) ? right : left;
            if (nonNull.Type.IsValueType && Nullable.GetUnderlyingType(nonNull.Type) is null)
            {
                // Comparing a value type to null — always false
                return LinqExpression.Constant(false);
            }
            var nullConst = LinqExpression.Constant(null, nonNull.Type);
            return LinqExpression.Equal(nonNull, nullConst);
        }

        // Bytes equality: use SequenceEqual
        if (left.Type == typeof(byte[]) && right.Type == typeof(byte[]))
        {
            return LinqExpression.Call(CelFunctions.BytesEqualMethod, left, right);
        }

        // Array/list structural equality: use CelFunctions.ArraysEqual
        if (left.Type.IsArray && right.Type.IsArray)
        {
            return LinqExpression.Call(CelFunctions.ArraysEqualMethod,
                LinqExpression.Convert(left, typeof(object)),
                LinqExpression.Convert(right, typeof(object)));
        }

        // Dictionary equality: use CelFunctions.MapsEqual
        if (IsDictionaryType(left.Type) && IsDictionaryType(right.Type))
        {
            return LinqExpression.Call(CelFunctions.MapsEqualMethod,
                LinqExpression.Convert(left, typeof(object)),
                LinqExpression.Convert(right, typeof(object)));
        }

        // Dynamic equality when one or both sides are object
        if (left.Type == typeof(object) || right.Type == typeof(object))
        {
            return LinqExpression.Call(CelFunctions.DynamicEqualMethod,
                EnsureType(left, typeof(object)),
                EnsureType(right, typeof(object)));
        }

        // Harmonise numeric types
        HarmoniseTypes(ref left, ref right);
        return LinqExpression.Equal(left, right);
    }

    private static LinqExpression MakeComparison(
        Func<LinqExpression, LinqExpression, LinqExpression> factory,
        LinqExpression left,
        LinqExpression right)
    {
        // CEL does not support ordering on null
        if (IsNullConstant(left) || IsNullConstant(right))
            throw new CelException("no such overload: comparison on null");

        // String comparison: use string.Compare(a, b, StringComparison.Ordinal)
        if (left.Type == typeof(string) && right.Type == typeof(string))
        {
            var compareResult = LinqExpression.Call(
                CelFunctions.StringCompareOrdinal, left, right);
            return factory(compareResult, LinqExpression.Constant(0));
        }

        // Bytes comparison: use CelFunctions.CompareBytes(a, b)
        if (left.Type == typeof(byte[]) && right.Type == typeof(byte[]))
        {
            var compareResult = LinqExpression.Call(
                CelFunctions.CompareBytesMethod, left, right);
            return factory(compareResult, LinqExpression.Constant(0));
        }

        // Bool comparison: CEL defines false < true, so convert to int
        if (left.Type == typeof(bool) && right.Type == typeof(bool))
        {
            var leftInt = LinqExpression.Condition(left,
                LinqExpression.Constant(1), LinqExpression.Constant(0));
            var rightInt = LinqExpression.Condition(right,
                LinqExpression.Constant(1), LinqExpression.Constant(0));
            return factory(leftInt, rightInt);
        }

        // Dynamic comparison when one side is object (e.g. from empty list iteration)
        if (left.Type == typeof(object) || right.Type == typeof(object))
        {
            var compareResult = LinqExpression.Call(
                CelFunctions.DynamicCompareMethod,
                EnsureType(left, typeof(object)),
                EnsureType(right, typeof(object)));
            return factory(compareResult, LinqExpression.Constant(0));
        }

        HarmoniseTypes(ref left, ref right);
        return factory(left, right);
    }

    private static LinqExpression MakeArithmetic(
        Func<LinqExpression, LinqExpression, LinqExpression> factory,
        LinqExpression left,
        LinqExpression right)
    {
        // Dynamic arithmetic when one side is object
        if (left.Type == typeof(object) || right.Type == typeof(object))
        {
            // Determine the operation name from the factory
            var testResult = factory(LinqExpression.Constant(1), LinqExpression.Constant(1));
            var opName = testResult.NodeType.ToString();
            return LinqExpression.Call(
                CelFunctions.DynamicArithmeticMethod,
                EnsureType(left, typeof(object)),
                EnsureType(right, typeof(object)),
                LinqExpression.Constant(opName));
        }

        HarmoniseTypes(ref left, ref right);
        return factory(left, right);
    }

    /// <summary>
    /// Makes arithmetic that uses checked variants for integer types to detect overflow.
    /// Falls back to unchecked for floating point.
    /// </summary>
    private static LinqExpression MakeCheckedArithmetic(
        Func<LinqExpression, LinqExpression, LinqExpression> checkedFactory,
        Func<LinqExpression, LinqExpression, LinqExpression> uncheckedFactory,
        LinqExpression left,
        LinqExpression right)
    {
        // Dynamic arithmetic when one side is object
        if (left.Type == typeof(object) || right.Type == typeof(object))
        {
            var testResult = uncheckedFactory(LinqExpression.Constant(1), LinqExpression.Constant(1));
            var opName = testResult.NodeType.ToString();
            return LinqExpression.Call(
                CelFunctions.DynamicArithmeticMethod,
                EnsureType(left, typeof(object)),
                EnsureType(right, typeof(object)),
                LinqExpression.Constant(opName));
        }

        HarmoniseTypes(ref left, ref right);

        // Use checked for integer types, unchecked for floating point
        if (left.Type == typeof(double) || left.Type == typeof(float))
            return uncheckedFactory(left, right);

        return checkedFactory(left, right);
    }

    /// <summary>
    /// Division — CEL requires divide-by-zero to be an eval error.
    /// Also disallow double modulo.
    /// </summary>
    private static LinqExpression MakeDivide(LinqExpression left, LinqExpression right)
    {
        // Dynamic arithmetic when one side is object
        if (left.Type == typeof(object) || right.Type == typeof(object))
        {
            return LinqExpression.Call(
                CelFunctions.DynamicArithmeticMethod,
                EnsureType(left, typeof(object)),
                EnsureType(right, typeof(object)),
                LinqExpression.Constant("Divide"));
        }

        HarmoniseTypes(ref left, ref right);
        return LinqExpression.Divide(left, right);
    }

    /// <summary>
    /// Modulo — CEL does not support % on doubles.
    /// </summary>
    private static LinqExpression MakeModulo(LinqExpression left, LinqExpression right)
    {
        // Dynamic arithmetic when one side is object
        if (left.Type == typeof(object) || right.Type == typeof(object))
        {
            return LinqExpression.Call(
                CelFunctions.DynamicArithmeticMethod,
                EnsureType(left, typeof(object)),
                EnsureType(right, typeof(object)),
                LinqExpression.Constant("Modulo"));
        }

        HarmoniseTypes(ref left, ref right);

        // CEL does not support modulo on doubles
        if (left.Type == typeof(double) || left.Type == typeof(float))
            throw new CelException("found no matching overload for '_%_' applied to '(double, double)'");

        return LinqExpression.Modulo(left, right);
    }

    private static LinqExpression MakeAdd(LinqExpression left, LinqExpression right)
    {
        // String concatenation
        if (left.Type == typeof(string) && right.Type == typeof(string))
        {
            var concatMethod = typeof(string).GetMethod(
                nameof(string.Concat),
                [typeof(string), typeof(string)])!;
            return LinqExpression.Call(concatMethod, left, right);
        }

        // Bytes concatenation
        if (left.Type == typeof(byte[]) && right.Type == typeof(byte[]))
        {
            return LinqExpression.Call(CelFunctions.ConcatBytesMethod, left, right);
        }

        // List concatenation (array + array)
        if (left.Type.IsArray && right.Type.IsArray)
        {
            var leftElem = left.Type.GetElementType()!;
            var rightElem = right.Type.GetElementType()!;

            Type commonType;
            if (leftElem == rightElem)
                commonType = leftElem;
            else if (leftElem == typeof(object) && rightElem != typeof(object))
                commonType = rightElem; // empty list adapts to typed list
            else if (rightElem == typeof(object) && leftElem != typeof(object))
                commonType = leftElem; // empty list adapts to typed list
            else
                commonType = typeof(object);

            var method = CelFunctions.ConcatArraysMethod.MakeGenericMethod(commonType);

            // Convert arrays to the common element type if needed
            var leftArr = leftElem == commonType
                ? left
                : LinqExpression.Call(CelFunctions.ConvertArrayMethod.MakeGenericMethod(commonType),
                    LinqExpression.Convert(left, typeof(object)));
            var rightArr = rightElem == commonType
                ? right
                : LinqExpression.Call(CelFunctions.ConvertArrayMethod.MakeGenericMethod(commonType),
                    LinqExpression.Convert(right, typeof(object)));

            return LinqExpression.Call(method, leftArr, rightArr);
        }

        // Timestamp + Duration → checked helper
        if (left.Type == typeof(DateTimeOffset) && right.Type == typeof(TimeSpan))
        {
            return LinqExpression.Call(CelFunctions.TimestampAddDurationMethod, left, right);
        }
        if (left.Type == typeof(TimeSpan) && right.Type == typeof(DateTimeOffset))
        {
            return LinqExpression.Call(CelFunctions.TimestampAddDurationMethod, right, left);
        }

        // Duration + Duration → checked helper
        if (left.Type == typeof(TimeSpan) && right.Type == typeof(TimeSpan))
        {
            return LinqExpression.Call(CelFunctions.DurationAddDurationMethod, left, right);
        }

        HarmoniseTypes(ref left, ref right);

        // Use checked for integer types
        if (left.Type == typeof(double) || left.Type == typeof(float))
            return LinqExpression.Add(left, right);
        return LinqExpression.AddChecked(left, right);
    }

    /// <summary>
    /// CEL "in" operator: value in list → list.Contains(value)
    ///                    value in map → map.ContainsKey(value)
    /// </summary>
    private LinqExpression VisitInOperator(CelExpr.Binary binary)
    {
        var value = Visit(binary.Left);
        var collection = Visit(binary.Right);

        // Map: key in map → map.ContainsKey(key)
        if (collection.Type.IsGenericType &&
            collection.Type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            var keyType = collection.Type.GetGenericArguments()[0];
            var containsKeyMethod = collection.Type.GetMethod(nameof(Dictionary<object, object>.ContainsKey))!;
            return LinqExpression.Call(collection, containsKeyMethod, EnsureType(value, keyType));
        }

        // List/array: value in list → Enumerable.Contains
        var elementType = GetElementType(collection.Type) ?? value.Type;
        var containsMethod = CelFunctions.EnumerableContains.MakeGenericMethod(elementType);
        var convertedValue = EnsureType(value, elementType);

        return LinqExpression.Call(containsMethod, collection, convertedValue);
    }

    private static LinqExpression MakeSubtract(LinqExpression left, LinqExpression right)
    {
        // Timestamp - Duration → checked helper
        if (left.Type == typeof(DateTimeOffset) && right.Type == typeof(TimeSpan))
        {
            return LinqExpression.Call(CelFunctions.TimestampSubtractDurationMethod, left, right);
        }

        // Timestamp - Timestamp → checked helper with duration range validation
        if (left.Type == typeof(DateTimeOffset) && right.Type == typeof(DateTimeOffset))
        {
            return LinqExpression.Call(CelFunctions.TimestampSubtractTimestampMethod, left, right);
        }

        // Duration - Duration → checked helper
        if (left.Type == typeof(TimeSpan) && right.Type == typeof(TimeSpan))
        {
            return LinqExpression.Call(CelFunctions.DurationSubtractDurationMethod, left, right);
        }

        HarmoniseTypes(ref left, ref right);

        // Use checked for integer types
        if (left.Type == typeof(double) || left.Type == typeof(float))
            return LinqExpression.Subtract(left, right);
        return LinqExpression.SubtractChecked(left, right);
    }

    #endregion

    #region Function Calls

    private LinqExpression VisitCall(CelExpr.Call call)
    {
        // Receiver-style calls: target.method(args)
        if (call.Target is not null)
            return VisitReceiverCall(call);

        // Global function calls
        return VisitGlobalCall(call);
    }

    private LinqExpression VisitReceiverCall(CelExpr.Call call)
    {
        var target = Visit(call.Target!);

        // Duration-specific getters: target is TimeSpan
        if (target.Type == typeof(TimeSpan))
        {
            return call.Function switch
            {
                "getHours" => LinqExpression.Call(CelFunctions.DurationGetHoursMethod, target),
                "getMinutes" => LinqExpression.Call(CelFunctions.DurationGetMinutesMethod, target),
                "getSeconds" => LinqExpression.Call(CelFunctions.DurationGetSecondsMethod, target),
                "getMilliseconds" => LinqExpression.Call(CelFunctions.DurationGetMillisecondsMethod, target),
                _ => throw new CelException($"unknown duration function '{call.Function}'"),
            };
        }

        return call.Function switch
        {
            "contains" => VisitStringMethod(target, call, CelFunctions.StringContains),
            "startsWith" => VisitStringMethod(target, call, CelFunctions.StringStartsWith),
            "endsWith" => VisitStringMethod(target, call, CelFunctions.StringEndsWith),
            "size" => VisitSizeCall(target),
            "matches" => VisitMatchesCall(target, call),
            // Timestamp member functions — with optional timezone arg
            "getFullYear" => VisitTimestampMethodWithTz(target, call,
                CelFunctions.TimestampGetFullYear, CelFunctions.TimestampGetFullYearTz),
            "getMonth" => VisitTimestampMethodWithTz(target, call,
                CelFunctions.TimestampGetMonth, CelFunctions.TimestampGetMonthTz),
            "getDayOfMonth" => VisitTimestampMethodWithTz(target, call,
                CelFunctions.TimestampGetDayOfMonth, CelFunctions.TimestampGetDayOfMonthTz),
            "getDate" => VisitTimestampMethodWithTz(target, call,
                CelFunctions.TimestampGetDate, CelFunctions.TimestampGetDateTz),
            "getDayOfWeek" => VisitTimestampMethodWithTz(target, call,
                CelFunctions.TimestampGetDayOfWeek, CelFunctions.TimestampGetDayOfWeekTz),
            "getDayOfYear" => VisitTimestampMethodWithTz(target, call,
                CelFunctions.TimestampGetDayOfYear, CelFunctions.TimestampGetDayOfYearTz),
            "getHours" => VisitTimestampMethodWithTz(target, call,
                CelFunctions.TimestampGetHours, CelFunctions.TimestampGetHoursTz),
            "getMinutes" => VisitTimestampMethodWithTz(target, call,
                CelFunctions.TimestampGetMinutes, CelFunctions.TimestampGetMinutesTz),
            "getSeconds" => VisitTimestampMethodWithTz(target, call,
                CelFunctions.TimestampGetSeconds, CelFunctions.TimestampGetSecondsTz),
            "getMilliseconds" => VisitTimestampMethodWithTz(target, call,
                CelFunctions.TimestampGetMilliseconds, CelFunctions.TimestampGetMillisecondsTz),
            _ => throw new CelException($"unknown receiver function '{call.Function}'"),
        };
    }

    private LinqExpression VisitGlobalCall(CelExpr.Call call)
    {
        return call.Function switch
        {
            "size" when call.Args.Count == 1 => VisitSizeCall(Visit(call.Args[0])),
            "has" when call.Args.Count == 1 => VisitHasCall(call),
            "matches" when call.Args.Count == 2 => VisitGlobalMatchesCall(call),

            // Type conversion functions
            "int" when call.Args.Count == 1 => VisitTypeConversion(call, typeof(long)),
            "uint" when call.Args.Count == 1 => VisitTypeConversion(call, typeof(ulong)),
            "double" when call.Args.Count == 1 => VisitTypeConversion(call, typeof(double)),
            "string" when call.Args.Count == 1 => VisitStringConversion(call),
            "bool" when call.Args.Count == 1 => VisitBoolConversion(call),
            "bytes" when call.Args.Count == 1 => VisitBytesConversion(call),

            // Timestamp/Duration constructors
            "timestamp" when call.Args.Count == 1 => VisitTimestampConstructor(call),
            "duration" when call.Args.Count == 1 => VisitDurationConstructor(call),

            _ => throw new CelException($"unknown function '{call.Function}'"),
        };
    }

    private LinqExpression VisitStringMethod(LinqExpression target, CelExpr.Call call, MethodInfo method)
    {
        if (call.Args.Count != 1)
            throw new CelException($"{call.Function}() expects 1 argument, got {call.Args.Count}");

        var arg = Visit(call.Args[0]);
        return LinqExpression.Call(target, method, EnsureType(arg, typeof(string)));
    }

    private LinqExpression VisitSizeCall(LinqExpression target)
    {
        // For strings, use .Length directly (EF Core translatable)
        if (target.Type == typeof(string))
        {
            return LinqExpression.Convert(
                LinqExpression.Property(target, nameof(string.Length)),
                typeof(long));
        }

        // For arrays, use .Length
        if (target.Type.IsArray)
        {
            return LinqExpression.Convert(
                LinqExpression.ArrayLength(target),
                typeof(long));
        }

        // For ICollection<T>, use .Count
        var countProp = target.Type.GetProperty("Count");
        if (countProp is not null)
        {
            return LinqExpression.Convert(
                LinqExpression.Property(target, countProp),
                typeof(long));
        }

        // Fallback to runtime helper
        return LinqExpression.Call(
            CelFunctions.SizeMethod,
            LinqExpression.Convert(target, typeof(object)));
    }

    /// <summary>
    /// has(x.field) → x.Field != null
    /// </summary>
    private LinqExpression VisitHasCall(CelExpr.Call call)
    {
        if (call.Args[0] is not CelExpr.Select selectArg)
            throw new CelException("has() argument must be a field selection");

        var operand = Visit(selectArg.Operand);
        var prop = CompilerContext.ResolveProperty(operand.Type, selectArg.Field)
            ?? throw new CelException($"no property '{selectArg.Field}' on type {operand.Type.Name}");

        var access = LinqExpression.Property(operand, prop);

        // For reference types and nullable value types: != null
        if (!access.Type.IsValueType || Nullable.GetUnderlyingType(access.Type) is not null)
        {
            return LinqExpression.NotEqual(
                access,
                LinqExpression.Constant(null, access.Type));
        }

        // For non-nullable value types, has() is always true
        return LinqExpression.Constant(true);
    }

    /// <summary>
    /// string.matches(regex) → Regex.IsMatch(string, regex)
    /// </summary>
    private LinqExpression VisitMatchesCall(LinqExpression target, CelExpr.Call call)
    {
        if (call.Args.Count != 1)
            throw new CelException($"matches() expects 1 argument, got {call.Args.Count}");

        var pattern = Visit(call.Args[0]);
        return LinqExpression.Call(CelFunctions.RegexIsMatch,
            EnsureType(target, typeof(string)),
            EnsureType(pattern, typeof(string)));
    }

    /// <summary>
    /// Global matches(string, regex) → Regex.IsMatch(string, regex)
    /// </summary>
    private LinqExpression VisitGlobalMatchesCall(CelExpr.Call call)
    {
        var str = Visit(call.Args[0]);
        var pattern = Visit(call.Args[1]);
        return LinqExpression.Call(CelFunctions.RegexIsMatch,
            EnsureType(str, typeof(string)),
            EnsureType(pattern, typeof(string)));
    }

    /// <summary>
    /// Type conversion: int(x), uint(x), double(x) → Convert(x, targetType) with range checking.
    /// </summary>
    private LinqExpression VisitTypeConversion(CelExpr.Call call, Type targetType)
    {
        var arg = Visit(call.Args[0]);

        // String to numeric: parse at runtime
        if (arg.Type == typeof(string))
        {
            if (targetType == typeof(long))
                return LinqExpression.Call(
                    typeof(long).GetMethod(nameof(long.Parse), [typeof(string)])!,
                    arg);
            if (targetType == typeof(ulong))
                return LinqExpression.Call(
                    typeof(ulong).GetMethod(nameof(ulong.Parse), [typeof(string)])!,
                    arg);
            if (targetType == typeof(double))
                return LinqExpression.Call(
                    typeof(double).GetMethod(nameof(double.Parse), [typeof(string)])!,
                    arg);
        }

        // Timestamp to int: epoch seconds
        if (arg.Type == typeof(DateTimeOffset) && targetType == typeof(long))
        {
            var toUnixMethod = typeof(DateTimeOffset).GetMethod(nameof(DateTimeOffset.ToUnixTimeSeconds))!;
            return LinqExpression.Call(arg, toUnixMethod);
        }

        // double → int with range check
        if (arg.Type == typeof(double) && targetType == typeof(long))
            return LinqExpression.Call(CelFunctions.DoubleToInt64Method, arg);

        // double → uint with range check
        if (arg.Type == typeof(double) && targetType == typeof(ulong))
            return LinqExpression.Call(CelFunctions.DoubleToUint64Method, arg);

        // ulong → long with range check
        if (arg.Type == typeof(ulong) && targetType == typeof(long))
            return LinqExpression.Call(CelFunctions.Uint64ToInt64Method, arg);

        // long → ulong with range check
        if (arg.Type == typeof(long) && targetType == typeof(ulong))
            return LinqExpression.Call(CelFunctions.Int64ToUint64Method, arg);

        // Identity: same type
        if (arg.Type == targetType)
            return arg;

        return LinqExpression.Convert(arg, targetType);
    }

    /// <summary>
    /// string(x) → proper CEL string conversion for each type.
    /// </summary>
    private LinqExpression VisitStringConversion(CelExpr.Call call)
    {
        var arg = Visit(call.Args[0]);

        if (arg.Type == typeof(string))
            return arg;

        // bytes → string: UTF-8 decode with validation
        if (arg.Type == typeof(byte[]))
            return LinqExpression.Call(CelFunctions.BytesToStringMethod, arg);

        // int → string
        if (arg.Type == typeof(long))
            return LinqExpression.Call(CelFunctions.Int64ToStringMethod, arg);

        // uint → string
        if (arg.Type == typeof(ulong))
            return LinqExpression.Call(CelFunctions.Uint64ToStringMethod, arg);

        // double → string
        if (arg.Type == typeof(double))
            return LinqExpression.Call(CelFunctions.DoubleToStringMethod, arg);

        // bool → string
        if (arg.Type == typeof(bool))
            return LinqExpression.Call(CelFunctions.BoolToStringMethod, arg);

        // timestamp → string
        if (arg.Type == typeof(DateTimeOffset))
            return LinqExpression.Call(CelFunctions.TimestampToStringMethod, arg);

        // duration → string
        if (arg.Type == typeof(TimeSpan))
            return LinqExpression.Call(CelFunctions.DurationToStringMethod, arg);

        // Fallback: .ToString()
        var toStringMethod = arg.Type.GetMethod(nameof(object.ToString), Type.EmptyTypes)!;
        return LinqExpression.Call(arg, toStringMethod);
    }

    /// <summary>
    /// bool(x) → CEL bool conversion.
    /// bool(string) accepts "true"/"false"/"1"/"0"/"t"/"f" (case-sensitive).
    /// bool(bool) is identity.
    /// </summary>
    private LinqExpression VisitBoolConversion(CelExpr.Call call)
    {
        var arg = Visit(call.Args[0]);

        if (arg.Type == typeof(bool))
            return arg;

        if (arg.Type == typeof(string))
            return LinqExpression.Call(CelFunctions.CelParseBoolMethod, arg);

        throw new CelException($"no such overload: bool({arg.Type.Name})");
    }

    /// <summary>
    /// bytes(x) → CEL bytes conversion.
    /// bytes(string) → UTF-8 encode.
    /// bytes(bytes) → identity.
    /// </summary>
    private LinqExpression VisitBytesConversion(CelExpr.Call call)
    {
        var arg = Visit(call.Args[0]);

        if (arg.Type == typeof(byte[]))
            return arg;

        if (arg.Type == typeof(string))
            return LinqExpression.Call(CelFunctions.StringToBytesMethod, arg);

        throw new CelException($"no such overload: bytes({arg.Type.Name})");
    }

    /// <summary>
    /// timestamp(x):
    ///   timestamp(string) → CelFunctions.ParseTimestamp(str)
    ///   timestamp(int)    → CelFunctions.TimestampFromEpoch(epochSeconds)
    ///   timestamp(timestamp) → identity
    /// </summary>
    private LinqExpression VisitTimestampConstructor(CelExpr.Call call)
    {
        var arg = Visit(call.Args[0]);

        // Identity: timestamp(timestamp) → pass-through
        if (arg.Type == typeof(DateTimeOffset))
            return arg;

        // timestamp(int) → from epoch seconds
        if (arg.Type == typeof(long))
            return LinqExpression.Call(CelFunctions.TimestampFromEpochMethod, arg);

        // timestamp(string)
        return LinqExpression.Call(CelFunctions.ParseTimestampMethod,
            EnsureType(arg, typeof(string)));
    }

    /// <summary>
    /// duration(x):
    ///   duration(string)   → CelFunctions.ParseDuration(str)
    ///   duration(duration)  → identity
    /// </summary>
    private LinqExpression VisitDurationConstructor(CelExpr.Call call)
    {
        var arg = Visit(call.Args[0]);

        // Identity: duration(duration) → pass-through
        if (arg.Type == typeof(TimeSpan))
            return arg;

        return LinqExpression.Call(CelFunctions.ParseDurationMethod,
            EnsureType(arg, typeof(string)));
    }

    /// <summary>
    /// Timestamp member access: ts.getFullYear() or ts.getFullYear("timezone").
    /// With 0 args, calls the UTC method. With 1 string arg, calls the timezone-aware method.
    /// </summary>
    private LinqExpression VisitTimestampMethodWithTz(
        LinqExpression target, CelExpr.Call call,
        MethodInfo utcMethod, MethodInfo tzMethod)
    {
        var tsExpr = EnsureType(target, typeof(DateTimeOffset));

        if (call.Args.Count == 0)
        {
            // UTC variant
            return LinqExpression.Convert(
                LinqExpression.Call(utcMethod, tsExpr),
                typeof(long));
        }

        if (call.Args.Count == 1)
        {
            // Timezone variant
            var tzArg = Visit(call.Args[0]);
            return LinqExpression.Convert(
                LinqExpression.Call(tzMethod, tsExpr, EnsureType(tzArg, typeof(string))),
                typeof(long));
        }

        throw new CelException($"{call.Function}() expects 0 or 1 arguments, got {call.Args.Count}");
    }

    /// <summary>
    /// Timestamp member access: ts.getFullYear() etc. (UTC only, no timezone arg).
    /// </summary>
    private static LinqExpression VisitTimestampMethod(LinqExpression target, MethodInfo method)
    {
        return LinqExpression.Convert(
            LinqExpression.Call(method, EnsureType(target, typeof(DateTimeOffset))),
            typeof(long));
    }

    #endregion

    #region Comprehension (Macros → LINQ)

    /// <summary>
    /// Compiles Comprehension AST nodes (produced by macro expansion) into LINQ method calls.
    /// Pattern-matches the structure to determine which macro was used:
    ///   all(x, pred)      → Enumerable.All(source, x => pred)
    ///   exists(x, pred)   → Enumerable.Any(source, x => pred)
    ///   exists_one(x, pred) → Enumerable.Count(source, x => pred) == 1
    ///   filter(x, pred)   → Enumerable.Where(source, x => pred)
    ///   map(x, transform) → Enumerable.Select(source, x => transform)
    ///   map(x, filter, transform) → source.Where(x => filter).Select(x => transform)
    /// </summary>
    private LinqExpression VisitComprehension(CelExpr.Comprehension comp)
    {
        var source = Visit(comp.IterRange);

        // For dictionaries, CEL iterates over keys, not KeyValuePairs
        if (source.Type.IsGenericType &&
            source.Type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            var keysProp = source.Type.GetProperty("Keys")!;
            source = LinqExpression.Property(source, keysProp);
        }

        var elementType = GetElementType(source.Type)
            ?? throw new CelException($"cannot iterate over type {source.Type.Name}");

        // Detect which macro pattern this is
        if (IsAllPattern(comp))
            return CompileAll(source, elementType, comp);
        if (IsExistsPattern(comp))
            return CompileExists(source, elementType, comp);
        if (IsExistsOnePattern(comp))
            return CompileExistsOne(source, elementType, comp);
        if (IsFilterPattern(comp))
            return CompileFilter(source, elementType, comp);
        if (IsMapPattern(comp))
            return CompileMap(source, elementType, comp);

        throw new CelException("unsupported comprehension pattern");
    }

    // --- Pattern detection ---

    private static bool IsAllPattern(CelExpr.Comprehension comp) =>
        comp.AccuInit is CelExpr.Literal { TypeKind: CelTypeKind.Bool, Value: true }
        && comp.LoopStep is CelExpr.Binary { Op: BinaryOp.And };

    private static bool IsExistsPattern(CelExpr.Comprehension comp) =>
        comp.AccuInit is CelExpr.Literal { TypeKind: CelTypeKind.Bool, Value: false }
        && comp.LoopStep is CelExpr.Binary { Op: BinaryOp.Or };

    private static bool IsExistsOnePattern(CelExpr.Comprehension comp) =>
        comp.AccuInit is CelExpr.Literal { TypeKind: CelTypeKind.Int, Value: 0L }
        && comp.Result is CelExpr.Binary { Op: BinaryOp.Equal };

    private static bool IsFilterPattern(CelExpr.Comprehension comp) =>
        comp.AccuInit is CelExpr.CreateList { Elements.Count: 0 }
        && comp.LoopStep is CelExpr.Conditional condStep
        && condStep.TrueExpr is CelExpr.Binary { Op: BinaryOp.Add }
        && comp.Result is CelExpr.Ident;

    private static bool IsMapPattern(CelExpr.Comprehension comp) =>
        comp.AccuInit is CelExpr.CreateList { Elements.Count: 0 }
        && comp.Result is CelExpr.Ident;

    // --- Compilation helpers ---

    /// <summary>
    /// Builds a lambda for a comprehension predicate/transform, with the iteration variable bound.
    /// </summary>
    private LambdaExpression BuildIterLambda(Type elementType, string iterVar, CelExpr body)
    {
        var childScope = _context.CreateChildScope();
        var iterParam = LinqExpression.Parameter(elementType, iterVar);
        childScope.SetVariable(iterVar, iterParam);

        var childCompiler = new ExpressionCompiler(childScope);
        var bodyExpr = childCompiler.Visit(body);

        return LinqExpression.Lambda(bodyExpr, iterParam);
    }

    /// <summary>
    /// Extracts the predicate from the LoopStep of all/exists patterns.
    /// For all: LoopStep = accu && pred → returns pred (right side)
    /// For exists: LoopStep = accu || pred → returns pred (right side)
    /// </summary>
    private static CelExpr ExtractPredicateFromBinaryStep(CelExpr.Comprehension comp)
    {
        var binary = (CelExpr.Binary)comp.LoopStep;
        return binary.Right;
    }

    /// <summary>
    /// Extracts the predicate from exists_one: the condition of the Conditional in LoopStep.
    /// </summary>
    private static CelExpr ExtractPredicateFromExistsOne(CelExpr.Comprehension comp)
    {
        var cond = (CelExpr.Conditional)comp.LoopStep;
        return cond.Condition;
    }

    private LinqExpression CompileAll(LinqExpression source, Type elementType, CelExpr.Comprehension comp)
    {
        var predicate = ExtractPredicateFromBinaryStep(comp);
        var lambda = BuildIterLambda(elementType, comp.IterVar, predicate);
        var allMethod = CelFunctions.CelAllMethod.MakeGenericMethod(elementType);
        return LinqExpression.Call(allMethod, source, lambda);
    }

    private LinqExpression CompileExists(LinqExpression source, Type elementType, CelExpr.Comprehension comp)
    {
        var predicate = ExtractPredicateFromBinaryStep(comp);
        var lambda = BuildIterLambda(elementType, comp.IterVar, predicate);
        var anyMethod = CelFunctions.CelAnyMethod.MakeGenericMethod(elementType);
        return LinqExpression.Call(anyMethod, source, lambda);
    }

    private LinqExpression CompileExistsOne(LinqExpression source, Type elementType, CelExpr.Comprehension comp)
    {
        var predicate = ExtractPredicateFromExistsOne(comp);
        var lambda = BuildIterLambda(elementType, comp.IterVar, predicate);
        var countMethod = CelFunctions.EnumerableCount.MakeGenericMethod(elementType);
        var count = LinqExpression.Call(countMethod, source, lambda);
        return LinqExpression.Equal(count, LinqExpression.Constant(1));
    }

    private LinqExpression CompileFilter(LinqExpression source, Type elementType, CelExpr.Comprehension comp)
    {
        var condStep = (CelExpr.Conditional)comp.LoopStep;
        var predicate = condStep.Condition;
        var lambda = BuildIterLambda(elementType, comp.IterVar, predicate);
        var whereMethod = CelFunctions.EnumerableWhere.MakeGenericMethod(elementType);
        var filtered = LinqExpression.Call(whereMethod, source, lambda);

        // Materialise to array so equality/size works
        var toArrayMethod = CelFunctions.EnumerableToArray.MakeGenericMethod(elementType);
        return LinqExpression.Call(toArrayMethod, filtered);
    }

    private LinqExpression CompileMap(LinqExpression source, Type elementType, CelExpr.Comprehension comp)
    {
        // Detect map with filter: LoopStep is Conditional(filter, Add(accu, [transform]), accu)
        if (comp.LoopStep is CelExpr.Conditional condStep)
        {
            var filterPred = condStep.Condition;
            var filterLambda = BuildIterLambda(elementType, comp.IterVar, filterPred);
            var whereMethod = CelFunctions.EnumerableWhere.MakeGenericMethod(elementType);
            source = LinqExpression.Call(whereMethod, source, filterLambda);

            // Extract transform from the Add's right-side list
            var addExpr = (CelExpr.Binary)condStep.TrueExpr;
            var transformList = (CelExpr.CreateList)addExpr.Right;
            var transformBody = transformList.Elements[0];

            var transformLambda = BuildIterLambda(elementType, comp.IterVar, transformBody);
            var resultType = transformLambda.Body.Type;
            var selectMethod = CelFunctions.EnumerableSelect.MakeGenericMethod(elementType, resultType);
            var mapped = LinqExpression.Call(selectMethod, source, transformLambda);

            // Materialise to array
            var toArrayMethod = CelFunctions.EnumerableToArray.MakeGenericMethod(resultType);
            return LinqExpression.Call(toArrayMethod, mapped);
        }

        // Simple map: LoopStep is Add(accu, [transform])
        if (comp.LoopStep is CelExpr.Binary { Op: BinaryOp.Add } addStep)
        {
            var transformList = (CelExpr.CreateList)addStep.Right;
            var transformBody = transformList.Elements[0];

            var transformLambda = BuildIterLambda(elementType, comp.IterVar, transformBody);
            var resultType = transformLambda.Body.Type;
            var selectMethod = CelFunctions.EnumerableSelect.MakeGenericMethod(elementType, resultType);
            var mapped = LinqExpression.Call(selectMethod, source, transformLambda);

            // Materialise to array
            var toArrayMethod = CelFunctions.EnumerableToArray.MakeGenericMethod(resultType);
            return LinqExpression.Call(toArrayMethod, mapped);
        }

        throw new CelException("unsupported map() comprehension structure");
    }

    #endregion

    #region Conditional

    private LinqExpression VisitConditional(CelExpr.Conditional cond)
    {
        var test = Visit(cond.Condition);
        var ifTrue = Visit(cond.TrueExpr);
        var ifFalse = Visit(cond.FalseExpr);

        // Ensure both branches have the same type
        HarmoniseTypes(ref ifTrue, ref ifFalse);

        // If types still don't match after harmonisation, box to object
        if (ifTrue.Type != ifFalse.Type)
        {
            ifTrue = EnsureType(ifTrue, typeof(object));
            ifFalse = EnsureType(ifFalse, typeof(object));
        }

        return LinqExpression.Condition(
            EnsureType(test, typeof(bool)),
            ifTrue,
            ifFalse);
    }

    #endregion

    #region List Creation

    private LinqExpression VisitCreateList(CelExpr.CreateList list)
    {
        if (list.Elements.Count == 0)
        {
            // Empty list — default to object[]
            return LinqExpression.NewArrayInit(typeof(object));
        }

        var elements = list.Elements.Select(Visit).ToList();

        // Determine the common element type
        var elementType = DetermineCommonType(elements);

        var converted = elements
            .Select(e => EnsureType(e, elementType))
            .ToArray();

        return LinqExpression.NewArrayInit(elementType, converted);
    }

    #endregion

    #region Map Creation

    private LinqExpression VisitCreateMap(CelExpr.CreateMap map)
    {
        var dictType = typeof(Dictionary<object, object>);
        var ctor = dictType.GetConstructor(Type.EmptyTypes)!;
        var addMethod = dictType.GetMethod(nameof(Dictionary<object, object>.Add))!;

        if (map.Entries.Count == 0)
        {
            // Empty map: new Dictionary<object, object>()
            return LinqExpression.New(ctor);
        }

        var inits = new List<System.Linq.Expressions.ElementInit>();
        foreach (var entry in map.Entries)
        {
            var key = Visit(entry.Key);
            var value = Visit(entry.Value);
            inits.Add(LinqExpression.ElementInit(addMethod,
                EnsureType(key, typeof(object)),
                EnsureType(value, typeof(object))));
        }

        return LinqExpression.ListInit(LinqExpression.New(ctor), inits);
    }

    #endregion

    #region Type Helpers

    private static LinqExpression EnsureType(LinqExpression expr, Type targetType)
    {
        if (expr.Type == targetType)
            return expr;

        // Handle null constant
        if (IsNullConstant(expr))
            return LinqExpression.Constant(null, targetType);

        return LinqExpression.Convert(expr, targetType);
    }

    private static bool IsNullConstant(LinqExpression expr) =>
        expr is ConstantExpression { Value: null };

    /// <summary>
    /// Promotes numeric types so both sides of an operation have the same type.
    /// CEL type promotion: int → double, uint → double.
    /// Also handles int (C# int) → long (CEL int).
    /// </summary>
    private static void HarmoniseTypes(ref LinqExpression left, ref LinqExpression right)
    {
        if (left.Type == right.Type)
            return;

        // Handle nullable types: unwrap for comparison
        var leftType = Nullable.GetUnderlyingType(left.Type) ?? left.Type;
        var rightType = Nullable.GetUnderlyingType(right.Type) ?? right.Type;

        // int → long promotion (C# property int → CEL long literal)
        if (leftType == typeof(int) && rightType == typeof(long))
        {
            left = LinqExpression.Convert(left, typeof(long));
            return;
        }
        if (leftType == typeof(long) && rightType == typeof(int))
        {
            right = LinqExpression.Convert(right, typeof(long));
            return;
        }

        // int/long → double promotion
        if (IsNumericType(leftType) && IsNumericType(rightType))
        {
            if (leftType == typeof(double) || rightType == typeof(double))
            {
                if (leftType != typeof(double))
                    left = LinqExpression.Convert(left, typeof(double));
                if (rightType != typeof(double))
                    right = LinqExpression.Convert(right, typeof(double));
                return;
            }

            // Both are integer types — promote to the wider one
            var target = GetWiderIntegerType(leftType, rightType);
            if (leftType != target)
                left = LinqExpression.Convert(left, target);
            if (rightType != target)
                right = LinqExpression.Convert(right, target);
        }
    }

    private static bool IsNumericType(Type t) =>
        t == typeof(int) || t == typeof(long) || t == typeof(ulong) ||
        t == typeof(double) || t == typeof(float) ||
        t == typeof(short) || t == typeof(byte) || t == typeof(decimal);

    private static bool IsDictionaryType(Type t) =>
        t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>);

    private static Type GetWiderIntegerType(Type a, Type b)
    {
        // Precedence: ulong > long > int > short > byte
        int PrecedenceOf(Type t) => t == typeof(ulong) ? 4 :
            t == typeof(long) ? 3 : t == typeof(int) ? 2 :
            t == typeof(short) ? 1 : 0;

        return PrecedenceOf(a) >= PrecedenceOf(b) ? a : b;
    }

    private static Type? GetElementType(Type type)
    {
        if (type.IsArray)
            return type.GetElementType();

        if (type.IsGenericType)
        {
            // IEnumerable<T>, IList<T>, List<T>, etc.
            foreach (var iface in type.GetInterfaces().Prepend(type))
            {
                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    return iface.GetGenericArguments()[0];
            }
        }

        return null;
    }

    private static Type DetermineCommonType(List<LinqExpression> expressions)
    {
        var types = expressions.Select(e => e.Type).Distinct().ToList();
        if (types.Count == 1)
            return types[0];

        // If all numeric, promote to the widest
        if (types.All(IsNumericType))
        {
            if (types.Any(t => t == typeof(double)))
                return typeof(double);
            if (types.Any(t => t == typeof(ulong)))
                return typeof(ulong);
            if (types.Any(t => t == typeof(long)))
                return typeof(long);
            return typeof(long);
        }

        // Mixed types — fall back to object
        return typeof(object);
    }

    #endregion
}
