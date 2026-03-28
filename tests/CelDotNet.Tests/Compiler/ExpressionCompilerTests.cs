using Xunit;

namespace CelDotNet.Tests.Compiler;

/// <summary>
/// Tests for ExpressionCompiler via the CelExpression public API.
/// Covers comparisons, logical ops, arithmetic, field access, string functions, in operator,
/// and field name resolution with [CelField].
/// </summary>
public class ExpressionCompilerTests
{
    #region Test Models

    public class Person
    {
        public string Name { get; set; } = "";
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public int Age { get; set; }
        public bool IsActive { get; set; }
        public double Score { get; set; }
        public string? NickName { get; set; }
        public Address? Address { get; set; }
    }

    public class Address
    {
        public string City { get; set; } = "";
        public string PostCode { get; set; } = "";
    }

    public class Product
    {
        [CelField("product_name")]
        public string ProductName { get; set; } = "";

        [CelField("unit_price")]
        public double UnitPrice { get; set; }

        public int StockCount { get; set; }
        public bool InStock { get; set; }
        public List<string> Tags { get; set; } = [];
        public List<int> Scores { get; set; } = [];
    }

    public class Event
    {
        public string Name { get; set; } = "";
        public DateTimeOffset StartTime { get; set; }
        public DateTimeOffset EndTime { get; set; }
        public TimeSpan Duration { get; set; }
    }

    #endregion

    #region Comparison Operators

    [Fact]
    public void Compile_EqualString()
    {
        var fn = CelExpression.Parse("Name == 'Alice'").Compile<Person>();
        Assert.True(fn(new Person { Name = "Alice" }));
        Assert.False(fn(new Person { Name = "Bob" }));
    }

    [Fact]
    public void Compile_NotEqualString()
    {
        var fn = CelExpression.Parse("Name != 'Alice'").Compile<Person>();
        Assert.False(fn(new Person { Name = "Alice" }));
        Assert.True(fn(new Person { Name = "Bob" }));
    }

    [Fact]
    public void Compile_LessThan()
    {
        var fn = CelExpression.Parse("Age < 30").Compile<Person>();
        Assert.True(fn(new Person { Age = 25 }));
        Assert.False(fn(new Person { Age = 35 }));
    }

    [Fact]
    public void Compile_LessThanOrEqual()
    {
        var fn = CelExpression.Parse("Age <= 30").Compile<Person>();
        Assert.True(fn(new Person { Age = 30 }));
        Assert.False(fn(new Person { Age = 31 }));
    }

    [Fact]
    public void Compile_GreaterThan()
    {
        var fn = CelExpression.Parse("Age > 21").Compile<Person>();
        Assert.True(fn(new Person { Age = 25 }));
        Assert.False(fn(new Person { Age = 18 }));
    }

    [Fact]
    public void Compile_GreaterThanOrEqual()
    {
        var fn = CelExpression.Parse("Age >= 21").Compile<Person>();
        Assert.True(fn(new Person { Age = 21 }));
        Assert.False(fn(new Person { Age = 20 }));
    }

    [Fact]
    public void Compile_EqualBool()
    {
        var fn = CelExpression.Parse("IsActive == true").Compile<Person>();
        Assert.True(fn(new Person { IsActive = true }));
        Assert.False(fn(new Person { IsActive = false }));
    }

    [Fact]
    public void Compile_EqualDouble()
    {
        var fn = CelExpression.Parse("Score == 9.5").Compile<Person>();
        Assert.True(fn(new Person { Score = 9.5 }));
        Assert.False(fn(new Person { Score = 8.0 }));
    }

    #endregion

    #region Logical Operators

    [Fact]
    public void Compile_AndOperator()
    {
        var fn = CelExpression.Parse("Name == 'Alice' && Age > 21").Compile<Person>();
        Assert.True(fn(new Person { Name = "Alice", Age = 25 }));
        Assert.False(fn(new Person { Name = "Alice", Age = 18 }));
        Assert.False(fn(new Person { Name = "Bob", Age = 25 }));
    }

    [Fact]
    public void Compile_OrOperator()
    {
        var fn = CelExpression.Parse("Name == 'Alice' || Name == 'Bob'").Compile<Person>();
        Assert.True(fn(new Person { Name = "Alice" }));
        Assert.True(fn(new Person { Name = "Bob" }));
        Assert.False(fn(new Person { Name = "Charlie" }));
    }

