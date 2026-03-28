using BenchmarkDotNet.Attributes;

namespace CelDotNet.Benchmarks;

/// <summary>
/// Benchmarks for the expression compiler (AST → System.Linq.Expressions).
/// Measures compilation cost in isolation (parsing done once in setup).
/// </summary>
[MemoryDiagnoser]
public class CompilerBenchmarks
{
    public class Person
    {
        public string Name { get; set; } = "";
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public int Age { get; set; }
        public bool IsActive { get; set; }
        public double Score { get; set; }
        public string? NickName { get; set; }
        public List<string> Tags { get; set; } = [];
        public List<int> Scores { get; set; } = [];
    }

    private CelExpression _simpleExpr = null!;
    private CelExpression _mediumExpr = null!;
    private CelExpression _complexExpr = null!;
    private CelExpression _comprehensionExpr = null!;

    [GlobalSetup]
    public void Setup()
    {
        _simpleExpr = CelExpression.Parse("name == 'foo'");
        _mediumExpr = CelExpression.Parse("age > 21 && is_active == true && score >= 95.5");
        _complexExpr = CelExpression.Parse(
            "name == 'foo' && age > 21 && is_active == true && score >= 95.5 && nick_name != null");
        _comprehensionExpr = CelExpression.Parse(
            "tags.exists(t, t == 'important') && scores.all(s, s > 0)");
    }

    [Benchmark(Description = "simple: name == 'foo'")]
    public Func<Person, bool> CompileSimple()
    {
        return _simpleExpr.Compile<Person>();
    }

    [Benchmark(Description = "medium: 3 conditions")]
    public Func<Person, bool> CompileMedium()
    {
        return _mediumExpr.Compile<Person>();
    }

    [Benchmark(Description = "complex: 5 conditions")]
    public Func<Person, bool> CompileComplex()
    {
        return _complexExpr.Compile<Person>();
    }

    [Benchmark(Description = "comprehension: exists + all")]
    public Func<Person, bool> CompileComprehension()
    {
        return _comprehensionExpr.Compile<Person>();
    }
}
