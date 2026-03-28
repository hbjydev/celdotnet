using System.Reflection;
using System.Text.RegularExpressions;

namespace CelDotNet.Compiler;

/// <summary>
/// Runtime helper methods and cached MethodInfo references for CEL built-in functions.
/// The expression compiler emits calls to these when translating CEL function invocations.
/// </summary>
internal static class CelFunctions
{
    // --- string methods ---

    public static readonly MethodInfo StringContains =
        typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;

    public static readonly MethodInfo StringStartsWith =
        typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!;

    public static readonly MethodInfo StringEndsWith =
        typeof(string).GetMethod(nameof(string.EndsWith), [typeof(string)])!;

    public static readonly MethodInfo StringLength =
        typeof(string).GetProperty(nameof(string.Length))!.GetGetMethod()!;

    // --- string comparison (ordinal) ---

    public static readonly MethodInfo StringCompareOrdinal =
        typeof(string).GetMethod(nameof(string.Compare),
            [typeof(string), typeof(string), typeof(StringComparison)])!
        is null
            ? typeof(CelFunctions).GetMethod(nameof(CompareStringsOrdinal), BindingFlags.Public | BindingFlags.Static)!
            : typeof(CelFunctions).GetMethod(nameof(CompareStringsOrdinal), BindingFlags.Public | BindingFlags.Static)!;

    public static int CompareStringsOrdinal(string a, string b) =>
        string.Compare(a, b, StringComparison.Ordinal);

    // --- bytes helpers ---

    public static int CompareBytes(byte[] a, byte[] b)
    {
        var minLen = Math.Min(a.Length, b.Length);
        for (int i = 0; i < minLen; i++)
        {
            var cmp = a[i].CompareTo(b[i]);
            if (cmp != 0) return cmp;
        }
        return a.Length.CompareTo(b.Length);
    }

    public static readonly MethodInfo CompareBytesMethod =
        typeof(CelFunctions).GetMethod(nameof(CompareBytes), BindingFlags.Public | BindingFlags.Static)!;

    public static byte[] ConcatBytes(byte[] a, byte[] b)
    {
        var result = new byte[a.Length + b.Length];
        a.CopyTo(result, 0);
        b.CopyTo(result, a.Length);
        return result;
    }

    public static readonly MethodInfo ConcatBytesMethod =
        typeof(CelFunctions).GetMethod(nameof(ConcatBytes), BindingFlags.Public | BindingFlags.Static)!;

    public static bool BytesEqual(byte[] a, byte[] b) =>
        a.AsSpan().SequenceEqual(b.AsSpan());

    public static readonly MethodInfo BytesEqualMethod =
        typeof(CelFunctions).GetMethod(nameof(BytesEqual), BindingFlags.Public | BindingFlags.Static)!;

    // --- array helpers ---

    public static T[] ConcatArrays<T>(T[] a, T[] b)
    {
        var result = new T[a.Length + b.Length];
        a.CopyTo(result, 0);
        b.CopyTo(result, a.Length);
        return result;
    }