    [Fact]
    public void Compile_NotOperator()
    {
        var fn = CelExpression.Parse("!IsActive").Compile<Person>();
        Assert.True(fn(new Person { IsActive = false }));
        Assert.False(fn(new Person { IsActive = true }));
    }

    [Fact]
    public void Compile_ComplexLogical()
    {
        var fn = CelExpression.Parse("(Name == 'Alice' || Name == 'Bob') && Age >= 21").Compile<Person>();
        Assert.True(fn(new Person { Name = "Alice", Age = 25 }));
        Assert.True(fn(new Person { Name = "Bob", Age = 21 }));
        Assert.False(fn(new Person { Name = "Alice", Age = 18 }));
        Assert.False(fn(new Person { Name = "Charlie", Age = 25 }));
    }

    #endregion

    #region Arithmetic

    [Fact]
    public void Compile_ArithmeticComparison()
    {
        var fn = CelExpression.Parse("Age + 5 > 30").Compile<Person>();
        Assert.True(fn(new Person { Age = 30 }));
        Assert.False(fn(new Person { Age = 20 }));
    }

    [Fact]
    public void Compile_NegateOperator()
    {
        // -Age < 0 should be true for positive ages
        var fn = CelExpression.Parse("Score > 0.0").Compile<Person>();
        Assert.True(fn(new Person { Score = 5.0 }));
        Assert.False(fn(new Person { Score = -1.0 }));
    }

    #endregion

    #region String Functions

    [Fact]
    public void Compile_StringContains()
    {
        var fn = CelExpression.Parse("Name.contains('lic')").Compile<Person>();
        Assert.True(fn(new Person { Name = "Alice" }));
        Assert.False(fn(new Person { Name = "Bob" }));
    }

    [Fact]
    public void Compile_StringStartsWith()
    {
        var fn = CelExpression.Parse("Name.startsWith('Al')").Compile<Person>();
        Assert.True(fn(new Person { Name = "Alice" }));
        Assert.False(fn(new Person { Name = "Bob" }));
    }

    [Fact]
    public void Compile_StringEndsWith()
    {
        var fn = CelExpression.Parse("Name.endsWith('ce')").Compile<Person>();
        Assert.True(fn(new Person { Name = "Alice" }));
        Assert.False(fn(new Person { Name = "Bob" }));
    }

    [Fact]
    public void Compile_StringSize()
    {
        var fn = CelExpression.Parse("Name.size() > 3").Compile<Person>();
        Assert.True(fn(new Person { Name = "Alice" }));
        Assert.False(fn(new Person { Name = "Al" }));
    }

    [Fact]
    public void Compile_SizeGlobalFunction()
    {
        var fn = CelExpression.Parse("size(Name) > 3").Compile<Person>();
        Assert.True(fn(new Person { Name = "Alice" }));
        Assert.False(fn(new Person { Name = "Al" }));
    }

    #endregion

    #region In Operator

    [Fact]
    public void Compile_InOperator_IntList()
    {
        var fn = CelExpression.Parse("Age in [21, 25, 30]").Compile<Person>();
        Assert.True(fn(new Person { Age = 25 }));
        Assert.False(fn(new Person { Age = 22 }));
    }

    [Fact]
    public void Compile_InOperator_StringList()
    {
        var fn = CelExpression.Parse("Name in ['Alice', 'Bob', 'Charlie']").Compile<Person>();
        Assert.True(fn(new Person { Name = "Alice" }));
        Assert.False(fn(new Person { Name = "Dave" }));
    }

    #endregion

    #region Field Name Resolution

    [Fact]
    public void Compile_SnakeCaseFieldAccess()
    {
        var fn = CelExpression.Parse("first_name == 'Alice'").Compile<Person>();
        Assert.True(fn(new Person { FirstName = "Alice" }));
        Assert.False(fn(new Person { FirstName = "Bob" }));
    }

    [Fact]
    public void Compile_CelFieldAttributeAccess()
    {
        var fn = CelExpression.Parse("product_name == 'Widget'").Compile<Product>();
        Assert.True(fn(new Product { ProductName = "Widget" }));
        Assert.False(fn(new Product { ProductName = "Gadget" }));
    }

    [Fact]
    public void Compile_CelFieldAttribute_NumericComparison()
    {
        var fn = CelExpression.Parse("unit_price > 10.0").Compile<Product>();
        Assert.True(fn(new Product { UnitPrice = 15.0 }));
        Assert.False(fn(new Product { UnitPrice = 5.0 }));
    }

