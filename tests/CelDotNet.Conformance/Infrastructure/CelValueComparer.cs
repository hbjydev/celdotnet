namespace CelDotNet.Conformance.Infrastructure;

/// <summary>
/// Compares actual evaluation results against expected conformance test values.
/// Handles type coercions, NaN comparison, and recursive list/map equality.
/// </summary>
internal static class CelValueComparer
{
    /// <summary>
    /// Returns true if the actual result matches the expected value.
    /// </summary>
    public static bool AreEqual(ExpectedValue expected, object? actual)
    {
        return expected switch
        {
            ExpectedValue.BoolValue b => actual is bool ab && ab == b.Value,
            ExpectedValue.Int64Value i => CompareInt64(i.Value, actual),
            ExpectedValue.Uint64Value u => CompareUint64(u.Value, actual),
            ExpectedValue.DoubleValue d => CompareDouble(d.Value, actual),
            ExpectedValue.StringValue s => actual is string str && str == s.Value,
            ExpectedValue.BytesValue b => actual is byte[] bytes && BytesEqual(b.Value, bytes),
            ExpectedValue.NullValue => actual is null,
            ExpectedValue.ListValue list => CompareList(list, actual),
            ExpectedValue.MapValue map => CompareMap(map, actual),
            _ => false,
        };
    }

    /// <summary>
    /// Provides a human-readable description of what was expected vs what was received.
    /// </summary>
    public static string Describe(ExpectedValue expected, object? actual)
    {
        var expectedStr = DescribeExpected(expected);
        var actualStr = actual is null ? "null" : $"{actual} ({actual.GetType().Name})";
        return $"Expected: {expectedStr}, Actual: {actualStr}";
    }

    private static string DescribeExpected(ExpectedValue expected) => expected switch
    {
        ExpectedValue.BoolValue b => $"bool({b.Value})",
        ExpectedValue.Int64Value i => $"int64({i.Value})",
        ExpectedValue.Uint64Value u => $"uint64({u.Value})",
        ExpectedValue.DoubleValue d => $"double({d.Value})",
        ExpectedValue.StringValue s => $"string(\"{s.Value}\")",
        ExpectedValue.BytesValue b => $"bytes({b.Value.Length} bytes)",
        ExpectedValue.NullValue => "null",
        ExpectedValue.ListValue l => $"list({l.Values.Count} items)",
        ExpectedValue.MapValue m => $"map({m.Entries.Count} entries)",
        _ => expected.ToString() ?? "unknown",
    };

    private static bool CompareInt64(long expected, object? actual) => actual switch
    {
        long l => l == expected,
        int i => i == expected,
        short s => s == expected,
        byte b => b == expected,
        _ => false,
    };

    private static bool CompareUint64(ulong expected, object? actual) => actual switch
    {
        ulong u => u == expected,
        long l when l >= 0 => (ulong)l == expected,
        int i when i >= 0 => (ulong)i == expected,
        _ => false,
    };

    private static bool CompareDouble(double expected, object? actual)
    {
        if (actual is not double d)
        {
            // Allow int-to-double promotion for comparison
            if (actual is long l) d = l;
            else if (actual is int i) d = i;
            else if (actual is float f) d = f;
            else return false;
        }

        // NaN == NaN for test comparison purposes
        if (double.IsNaN(expected) && double.IsNaN(d))
            return true;

        return d == expected;
    }

    private static bool BytesEqual(byte[] expected, byte[] actual)
    {
        if (expected.Length != actual.Length)
            return false;
        return expected.AsSpan().SequenceEqual(actual.AsSpan());
    }

    private static bool CompareList(ExpectedValue.ListValue expected, object? actual)
    {
        if (actual is not System.Collections.IEnumerable enumerable)
            return false;

        var actualList = new List<object?>();
        foreach (var item in enumerable)
            actualList.Add(item);

        if (actualList.Count != expected.Values.Count)
            return false;

        for (int i = 0; i < expected.Values.Count; i++)
        {
            if (!AreEqual(expected.Values[i], actualList[i]))
                return false;
        }

        return true;
    }

    private static bool CompareMap(ExpectedValue.MapValue expected, object? actual)
    {
        if (actual is not System.Collections.IDictionary dict)
            return false;

        if (dict.Count != expected.Entries.Count)
            return false;

        // For each expected entry, find a matching key in the actual dict
        foreach (var entry in expected.Entries)
        {
            bool found = false;
            foreach (System.Collections.DictionaryEntry actualEntry in dict)
            {
                if (AreEqual(entry.Key, actualEntry.Key) && AreEqual(entry.Value, actualEntry.Value))
                {
                    found = true;
                    break;
                }
            }
            if (!found)
                return false;
        }

        return true;
    }
}
