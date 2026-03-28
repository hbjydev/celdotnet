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
            CelTypeKind.Int => LinqExpression.Constant(lit.Value!, typeof(long)),
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

        // Try to find an indexer on the type
        var indexer = operand.Type.GetProperties()
            .FirstOrDefault(p => p.GetIndexParameters().Length == 1);

        if (indexer is not null)
        {
            var indexParam = indexer.GetIndexParameters()[0];
            var convertedKey = EnsureType(key, indexParam.ParameterType);
            return LinqExpression.MakeIndex(operand, indexer, [convertedKey]);
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
        if (operand.Type == typeof(long))
            return LinqExpression.Negate(operand);
        if (operand.Type == typeof(double))
            return LinqExpression.Negate(operand);
        if (operand.Type == typeof(int))
            return LinqExpression.Negate(LinqExpression.Convert(operand, typeof(long)));

        throw new CelException($"cannot negate type {operand.Type.Name}");
    }

    #endregion

    #region Binary Operations

    private LinqExpression VisitBinary(CelExpr.Binary binary)
    {
        // Special case: "in" operator
        if (binary.Op == BinaryOp.In)
            return VisitInOperator(binary);

        var left = Visit(binary.Left);
        var right = Visit(binary.Right);

        return binary.Op switch
        {
            // Logical
            BinaryOp.And => LinqExpression.AndAlso(
                EnsureType(left, typeof(bool)),
                EnsureType(right, typeof(bool))),
            BinaryOp.Or => LinqExpression.OrElse(
                EnsureType(left, typeof(bool)),
                EnsureType(right, typeof(bool))),

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
            BinaryOp.Multiply => MakeArithmetic(LinqExpression.Multiply, left, right),
            BinaryOp.Divide => MakeArithmetic(LinqExpression.Divide, left, right),
            BinaryOp.Modulo => MakeArithmetic(LinqExpression.Modulo, left, right),

            _ => throw new CelException($"unsupported binary operator: {binary.Op}"),
        };
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

        // Harmonise numeric types
        HarmoniseTypes(ref left, ref right);
        return LinqExpression.Equal(left, right);
    }

    private static LinqExpression MakeComparison(
        Func<LinqExpression, LinqExpression, LinqExpression> factory,
        LinqExpression left,
        LinqExpression right)
    {
        HarmoniseTypes(ref left, ref right);
        return factory(left, right);
    }

    private static LinqExpression MakeArithmetic(
        Func<LinqExpression, LinqExpression, LinqExpression> factory,
        LinqExpression left,
        LinqExpression right)
    {
        HarmoniseTypes(ref left, ref right);
        return factory(left, right);
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

        // Timestamp + Duration → DateTimeOffset.Add(TimeSpan)
        if (left.Type == typeof(DateTimeOffset) && right.Type == typeof(TimeSpan))
        {
            var addMethod = typeof(DateTimeOffset).GetMethod(nameof(DateTimeOffset.Add), [typeof(TimeSpan)])!;
            return LinqExpression.Call(left, addMethod, right);
        }
        if (left.Type == typeof(TimeSpan) && right.Type == typeof(DateTimeOffset))
        {
            var addMethod = typeof(DateTimeOffset).GetMethod(nameof(DateTimeOffset.Add), [typeof(TimeSpan)])!;
            return LinqExpression.Call(right, addMethod, left);
        }

        // Duration + Duration → TimeSpan.Add(TimeSpan)
        if (left.Type == typeof(TimeSpan) && right.Type == typeof(TimeSpan))
        {
            return LinqExpression.Add(left, right);
        }

        HarmoniseTypes(ref left, ref right);
        return LinqExpression.Add(left, right);
    }

    /// <summary>
    /// CEL "in" operator: value in list → list.Contains(value)
    /// </summary>
    private LinqExpression VisitInOperator(CelExpr.Binary binary)
    {
        var value = Visit(binary.Left);
        var collection = Visit(binary.Right);

        // Use Enumerable.Contains<T>(source, value)
        var elementType = GetElementType(collection.Type) ?? value.Type;
        var containsMethod = CelFunctions.EnumerableContains.MakeGenericMethod(elementType);
        var convertedValue = EnsureType(value, elementType);

        return LinqExpression.Call(containsMethod, collection, convertedValue);
    }

    private static LinqExpression MakeSubtract(LinqExpression left, LinqExpression right)
    {
        // Timestamp - Duration → DateTimeOffset.Subtract(TimeSpan)
        if (left.Type == typeof(DateTimeOffset) && right.Type == typeof(TimeSpan))
        {
            var subtractMethod = typeof(DateTimeOffset).GetMethod(nameof(DateTimeOffset.Subtract), [typeof(TimeSpan)])!;
            return LinqExpression.Call(left, subtractMethod, right);
        }

        // Timestamp - Timestamp → TimeSpan
        if (left.Type == typeof(DateTimeOffset) && right.Type == typeof(DateTimeOffset))
        {
            return LinqExpression.Subtract(left, right);
        }

        // Duration - Duration → TimeSpan
        if (left.Type == typeof(TimeSpan) && right.Type == typeof(TimeSpan))
        {
            return LinqExpression.Subtract(left, right);
        }

        HarmoniseTypes(ref left, ref right);
        return LinqExpression.Subtract(left, right);
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

        return call.Function switch
        {
            "contains" => VisitStringMethod(target, call, CelFunctions.StringContains),
            "startsWith" => VisitStringMethod(target, call, CelFunctions.StringStartsWith),
            "endsWith" => VisitStringMethod(target, call, CelFunctions.StringEndsWith),
            "size" => VisitSizeCall(target),
            "matches" => VisitMatchesCall(target, call),
            // Timestamp member functions
            "getFullYear" => VisitTimestampMethod(target, CelFunctions.TimestampGetFullYear),
            "getMonth" => VisitTimestampMethod(target, CelFunctions.TimestampGetMonth),
            "getDayOfMonth" => VisitTimestampMethod(target, CelFunctions.TimestampGetDayOfMonth),
            "getDayOfWeek" => VisitTimestampMethod(target, CelFunctions.TimestampGetDayOfWeek),
            "getDayOfYear" => VisitTimestampMethod(target, CelFunctions.TimestampGetDayOfYear),
            "getHours" => VisitTimestampMethod(target, CelFunctions.TimestampGetHours),
            "getMinutes" => VisitTimestampMethod(target, CelFunctions.TimestampGetMinutes),
            "getSeconds" => VisitTimestampMethod(target, CelFunctions.TimestampGetSeconds),
            "getMilliseconds" => VisitTimestampMethod(target, CelFunctions.TimestampGetMilliseconds),
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
            "bool" when call.Args.Count == 1 => VisitTypeConversion(call, typeof(bool)),

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
    /// Type conversion: int(x), uint(x), double(x), bool(x) → Convert(x, targetType)
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
            if (targetType == typeof(bool))
                return LinqExpression.Call(
                    typeof(bool).GetMethod(nameof(bool.Parse), [typeof(string)])!,
                    arg);
        }

        // Timestamp to int: epoch seconds
        if (arg.Type == typeof(DateTimeOffset) && targetType == typeof(long))
        {
            var toUnixMethod = typeof(DateTimeOffset).GetMethod(nameof(DateTimeOffset.ToUnixTimeSeconds))!;
            return LinqExpression.Call(arg, toUnixMethod);
        }

        if (arg.Type == targetType)
            return arg;

        return LinqExpression.Convert(arg, targetType);
    }

    /// <summary>
    /// string(x) → Convert.ToString or .ToString() call
    /// </summary>
    private LinqExpression VisitStringConversion(CelExpr.Call call)
    {
        var arg = Visit(call.Args[0]);

        if (arg.Type == typeof(string))
            return arg;

        // For value types, call ToString()
        var toStringMethod = arg.Type.GetMethod(nameof(object.ToString), Type.EmptyTypes)!;
        return LinqExpression.Call(arg, toStringMethod);
    }

    /// <summary>
    /// timestamp("2023-01-01T00:00:00Z") → CelFunctions.ParseTimestamp(str)
    /// </summary>
    private LinqExpression VisitTimestampConstructor(CelExpr.Call call)
    {
        var arg = Visit(call.Args[0]);
        return LinqExpression.Call(CelFunctions.ParseTimestampMethod,
            EnsureType(arg, typeof(string)));
    }

    /// <summary>
    /// duration("3600s") → CelFunctions.ParseDuration(str)
    /// </summary>
    private LinqExpression VisitDurationConstructor(CelExpr.Call call)
    {
        var arg = Visit(call.Args[0]);
        return LinqExpression.Call(CelFunctions.ParseDurationMethod,
            EnsureType(arg, typeof(string)));
    }

    /// <summary>
    /// Timestamp member access: ts.getFullYear() etc.
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
        var allMethod = CelFunctions.EnumerableAll.MakeGenericMethod(elementType);
        return LinqExpression.Call(allMethod, source, lambda);
    }

    private LinqExpression CompileExists(LinqExpression source, Type elementType, CelExpr.Comprehension comp)
    {
        var predicate = ExtractPredicateFromBinaryStep(comp);
        var lambda = BuildIterLambda(elementType, comp.IterVar, predicate);
        var anyMethod = CelFunctions.EnumerableAny.MakeGenericMethod(elementType);
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
        return LinqExpression.Call(whereMethod, source, lambda);
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
            var selectMethod = CelFunctions.EnumerableSelect.MakeGenericMethod(elementType, transformLambda.Body.Type);
            return LinqExpression.Call(selectMethod, source, transformLambda);
        }

        // Simple map: LoopStep is Add(accu, [transform])
        if (comp.LoopStep is CelExpr.Binary { Op: BinaryOp.Add } addStep)
        {
            var transformList = (CelExpr.CreateList)addStep.Right;
            var transformBody = transformList.Elements[0];

            var transformLambda = BuildIterLambda(elementType, comp.IterVar, transformBody);
            var selectMethod = CelFunctions.EnumerableSelect.MakeGenericMethod(elementType, transformLambda.Body.Type);
            return LinqExpression.Call(selectMethod, source, transformLambda);
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