    #endregion

    #region Nested Property Access

    [Fact]
    public void Compile_NestedFieldAccess()
    {
        var fn = CelExpression.Parse("Address.City == 'London'").Compile<Person>();
        Assert.True(fn(new Person { Address = new Address { City = "London" } }));
        Assert.False(fn(new Person { Address = new Address { City = "Paris" } }));
    }

    #endregion

    #region Has Macro

    [Fact]
    public void Compile_HasMacro_NestedReferenceField()
    {
        // has() requires a field selection per the CEL spec: has(obj.field)
        // For reference types, compiles to field != null
        var fn = CelExpression.Parse("has(Address.City)").Compile<Person>();
        Assert.True(fn(new Person { Address = new Address { City = "London" } }));
    }

    #endregion

    #region Conditional (Ternary)

    [Fact]
    public void Compile_Ternary()
    {
        // This tests the ternary in a boolean context
        var fn = CelExpression.Parse("(Age > 21 ? IsActive : false)").Compile<Person>();
        Assert.True(fn(new Person { Age = 25, IsActive = true }));
        Assert.False(fn(new Person { Age = 25, IsActive = false }));
        Assert.False(fn(new Person { Age = 18, IsActive = true }));
    }

    #endregion

    #region Expression Tree Output

    [Fact]
    public void ToExpression_ReturnsUsableExpressionTree()
    {
        var expr = CelExpression.Parse("Name == 'Alice'");
        var lambda = expr.ToExpression<Person>();

        // Should be an Expression<Func<Person, bool>>
        Assert.NotNull(lambda);
        Assert.Single(lambda.Parameters);
        Assert.Equal(typeof(Person), lambda.Parameters[0].Type);

        // Should compile and work
        var fn = lambda.Compile();
        Assert.True(fn(new Person { Name = "Alice" }));
    }

    [Fact]
    public void ToExpression_CanBeUsedWithLinqWhere()
    {
        var people = new List<Person>
        {
            new() { Name = "Alice", Age = 25 },
            new() { Name = "Bob", Age = 30 },
            new() { Name = "Charlie", Age = 20 },
        };

        var expr = CelExpression.Parse("Age >= 25");
        var predicate = expr.ToExpression<Person>();

        var results = people.AsQueryable().Where(predicate).ToList();
        Assert.Equal(2, results.Count);
        Assert.Contains(results, p => p.Name == "Alice");
        Assert.Contains(results, p => p.Name == "Bob");
    }

    #endregion

    #region Comprehension Macros — all()

    [Fact]
    public void Compile_All_AllMatch()
    {
        var fn = CelExpression.Parse("Tags.all(t, t.size() > 1)").Compile<Product>();
        Assert.True(fn(new Product { Tags = ["foo", "bar", "baz"] }));
    }

    [Fact]
    public void Compile_All_SomeFail()
    {
        var fn = CelExpression.Parse("Tags.all(t, t.size() > 2)").Compile<Product>();
        Assert.False(fn(new Product { Tags = ["foo", "ab", "baz"] }));
    }

    [Fact]
    public void Compile_All_EmptyList()
    {
        // all() on empty list is vacuously true
        var fn = CelExpression.Parse("Tags.all(t, t == 'nope')").Compile<Product>();
        Assert.True(fn(new Product { Tags = [] }));
    }

    [Fact]
    public void Compile_All_IntList()
    {
        var fn = CelExpression.Parse("Scores.all(s, s > 0)").Compile<Product>();
        Assert.True(fn(new Product { Scores = [1, 2, 3] }));
        Assert.False(fn(new Product { Scores = [1, -1, 3] }));
    }

    #endregion

    #region Comprehension Macros — exists()

    [Fact]
    public void Compile_Exists_Match()
    {
        var fn = CelExpression.Parse("Tags.exists(t, t == 'foo')").Compile<Product>();
        Assert.True(fn(new Product { Tags = ["foo", "bar"] }));
    }

    [Fact]
    public void Compile_Exists_NoMatch()
    {
        var fn = CelExpression.Parse("Tags.exists(t, t == 'nope')").Compile<Product>();
        Assert.False(fn(new Product { Tags = ["foo", "bar"] }));
    }

    [Fact]
    public void Compile_Exists_EmptyList()
    {
        var fn = CelExpression.Parse("Tags.exists(t, t == 'any')").Compile<Product>();
        Assert.False(fn(new Product { Tags = [] }));
    }

