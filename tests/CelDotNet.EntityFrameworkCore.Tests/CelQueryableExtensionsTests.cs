using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CelDotNet.EntityFrameworkCore.Tests;

/// <summary>
/// Integration tests for CelDotNet.EntityFrameworkCore.
/// Uses SQLite in-memory to verify that CEL expressions translate to actual SQL queries.
/// </summary>
public class CelQueryableExtensionsTests : IAsyncLifetime
{
    #region Test Models & DbContext

    public class Person
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public int Age { get; set; }
        public bool IsActive { get; set; }
        public double Score { get; set; }
        public string? NickName { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    public class Product
    {
        public int Id { get; set; }

        [CelField("product_name")]
        public string ProductName { get; set; } = "";

        [CelField("unit_price")]
        public double UnitPrice { get; set; }

        public int StockCount { get; set; }
        public bool InStock { get; set; }
    }

    public class TestDbContext : DbContext
    {
        public DbSet<Person> People => Set<Person>();
        public DbSet<Product> Products => Set<Product>();

        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // SQLite doesn't have native DateTimeOffset support, store as text
            modelBuilder.Entity<Person>(e =>
            {
                e.Property(p => p.CreatedAt)
                    .HasConversion(
                        v => v.ToString("O"),
                        v => DateTimeOffset.Parse(v));
            });
        }
    }

    private TestDbContext _db = null!;

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _db = new TestDbContext(options);
        await _db.Database.OpenConnectionAsync(ct);
        await _db.Database.EnsureCreatedAsync(ct);

