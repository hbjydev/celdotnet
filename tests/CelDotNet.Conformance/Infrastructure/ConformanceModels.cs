namespace CelDotNet.Conformance.Infrastructure;

/// <summary>
/// Represents an expected value from a conformance test.
/// Maps to the proto <c>cel.expr.Value</c> oneof.
/// </summary>
public abstract record ExpectedValue
{
    public sealed record Int64Value(long Value) : ExpectedValue;
    public sealed record Uint64Value(ulong Value) : ExpectedValue;
    public sealed record DoubleValue(double Value) : ExpectedValue;
    public sealed record StringValue(string Value) : ExpectedValue;
    public sealed record BoolValue(bool Value) : ExpectedValue;
    public sealed record BytesValue(byte[] Value) : ExpectedValue;
    public sealed record NullValue : ExpectedValue;
    public sealed record ListValue(List<ExpectedValue> Values) : ExpectedValue;
    public sealed record MapValue(List<MapEntry> Entries) : ExpectedValue;
    public sealed record TypeValue(string Value) : ExpectedValue;
    /// <summary>Protobuf Any (object_value) — tests using this should be skipped.</summary>
    public sealed record ObjectValue(string TypeUrl) : ExpectedValue;
}

/// <summary>
/// A key-value pair in a map value.
/// </summary>
public sealed record MapEntry(ExpectedValue Key, ExpectedValue Value);

/// <summary>
/// The expected result of a conformance test.
/// </summary>
public abstract record ExpectedResult
{
    /// <summary>The expression should evaluate to a specific value.</summary>
    public sealed record Value(ExpectedValue Expected) : ExpectedResult;

    /// <summary>The expression should produce an evaluation error.</summary>
    public sealed record EvalError(string? Message = null) : ExpectedResult;
}

/// <summary>
/// A variable binding for a conformance test.
/// </summary>
public sealed record Binding(string Key, ExpectedValue Value);

/// <summary>
/// A type declaration in the test environment.
/// </summary>
public sealed record TypeDecl(string Name, string PrimitiveType);

/// <summary>
/// A single conformance test case.
/// </summary>
public sealed record SimpleTest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Expr { get; init; }
    public bool DisableMacros { get; init; }
    public bool DisableCheck { get; init; }
    public bool CheckOnly { get; init; }
    public string? Container { get; init; }
    public required ExpectedResult Result { get; init; }
    public List<Binding> Bindings { get; init; } = [];
    public List<TypeDecl> TypeEnv { get; init; } = [];
}

/// <summary>
/// A section grouping related conformance tests.
/// </summary>
public sealed record SimpleTestSection
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required List<SimpleTest> Tests { get; init; }
}

/// <summary>
/// A conformance test file containing multiple sections.
/// </summary>
public sealed record SimpleTestFile
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required List<SimpleTestSection> Sections { get; init; }
}