    [Fact]
    public void Compile_Exists_IntList()
    {
        var fn = CelExpression.Parse("Scores.exists(s, s > 5)").Compile<Product>();
        Assert.True(fn(new Product { Scores = [1, 6, 3] }));
        Assert.False(fn(new Product { Scores = [1, 2, 3] }));
    }

    #endregion

    #region Comprehension Macros — exists_one()

    [Fact]
    public void Compile_ExistsOne_ExactlyOne()
    {
        var fn = CelExpression.Parse("Tags.exists_one(t, t == 'foo')").Compile<Product>();
        Assert.True(fn(new Product { Tags = ["foo", "bar", "baz"] }));
    }

    [Fact]
    public void Compile_ExistsOne_MoreThanOne()
    {
        var fn = CelExpression.Parse("Tags.exists_one(t, t.startsWith('b'))").Compile<Product>();
        Assert.False(fn(new Product { Tags = ["foo", "bar", "baz"] }));
    }

    [Fact]
    public void Compile_ExistsOne_None()
    {
        var fn = CelExpression.Parse("Tags.exists_one(t, t == 'nope')").Compile<Product>();
        Assert.False(fn(new Product { Tags = ["foo", "bar"] }));
    }

    #endregion

    #region Comprehension Macros — filter()

    [Fact]
    public void Compile_Filter_ReturnsMatchingElements()
    {
        // filter returns IEnumerable, not bool — use untyped compilation
        var untypedLambda = CelDotNet.Compiler.ExpressionCompiler.CompileUntyped(
            CelExpression.Parse("Tags.filter(t, t.startsWith('b'))").Ast,
            typeof(Product));
        var fn = untypedLambda.Compile();
        var result = fn.DynamicInvoke(new Product { Tags = ["apple", "banana", "blueberry", "cherry"] });
        var list = ((System.Collections.IEnumerable)result!).Cast<string>().ToList();
        Assert.Equal(2, list.Count);
        Assert.Contains("banana", list);
        Assert.Contains("blueberry", list);
    }

    [Fact]
    public void Compile_Filter_EmptyResult()
    {
        var untypedLambda = CelDotNet.Compiler.ExpressionCompiler.CompileUntyped(
            CelExpression.Parse("Tags.filter(t, t == 'nope')").Ast,
            typeof(Product));
        var fn = untypedLambda.Compile();
        var result = fn.DynamicInvoke(new Product { Tags = ["foo", "bar"] });
        var list = ((System.Collections.IEnumerable)result!).Cast<string>().ToList();
        Assert.Empty(list);
    }

    #endregion

    #region Comprehension Macros — map()

    [Fact]
    public void Compile_Map_TransformElements()
    {
        var untypedLambda = CelDotNet.Compiler.ExpressionCompiler.CompileUntyped(
            CelExpression.Parse("Tags.map(t, t.size())").Ast,
            typeof(Product));
        var fn = untypedLambda.Compile();
        var result = fn.DynamicInvoke(new Product { Tags = ["hi", "hey", "hello"] });
        var list = ((System.Collections.IEnumerable)result!).Cast<long>().ToList();
        Assert.Equal([2L, 3L, 5L], list);
    }

    #endregion

    #region Comprehension Macros — boolean context

    [Fact]
    public void Compile_All_InBooleanExpression()
    {
        // all() combined with other conditions
        var fn = CelExpression.Parse("Tags.all(t, t.size() > 1) && InStock").Compile<Product>();
        Assert.True(fn(new Product { Tags = ["foo", "bar"], InStock = true }));
        Assert.False(fn(new Product { Tags = ["foo", "bar"], InStock = false }));
    }

    [Fact]
    public void Compile_Exists_InBooleanExpression()
    {
        var fn = CelExpression.Parse("Tags.exists(t, t == 'sale') || InStock").Compile<Product>();
        Assert.True(fn(new Product { Tags = ["sale"], InStock = false }));
        Assert.True(fn(new Product { Tags = ["nope"], InStock = true }));
        Assert.False(fn(new Product { Tags = ["nope"], InStock = false }));
    }

    #endregion

    #region matches() Regex

    [Fact]
    public void Compile_Matches_ReceiverStyle()
    {
        var fn = CelExpression.Parse("Name.matches('^A.*e$')").Compile<Person>();
        Assert.True(fn(new Person { Name = "Alice" }));
        Assert.False(fn(new Person { Name = "Bob" }));
    }