        // Seed test data
        _db.People.AddRange(
            new Person { Id = 1, Name = "Alice", FirstName = "Alice", LastName = "Smith", Age = 30, IsActive = true, Score = 95.5, NickName = "Al", CreatedAt = new DateTimeOffset(2023, 6, 15, 10, 30, 0, TimeSpan.Zero) },
            new Person { Id = 2, Name = "Bob", FirstName = "Bob", LastName = "Jones", Age = 25, IsActive = true, Score = 82.0, NickName = null, CreatedAt = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero) },
            new Person { Id = 3, Name = "Charlie", FirstName = "Charlie", LastName = "Brown", Age = 35, IsActive = false, Score = 70.0, NickName = "Chuck", CreatedAt = new DateTimeOffset(2022, 12, 25, 18, 0, 0, TimeSpan.Zero) }
        );

        _db.Products.AddRange(
            new Product { Id = 1, ProductName = "Widget", UnitPrice = 9.99, StockCount = 100, InStock = true },
            new Product { Id = 2, ProductName = "Gadget", UnitPrice = 24.99, StockCount = 0, InStock = false },
            new Product { Id = 3, ProductName = "Gizmo", UnitPrice = 14.50, StockCount = 42, InStock = true }
        );

        await _db.SaveChangesAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
    }

    #endregion

    #region String Equality

    [Fact]
    public async Task WhereCel_EqualString()
    {
        var results = await _db.People
            .WhereCel("Name == 'Alice'")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("Alice", results[0].Name);
    }

    [Fact]
    public async Task WhereCel_NotEqualString()
    {
        var results = await _db.People
            .WhereCel("Name != 'Alice'")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Count);
        Assert.DoesNotContain(results, p => p.Name == "Alice");
    }

    #endregion

    #region Numeric Comparisons

    [Fact]
    public async Task WhereCel_GreaterThan()
    {
        var results = await _db.People
            .WhereCel("Age > 28")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Count);
        Assert.All(results, p => Assert.True(p.Age > 28));
    }

    [Fact]
    public async Task WhereCel_LessThanOrEqual()
    {
        var results = await _db.People
            .WhereCel("Age <= 30")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Count);
        Assert.All(results, p => Assert.True(p.Age <= 30));
    }

    [Fact]
    public async Task WhereCel_DoubleComparison()
    {
        var results = await _db.People
            .WhereCel("Score > 80.0")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Count);
        Assert.All(results, p => Assert.True(p.Score > 80.0));
    }

    #endregion

    #region Logical Operators

    [Fact]
    public async Task WhereCel_And()
    {
        var results = await _db.People
            .WhereCel("IsActive == true && Age > 28")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("Alice", results[0].Name);
    }

    [Fact]
    public async Task WhereCel_Or()
    {
        var results = await _db.People
            .WhereCel("Name == 'Alice' || Name == 'Charlie'")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task WhereCel_Not()
    {
        var results = await _db.People
            .WhereCel("!IsActive")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("Charlie", results[0].Name);
    }

    [Fact]
    public async Task WhereCel_ComplexLogical()
    {
        var results = await _db.People
            .WhereCel("(Age >= 30 && IsActive) || Score > 90.0")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("Alice", results[0].Name);
    }

    #endregion

    #region String Functions

    [Fact]
    public async Task WhereCel_StringContains()
    {
        var results = await _db.People
            .WhereCel("Name.contains('li')")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Count); // Alice, Charlie
    }

    [Fact]
    public async Task WhereCel_StringStartsWith()
    {
        var results = await _db.People
            .WhereCel("Name.startsWith('Al')")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("Alice", results[0].Name);
    }

    [Fact]
    public async Task WhereCel_StringEndsWith()
    {
        var results = await _db.People
            .WhereCel("Name.endsWith('ie')")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("Charlie", results[0].Name);
    }

    [Fact]
    public async Task WhereCel_StringSize()
    {
        var results = await _db.People
            .WhereCel("size(Name) > 4")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Count); // Alice (5), Charlie (7)
    }

    #endregion

    #region CelField Attribute

    [Fact]
    public async Task WhereCel_CelFieldAttribute()
    {
        var results = await _db.Products
            .WhereCel("product_name == 'Widget'")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("Widget", results[0].ProductName);
    }

    [Fact]
    public async Task WhereCel_CelFieldAttributeNumeric()
    {
        var results = await _db.Products
            .WhereCel("unit_price < 15.0")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Count); // Widget (9.99), Gizmo (14.50)
    }

    #endregion

    #region Snake Case to PascalCase

    [Fact]
    public async Task WhereCel_AutoSnakeCaseToPascal()
    {
        var results = await _db.People
            .WhereCel("first_name == 'Alice'")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("Alice", results[0].FirstName);
    }

    #endregion

    #region Ternary Operator

    [Fact]
    public async Task WhereCel_Ternary()
    {
        var results = await _db.People
            .WhereCel("(IsActive ? Age : 0) > 28")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("Alice", results[0].Name);
    }

    #endregion

    #region Null Checks

    [Fact]
    public async Task WhereCel_NullableFieldNotNull()
    {
        var results = await _db.People
            .WhereCel("NickName != null")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Count); // Alice (Al), Charlie (Chuck)
    }

    [Fact]
    public async Task WhereCel_NullableFieldIsNull()
    {
        var results = await _db.People
            .WhereCel("NickName == null")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("Bob", results[0].Name);
    }

    #endregion

    #region In Operator

    [Fact]
    public async Task WhereCel_InList_String()
    {
        var results = await _db.People
            .WhereCel("Name in ['Alice', 'Charlie']")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task WhereCel_InList_Int()
    {
        var results = await _db.People
            .WhereCel("Age in [25, 35]")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Count); // Bob (25), Charlie (35)
    }

    #endregion

    #region Arithmetic

    [Fact]
    public async Task WhereCel_Arithmetic()
    {
        var results = await _db.People
            .WhereCel("Age + 5 > 33")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Count); // Alice (30+5=35), Charlie (35+5=40)
    }

    [Fact]
    public async Task WhereCel_StringConcat()
    {
        var results = await _db.People
            .WhereCel("FirstName + ' ' + LastName == 'Alice Smith'")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("Alice", results[0].Name);
    }

    #endregion

    #region Boolean Fields

    [Fact]
    public async Task WhereCel_BooleanField()
    {
        var results = await _db.Products
            .WhereCel("InStock")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task WhereCel_NegatedBooleanField()
    {
        var results = await _db.Products
            .WhereCel("!InStock")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("Gadget", results[0].ProductName);
    }

    #endregion

    #region With Environment

    [Fact]
    public async Task WhereCel_WithEnvironment()
    {
        var env = new CelEnvironment().DisableTypeChecking();

        var results = await _db.People
            .WhereCel("Name == 'Alice' && Age > 20", env)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("Alice", results[0].Name);
    }

    #endregion

    #region Error Cases

    [Fact]
    public void WhereCel_ParseError_Throws()
    {
        Assert.Throws<CelParseException>(() =>
            _db.People.WhereCel("== invalid").ToList());
    }

    [Fact]
    public void WhereCel_UnknownField_Throws()
    {
        Assert.Throws<CelException>(() =>
            _db.People.WhereCel("nonexistent_field == 'x'").ToList());
    }

    #endregion

    #region Multiple Conditions

    [Fact]
    public async Task WhereCel_ChainedWhereCel()
    {
        var results = await _db.People
            .WhereCel("IsActive == true")
            .WhereCel("Age > 28")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("Alice", results[0].Name);
    }

    [Fact]
    public async Task WhereCel_MixedWithLinq()
    {
        var results = await _db.People
            .Where(p => p.IsActive)
            .WhereCel("Age > 20")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Count);
    }

    #endregion

    #region Empty Results

    [Fact]
    public async Task WhereCel_NoMatches()
    {
        var results = await _db.People
            .WhereCel("Age > 100")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Empty(results);
    }

    [Fact]
    public async Task WhereCel_AllMatch()
    {
        var results = await _db.People
            .WhereCel("Age > 0")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, results.Count);
    }

    #endregion
}
