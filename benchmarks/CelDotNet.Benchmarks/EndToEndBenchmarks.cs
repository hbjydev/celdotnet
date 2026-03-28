using System.Linq.Expressions;
using BenchmarkDotNet.Attributes;

namespace CelDotNet.Benchmarks;

/// <summary>
/// End-to-end benchmarks: parse → compile → execute.
/// This is the hot path users actually care about.
/// </summary>
[MemoryDiagnoser]
public class EndToEndBenchmarks
{
    public class Person
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public bool IsActive { get; set; }
        public double Score { get; set; }
        public string? NickName { get; set; }
        public List<string> Tags { get; set; } = [];
        public List<int> Scores { get; set; } = [];
    }

    private readonly Person _matchingPerson = new()
    {
        Name = "foo",
        Age = 30,
        IsActive = true,
        Score = 98.5,
        NickName = "bar",
        Tags = ["important", "urgent"],
        Scores = [90, 95, 100]
    };

    private readonly Person _nonMatchingPerson = new()
    {
        Name = "baz",
        Age = 15,
        IsActive = false,
        Score = 20.0,
        Tags = ["low-priority"],
        Scores = [0, -1, 5]
    };

    // Pre-compiled delegates for the "execute only" benchmarks
    private Func<Person, bool> _simpleCompiled = null!;
    private Func<Person, bool> _mediumCompiled = null!;
    private Func<Person, bool> _complexCompiled = null!;
    private Func<Person, bool> _comprehensionCompiled = null!;

    // Pre-compiled expression trees for the "ToExpression" benchmark
    private Expression<Func<Person, bool>> _simpleExprTree = null!;

    private const string SimpleExpr = "name == 'foo'";
    private const string MediumExpr = "age > 21 && is_active == true && score >= 95.5";
    private const string ComplexExpr =
        "name == 'foo' && age > 21 && is_active == true && score >= 95.5 && nick_name != null";
    private const string ComprehensionExpr =
        "tags.exists(t, t == 'important') && scores.all(s, s > 0)";

    [GlobalSetup]
    public void Setup()
    {
        _simpleCompiled = CelExpression.Parse(SimpleExpr).Compile<Person>();
        _mediumCompiled = CelExpression.Parse(MediumExpr).Compile<Person>();
        _complexCompiled = CelExpression.Parse(ComplexExpr).Compile<Person>();
        _comprehensionCompiled = CelExpression.Parse(ComprehensionExpr).Compile<Person>();
        _simpleExprTree = CelExpression.Parse(SimpleExpr).ToExpression<Person>();
    }

    // --- Full pipeline: parse + compile + execute ---

    [Benchmark(Description = "full pipeline: simple")]
    public bool FullPipelineSimple()
    {
        return CelExpression.Parse(SimpleExpr).Compile<Person>()(_matchingPerson);
    }

    [Benchmark(Description = "full pipeline: medium")]
    public bool FullPipelineMedium()
    {
        return CelExpression.Parse(MediumExpr).Compile<Person>()(_matchingPerson);
    }

    [Benchmark(Description = "full pipeline: complex")]
    public bool FullPipelineComplex()
    {
        return CelExpression.Parse(ComplexExpr).Compile<Person>()(_matchingPerson);
    }

    [Benchmark(Description = "full pipeline: comprehension")]
    public bool FullPipelineComprehension()
    {
        return CelExpression.Parse(ComprehensionExpr).Compile<Person>()(_matchingPerson);
    }

    // --- Execute only (pre-compiled delegate) ---

    [Benchmark(Description = "execute only: simple (match)")]
    public bool ExecuteSimpleMatch()
    {
        return _simpleCompiled(_matchingPerson);
    }

    [Benchmark(Description = "execute only: simple (no match)")]
    public bool ExecuteSimpleNoMatch()
    {
        return _simpleCompiled(_nonMatchingPerson);
    }

    [Benchmark(Description = "execute only: medium")]
    public bool ExecuteMedium()
    {
        return _mediumCompiled(_matchingPerson);
    }

    [Benchmark(Description = "execute only: complex")]
    public bool ExecuteComplex()
    {
        return _complexCompiled(_matchingPerson);
    }

    [Benchmark(Description = "execute only: comprehension")]
    public bool ExecuteComprehension()
    {
        return _comprehensionCompiled(_matchingPerson);
    }

    // --- ToExpression only (for IQueryable users who just need the tree) ---

    [Benchmark(Description = "parse + ToExpression: simple")]
    public Expression<Func<Person, bool>> ToExpressionSimple()
    {
        return CelExpression.Parse(SimpleExpr).ToExpression<Person>();
    }
}
