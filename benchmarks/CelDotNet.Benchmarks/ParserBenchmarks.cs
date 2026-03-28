using BenchmarkDotNet.Attributes;
using CelDotNet.Ast;
using CelDotNet.Parser;

namespace CelDotNet.Benchmarks;

/// <summary>
/// Benchmarks for the CEL parser (parse throughput).
/// Measures the cost of lexing + parsing together since that's the real-world path.
/// </summary>
[MemoryDiagnoser]
public class ParserBenchmarks
{
    [Params(
        "true",
        "name == 'foo'",
        "age > 21 && name == 'bar'",
        "name == 'foo' && age > 21 && is_active == true && score >= 95.5",
        "items.exists(x, x > 10) && name.startsWith('test') || status in ['active', 'pending', 'review']",
        "a > 1 ? b + c * 2 : d - e / 3"
    )]
    public string Expression { get; set; } = "";

    [Benchmark]
    public CelExpr Parse()
    {
        return CelParser.Parse(Expression);
    }
}
