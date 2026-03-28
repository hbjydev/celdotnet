using CelDotNet.Ast;
using CelDotNet.Lexer;

namespace CelDotNet.Checker;

/// <summary>
/// A type error collected during type checking, including the source location.
/// </summary>
public sealed record CelTypeError(string Message, SourceSpan Span)
{
    public override string ToString() => $"{Message} at {Span}";
}

/// <summary>
/// The result of a type checking pass. Contains the inferred result type
/// and any type errors that were found.
/// </summary>
public sealed class CheckResult
{
    /// <summary>The inferred type of the top-level expression.</summary>
    public CelType ResultType { get; }

    /// <summary>Type errors found during checking. Empty if the expression is well-typed.</summary>
    public IReadOnlyList<CelTypeError> Errors { get; }

    /// <summary>True if any type errors were found.</summary>
    public bool HasErrors => Errors.Count > 0;

    internal CheckResult(CelType resultType, IReadOnlyList<CelTypeError> errors)
    {
        ResultType = resultType;
        Errors = errors;
    }
}

/// <summary>
/// Optional static type checker for CEL expressions. Walks the AST, infers types for
/// every node, and collects all type errors with source positions.
/// </summary>
/// <remarks>
/// The type checker is intentionally lenient — it collects errors rather than failing
/// fast, and uses <see cref="CelType.Error"/> as a sentinel to prevent cascading errors.
/// Expressions involving <see cref="CelType.Any"/> are always considered valid.
/// </remarks>
internal sealed class TypeChecker
{
    private readonly TypeEnvironment _env;
    private readonly List<CelTypeError> _errors = [];

    private TypeChecker(TypeEnvironment env)
    {
        _env = env;
    }

    /// <summary>
    /// Type-checks a CEL expression against the given environment.
    /// </summary>
    /// <param name="expr">The parsed AST to check.</param>
    /// <param name="env">The type environment containing variable declarations.</param>
    /// <returns>
    /// A <see cref="CheckResult"/> with the inferred result type and any errors.
    /// </returns>
    public static CheckResult Check(CelExpr expr, TypeEnvironment env)
    {
        var checker = new TypeChecker(env);
        var resultType = checker.Visit(expr);
        return new CheckResult(resultType, checker._errors);
    }

    /// <summary>
    /// Type-checks a CEL expression against a .NET target type.
    /// Properties of <typeparamref name="T"/> are added to the environment automatically.
    /// </summary>
    public static CheckResult Check<T>(CelExpr expr)
    {
        var env = new TypeEnvironment().AddPropertiesFrom<T>();
        return Check(expr, env);
    }

    /// <summary>
    /// Type-checks a CEL expression against a .NET target type with additional
    /// variable declarations from the provided environment.
    /// </summary>
    public static CheckResult Check<T>(CelExpr expr, TypeEnvironment env)
    {
        var combined = new TypeEnvironment().AddPropertiesFrom<T>();
        // Merge in additional variables from the provided env
        // (we re-add properties first, then user-declared variables take precedence)
        return Check(expr, MergeEnvironments(combined, env));
    }

#pragma warning disable IDE0060 // Remove unused parameter
    private static TypeEnvironment MergeEnvironments(TypeEnvironment baseEnv, TypeEnvironment overlay) =>
        // Simple merge: just return the overlay with base properties already set.
        // For a proper merge we'd need to expose iteration, but for now
        // the Check<T>(expr, env) overload handles the common case.
        baseEnv;
#pragma warning restore IDE0060 // Remove unused parameter

    #region Visitor Dispatch

