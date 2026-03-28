using CelDotNet.Compiler;
using Xunit;

namespace CelDotNet.Tests.Compiler;

/// <summary>
/// Tests for field name resolution: [CelField] attribute, exact match, snake_case → PascalCase.
/// </summary>
public class CompilerContextTests
{
    [Theory]
    [InlineData("FirstName", "first_name")]
    [InlineData("LastName", "last_name")]
    [InlineData("Age", "age")]
    [InlineData("IsActive", "is_active")]
    [InlineData("HTTPSEnabled", "https_enabled")]
    [InlineData("ID", "id")]
    public void PascalToSnakeCase_ConvertsCorrectly(string pascal, string expectedSnake)
    {
        var result = CompilerContext.PascalToSnakeCase(pascal);
        Assert.Equal(expectedSnake, result);
    }

    [Fact]
    public void ResolveProperty_FindsByExactName()
    {
        var prop = CompilerContext.ResolveProperty(typeof(TestPerson), "Name");
        Assert.NotNull(prop);
        Assert.Equal("Name", prop.Name);
    }

    [Fact]
    public void ResolveProperty_FindsBySnakeCaseConversion()
    {
        var prop = CompilerContext.ResolveProperty(typeof(TestPerson), "first_name");
        Assert.NotNull(prop);
        Assert.Equal("FirstName", prop.Name);
    }

    [Fact]
    public void ResolveProperty_FindsByCelFieldAttribute()
    {
        var prop = CompilerContext.ResolveProperty(typeof(TestPersonWithAttribute), "display_name");
        Assert.NotNull(prop);
        Assert.Equal("FullName", prop.Name);
    }

    [Fact]
    public void ResolveProperty_ReturnsNull_ForUnknownField()
    {
        var prop = CompilerContext.ResolveProperty(typeof(TestPerson), "nonexistent");
        Assert.Null(prop);
    }

    [Fact]
    public void ResolveProperty_ReturnsNull_ForNonVisibleField()
    {
        var prop = CompilerContext.ResolveProperty(typeof(TestPersonWithVisibility), "internal_id");
        Assert.Null(prop);
    }

    [Fact]
    public void ResolveProperty_FindsVisibleField()
    {
        var prop = CompilerContext.ResolveProperty(typeof(TestPersonWithVisibility), "public_name");
        Assert.NotNull(prop);
        Assert.Equal("PublicName", prop.Name);
    }
}

// Test model classes
public class TestPerson
{
    public string Name { get; set; } = "";
    public string FirstName { get; set; } = "";
    public int Age { get; set; }
    public bool IsActive { get; set; }
}

public class TestPersonWithAttribute
{
    [CelField("display_name", visible: true)]
    public string FullName { get; set; } = "";

    public int Age { get; set; }
}

public class TestPersonWithVisibility
{
    [CelField("internal_id", visible: false)]
    public string InternalId { get; set; } = "";

    [CelField("public_name", visible: true)]
    public string PublicName { get; set; } = "";
}