    [Fact]
    public void Compile_Matches_ReceiverStyle_DigitPattern()
    {
        var fn = CelExpression.Parse("Name.matches('[0-9]+')").Compile<Person>();
        Assert.True(fn(new Person { Name = "Agent007" }));
        Assert.False(fn(new Person { Name = "Alice" }));
    }

    [Fact]
    public void Compile_Matches_InBooleanExpression()
    {
        var fn = CelExpression.Parse("Name.matches('^A') && Age > 20").Compile<Person>();
        Assert.True(fn(new Person { Name = "Alice", Age = 25 }));
        Assert.False(fn(new Person { Name = "Alice", Age = 18 }));
        Assert.False(fn(new Person { Name = "Bob", Age = 25 }));
    }

    #endregion

    #region Type Conversion Functions

    [Fact]
    public void Compile_IntConversion_FromDouble()
    {
        // int(Score) == 9 — truncates
        var untypedLambda = CelDotNet.Compiler.ExpressionCompiler.CompileUntyped(
            CelExpression.Parse("int(Score)").Ast,
            typeof(Person));
        var fn = untypedLambda.Compile();
        var result = fn.DynamicInvoke(new Person { Score = 9.7 });
        Assert.Equal(9L, result);
    }

    [Fact]
    public void Compile_IntConversion_FromString()
    {
        var untypedLambda = CelDotNet.Compiler.ExpressionCompiler.CompileUntyped(
            CelExpression.Parse("int('42')").Ast,
            typeof(Person));
        var fn = untypedLambda.Compile();
        var result = fn.DynamicInvoke(new Person());
        Assert.Equal(42L, result);
    }

    [Fact]
    public void Compile_DoubleConversion_InComparison()
    {
        var fn = CelExpression.Parse("double(Age) > 21.5").Compile<Person>();
        Assert.True(fn(new Person { Age = 22 }));
        Assert.False(fn(new Person { Age = 21 }));
    }

    [Fact]
    public void Compile_StringConversion()
    {
        var untypedLambda = CelDotNet.Compiler.ExpressionCompiler.CompileUntyped(
            CelExpression.Parse("string(Age)").Ast,
            typeof(Person));
        var fn = untypedLambda.Compile();
        var result = fn.DynamicInvoke(new Person { Age = 42 });
        Assert.Equal("42", result);
    }

    [Fact]
    public void Compile_StringConversion_AlreadyString()
    {
        var untypedLambda = CelDotNet.Compiler.ExpressionCompiler.CompileUntyped(
            CelExpression.Parse("string(Name)").Ast,
            typeof(Person));
        var fn = untypedLambda.Compile();
        var result = fn.DynamicInvoke(new Person { Name = "Alice" });
        Assert.Equal("Alice", result);
    }

    #endregion

    #region Timestamp Construction and Member Access