    public static readonly MethodInfo ConcatArraysMethod =
        typeof(CelFunctions).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == nameof(ConcatArrays) && m.IsGenericMethod);

    /// <summary>
    /// Converts an array (possibly object[]) to a typed array T[].
    /// Used when concatenating empty list (object[]) with typed list.
    /// </summary>
    public static T[] ConvertArray<T>(object source)
    {
        if (source is T[] typed) return typed;
        if (source is Array arr)
        {
            var result = new T[arr.Length];
            for (int i = 0; i < arr.Length; i++)
            {
                var elem = arr.GetValue(i);
                result[i] = elem is null ? default! : (T)Convert.ChangeType(elem, typeof(T));
            }
            return result;
        }
        throw new CelException($"cannot convert {source.GetType().Name} to {typeof(T).Name}[]");
    }

    public static readonly MethodInfo ConvertArrayMethod =
        typeof(CelFunctions).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == nameof(ConvertArray) && m.IsGenericMethod);

    /// <summary>
    /// Structural equality for arrays/lists. Handles nested arrays, boxed value types,
    /// and cross-type numeric comparison.
    /// </summary>
    public static bool ArraysEqual(object a, object b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is not Array arrA || b is not Array arrB) return false;
        if (arrA.Length != arrB.Length) return false;

        for (int i = 0; i < arrA.Length; i++)
        {
            var elemA = arrA.GetValue(i);
            var elemB = arrB.GetValue(i);

            if (!CelValuesEqual(elemA, elemB)) return false;
        }
        return true;
    }

    public static readonly MethodInfo ArraysEqualMethod =
        typeof(CelFunctions).GetMethod(nameof(ArraysEqual), BindingFlags.Public | BindingFlags.Static)!;

    // --- map equality ---

    /// <summary>
    /// Structural equality for dictionaries. Handles cross-type numeric comparison.
    /// </summary>
    public static bool MapsEqual(object a, object b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is not System.Collections.IDictionary dictA || b is not System.Collections.IDictionary dictB) return false;
        if (dictA.Count != dictB.Count) return false;

        foreach (System.Collections.DictionaryEntry entry in dictA)
        {
            bool found = false;
            foreach (System.Collections.DictionaryEntry other in dictB)
            {
                if (CelValuesEqual(entry.Key, other.Key))
                {
                    if (!CelValuesEqual(entry.Value, other.Value)) return false;
                    found = true;
                    break;
                }
            }
            if (!found) return false;
        }
        return true;
    }

    public static readonly MethodInfo MapsEqualMethod =
        typeof(CelFunctions).GetMethod(nameof(MapsEqual), BindingFlags.Public | BindingFlags.Static)!;

    // --- dynamic comparison/equality for object types ---

    /// <summary>
    /// CEL-aware equality that handles cross-type numeric comparison.
    /// </summary>
    public static bool CelValuesEqual(object? a, object? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return a is null && b is null;

        // Cross-type numeric comparison
        if (IsNumericValue(a) && IsNumericValue(b))
        {
            return ToDouble(a) == ToDouble(b);
        }

        // Array structural equality
        if (a is Array && b is Array)
            return ArraysEqual(a, b);

        // Dictionary structural equality
        if (a is System.Collections.IDictionary && b is System.Collections.IDictionary)
            return MapsEqual(a, b);

        return a.Equals(b);
    }

    public static bool DynamicEqual(object? a, object? b) => CelValuesEqual(a, b);

    public static readonly MethodInfo DynamicEqualMethod =
        typeof(CelFunctions).GetMethod(nameof(DynamicEqual), BindingFlags.Public | BindingFlags.Static)!;

    public static int DynamicCompare(object a, object b)
    {
        if (IsNumericValue(a) && IsNumericValue(b))
            return ToDouble(a).CompareTo(ToDouble(b));
        if (a is string sa && b is string sb)
            return string.Compare(sa, sb, StringComparison.Ordinal);
        if (a is bool ba && b is bool bb)
            return ba.CompareTo(bb);
        throw new CelException($"cannot compare {a.GetType().Name} and {b.GetType().Name}");
    }

    public static readonly MethodInfo DynamicCompareMethod =
        typeof(CelFunctions).GetMethod(nameof(DynamicCompare), BindingFlags.Public | BindingFlags.Static)!;

    public static object DynamicArithmetic(object a, object b, string op)
    {
        if (!IsNumericValue(a) || !IsNumericValue(b))
            throw new CelException($"cannot perform {op} on {a.GetType().Name} and {b.GetType().Name}");

        // Try to keep integer types if both sides are integers
        if (a is long la && b is long lb)
        {
            return op switch
            {
                "Add" => la + lb,
                "Subtract" => la - lb,
                "Multiply" => la * lb,
                "Divide" => lb == 0 ? throw new CelException("divide by zero") : la / lb,
                "Modulo" => lb == 0 ? throw new CelException("modulo by zero") : la % lb,
                _ => throw new CelException($"unsupported arithmetic: {op}"),
            };
        }

        var da = ToDouble(a);
        var db = ToDouble(b);
        return op switch
        {
            "Add" => da + db,
            "Subtract" => da - db,
            "Multiply" => da * db,
            "Divide" => db == 0.0 ? throw new CelException("divide by zero") : da / db,
            "Modulo" => db == 0.0 ? throw new CelException("modulo by zero") : da % db,
            _ => throw new CelException($"unsupported arithmetic: {op}"),
        };
    }

    public static readonly MethodInfo DynamicArithmeticMethod =
        typeof(CelFunctions).GetMethod(nameof(DynamicArithmetic), BindingFlags.Public | BindingFlags.Static)!;

    private static bool IsNumericValue(object? v) =>
        v is int or long or ulong or double or float or short or byte or decimal;

    private static double ToDouble(object v) => v switch
    {
        int i => i,
        long l => l,
        ulong u => u,
        double d => d,
        float f => f,
        decimal dec => (double)dec,
        _ => Convert.ToDouble(v),
    };

    // --- logical operators with non-bool short-circuit ---

    /// <summary>
    /// CEL logical AND: if either operand is false (bool), result is false.
    /// Non-bool operands are errors, but short-circuit can still produce a result.
    /// </summary>
    public static object LogicalAnd(object left, object right)
    {
        // If left is false, short-circuit
        if (left is bool lb)
        {
            if (!lb) return false;
            // left is true, check right
            if (right is bool rb) return rb;
            throw new CelException("no such overload: _&&_ applied to non-bool");
        }
        // Left is not bool — check if right is false (short-circuit)
        if (right is bool rb2 && !rb2) return false;
        throw new CelException("no such overload: _&&_ applied to non-bool");
    }

    public static readonly MethodInfo LogicalAndMethod =
        typeof(CelFunctions).GetMethod(nameof(LogicalAnd), BindingFlags.Public | BindingFlags.Static,
            [typeof(object), typeof(object)])!;

    /// <summary>
    /// Lazy CEL logical AND with commutative error handling.
    /// Evaluates both sides, catching errors, and returns false if either side is false.
    /// </summary>
    public static object LogicalAndLazy(Func<object> left, Func<object> right)
    {
        object? leftVal = null;
        Exception? leftErr = null;
        object? rightVal = null;
        Exception? rightErr = null;

        try { leftVal = left(); }
        catch (Exception ex) { leftErr = ex; }

        try { rightVal = right(); }
        catch (Exception ex) { rightErr = ex; }

        // If either side is false, result is false
        if (leftVal is false) return false;
        if (rightVal is false) return false;

        // If both succeeded and are bools
        if (leftErr is null && rightErr is null)
        {
            if (leftVal is bool lb && rightVal is bool rb)
                return lb && rb;
            throw new CelException("no such overload: _&&_ applied to non-bool");
        }

        // At least one error, and the other isn't false — propagate
        throw leftErr ?? rightErr!;
    }

    public static readonly MethodInfo LogicalAndLazyMethod =
        typeof(CelFunctions).GetMethod(nameof(LogicalAndLazy), BindingFlags.Public | BindingFlags.Static)!;

    /// <summary>
    /// CEL logical OR: if either operand is true (bool), result is true.
    /// Non-bool operands are errors, but short-circuit can still produce a result.
    /// </summary>
    public static object LogicalOr(object left, object right)
    {
        // If left is true, short-circuit
        if (left is bool lb)
        {
            if (lb) return true;
            // left is false, check right
            if (right is bool rb) return rb;
            throw new CelException("no such overload: _||_ applied to non-bool");
        }
        // Left is not bool — check if right is true (short-circuit)
        if (right is bool rb2 && rb2) return true;
        throw new CelException("no such overload: _||_ applied to non-bool");
    }

    public static readonly MethodInfo LogicalOrMethod =
        typeof(CelFunctions).GetMethod(nameof(LogicalOr), BindingFlags.Public | BindingFlags.Static,
            [typeof(object), typeof(object)])!;

    /// <summary>
    /// Lazy CEL logical OR with commutative error handling.
    /// Evaluates both sides, catching errors, and returns true if either side is true.
    /// </summary>
    public static object LogicalOrLazy(Func<object> left, Func<object> right)
    {
        object? leftVal = null;
        Exception? leftErr = null;
        object? rightVal = null;
        Exception? rightErr = null;

        try { leftVal = left(); }
        catch (Exception ex) { leftErr = ex; }

        try { rightVal = right(); }
        catch (Exception ex) { rightErr = ex; }

        // If either side is true, result is true
        if (leftVal is true) return true;
        if (rightVal is true) return true;

        // If both succeeded and are bools
        if (leftErr is null && rightErr is null)
        {
            if (leftVal is bool lb && rightVal is bool rb)
                return lb || rb;
            throw new CelException("no such overload: _||_ applied to non-bool");
        }

        // At least one error, and the other isn't true — propagate
        throw leftErr ?? rightErr!;
    }

    public static readonly MethodInfo LogicalOrLazyMethod =
        typeof(CelFunctions).GetMethod(nameof(LogicalOrLazy), BindingFlags.Public | BindingFlags.Static)!;

    // --- matches() → Regex.IsMatch ---

    public static readonly MethodInfo RegexIsMatch =
        typeof(Regex).GetMethod(nameof(Regex.IsMatch), BindingFlags.Public | BindingFlags.Static,
            [typeof(string), typeof(string)])!;

    // --- size() helpers ---

    /// <summary>
    /// Returns the size of a string, list, map, or bytes value.
    /// Used as a runtime fallback for size() calls.
    /// </summary>
    public static long Size(object? value) => value switch
    {
        string s => s.Length,
        byte[] b => b.Length,
        System.Collections.ICollection c => c.Count,
        System.Collections.IEnumerable e => e.Cast<object>().LongCount(),
        null => throw new CelException("size() called on null"),
        _ => throw new CelException($"size() not supported for type {value.GetType().Name}"),
    };

    public static readonly MethodInfo SizeMethod =
        typeof(CelFunctions).GetMethod(nameof(Size), BindingFlags.Public | BindingFlags.Static)!;

    // --- contains() for "in" operator on lists ---

    /// <summary>
    /// Generic Contains method — used to translate the CEL "in" operator against lists.
    /// </summary>
    public static bool ListContains<T>(System.Collections.Generic.IEnumerable<T> source, T value) =>
        source.Contains(value);

    public static readonly MethodInfo ListContainsGeneric =
        typeof(CelFunctions).GetMethod(nameof(ListContains), BindingFlags.Public | BindingFlags.Static)!;

    // --- Enumerable.Contains for in-operator fallback ---

    public static readonly MethodInfo EnumerableContains =
        typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2);

    // --- Enumerable methods for comprehension macros ---

    public static readonly MethodInfo EnumerableAll =
        typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == nameof(Enumerable.All) && m.GetParameters().Length == 2);

    public static readonly MethodInfo EnumerableAny =
        typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == nameof(Enumerable.Any) && m.GetParameters().Length == 2);

    /// <summary>
    /// CEL-aware All: returns false if any element is false, even if other elements throw.
    /// This implements CEL's commutative error handling for quantifiers.
    /// </summary>
    public static bool CelAll<T>(IEnumerable<T> source, Func<T, bool> predicate)
    {
        Exception? firstError = null;
        foreach (var item in source)
        {
            try
            {
                if (!predicate(item)) return false;
            }
            catch (Exception ex)
            {
                firstError ??= ex;
            }
        }
        if (firstError is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(firstError).Throw();
        return true;
    }

    public static readonly MethodInfo CelAllMethod =
        typeof(CelFunctions).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == nameof(CelAll));

    /// <summary>
    /// CEL-aware Any: returns true if any element is true, even if other elements throw.
    /// </summary>
    public static bool CelAny<T>(IEnumerable<T> source, Func<T, bool> predicate)
    {
        Exception? firstError = null;
        foreach (var item in source)
        {
            try
            {
                if (predicate(item)) return true;
            }
            catch (Exception ex)
            {
                firstError ??= ex;
            }
        }
        if (firstError is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(firstError).Throw();
        return false;
    }

    public static readonly MethodInfo CelAnyMethod =
        typeof(CelFunctions).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == nameof(CelAny));

    public static readonly MethodInfo EnumerableCount =
        typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == nameof(Enumerable.Count) && m.GetParameters().Length == 2);

    public static readonly MethodInfo EnumerableWhere =
        typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == nameof(Enumerable.Where) && m.GetParameters().Length == 2
                && m.GetParameters()[1].ParameterType.GetGenericArguments().Length == 2);

    public static readonly MethodInfo EnumerableSelect =
        typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == nameof(Enumerable.Select) && m.GetParameters().Length == 2
                && m.GetParameters()[1].ParameterType.GetGenericArguments().Length == 2);

    public static readonly MethodInfo EnumerableToArray =
        typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == nameof(Enumerable.ToArray) && m.GetParameters().Length == 1);

    // --- Type conversion runtime helpers ---

    /// <summary>
    /// CEL bool(string) conversion. Accepts exactly: "true", "false", "1", "0", "t", "f".
    /// Accepts: "true", "TRUE", "True", "1", "t" → true;
    ///          "false", "FALSE", "False", "0", "f" → false.
    /// Mixed-case like "TrUe" or "FaLsE" is an error.
    /// </summary>
    public static bool CelParseBool(string value) => value switch
    {
        "true" or "TRUE" or "True" or "1" or "t" => true,
        "false" or "FALSE" or "False" or "0" or "f" => false,
        _ => throw new CelException($"Type conversion error from 'string' to 'bool': bad value '{value}'"),
    };

    public static readonly MethodInfo CelParseBoolMethod =
        typeof(CelFunctions).GetMethod(nameof(CelParseBool), BindingFlags.Public | BindingFlags.Static)!;

    /// <summary>
    /// bytes(string) → UTF-8 encode.
    /// </summary>
    public static byte[] StringToBytes(string value) =>
        System.Text.Encoding.UTF8.GetBytes(value);

    public static readonly MethodInfo StringToBytesMethod =
        typeof(CelFunctions).GetMethod(nameof(StringToBytes), BindingFlags.Public | BindingFlags.Static)!;

    /// <summary>
    /// string(bytes) → UTF-8 decode. Invalid UTF-8 → eval_error.
    /// </summary>
    public static string BytesToString(byte[] value)
    {
        try
        {
            var encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            return encoding.GetString(value);
        }
        catch (System.Text.DecoderFallbackException)
        {
            throw new CelException("invalid UTF-8 in bytes-to-string conversion");
        }
    }

    public static readonly MethodInfo BytesToStringMethod =
        typeof(CelFunctions).GetMethod(nameof(BytesToString), BindingFlags.Public | BindingFlags.Static)!;

    /// <summary>
    /// int(double) with range checking. CEL requires overflow → eval_error.
    /// </summary>
    public static long DoubleToInt64(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new CelException("range error converting double to int");
        // (double)long.MaxValue rounds up to 2^63; (double)long.MinValue is exactly -2^63.
        // CEL spec rejects boundary values where double precision cannot distinguish the exact int.
        if (value >= (double)long.MaxValue || value <= (double)long.MinValue)
            throw new CelException("range error converting double to int");
        return (long)value;
    }

    public static readonly MethodInfo DoubleToInt64Method =
        typeof(CelFunctions).GetMethod(nameof(DoubleToInt64), BindingFlags.Public | BindingFlags.Static)!;

    /// <summary>
    /// uint(double) with range checking.
    /// </summary>
    public static ulong DoubleToUint64(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new CelException("range error converting double to uint");
        if (value > (double)ulong.MaxValue || value < 0.0)
            throw new CelException("range error converting double to uint");
        return (ulong)value;
    }

    public static readonly MethodInfo DoubleToUint64Method =
        typeof(CelFunctions).GetMethod(nameof(DoubleToUint64), BindingFlags.Public | BindingFlags.Static)!;

    /// <summary>
    /// int(uint) with range checking — ulong to long.
    /// </summary>
    public static long Uint64ToInt64(ulong value)
    {
        if (value > (ulong)long.MaxValue)
            throw new CelException("range error converting uint to int");
        return (long)value;
    }

    public static readonly MethodInfo Uint64ToInt64Method =
        typeof(CelFunctions).GetMethod(nameof(Uint64ToInt64), BindingFlags.Public | BindingFlags.Static)!;

    /// <summary>
    /// uint(int) with range checking — long to ulong.
    /// </summary>
    public static ulong Int64ToUint64(long value)
    {
        if (value < 0)
            throw new CelException("range error converting int to uint");
        return (ulong)value;
    }

    public static readonly MethodInfo Int64ToUint64Method =
        typeof(CelFunctions).GetMethod(nameof(Int64ToUint64), BindingFlags.Public | BindingFlags.Static)!;

    /// <summary>
    /// string(int) — CEL uses no formatting, just plain decimal.
    /// </summary>
    public static string Int64ToString(long value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static readonly MethodInfo Int64ToStringMethod =
        typeof(CelFunctions).GetMethod(nameof(Int64ToString), BindingFlags.Public | BindingFlags.Static)!;

    /// <summary>
    /// string(uint) — CEL uses no formatting, just plain decimal.
    /// </summary>
    public static string Uint64ToString(ulong value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static readonly MethodInfo Uint64ToStringMethod =
        typeof(CelFunctions).GetMethod(nameof(Uint64ToString), BindingFlags.Public | BindingFlags.Static)!;

    /// <summary>
    /// string(double) — CEL formatting.
    /// </summary>
    public static string DoubleToString(double value)
    {
        if (double.IsPositiveInfinity(value)) return "Infinity";
        if (double.IsNegativeInfinity(value)) return "-Infinity";
        if (double.IsNaN(value)) return "NaN";
        return value.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
    }

    public static readonly MethodInfo DoubleToStringMethod =
        typeof(CelFunctions).GetMethod(nameof(DoubleToString), BindingFlags.Public | BindingFlags.Static)!;

    /// <summary>
    /// string(bool) — CEL uses "true"/"false".
    /// </summary>
    public static string BoolToString(bool value) => value ? "true" : "false";

    public static readonly MethodInfo BoolToStringMethod =
        typeof(CelFunctions).GetMethod(nameof(BoolToString), BindingFlags.Public | BindingFlags.Static)!;

    /// <summary>
    /// string(timestamp) — RFC3339 format with Z for UTC and fractional seconds when present.
    /// </summary>
    public static string TimestampToString(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        // Determine sub-second precision
        long ticks = utc.Ticks % TimeSpan.TicksPerSecond; // ticks within the current second
        string fractional;
        if (ticks == 0)
            fractional = "";
        else
        {
            // Format as up to 9 digits, trimming trailing zeros
            // .NET ticks are 100ns units, so we have 7 digits max. Pad to 9 for nano display.
            var nanos = ticks * 100; // convert 100ns ticks to nanoseconds
            var frac = nanos.ToString("D9");
            fractional = "." + frac.TrimEnd('0');
        }
        return utc.ToString($"yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)
            + fractional + "Z";
    }

    public static readonly MethodInfo TimestampToStringMethod =
        typeof(CelFunctions).GetMethod(nameof(TimestampToString), BindingFlags.Public | BindingFlags.Static)!;

    /// <summary>
    /// string(duration) — CEL duration format: total seconds with "s" suffix.
    /// </summary>
    public static string DurationToString(TimeSpan value)
    {
        var totalSeconds = (long)value.TotalSeconds;
        var fractionalTicks = Math.Abs(value.Ticks % TimeSpan.TicksPerSecond);
        if (fractionalTicks == 0)
            return $"{totalSeconds}s";
        // Format fractional part as nanoseconds, trimming trailing zeros
        var nanos = fractionalTicks * 100; // 100ns ticks → nanoseconds
        var frac = nanos.ToString("D9").TrimEnd('0');
        return $"{totalSeconds}.{frac}s";
    }

    public static readonly MethodInfo DurationToStringMethod =
        typeof(CelFunctions).GetMethod(nameof(DurationToString), BindingFlags.Public | BindingFlags.Static)!;

    /// <summary>
    /// timestamp(int) — epoch seconds to DateTimeOffset.
    /// </summary>
    public static DateTimeOffset TimestampFromEpoch(long epochSeconds) =>
        DateTimeOffset.FromUnixTimeSeconds(epochSeconds);

    public static readonly MethodInfo TimestampFromEpochMethod =
        typeof(CelFunctions).GetMethod(nameof(TimestampFromEpoch), BindingFlags.Public | BindingFlags.Static)!;

    /// <summary>Parses a timestamp string to DateTimeOffset. Used by timestamp() function.
    /// Handles nanosecond-precision fractional seconds (>7 digits) by truncating to 7 for .NET.</summary>
    public static DateTimeOffset ParseTimestamp(string value)
    {
        // CEL timestamps can have up to 9 fractional second digits (nanoseconds).
        // .NET DateTimeOffset.Parse only handles up to 7. Truncate excess digits.
        var normalized = TruncateFractionalSeconds(value, 7);
        return DateTimeOffset.Parse(normalized, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);
    }

    /// <summary>Truncates fractional seconds in an RFC3339 timestamp string to at most maxDigits.</summary>
    private static string TruncateFractionalSeconds(string value, int maxDigits)
    {
        // Find the '.' that starts fractional seconds (after time part, before timezone)
        int dotIndex = -1;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '.' && i > 0 && char.IsDigit(value[i - 1]))
            {
                dotIndex = i;
                break;
            }
        }
        if (dotIndex < 0) return value; // no fractional seconds

        // Count digits after the dot
        int fracStart = dotIndex + 1;
        int fracEnd = fracStart;
        while (fracEnd < value.Length && char.IsDigit(value[fracEnd]))
            fracEnd++;

        int fracDigits = fracEnd - fracStart;
        if (fracDigits <= maxDigits) return value; // already within bounds

        // Truncate: keep only maxDigits of fractional part
        return string.Concat(value.AsSpan(0, fracStart + maxDigits), value.AsSpan(fracEnd));
    }

    public static readonly MethodInfo ParseTimestampMethod =
        typeof(CelFunctions).GetMethod(nameof(ParseTimestamp), BindingFlags.Public | BindingFlags.Static)!;

    /// <summary>Parses a duration string (e.g. "3600s", "1.5h") to TimeSpan. Used by duration() function.</summary>
    public static TimeSpan ParseDuration(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new CelException("invalid duration: empty string");

        // CEL duration format: optional negative sign, then number followed by unit suffix
        // Supported: s (seconds), ms (milliseconds), us (microseconds), ns (nanoseconds),
        //            m (minutes), h (hours)
        var span = value.AsSpan().Trim();

        bool negative = false;
        if (span.Length > 0 && span[0] == '-')
        {
            negative = true;
            span = span[1..];
        }

        // Find where the digits end and the unit starts
        int unitStart = 0;
        for (int i = 0; i < span.Length; i++)
        {
            if (!char.IsDigit(span[i]) && span[i] != '.')
            {
                unitStart = i;
                break;
            }
        }

        if (unitStart == 0)
            throw new CelException($"invalid duration format: '{value}'");

        var numberPart = span[..unitStart];
        var unitPart = span[unitStart..];

        if (!double.TryParse(numberPart, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var number))
            throw new CelException($"invalid duration number: '{numberPart.ToString()}'");

        if (negative) number = -number;

        TimeSpan result;
        if (unitPart is "ns")
        {
            // Nanoseconds: use integer arithmetic to avoid float precision loss.
            // .NET TimeSpan has 100ns resolution (ticks).
            long nanos = (long)number;
            long ticks = nanos / 100;
            result = TimeSpan.FromTicks(ticks);
        }
        else
        {
            result = unitPart switch
            {
                "h" => TimeSpan.FromHours(number),
                "m" => TimeSpan.FromMinutes(number),
                "s" => TimeSpan.FromSeconds(number),
                "ms" => TimeSpan.FromMilliseconds(number),
                "us" => TimeSpan.FromMicroseconds(number),
                _ => throw new CelException($"unknown duration unit: '{unitPart.ToString()}'"),
            };
        }

        // CEL limits durations to ±315,576,000,000 seconds (~10,000 years)
        ValidateDurationRange(result);
        return result;
    }

    /// <summary>Maximum CEL duration: ±315,576,000,000 seconds.</summary>
    private const long MaxDurationSeconds = 315_576_000_000L;

    /// <summary>
    /// Validates that a duration is within CEL's allowed range.
    /// </summary>
    public static void ValidateDurationRange(TimeSpan duration)
    {
        long totalSeconds = (long)duration.TotalSeconds;
        if (totalSeconds > MaxDurationSeconds || totalSeconds < -MaxDurationSeconds)
            throw new CelException("duration out of range");
    }

    public static readonly MethodInfo ValidateDurationRangeMethod =
        typeof(CelFunctions).GetMethod(nameof(ValidateDurationRange), BindingFlags.Public | BindingFlags.Static)!;

    // --- Checked timestamp/duration arithmetic ---

    /// <summary>Timestamp + Duration with range validation.</summary>
    public static DateTimeOffset TimestampAddDuration(DateTimeOffset ts, TimeSpan dur)
    {
        try
        {
            var result = ts.Add(dur);
            return result;
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new CelException("timestamp overflow");
        }
    }

    public static readonly MethodInfo TimestampAddDurationMethod =
        typeof(CelFunctions).GetMethod(nameof(TimestampAddDuration), BindingFlags.Public | BindingFlags.Static)!;

    /// <summary>Timestamp - Duration with range validation.</summary>
    public static DateTimeOffset TimestampSubtractDuration(DateTimeOffset ts, TimeSpan dur)
    {
        try
        {
            return ts.Subtract(dur);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new CelException("timestamp overflow");
        }
    }

    public static readonly MethodInfo TimestampSubtractDurationMethod =
        typeof(CelFunctions).GetMethod(nameof(TimestampSubtractDuration), BindingFlags.Public | BindingFlags.Static)!;

    /// <summary>Timestamp - Timestamp with duration range validation.</summary>
    /// <remarks>
    /// CEL's reference implementation (cel-go) stores durations as int64 nanoseconds.
    /// Timestamp subtraction that exceeds the int64 nanosecond range (~292 years) overflows
    /// in cel-go and the cel-spec tests expect an error. We replicate this check here even
    /// though .NET's TimeSpan could represent the value.
    /// </remarks>
    public static TimeSpan TimestampSubtractTimestamp(DateTimeOffset a, DateTimeOffset b)
    {
        var result = a - b;
        // Check if result would overflow int64 nanoseconds (cel-go's representation)
        // MaxNanosecondSeconds = long.MaxValue / 1_000_000_000 ≈ 9,223,372,036 seconds
        const long NanosPerSecond = 1_000_000_000L;
        long totalSeconds = (long)result.TotalSeconds;
        if (totalSeconds > long.MaxValue / NanosPerSecond || totalSeconds < long.MinValue / NanosPerSecond)
            throw new CelException("duration out of range");
        ValidateDurationRange(result);
        return result;
    }

    public static readonly MethodInfo TimestampSubtractTimestampMethod =
        typeof(CelFunctions).GetMethod(nameof(TimestampSubtractTimestamp), BindingFlags.Public | BindingFlags.Static)!;

    /// <summary>Duration + Duration with range validation.</summary>
    public static TimeSpan DurationAddDuration(TimeSpan a, TimeSpan b)
    {
        var result = a + b;
        ValidateDurationRange(result);
        return result;
    }

    public static readonly MethodInfo DurationAddDurationMethod =
        typeof(CelFunctions).GetMethod(nameof(DurationAddDuration), BindingFlags.Public | BindingFlags.Static)!;

    /// <summary>Duration - Duration with range validation.</summary>
    public static TimeSpan DurationSubtractDuration(TimeSpan a, TimeSpan b)
    {
        var result = a - b;
        ValidateDurationRange(result);
        return result;
    }

    public static readonly MethodInfo DurationSubtractDurationMethod =
        typeof(CelFunctions).GetMethod(nameof(DurationSubtractDuration), BindingFlags.Public | BindingFlags.Static)!;

    public static readonly MethodInfo ParseDurationMethod =
        typeof(CelFunctions).GetMethod(nameof(ParseDuration), BindingFlags.Public | BindingFlags.Static)!;

    // --- Timestamp member access helpers ---

    public static int GetFullYear(DateTimeOffset ts) => ts.Year;
    public static int GetMonth(DateTimeOffset ts) => ts.Month - 1; // CEL months are 0-based
    public static int GetDayOfMonth(DateTimeOffset ts) => ts.Day - 1; // CEL days are 0-based
    public static int GetDate(DateTimeOffset ts) => ts.Day; // CEL getDate() is 1-based day of month
    public static int GetDayOfWeek(DateTimeOffset ts) => (int)ts.DayOfWeek;
    public static int GetDayOfYear(DateTimeOffset ts) => ts.DayOfYear - 1; // CEL is 0-based
    public static int GetHours(DateTimeOffset ts) => ts.Hour;
    public static int GetMinutes(DateTimeOffset ts) => ts.Minute;
    public static int GetSeconds(DateTimeOffset ts) => ts.Second;
    public static int GetMilliseconds(DateTimeOffset ts) => ts.Millisecond;

    // --- Timezone-aware timestamp helpers ---

    /// <summary>
    /// Applies a timezone to a DateTimeOffset. Handles both IANA names and fixed UTC offsets.
    /// </summary>
    public static DateTimeOffset ApplyTimezone(DateTimeOffset ts, string tz)
    {
        // Try fixed numeric offset: "+HH:MM", "-HH:MM", "HH:MM" (implicit positive)
        if (TryParseFixedOffset(tz, out var offset))
        {
            return ts.ToOffset(offset);
        }

        // Try IANA / system timezone ID
        try
        {
            var tzInfo = TimeZoneInfo.FindSystemTimeZoneById(tz);
            return TimeZoneInfo.ConvertTime(ts, tzInfo);
        }
        catch (TimeZoneNotFoundException)
        {
            throw new CelException($"unknown timezone: '{tz}'");
        }
    }

    /// <summary>Tries to parse a fixed timezone offset like "+02:00", "-02:30", "02:00".</summary>
    private static bool TryParseFixedOffset(string tz, out TimeSpan offset)
    {
        offset = TimeSpan.Zero;
        if (string.IsNullOrEmpty(tz)) return false;

        bool negative = false;
        var s = tz.AsSpan();

        if (s[0] == '+')
            s = s[1..];
        else if (s[0] == '-')
        {
            negative = true;
            s = s[1..];
        }

        // Must look like HH:MM or H:MM
        if (!s.Contains(':')) return false;

        if (TimeSpan.TryParse(s.ToString(), System.Globalization.CultureInfo.InvariantCulture, out offset))
        {
            if (negative) offset = -offset;
            return true;
        }
        return false;
    }

    public static readonly MethodInfo ApplyTimezoneMethod =
        typeof(CelFunctions).GetMethod(nameof(ApplyTimezone), BindingFlags.Public | BindingFlags.Static)!;

    public static int GetFullYearTz(DateTimeOffset ts, string tz) => ApplyTimezone(ts, tz).Year;
    public static int GetMonthTz(DateTimeOffset ts, string tz) => ApplyTimezone(ts, tz).Month - 1;
    public static int GetDayOfMonthTz(DateTimeOffset ts, string tz) => ApplyTimezone(ts, tz).Day - 1;
    public static int GetDateTz(DateTimeOffset ts, string tz) => ApplyTimezone(ts, tz).Day;
    public static int GetDayOfWeekTz(DateTimeOffset ts, string tz) => (int)ApplyTimezone(ts, tz).DayOfWeek;
    public static int GetDayOfYearTz(DateTimeOffset ts, string tz) => ApplyTimezone(ts, tz).DayOfYear - 1;
    public static int GetHoursTz(DateTimeOffset ts, string tz) => ApplyTimezone(ts, tz).Hour;
    public static int GetMinutesTz(DateTimeOffset ts, string tz) => ApplyTimezone(ts, tz).Minute;
    public static int GetSecondsTz(DateTimeOffset ts, string tz) => ApplyTimezone(ts, tz).Second;
    public static int GetMillisecondsTz(DateTimeOffset ts, string tz) => ApplyTimezone(ts, tz).Millisecond;

    public static readonly MethodInfo TimestampGetFullYearTz =
        typeof(CelFunctions).GetMethod(nameof(GetFullYearTz), BindingFlags.Public | BindingFlags.Static)!;
    public static readonly MethodInfo TimestampGetMonthTz =
        typeof(CelFunctions).GetMethod(nameof(GetMonthTz), BindingFlags.Public | BindingFlags.Static)!;
    public static readonly MethodInfo TimestampGetDayOfMonthTz =
        typeof(CelFunctions).GetMethod(nameof(GetDayOfMonthTz), BindingFlags.Public | BindingFlags.Static)!;
    public static readonly MethodInfo TimestampGetDateTz =
        typeof(CelFunctions).GetMethod(nameof(GetDateTz), BindingFlags.Public | BindingFlags.Static)!;
    public static readonly MethodInfo TimestampGetDayOfWeekTz =
        typeof(CelFunctions).GetMethod(nameof(GetDayOfWeekTz), BindingFlags.Public | BindingFlags.Static)!;
    public static readonly MethodInfo TimestampGetDayOfYearTz =
        typeof(CelFunctions).GetMethod(nameof(GetDayOfYearTz), BindingFlags.Public | BindingFlags.Static)!;
    public static readonly MethodInfo TimestampGetHoursTz =
        typeof(CelFunctions).GetMethod(nameof(GetHoursTz), BindingFlags.Public | BindingFlags.Static)!;
    public static readonly MethodInfo TimestampGetMinutesTz =
        typeof(CelFunctions).GetMethod(nameof(GetMinutesTz), BindingFlags.Public | BindingFlags.Static)!;
    public static readonly MethodInfo TimestampGetSecondsTz =
        typeof(CelFunctions).GetMethod(nameof(GetSecondsTz), BindingFlags.Public | BindingFlags.Static)!;
    public static readonly MethodInfo TimestampGetMillisecondsTz =
        typeof(CelFunctions).GetMethod(nameof(GetMillisecondsTz), BindingFlags.Public | BindingFlags.Static)!;

    public static readonly MethodInfo TimestampGetFullYear =
        typeof(CelFunctions).GetMethod(nameof(GetFullYear), BindingFlags.Public | BindingFlags.Static)!;
    public static readonly MethodInfo TimestampGetMonth =
        typeof(CelFunctions).GetMethod(nameof(GetMonth), BindingFlags.Public | BindingFlags.Static)!;
    public static readonly MethodInfo TimestampGetDayOfMonth =
        typeof(CelFunctions).GetMethod(nameof(GetDayOfMonth), BindingFlags.Public | BindingFlags.Static)!;
    public static readonly MethodInfo TimestampGetDate =
        typeof(CelFunctions).GetMethod(nameof(GetDate), BindingFlags.Public | BindingFlags.Static)!;
    public static readonly MethodInfo TimestampGetDayOfWeek =
        typeof(CelFunctions).GetMethod(nameof(GetDayOfWeek), BindingFlags.Public | BindingFlags.Static)!;
    public static readonly MethodInfo TimestampGetDayOfYear =
        typeof(CelFunctions).GetMethod(nameof(GetDayOfYear), BindingFlags.Public | BindingFlags.Static)!;
    public static readonly MethodInfo TimestampGetHours =
        typeof(CelFunctions).GetMethod(nameof(GetHours), BindingFlags.Public | BindingFlags.Static)!;
    public static readonly MethodInfo TimestampGetMinutes =
        typeof(CelFunctions).GetMethod(nameof(GetMinutes), BindingFlags.Public | BindingFlags.Static)!;
    public static readonly MethodInfo TimestampGetSeconds =
        typeof(CelFunctions).GetMethod(nameof(GetSeconds), BindingFlags.Public | BindingFlags.Static)!;
    public static readonly MethodInfo TimestampGetMilliseconds =
        typeof(CelFunctions).GetMethod(nameof(GetMilliseconds), BindingFlags.Public | BindingFlags.Static)!;

    // --- Duration member access helpers ---

    /// <summary>Duration getHours() — returns TOTAL hours (floor division).</summary>
    public static long DurationGetHours(TimeSpan ts) => (long)ts.TotalSeconds / 3600;
    /// <summary>Duration getMinutes() — returns TOTAL minutes (floor division).</summary>
    public static long DurationGetMinutes(TimeSpan ts) => (long)ts.TotalSeconds / 60;
    /// <summary>Duration getSeconds() — returns TOTAL seconds (floor division).</summary>
    public static long DurationGetSeconds(TimeSpan ts) => (long)ts.TotalSeconds;
    /// <summary>Duration getMilliseconds() — returns TOTAL milliseconds.</summary>
    public static long DurationGetMilliseconds(TimeSpan ts) => (long)ts.TotalMilliseconds;

    public static readonly MethodInfo DurationGetHoursMethod =
        typeof(CelFunctions).GetMethod(nameof(DurationGetHours), BindingFlags.Public | BindingFlags.Static)!;
    public static readonly MethodInfo DurationGetMinutesMethod =
        typeof(CelFunctions).GetMethod(nameof(DurationGetMinutes), BindingFlags.Public | BindingFlags.Static)!;
    public static readonly MethodInfo DurationGetSecondsMethod =
        typeof(CelFunctions).GetMethod(nameof(DurationGetSeconds), BindingFlags.Public | BindingFlags.Static)!;
    public static readonly MethodInfo DurationGetMillisecondsMethod =
        typeof(CelFunctions).GetMethod(nameof(DurationGetMilliseconds), BindingFlags.Public | BindingFlags.Static)!;
}
