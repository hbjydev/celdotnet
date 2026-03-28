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

    // --- Type conversion runtime helpers ---

    /// <summary>Parses a timestamp string to DateTimeOffset. Used by timestamp() function.</summary>
    public static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);

    public static readonly MethodInfo ParseTimestampMethod =
        typeof(CelFunctions).GetMethod(nameof(ParseTimestamp), BindingFlags.Public | BindingFlags.Static)!;

    /// <summary>Parses a duration string (e.g. "3600s", "1.5h") to TimeSpan. Used by duration() function.</summary>
    public static TimeSpan ParseDuration(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new CelException("invalid duration: empty string");

        // CEL duration format: number followed by unit suffix
        // Supported: s (seconds), ms (milliseconds), us (microseconds), ns (nanoseconds),
        //            m (minutes), h (hours)
        var span = value.AsSpan().Trim();

        // Find where the digits end and the unit starts
        int unitStart = 0;
        for (int i = 0; i < span.Length; i++)
        {
            if (!char.IsDigit(span[i]) && span[i] != '.' && span[i] != '-')
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

        return unitPart switch
        {
            "h" => TimeSpan.FromHours(number),
            "m" => TimeSpan.FromMinutes(number),
            "s" => TimeSpan.FromSeconds(number),
            "ms" => TimeSpan.FromMilliseconds(number),
            "us" => TimeSpan.FromMicroseconds(number),
            "ns" => TimeSpan.FromTicks((long)(number / 100.0)),
            _ => throw new CelException($"unknown duration unit: '{unitPart.ToString()}'"),
        };
    }

    public static readonly MethodInfo ParseDurationMethod =
        typeof(CelFunctions).GetMethod(nameof(ParseDuration), BindingFlags.Public | BindingFlags.Static)!;

    // --- Timestamp member access helpers ---

    public static int GetFullYear(DateTimeOffset ts) => ts.Year;
    public static int GetMonth(DateTimeOffset ts) => ts.Month - 1; // CEL months are 0-based
    public static int GetDayOfMonth(DateTimeOffset ts) => ts.Day - 1; // CEL days are 0-based
    public static int GetDayOfWeek(DateTimeOffset ts) => (int)ts.DayOfWeek;
    public static int GetDayOfYear(DateTimeOffset ts) => ts.DayOfYear - 1; // CEL is 0-based
    public static int GetHours(DateTimeOffset ts) => ts.Hour;
    public static int GetMinutes(DateTimeOffset ts) => ts.Minute;
    public static int GetSeconds(DateTimeOffset ts) => ts.Second;
    public static int GetMilliseconds(DateTimeOffset ts) => ts.Millisecond;

    public static readonly MethodInfo TimestampGetFullYear =
        typeof(CelFunctions).GetMethod(nameof(GetFullYear), BindingFlags.Public | BindingFlags.Static)!;
    public static readonly MethodInfo TimestampGetMonth =
        typeof(CelFunctions).GetMethod(nameof(GetMonth), BindingFlags.Public | BindingFlags.Static)!;
    public static readonly MethodInfo TimestampGetDayOfMonth =
        typeof(CelFunctions).GetMethod(nameof(GetDayOfMonth), BindingFlags.Public | BindingFlags.Static)!;
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
}
