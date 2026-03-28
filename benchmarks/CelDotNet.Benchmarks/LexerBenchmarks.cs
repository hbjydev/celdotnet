using BenchmarkDotNet.Attributes;
using CelDotNet.Lexer;

namespace CelDotNet.Benchmarks;

/// <summary>
/// Benchmarks for the CEL lexer (tokenisation throughput).
/// Tests various expression complexities from trivial to gnarly.
/// </summary>
[MemoryDiagnoser]
public class LexerBenchmarks
{
    [Params(
        "true",
        "name == 'foo'",
        "age > 21 && name == 'bar'",
        "name == 'foo' && age > 21 && is_active == true && score >= 95.5",
        "items.exists(x, x > 10) && name.startsWith('test') || status in ['active', 'pending', 'review']"
    )]
    public string Expression { get; set; } = "";

    [Benchmark]
    public List<Token> Tokenise()
    {
        var lexer = new CelLexer(Expression);
        return lexer.Tokenise();
    }
}
