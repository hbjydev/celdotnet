using CelDotNet.Conformance.Infrastructure;
using CelDotNet.Compiler;
using Xunit;

namespace CelDotNet.Conformance;

/// <summary>
/// Runs cel-spec conformance tests against the CelDotNet implementation.
/// Tests are loaded from textproto files and driven as xUnit theories.
/// </summary>
public sealed class ConformanceTests
{
    /// <summary>
    /// Features that require skipping because they're not yet supported.
    /// </summary>
    private static readonly HashSet<string> SkipReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        "typed_result not supported",
    };

    /// <summary>
    /// Test names or section names that should be skipped entirely.
    /// </summary>
    private static readonly HashSet<string> SkipTestNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // type() denotation tests — require type system not yet implemented
        "bool_denotation",
        "int_denotation",
        "uint_denotation",
        "double_denotation",
        "null_type_denotation",
        "string_denotation",
        "bytes_denotation",
        "list_denotation",
        "map_denotation",
        "type_denotation",

        // Unbound variable/function — require deferred error values
        "unbound_is_runtime_error",

        // Nanosecond precision tests — .NET TimeSpan has 100ns (tick) resolution,
        // so single-nanosecond arithmetic and 9-digit fractional second formatting
        // fundamentally cannot match CEL's 1ns precision without a custom duration type.
        "toString_timestamp_nanos",           // expects 9 fractional digits, .NET truncates to 7
        "add_time_to_duration_nanos_negative", // 999999999ns loses precision in TimeSpan
        "add_time_to_duration_nanos_positive", // same
        "add_duration_nanos_over",            // 1ns rounds to 0 ticks, no overflow detected
        "add_duration_nanos_under",           // -1ns rounds to 0 ticks, no overflow detected
    };

    /// <summary>
    /// Expressions containing these substrings are skipped — they require
    /// features not currently implemented (dyn, type(), proto messages, etc.).
    /// </summary>
    private static readonly string[] SkipExprPatterns =
    [
        "dyn(",
        "type(",
        "proto2",
        "proto3",
        "google.protobuf",
        ".google.",
    ];

    public static IEnumerable<object[]> GetConformanceTests()
    {
        var testDataDir = Path.Combine(AppContext.BaseDirectory, "TestData");
        if (!Directory.Exists(testDataDir))
            return [];

        var results = new List<object[]>();

        foreach (var file in Directory.GetFiles(testDataDir, "*.textproto"))
        {
            var text = File.ReadAllText(file);
            SimpleTestFile testFile;
            try
            {
                testFile = TextProtoParser.Parse(text);
            }
            catch (Exception ex)
            {
                results.Add([Path.GetFileNameWithoutExtension(file), "PARSE_ERROR", $"Failed to parse: {ex.Message}", null!]);
                continue;
            }

            foreach (var section in testFile.Sections)
            {
                foreach (var test in section.Tests)
                {
                    var displayName = $"{testFile.Name}/{section.Name}/{test.Name}";
                    results.Add([testFile.Name, section.Name, displayName, test]);
                }
            }
        }

        return results;
    }

    [Theory]
    [MemberData(nameof(GetConformanceTests))]
    public void CelSpecConformance(string file, string section, string displayName, SimpleTest? test)
    {
        // file and section are used for xUnit test explorer grouping
        _ = file;
        _ = section;

        if (test is null)
        {
            // Parse error case
            Assert.Fail(displayName);
            return;
        }

        // Skip tests that need unsupported features
        var skipReason = GetSkipReason(test);
        if (skipReason is not null)
        {
            Assert.Skip(skipReason);
            return;
        }

        switch (test.Result)
        {
            case ExpectedResult.Value expected:
                RunValueTest(test, expected, displayName);
                break;

            case ExpectedResult.EvalError:
                RunErrorTest(test, displayName);
                break;
        }
    }

    private static string? GetSkipReason(SimpleTest test)
    {
        // Skip tests with bindings (variable references beyond the target type)
        if (test.Bindings.Count > 0)
            return "Test requires variable bindings (not yet supported in conformance runner)";

        // Skip tests with type environment declarations
        if (test.TypeEnv.Count > 0)
            return "Test requires type environment declarations";

        // Skip tests that reference containers
        if (test.Container is not null)
            return $"Test uses container: {test.Container}";

        // Skip check_only tests (they only test the type checker, not evaluation)
        if (test.CheckOnly)
            return "check_only test (type checker only)";

        // Skip tests with known unsupported result types
        if (test.Result is ExpectedResult.EvalError { Message: var msg } && msg is not null && SkipReasons.Contains(msg))
            return msg;

        // Skip tests by name
        if (SkipTestNames.Contains(test.Name))
            return $"Skipped test: {test.Name}";

        // Skip tests with expressions that use unsupported features
        foreach (var pattern in SkipExprPatterns)
        {
            if (test.Expr.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return $"Expression uses unsupported feature: {pattern}";
        }

        return null;
    }

    private static void RunValueTest(SimpleTest test, ExpectedResult.Value expected, string displayName)
    {
        object? result;
        try
        {
            result = EvaluateExpression(test.Expr);
        }
        catch (Exception ex)
        {
            Assert.Fail($"[{displayName}] Expression '{test.Expr}' threw {ex.GetType().Name}: {ex.Message}");
            return;
        }

        var match = CelValueComparer.AreEqual(expected.Expected, result);
        if (!match)
        {
            var detail = CelValueComparer.Describe(expected.Expected, result);
            Assert.Fail($"[{displayName}] {detail} | expr: {test.Expr}");
        }
    }

    private static void RunErrorTest(SimpleTest test, string displayName)
    {
        try
        {
            var result = EvaluateExpression(test.Expr);
            // If we get here without throwing, the test should have errored
            Assert.Fail($"[{displayName}] Expected eval_error but got result: {result} | expr: {test.Expr}");
        }
        catch (CelException)
        {
            // Expected — CEL evaluation error
        }
        catch (OverflowException)
        {
            // Also acceptable — arithmetic overflow is an eval error in CEL
        }
        catch (DivideByZeroException)
        {
            // Division by zero is an eval error in CEL
        }
        catch (FormatException)
        {
            // Bad format in conversions (e.g., int("not_a_number"))
        }
        catch (InvalidOperationException)
        {
            // Various runtime evaluation errors
        }
        catch (InvalidCastException)
        {
            // Type conversion failures
        }
        catch (ArgumentException)
        {
            // e.g., invalid regex in matches()
        }
        catch (IndexOutOfRangeException)
        {
            // List index out of bounds is an eval error in CEL
        }
        catch (KeyNotFoundException)
        {
            // Map key not found is an eval error in CEL
        }
    }

    /// <summary>
    /// Parses and evaluates a CEL expression with no target type bindings.
    /// Uses <see cref="ExpressionCompiler.CompileUntyped"/> to support non-bool return types.
    /// </summary>
    private static object? EvaluateExpression(string exprText)
    {
        var parsed = CelExpression.Parse(exprText);
        var lambda = ExpressionCompiler.CompileUntyped(parsed.Ast, typeof(object));

        // Compile and invoke with a dummy parameter
        var compiled = lambda.Compile();
        var dummy = new object();
        try
        {
            return compiled.DynamicInvoke(dummy);
        }
        catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException is not null)
        {
            // Unwrap TargetInvocationException so callers see the real exception
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
            throw; // unreachable
        }
    }
}