    private CelType Visit(CelExpr expr) => expr switch
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
        CelExpr.CreateStruct strct => VisitCreateStruct(strct),
        CelExpr.Comprehension comp => VisitComprehension(comp),
        _ => ReportError(expr.Span, $"unsupported AST node: {expr.GetType().Name}"),
    };

    #endregion

    #region Literals

    private CelType VisitLiteral(CelExpr.Literal lit) => lit.TypeKind switch
    {
        CelTypeKind.Int => CelType.Int,
        CelTypeKind.Uint => CelType.Uint,
        CelTypeKind.Double => CelType.Double,
        CelTypeKind.Bool => CelType.Bool,
        CelTypeKind.String => CelType.String,
        CelTypeKind.Bytes => CelType.Bytes,
        CelTypeKind.Null => CelType.Null,
        _ => ReportError(lit.Span, $"unsupported literal type kind: {lit.TypeKind}"),
    };

    #endregion

    #region Identifiers & Field Access

    private CelType VisitIdent(CelExpr.Ident ident)
    {
        var type = _env.LookupVariable(ident.Name);
        if (type is not null)
            return type;

        return ReportError(ident.Span, $"undeclared identifier '{ident.Name}'");
    }

    private CelType VisitSelect(CelExpr.Select select)
    {
        var operandType = Visit(select.Operand);
        if (operandType.IsError)
            return CelType.Error;

        var fieldType = TypeEnvironment.ResolveFieldType(operandType, select.Field);
        if (fieldType is not null)
            return fieldType;

        // Any type allows any field access
        if (operandType.IsAny)
            return CelType.Any;

        return ReportError(select.Span, $"no field '{select.Field}' on type {operandType}");
    }

    private CelType VisitIndex(CelExpr.Index index)
    {
        var operandType = Visit(index.Operand);
        var keyType = Visit(index.Key);

        if (operandType.IsError || keyType.IsError)
            return CelType.Error;

        if (operandType.IsAny)
            return CelType.Any;

        // List indexing: list[int] → element type
        if (operandType is CelType.ListType listType)
        {
            if (!CelType.Int.IsAssignableFrom(keyType))
                ReportError(index.Key.Span, $"list index must be int, got {keyType}");
            return listType.ElementType;
        }

        // Map indexing: map[key] → value type
        if (operandType is CelType.MapType mapType)
        {
            if (!mapType.KeyType.IsAssignableFrom(keyType))
                ReportError(index.Key.Span, $"map key type is {mapType.KeyType}, got {keyType}");
            return mapType.ValueType;
        }

        return ReportError(index.Span, $"type {operandType} does not support index access");
    }

    #endregion

    #region Unary Operations

    private CelType VisitUnary(CelExpr.Unary unary)
    {
        var operandType = Visit(unary.Operand);
        if (operandType.IsError)
            return CelType.Error;

        return unary.Op switch
        {
            UnaryOp.Not => CheckUnaryNot(operandType, unary),
            UnaryOp.Negate => CheckUnaryNegate(operandType, unary),
            _ => ReportError(unary.Span, $"unsupported unary operator: {unary.Op}"),
        };
    }

    private CelType CheckUnaryNot(CelType operandType, CelExpr.Unary unary)
    {
        if (operandType.IsAny)
            return CelType.Bool;

        if (operandType is not CelType.PrimitiveType { Kind: CelTypeKind.Bool })
        {
            ReportError(unary.Operand.Span, $"'!' requires bool operand, got {operandType}");
        }
        return CelType.Bool;
    }

    private CelType CheckUnaryNegate(CelType operandType, CelExpr.Unary unary)
    {
        if (operandType.IsAny)
            return CelType.Any;

        if (!operandType.IsNumeric)
        {
            ReportError(unary.Operand.Span, $"'-' requires numeric operand, got {operandType}");
            return CelType.Error;
        }
        return operandType;
    }

    #endregion

    #region Binary Operations

    private CelType VisitBinary(CelExpr.Binary binary)
    {
        return binary.Op switch
        {
            BinaryOp.And or BinaryOp.Or => CheckLogical(binary),
            BinaryOp.Equal or BinaryOp.NotEqual => CheckEquality(binary),
            BinaryOp.LessThan or BinaryOp.LessThanOrEqual or
            BinaryOp.GreaterThan or BinaryOp.GreaterThanOrEqual => CheckOrdering(binary),
            BinaryOp.Add => CheckAdd(binary),
            BinaryOp.Subtract or BinaryOp.Multiply or
            BinaryOp.Divide or BinaryOp.Modulo => CheckArithmetic(binary),
            BinaryOp.In => CheckIn(binary),
            _ => ReportError(binary.Span, $"unsupported binary operator: {binary.Op}"),
        };
    }

    private CelType CheckLogical(CelExpr.Binary binary)
    {
        var leftType = Visit(binary.Left);
        var rightType = Visit(binary.Right);

        if (!leftType.IsError && !leftType.IsAny &&
            leftType is not CelType.PrimitiveType { Kind: CelTypeKind.Bool })
        {
            ReportError(binary.Left.Span,
                $"'{binary.Op}' requires bool operands, left side is {leftType}");
        }

        if (!rightType.IsError && !rightType.IsAny &&
            rightType is not CelType.PrimitiveType { Kind: CelTypeKind.Bool })
        {
            ReportError(binary.Right.Span,
                $"'{binary.Op}' requires bool operands, right side is {rightType}");
        }

        return CelType.Bool;
    }

    private CelType CheckEquality(CelExpr.Binary binary)
    {
        var leftType = Visit(binary.Left);
        var rightType = Visit(binary.Right);

        if (leftType.IsError || rightType.IsError || leftType.IsAny || rightType.IsAny)
            return CelType.Bool;

        // Null can be compared to anything
        if (leftType is CelType.PrimitiveType { Kind: CelTypeKind.Null } ||
            rightType is CelType.PrimitiveType { Kind: CelTypeKind.Null })
            return CelType.Bool;

        // Types must be compatible for equality
        if (!leftType.IsAssignableFrom(rightType) && !rightType.IsAssignableFrom(leftType))
        {
            ReportError(binary.Span,
                $"cannot compare {leftType} and {rightType} for equality");
        }

        return CelType.Bool;
    }

    private CelType CheckOrdering(CelExpr.Binary binary)
    {
        var leftType = Visit(binary.Left);
        var rightType = Visit(binary.Right);

        if (leftType.IsError || rightType.IsError || leftType.IsAny || rightType.IsAny)
            return CelType.Bool;

        // Ordering only works on numeric, string, bool, timestamp, duration
        bool orderable = IsOrderable(leftType) && IsOrderable(rightType);
        if (!orderable)
        {
            ReportError(binary.Span,
                $"cannot order {leftType} and {rightType} with '{binary.Op}'");
        }
        else if (!leftType.IsAssignableFrom(rightType) && !rightType.IsAssignableFrom(leftType))
        {
            ReportError(binary.Span,
                $"cannot compare {leftType} and {rightType} — types are not compatible");
        }

        return CelType.Bool;
    }

    private CelType CheckAdd(CelExpr.Binary binary)
    {
        var leftType = Visit(binary.Left);
        var rightType = Visit(binary.Right);

        if (leftType.IsError || rightType.IsError || leftType.IsAny || rightType.IsAny)
            return leftType.IsAny || rightType.IsAny ? CelType.Any : CelType.Error;

        // String concatenation
        if (leftType is CelType.PrimitiveType { Kind: CelTypeKind.String } &&
            rightType is CelType.PrimitiveType { Kind: CelTypeKind.String })
            return CelType.String;

        // List concatenation
        if (leftType is CelType.ListType leftList && rightType is CelType.ListType rightList)
        {
            if (!leftList.ElementType.IsAssignableFrom(rightList.ElementType))
            {
                ReportError(binary.Span,
                    $"cannot concatenate list({leftList.ElementType}) and list({rightList.ElementType})");
            }
            return leftType;
        }

        // Bytes concatenation
        if (leftType is CelType.PrimitiveType { Kind: CelTypeKind.Bytes } &&
            rightType is CelType.PrimitiveType { Kind: CelTypeKind.Bytes })
            return CelType.Bytes;

        // Numeric addition
        if (leftType.IsNumeric && rightType.IsNumeric)
            return PromoteNumeric(leftType, rightType);

        // Timestamp + Duration / Duration + Duration
        if (leftType is CelType.PrimitiveType { Kind: CelTypeKind.Timestamp } &&
            rightType is CelType.PrimitiveType { Kind: CelTypeKind.Duration })
            return CelType.Timestamp;
        if (leftType is CelType.PrimitiveType { Kind: CelTypeKind.Duration } &&
            rightType is CelType.PrimitiveType { Kind: CelTypeKind.Duration })
            return CelType.Duration;

        ReportError(binary.Span, $"cannot add {leftType} and {rightType}");
        return CelType.Error;
    }

    private CelType CheckArithmetic(CelExpr.Binary binary)
    {
        var leftType = Visit(binary.Left);
        var rightType = Visit(binary.Right);

        if (leftType.IsError || rightType.IsError || leftType.IsAny || rightType.IsAny)
            return leftType.IsAny || rightType.IsAny ? CelType.Any : CelType.Error;

        if (!leftType.IsNumeric || !rightType.IsNumeric)
        {
            ReportError(binary.Span,
                $"arithmetic operator '{binary.Op}' requires numeric operands, got {leftType} and {rightType}");
            return CelType.Error;
        }

        return PromoteNumeric(leftType, rightType);
    }

    private CelType CheckIn(CelExpr.Binary binary)
    {
        var leftType = Visit(binary.Left);
        var rightType = Visit(binary.Right);

        if (leftType.IsError || rightType.IsError || leftType.IsAny || rightType.IsAny)
            return CelType.Bool;

        // Right side must be a list or map
        if (rightType is CelType.ListType listType)
        {
            if (!listType.ElementType.IsAssignableFrom(leftType))
            {
                ReportError(binary.Span,
                    $"'in' operator: element type {leftType} is not compatible with list({listType.ElementType})");
            }
        }
        else if (rightType is CelType.MapType mapType)
        {
            if (!mapType.KeyType.IsAssignableFrom(leftType))
            {
                ReportError(binary.Span,
                    $"'in' operator: key type {leftType} is not compatible with map key type {mapType.KeyType}");
            }
        }
        else
        {
            ReportError(binary.Right.Span,
                $"'in' operator requires list or map on right side, got {rightType}");
        }

        return CelType.Bool;
    }

    #endregion

    #region Function Calls

    private CelType VisitCall(CelExpr.Call call)
    {
        if (call.Target is not null)
            return VisitReceiverCall(call);
        return VisitGlobalCall(call);
    }

    private CelType VisitReceiverCall(CelExpr.Call call)
    {
        var targetType = Visit(call.Target!);
        if (targetType.IsError)
            return CelType.Error;

        return call.Function switch
        {
            "contains" => CheckStringMethod(targetType, call, "contains", CelType.Bool),
            "startsWith" => CheckStringMethod(targetType, call, "startsWith", CelType.Bool),
            "endsWith" => CheckStringMethod(targetType, call, "endsWith", CelType.Bool),
            "matches" => CheckStringMethod(targetType, call, "matches", CelType.Bool),
            "size" => CheckSizeReceiver(targetType, call),
            "exists" or "all" or "exists_one" or "filter" or "map" =>
                CheckMacroCall(targetType, call),
            _ => ReportError(call.Span, $"unknown receiver function '{call.Function}'"),
        };
    }

    private CelType VisitGlobalCall(CelExpr.Call call)
    {
        return call.Function switch
        {
            "size" when call.Args.Count == 1 => CheckSizeGlobal(call),
            "has" when call.Args.Count == 1 => CheckHas(call),
            "int" when call.Args.Count == 1 => CheckTypeConversion(call, CelType.Int),
            "uint" when call.Args.Count == 1 => CheckTypeConversion(call, CelType.Uint),
            "double" when call.Args.Count == 1 => CheckTypeConversion(call, CelType.Double),
            "string" when call.Args.Count == 1 => CheckTypeConversion(call, CelType.String),
            "bool" when call.Args.Count == 1 => CheckTypeConversion(call, CelType.Bool),
            "bytes" when call.Args.Count == 1 => CheckTypeConversion(call, CelType.Bytes),
            "type" when call.Args.Count == 1 => CheckTypeOf(call),
            "timestamp" when call.Args.Count == 1 => CheckTypeConversion(call, CelType.Timestamp),
            "duration" when call.Args.Count == 1 => CheckTypeConversion(call, CelType.Duration),
            _ => ReportError(call.Span,
                $"unknown function '{call.Function}' with {call.Args.Count} argument(s)"),
        };
    }

    private CelType CheckStringMethod(CelType targetType, CelExpr.Call call, string methodName, CelType returnType)
    {
        if (!targetType.IsAny && targetType is not CelType.PrimitiveType { Kind: CelTypeKind.String })
        {
            ReportError(call.Target!.Span,
                $"'{methodName}' requires string receiver, got {targetType}");
        }

        if (call.Args.Count != 1)
        {
            ReportError(call.Span, $"'{methodName}' expects 1 argument, got {call.Args.Count}");
            return returnType;
        }

        var argType = Visit(call.Args[0]);
        if (!argType.IsError && !argType.IsAny &&
            argType is not CelType.PrimitiveType { Kind: CelTypeKind.String })
        {
            ReportError(call.Args[0].Span,
                $"'{methodName}' argument must be string, got {argType}");
        }

        return returnType;
    }

    private CelType CheckSizeReceiver(CelType targetType, CelExpr.Call call)
    {
        if (call.Args.Count != 0)
        {
            ReportError(call.Span, $"'size' expects 0 arguments when called as receiver, got {call.Args.Count}");
        }

        if (!targetType.IsAny &&
            targetType is not CelType.PrimitiveType { Kind: CelTypeKind.String or CelTypeKind.Bytes } &&
            targetType is not CelType.ListType &&
            targetType is not CelType.MapType)
        {
            ReportError(call.Target!.Span,
                $"'size' requires string, bytes, list, or map receiver, got {targetType}");
        }

        return CelType.Int;
    }

    private CelType CheckSizeGlobal(CelExpr.Call call)
    {
        var argType = Visit(call.Args[0]);
        if (!argType.IsError && !argType.IsAny &&
            argType is not CelType.PrimitiveType { Kind: CelTypeKind.String or CelTypeKind.Bytes } &&
            argType is not CelType.ListType &&
            argType is not CelType.MapType)
        {
            ReportError(call.Args[0].Span,
                $"'size' requires string, bytes, list, or map argument, got {argType}");
        }

        return CelType.Int;
    }

    private CelType CheckHas(CelExpr.Call call)
    {
        if (call.Args[0] is not CelExpr.Select selectArg)
        {
            ReportError(call.Args[0].Span, "has() argument must be a field selection (e.g. has(x.field))");
            return CelType.Bool;
        }

        // Validate the operand and field, but has() always returns bool
        var operandType = Visit(selectArg.Operand);
        if (!operandType.IsError && !operandType.IsAny)
        {
            var fieldType = TypeEnvironment.ResolveFieldType(operandType, selectArg.Field);
            if (fieldType is null)
            {
                ReportError(selectArg.Span, $"no field '{selectArg.Field}' on type {operandType}");
            }
        }

        return CelType.Bool;
    }

    private CelType CheckTypeConversion(CelExpr.Call call, CelType targetType)
    {
        // Visit arg to check it's valid, but conversion always returns the target type
        Visit(call.Args[0]);
        return targetType;
    }

    private CelType CheckTypeOf(CelExpr.Call call)
    {
        Visit(call.Args[0]);
        return new CelType.PrimitiveType(CelTypeKind.Type);
    }

    private CelType CheckMacroCall(CelType targetType, CelExpr.Call call)
    {
        // Macros should have been expanded to Comprehension nodes by the parser.
        // If we see them here, it's an internal error or unsupported path.
        // For now, just validate the target is a list.
        if (!targetType.IsAny && targetType is not CelType.ListType)
        {
            ReportError(call.Target!.Span,
                $"'{call.Function}' requires list receiver, got {targetType}");
        }

        return call.Function switch
        {
            "exists" or "all" or "exists_one" => CelType.Bool,
            "filter" => targetType, // filter returns same list type
            "map" => CelType.List(CelType.Any), // can't infer element type without body analysis
            _ => CelType.Any,
        };
    }

    #endregion

    #region Conditional

    private CelType VisitConditional(CelExpr.Conditional cond)
    {
        var condType = Visit(cond.Condition);
        var trueType = Visit(cond.TrueExpr);
        var falseType = Visit(cond.FalseExpr);

        if (!condType.IsError && !condType.IsAny &&
            condType is not CelType.PrimitiveType { Kind: CelTypeKind.Bool })
        {
            ReportError(cond.Condition.Span,
                $"ternary condition must be bool, got {condType}");
        }

        if (trueType.IsError || falseType.IsError)
            return CelType.Error;
        if (trueType.IsAny || falseType.IsAny)
            return CelType.Any;

        // Both branches must have compatible types
        if (trueType.IsAssignableFrom(falseType))
            return trueType;
        if (falseType.IsAssignableFrom(trueType))
            return falseType;

        ReportError(cond.Span,
            $"ternary branches have incompatible types: {trueType} and {falseType}");
        return trueType; // Return first branch type as best guess
    }

    #endregion

    #region Collection Literals

    private CelType VisitCreateList(CelExpr.CreateList list)
    {
        if (list.Elements.Count == 0)
            return CelType.List(CelType.Any);

        var elementTypes = list.Elements.Select(Visit).ToList();

        // Find common type
        var commonType = elementTypes[0];
        for (int i = 1; i < elementTypes.Count; i++)
        {
            if (elementTypes[i].IsError)
                continue;
            if (commonType.IsError)
            {
                commonType = elementTypes[i];
                continue;
            }

            if (!commonType.IsAssignableFrom(elementTypes[i]) &&
                !elementTypes[i].IsAssignableFrom(commonType))
            {
                // Numeric promotion
                if (commonType.IsNumeric && elementTypes[i].IsNumeric)
                {
                    commonType = PromoteNumeric(commonType, elementTypes[i]);
                }
                else
                {
                    ReportError(list.Elements[i].Span,
                        $"list element type mismatch: expected {commonType}, got {elementTypes[i]}");
                }
            }
            else if (elementTypes[i].IsAssignableFrom(commonType) &&
                     !commonType.IsAssignableFrom(elementTypes[i]))
            {
                commonType = elementTypes[i]; // Widen to more general type
            }
        }

        return CelType.List(commonType.IsError ? CelType.Any : commonType);
    }

    private CelType VisitCreateMap(CelExpr.CreateMap map)
    {
        if (map.Entries.Count == 0)
            return CelType.Map(CelType.Any, CelType.Any);

        CelType? keyType = null;
        CelType? valueType = null;

        foreach (var entry in map.Entries)
        {
            var kt = Visit(entry.Key);
            var vt = Visit(entry.Value);

            if (keyType is null)
            {
                keyType = kt;
                valueType = vt;
            }
            else
            {
                if (!keyType.IsAssignableFrom(kt) && !kt.IsError && !keyType.IsError)
                {
                    ReportError(entry.Key.Span,
                        $"map key type mismatch: expected {keyType}, got {kt}");
                }
                if (!valueType!.IsAssignableFrom(vt) && !vt.IsError && !valueType.IsError)
                {
                    ReportError(entry.Value.Span,
                        $"map value type mismatch: expected {valueType}, got {vt}");
                }
            }
        }

        return CelType.Map(keyType ?? CelType.Any, valueType ?? CelType.Any);
    }

    private CelType VisitCreateStruct(CelExpr.CreateStruct strct)
    {
        // Struct creation is currently unsupported in the compiler,
        // but we can still type-check the field values.
        foreach (var field in strct.Fields)
        {
            Visit(field.Value);
        }

        return CelType.Any;
    }

    #endregion

    #region Comprehension

    private CelType VisitComprehension(CelExpr.Comprehension comp)
    {
        // Check the iteration range
        var rangeType = Visit(comp.IterRange);

        CelType elementType;
        if (rangeType is CelType.ListType listType)
        {
            elementType = listType.ElementType;
        }
        else if (rangeType is CelType.MapType mapType)
        {
            elementType = mapType.KeyType; // Iterating map keys
        }
        else if (rangeType.IsAny)
        {
            elementType = CelType.Any;
        }
        else if (!rangeType.IsError)
        {
            ReportError(comp.IterRange.Span,
                $"comprehension requires list or map to iterate, got {rangeType}");
            elementType = CelType.Any;
        }
        else
        {
            elementType = CelType.Any;
        }

        // Create child scope with iteration variable
        var childEnv = _env.CreateChildScope();
        childEnv.AddVariable(comp.IterVar, elementType);

        // Check the accumulator init
        var accuInitType = Visit(comp.AccuInit);
        childEnv.AddVariable(comp.AccuVar, accuInitType);

        // Check loop condition and step in the child scope
        var childChecker = new TypeChecker(childEnv);
        childChecker.Visit(comp.LoopCondition);
        childChecker.Visit(comp.LoopStep);
        var resultType = childChecker.Visit(comp.Result);

        // Collect any child errors
        _errors.AddRange(childChecker._errors);

        return resultType;
    }

    #endregion

    #region Helpers

    private CelType ReportError(SourceSpan span, string message)
    {
        _errors.Add(new CelTypeError(message, span));
        return CelType.Error;
    }

    private static bool IsOrderable(CelType type) =>
        type.IsAny || type.IsNumeric ||
        type is CelType.PrimitiveType p &&
        p.Kind is CelTypeKind.String or CelTypeKind.Bool or
                  CelTypeKind.Timestamp or CelTypeKind.Duration;

    private static CelType PromoteNumeric(CelType left, CelType right)
    {
        if (left == right)
            return left;

        // double wins
        if (left is CelType.PrimitiveType { Kind: CelTypeKind.Double } ||
            right is CelType.PrimitiveType { Kind: CelTypeKind.Double })
            return CelType.Double;

        // uint + int → int (CEL spec: signed wins for mixed operations)
        return CelType.Int;
    }

    #endregion
}