    [Fact]
    public void Compile_Timestamp_GetFullYear()
    {
        var untypedLambda = CelDotNet.Compiler.ExpressionCompiler.CompileUntyped(
            CelExpression.Parse("StartTime.getFullYear()").Ast,
            typeof(Event));
        var fn = untypedLambda.Compile();
        var result = fn.DynamicInvoke(new Event { StartTime = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero) });
        Assert.Equal(2024L, result);
    }

    [Fact]
    public void Compile_Timestamp_GetMonth()
    {
        // CEL months are 0-based (January = 0)
        var untypedLambda = CelDotNet.Compiler.ExpressionCompiler.CompileUntyped(
            CelExpression.Parse("StartTime.getMonth()").Ast,
            typeof(Event));
        var fn = untypedLambda.Compile();
        var result = fn.DynamicInvoke(new Event { StartTime = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero) });
        Assert.Equal(5L, result); // June = 5 (0-based)
    }

    [Fact]
    public void Compile_Timestamp_GetDayOfMonth()
    {
        // CEL days are 0-based
        var untypedLambda = CelDotNet.Compiler.ExpressionCompiler.CompileUntyped(
            CelExpression.Parse("StartTime.getDayOfMonth()").Ast,
            typeof(Event));
        var fn = untypedLambda.Compile();
        var result = fn.DynamicInvoke(new Event { StartTime = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero) });
        Assert.Equal(14L, result); // 15th = index 14 (0-based)
    }

    [Fact]
    public void Compile_Timestamp_GetDayOfWeek()
    {
        var untypedLambda = CelDotNet.Compiler.ExpressionCompiler.CompileUntyped(
            CelExpression.Parse("StartTime.getDayOfWeek()").Ast,
            typeof(Event));
        var fn = untypedLambda.Compile();
        // 2024-06-15 is a Saturday = DayOfWeek.Saturday = 6
        var result = fn.DynamicInvoke(new Event { StartTime = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero) });
        Assert.Equal(6L, result);
    }

    [Fact]
    public void Compile_Timestamp_GetHours()
    {
        var untypedLambda = CelDotNet.Compiler.ExpressionCompiler.CompileUntyped(
            CelExpression.Parse("StartTime.getHours()").Ast,
            typeof(Event));
        var fn = untypedLambda.Compile();
        var result = fn.DynamicInvoke(new Event { StartTime = new DateTimeOffset(2024, 6, 15, 14, 30, 45, TimeSpan.Zero) });
        Assert.Equal(14L, result);
    }

    [Fact]
    public void Compile_Timestamp_GetMinutes()
    {
        var untypedLambda = CelDotNet.Compiler.ExpressionCompiler.CompileUntyped(
            CelExpression.Parse("StartTime.getMinutes()").Ast,
            typeof(Event));
        var fn = untypedLambda.Compile();
        var result = fn.DynamicInvoke(new Event { StartTime = new DateTimeOffset(2024, 6, 15, 14, 30, 45, TimeSpan.Zero) });
        Assert.Equal(30L, result);
    }

    [Fact]
    public void Compile_Timestamp_GetSeconds()
    {
        var untypedLambda = CelDotNet.Compiler.ExpressionCompiler.CompileUntyped(
            CelExpression.Parse("StartTime.getSeconds()").Ast,
            typeof(Event));
        var fn = untypedLambda.Compile();
        var result = fn.DynamicInvoke(new Event { StartTime = new DateTimeOffset(2024, 6, 15, 14, 30, 45, TimeSpan.Zero) });
        Assert.Equal(45L, result);
    }

    [Fact]
    public void Compile_Timestamp_InComparison()
    {
        // getFullYear() > 2020 should work in boolean context
        var fn = CelExpression.Parse("StartTime.getFullYear() > 2020").Compile<Event>();
        Assert.True(fn(new Event { StartTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero) }));
        Assert.False(fn(new Event { StartTime = new DateTimeOffset(2019, 1, 1, 0, 0, 0, TimeSpan.Zero) }));
    }

    #endregion

    #region Timestamp/Duration Arithmetic

    [Fact]
    public void Compile_TimestampPlusDuration()
    {
        var untypedLambda = CelDotNet.Compiler.ExpressionCompiler.CompileUntyped(
            CelExpression.Parse("StartTime + Duration").Ast,
            typeof(Event));
        var fn = untypedLambda.Compile();
        var evt = new Event
        {
            StartTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Duration = TimeSpan.FromHours(2),
        };
        var result = (DateTimeOffset)fn.DynamicInvoke(evt)!;
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 2, 0, 0, TimeSpan.Zero), result);
    }

    [Fact]
    public void Compile_TimestampMinusDuration()
    {
        var untypedLambda = CelDotNet.Compiler.ExpressionCompiler.CompileUntyped(
            CelExpression.Parse("EndTime - Duration").Ast,
            typeof(Event));
        var fn = untypedLambda.Compile();
        var evt = new Event
        {
            EndTime = new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero),
            Duration = TimeSpan.FromHours(3),
        };
        var result = (DateTimeOffset)fn.DynamicInvoke(evt)!;
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 7, 0, 0, TimeSpan.Zero), result);
    }

    [Fact]
    public void Compile_TimestampMinusTimestamp()
    {
        var untypedLambda = CelDotNet.Compiler.ExpressionCompiler.CompileUntyped(
            CelExpression.Parse("EndTime - StartTime").Ast,
            typeof(Event));
        var fn = untypedLambda.Compile();
        var evt = new Event
        {
            StartTime = new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2024, 1, 1, 13, 30, 0, TimeSpan.Zero),
        };
        var result = (TimeSpan)fn.DynamicInvoke(evt)!;
        Assert.Equal(TimeSpan.FromHours(3.5), result);
    }

    [Fact]
    public void Compile_DurationPlusDuration()
    {
        var untypedLambda = CelDotNet.Compiler.ExpressionCompiler.CompileUntyped(
            CelExpression.Parse("Duration + Duration").Ast,
            typeof(Event));
        var fn = untypedLambda.Compile();
        var evt = new Event { Duration = TimeSpan.FromMinutes(30) };
        var result = (TimeSpan)fn.DynamicInvoke(evt)!;
        Assert.Equal(TimeSpan.FromMinutes(60), result);
    }

    #endregion

    #region Timestamp/Duration Construction from Literals

    [Fact]
    public void Compile_TimestampConstructor()
    {
        var untypedLambda = CelDotNet.Compiler.ExpressionCompiler.CompileUntyped(
            CelExpression.Parse("timestamp('2024-06-15T12:00:00Z')").Ast,
            typeof(Event));
        var fn = untypedLambda.Compile();
        var result = (DateTimeOffset)fn.DynamicInvoke(new Event())!;
        Assert.Equal(new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero), result);
    }

    [Fact]
    public void Compile_DurationConstructor_Seconds()
    {
        var untypedLambda = CelDotNet.Compiler.ExpressionCompiler.CompileUntyped(
            CelExpression.Parse("duration('3600s')").Ast,
            typeof(Event));
        var fn = untypedLambda.Compile();
        var result = (TimeSpan)fn.DynamicInvoke(new Event())!;
        Assert.Equal(TimeSpan.FromSeconds(3600), result);
    }

    [Fact]
    public void Compile_DurationConstructor_Hours()
    {
        var untypedLambda = CelDotNet.Compiler.ExpressionCompiler.CompileUntyped(
            CelExpression.Parse("duration('2h')").Ast,
            typeof(Event));
        var fn = untypedLambda.Compile();
        var result = (TimeSpan)fn.DynamicInvoke(new Event())!;
        Assert.Equal(TimeSpan.FromHours(2), result);
    }

    [Fact]
    public void Compile_DurationConstructor_Milliseconds()
    {
        var untypedLambda = CelDotNet.Compiler.ExpressionCompiler.CompileUntyped(
            CelExpression.Parse("duration('500ms')").Ast,
            typeof(Event));
        var fn = untypedLambda.Compile();
        var result = (TimeSpan)fn.DynamicInvoke(new Event())!;
        Assert.Equal(TimeSpan.FromMilliseconds(500), result);
    }

    [Fact]
    public void Compile_DurationConstructor_Minutes()
    {
        var untypedLambda = CelDotNet.Compiler.ExpressionCompiler.CompileUntyped(
            CelExpression.Parse("duration('90m')").Ast,
            typeof(Event));
        var fn = untypedLambda.Compile();
        var result = (TimeSpan)fn.DynamicInvoke(new Event())!;
        Assert.Equal(TimeSpan.FromMinutes(90), result);
    }

    #endregion

    #region Timestamp Conversion to int (epoch seconds)

    [Fact]
    public void Compile_IntOfTimestamp()
    {
        var untypedLambda = CelDotNet.Compiler.ExpressionCompiler.CompileUntyped(
            CelExpression.Parse("int(StartTime)").Ast,
            typeof(Event));
        var fn = untypedLambda.Compile();
        var epoch = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var result = fn.DynamicInvoke(new Event { StartTime = epoch });
        Assert.Equal(0L, result);
    }

    [Fact]
    public void Compile_IntOfTimestamp_NonZero()
    {
        var untypedLambda = CelDotNet.Compiler.ExpressionCompiler.CompileUntyped(
            CelExpression.Parse("int(StartTime)").Ast,
            typeof(Event));
        var fn = untypedLambda.Compile();
        var ts = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var result = (long)fn.DynamicInvoke(new Event { StartTime = ts })!;
        Assert.Equal(ts.ToUnixTimeSeconds(), result);
    }

    #endregion

    #region Error Handling

    [Fact]
    public void Compile_ThrowsOnUnknownField()
    {
        var expr = CelExpression.Parse("nonexistent_field == 'foo'");
        Assert.Throws<CelException>(() => expr.Compile<Person>());
    }

    [Fact]
    public void Compile_ThrowsOnNonBoolResult()
    {
        var expr = CelExpression.Parse("Name");
        Assert.Throws<CelException>(() => expr.Compile<Person>());
    }

    [Fact]
    public void Parse_ThrowsOnSyntaxError()
    {
        Assert.Throws<CelParseException>(() => CelExpression.Parse("== invalid"));
    }

    #endregion
}
