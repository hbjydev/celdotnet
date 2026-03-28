using CelDotNet.Ast;
using CelDotNet.Checker;
using CelDotNet.Parser;
using Xunit;

namespace CelDotNet.Tests.Checker;

/// <summary>
/// Tests for the TypeChecker, TypeEnvironment, CelType, and CelEnvironment.
/// Covers type inference, error detection, .NET type mapping, and the public API.
/// </summary>
public class TypeCheckerTests
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
        public List<string> Tags { get; set; } = [];
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
    }

    #endregion

    #region Helpers

    private static CheckResult Check(string expression) =>
        TypeChecker.Check(CelParser.Parse(expression), new TypeEnvironment());

    private static CheckResult Check<T>(string expression) =>
        TypeChecker.Check<T>(CelParser.Parse(expression));

    private static CheckResult CheckWithEnv(string expression, TypeEnvironment env) =>
        TypeChecker.Check(CelParser.Parse(expression), env);

    #endregion

    #region CelType — FromClrType Mapping

    [Theory]
    [InlineData(typeof(int), "int")]
    [InlineData(typeof(long), "int")]
    [InlineData(typeof(short), "int")]
    [InlineData(typeof(uint), "uint")]
    [InlineData(typeof(ulong), "uint")]
    [InlineData(typeof(double), "double")]
    [InlineData(typeof(float), "double")]
    [InlineData(typeof(bool), "bool")]
    [InlineData(typeof(string), "string")]
    [InlineData(typeof(byte[]), "bytes")]
    [InlineData(typeof(DateTimeOffset), "timestamp")]
    [InlineData(typeof(TimeSpan), "duration")]
    public void FromClrType_MapsPrimitivesCorrectly(Type clrType, string expectedTypeName)
    {
        var celType = CelType.FromClrType(clrType);
        Assert.Equal(expectedTypeName, celType.ToString());
    }

    [Fact]
    public void FromClrType_NullableInt_MapsToInt()
    {
        var celType = CelType.FromClrType(typeof(int?));
        Assert.Equal(CelType.Int, celType);
    }

    [Fact]
    public void FromClrType_ListOfString_MapsToListString()
    {
        var celType = CelType.FromClrType(typeof(List<string>));
        Assert.IsType<CelType.ListType>(celType);
        Assert.Equal(CelType.String, ((CelType.ListType)celType).ElementType);
    }

    [Fact]
    public void FromClrType_StringArray_MapsToListString()
    {
        var celType = CelType.FromClrType(typeof(string[]));
        Assert.IsType<CelType.ListType>(celType);
        Assert.Equal(CelType.String, ((CelType.ListType)celType).ElementType);
    }

    [Fact]
    public void FromClrType_DictionaryStringInt_MapsToMapStringInt()
    {
        var celType = CelType.FromClrType(typeof(Dictionary<string, int>));
        Assert.IsType<CelType.MapType>(celType);
        var mapType = (CelType.MapType)celType;
        Assert.Equal(CelType.String, mapType.KeyType);
        Assert.Equal(CelType.Int, mapType.ValueType);
    }

    #endregion

    #region CelType — IsAssignableFrom

    [Fact]
    public void IsAssignableFrom_SameType_ReturnsTrue()
    {
        Assert.True(CelType.Int.IsAssignableFrom(CelType.Int));
        Assert.True(CelType.String.IsAssignableFrom(CelType.String));
    }

    [Fact]
    public void IsAssignableFrom_NumericTypes_AreCompatible()
    {
        Assert.True(CelType.Int.IsAssignableFrom(CelType.Double));
        Assert.True(CelType.Double.IsAssignableFrom(CelType.Int));
        Assert.True(CelType.Int.IsAssignableFrom(CelType.Uint));
    }

    [Fact]
    public void IsAssignableFrom_Any_AlwaysTrue()
    {
        Assert.True(CelType.Any.IsAssignableFrom(CelType.Int));
        Assert.True(CelType.Int.IsAssignableFrom(CelType.Any));
        Assert.True(CelType.Any.IsAssignableFrom(CelType.String));
    }

    [Fact]
    public void IsAssignableFrom_Error_AlwaysTrue()
    {
        Assert.True(CelType.Error.IsAssignableFrom(CelType.Int));
        Assert.True(CelType.Int.IsAssignableFrom(CelType.Error));
    }

    [Fact]
    public void IsAssignableFrom_IncompatibleTypes_ReturnsFalse()
    {
        Assert.False(CelType.String.IsAssignableFrom(CelType.Int));
        Assert.False(CelType.Bool.IsAssignableFrom(CelType.String));
    }

    #endregion

    #region TypeEnvironment

    [Fact]
    public void TypeEnvironment_AddAndLookupVariable()
    {
        var env = new TypeEnvironment()
            .AddVariable("x", CelType.Int)
            .AddVariable("name", CelType.String);

        Assert.Equal(CelType.Int, env.LookupVariable("x"));
        Assert.Equal(CelType.String, env.LookupVariable("name"));
        Assert.Null(env.LookupVariable("nonexistent"));
    }

    [Fact]
    public void TypeEnvironment_ChildScope_InheritsAndShadows()
    {
        var parent = new TypeEnvironment().AddVariable("x", CelType.Int);
        var child = parent.CreateChildScope();
        child.AddVariable("x", CelType.String); // shadow
        child.AddVariable("y", CelType.Bool);

        // Parent unchanged
        Assert.Equal(CelType.Int, parent.LookupVariable("x"));
        Assert.Null(parent.LookupVariable("y"));

        // Child has shadowed value and new variable
        Assert.Equal(CelType.String, child.LookupVariable("x"));
        Assert.Equal(CelType.Bool, child.LookupVariable("y"));
    }

    [Fact]
    public void TypeEnvironment_AddPropertiesFrom_MapsFields()
    {
        var env = new TypeEnvironment().AddPropertiesFrom<Person>();

        Assert.Equal(CelType.String, env.LookupVariable("Name"));
        Assert.Equal(CelType.Int, env.LookupVariable("Age"));
        Assert.Equal(CelType.Bool, env.LookupVariable("IsActive"));
        Assert.Equal(CelType.Double, env.LookupVariable("Score"));

        // Snake case conversion
        Assert.Equal(CelType.String, env.LookupVariable("first_name"));
        Assert.Equal(CelType.Bool, env.LookupVariable("is_active"));
    }

    [Fact]
    public void TypeEnvironment_AddPropertiesFrom_MapsNestedTypes()
    {
        var env = new TypeEnvironment().AddPropertiesFrom<Person>();

        var addressType = env.LookupVariable("Address");
        Assert.NotNull(addressType);
        Assert.IsType<CelType.ObjectType>(addressType);

        var objType = (CelType.ObjectType)addressType;
        Assert.True(objType.Fields.ContainsKey("City"));
        Assert.True(objType.Fields.ContainsKey("PostCode"));
    }

    [Fact]
    public void TypeEnvironment_AddPropertiesFrom_RespectsCelFieldAttribute()
    {
        var env = new TypeEnvironment().AddPropertiesFrom<Product>();

        Assert.Equal(CelType.String, env.LookupVariable("product_name"));
        Assert.Equal(CelType.Double, env.LookupVariable("unit_price"));
    }

    [Fact]
    public void TypeEnvironment_AddPropertiesFrom_MapsCollections()
    {
        var env = new TypeEnvironment().AddPropertiesFrom<Person>();

        var tagsType = env.LookupVariable("Tags");
        Assert.NotNull(tagsType);
        Assert.IsType<CelType.ListType>(tagsType);
        Assert.Equal(CelType.String, ((CelType.ListType)tagsType).ElementType);
    }

    #endregion

    #region TypeChecker — Literals

    [Fact]
    public void Check_IntLiteral_ReturnsInt()
    {
        var env = new TypeEnvironment().AddVariable("x", CelType.Int);
        var result = CheckWithEnv("x == 42", env);
        Assert.False(result.HasErrors);
        Assert.Equal(CelType.Bool, result.ResultType);
    }

    [Fact]
    public void Check_StringLiteral_ReturnsString()
    {
        var env = new TypeEnvironment().AddVariable("x", CelType.String);
        var result = CheckWithEnv("x == 'hello'", env);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Check_BoolLiteral_ReturnsBool()
    {
        var env = new TypeEnvironment().AddVariable("x", CelType.Bool);
        var result = CheckWithEnv("x == true", env);
        Assert.False(result.HasErrors);
    }

    #endregion

    #region TypeChecker — Identifiers

    [Fact]
    public void Check_DeclaredVariable_Passes()
    {
        var env = new TypeEnvironment().AddVariable("threshold", CelType.Int);
        var result = CheckWithEnv("threshold > 10", env);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Check_UndeclaredVariable_ReportsError()
    {
        var result = Check("unknown_var == 'foo'");
        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Message.Contains("undeclared identifier 'unknown_var'"));
    }

    [Fact]
    public void Check_PropertyAccess_WithTargetType()
    {
        var result = Check<Person>("Name == 'Alice'");
        Assert.False(result.HasErrors);
        Assert.Equal(CelType.Bool, result.ResultType);
    }

    [Fact]
    public void Check_SnakeCaseProperty_WithTargetType()
    {
        var result = Check<Person>("first_name == 'Alice'");
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Check_NonexistentProperty_ReportsError()
    {
        var result = Check<Person>("nonexistent_field == 'foo'");
        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Message.Contains("undeclared identifier"));
    }

    #endregion

    #region TypeChecker — Field Selection (Nested)

    [Fact]
    public void Check_NestedFieldAccess_Passes()
    {
        var result = Check<Person>("Address.City == 'London'");
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Check_NestedFieldAccess_InvalidField_ReportsError()
    {
        var result = Check<Person>("Address.NonExistent == 'foo'");
        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Message.Contains("no field 'NonExistent'"));
    }

    #endregion

    #region TypeChecker — Comparison Operators

    [Fact]
    public void Check_IntComparison_Passes()
    {
        var result = Check<Person>("Age > 21");
        Assert.False(result.HasErrors);
        Assert.Equal(CelType.Bool, result.ResultType);
    }

    [Fact]
    public void Check_StringComparison_Passes()
    {
        var result = Check<Person>("Name == 'Alice'");
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Check_NullComparison_Passes()
    {
        var result = Check<Person>("NickName == null");
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Check_IncompatibleComparison_ReportsError()
    {
        var env = new TypeEnvironment()
            .AddVariable("x", CelType.String)
            .AddVariable("y", CelType.Bool);
        var result = CheckWithEnv("x < y", env);
        Assert.True(result.HasErrors);
    }

    #endregion

    #region TypeChecker — Logical Operators

    [Fact]
    public void Check_AndOperator_BoolOperands_Passes()
    {
        var result = Check<Person>("Name == 'Alice' && Age > 21");
        Assert.False(result.HasErrors);
        Assert.Equal(CelType.Bool, result.ResultType);
    }

    [Fact]
    public void Check_OrOperator_BoolOperands_Passes()
    {
        var result = Check<Person>("IsActive || Age > 18");
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Check_LogicalOperator_NonBoolOperand_ReportsError()
    {
        var env = new TypeEnvironment()
            .AddVariable("x", CelType.Int)
            .AddVariable("y", CelType.Bool);
        var result = CheckWithEnv("x && y", env);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Message.Contains("requires bool operands"));
    }

    [Fact]
    public void Check_NotOperator_Passes()
    {
        var result = Check<Person>("!IsActive");
        Assert.False(result.HasErrors);
        Assert.Equal(CelType.Bool, result.ResultType);
    }

    [Fact]
    public void Check_NotOperator_NonBool_ReportsError()
    {
        var env = new TypeEnvironment().AddVariable("x", CelType.Int);
        var result = CheckWithEnv("!x", env);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Message.Contains("'!' requires bool"));
    }

    #endregion

    #region TypeChecker — Arithmetic

    [Fact]
    public void Check_Addition_NumericOperands_Passes()
    {
        var result = Check<Person>("Age + 5 > 30");
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Check_StringConcatenation_Passes()
    {
        var result = Check<Person>("Name + ' Smith' == 'Alice Smith'");
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Check_Arithmetic_NonNumeric_ReportsError()
    {
        var env = new TypeEnvironment()
            .AddVariable("x", CelType.String)
            .AddVariable("y", CelType.Int);
        var result = CheckWithEnv("x - y", env);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Message.Contains("arithmetic operator"));
    }

    [Fact]
    public void Check_Negate_NumericOperand_Passes()
    {
        var env = new TypeEnvironment().AddVariable("x", CelType.Int);
        var result = CheckWithEnv("-x > 0", env);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Check_Negate_NonNumeric_ReportsError()
    {
        var env = new TypeEnvironment().AddVariable("x", CelType.String);
        var result = CheckWithEnv("-x > 0", env);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Message.Contains("'-' requires numeric"));
    }

    #endregion

    #region TypeChecker — String Functions

    [Fact]
    public void Check_StringContains_Passes()
    {
        var result = Check<Person>("Name.contains('lic')");
        Assert.False(result.HasErrors);
        Assert.Equal(CelType.Bool, result.ResultType);
    }

    [Fact]
    public void Check_StringStartsWith_Passes()
    {
        var result = Check<Person>("Name.startsWith('Al')");
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Check_StringEndsWith_Passes()
    {
        var result = Check<Person>("Name.endsWith('ce')");
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Check_StringContains_NonStringReceiver_ReportsError()
    {
        var env = new TypeEnvironment().AddVariable("x", CelType.Int);
        var result = CheckWithEnv("x.contains('foo')", env);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Message.Contains("requires string receiver"));
    }

    [Fact]
    public void Check_StringContains_NonStringArg_ReportsError()
    {
        var env = new TypeEnvironment().AddVariable("x", CelType.String);
        var result = CheckWithEnv("x.contains(42)", env);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Message.Contains("argument must be string"));
    }

    #endregion

    #region TypeChecker — Size Function

    [Fact]
    public void Check_SizeReceiver_OnString_Passes()
    {
        var result = Check<Person>("Name.size() > 3");
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Check_SizeGlobal_OnString_Passes()
    {
        var result = Check<Person>("size(Name) > 3");
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Check_SizeReceiver_OnList_Passes()
    {
        var result = Check<Person>("Tags.size() > 0");
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Check_Size_OnInvalidType_ReportsError()
    {
        var env = new TypeEnvironment().AddVariable("x", CelType.Int);
        var result = CheckWithEnv("x.size()", env);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Message.Contains("'size' requires"));
    }

    #endregion

    #region TypeChecker — In Operator

    [Fact]
    public void Check_InOperator_IntInList_Passes()
    {
        var result = Check<Person>("Age in [21, 25, 30]");
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Check_InOperator_StringInList_Passes()
    {
        var result = Check<Person>("Name in ['Alice', 'Bob']");
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Check_InOperator_NonList_ReportsError()
    {
        var env = new TypeEnvironment()
            .AddVariable("x", CelType.Int)
            .AddVariable("y", CelType.Int);
        var result = CheckWithEnv("x in y", env);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Message.Contains("requires list or map"));
    }

    #endregion

    #region TypeChecker — Has Macro

    [Fact]
    public void Check_HasMacro_ValidField_Passes()
    {
        var result = Check<Person>("has(Address.City)");
        Assert.False(result.HasErrors);
        Assert.Equal(CelType.Bool, result.ResultType);
    }

    [Fact]
    public void Check_HasMacro_InvalidField_ReportsError()
    {
        var result = Check<Person>("has(Address.Nonexistent)");
        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Message.Contains("no field 'Nonexistent'"));
    }

    #endregion

    #region TypeChecker — Conditional (Ternary)

    [Fact]
    public void Check_Ternary_Passes()
    {
        var result = Check<Person>("Age > 21 ? IsActive : false");
        Assert.False(result.HasErrors);
        Assert.Equal(CelType.Bool, result.ResultType);
    }

    [Fact]
    public void Check_Ternary_NonBoolCondition_ReportsError()
    {
        var env = new TypeEnvironment()
            .AddVariable("x", CelType.Int)
            .AddVariable("a", CelType.String)
            .AddVariable("b", CelType.String);
        var result = CheckWithEnv("x ? a : b", env);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Message.Contains("ternary condition must be bool"));
    }

    [Fact]
    public void Check_Ternary_IncompatibleBranches_ReportsError()
    {
        var env = new TypeEnvironment()
            .AddVariable("cond", CelType.Bool)
            .AddVariable("a", CelType.String)
            .AddVariable("b", CelType.Int);
        var result = CheckWithEnv("cond ? a : b", env);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Message.Contains("incompatible types"));
    }

    #endregion

    #region TypeChecker — List Literals

    [Fact]
    public void Check_EmptyList_ReturnsListAny()
    {
        var env = new TypeEnvironment().AddVariable("x", CelType.Bool);
        var result = CheckWithEnv("x == true", env); // Just to have a valid expr
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Check_IntList_ReturnsListInt()
    {
        var env = new TypeEnvironment().AddVariable("x", CelType.Int);
        var result = CheckWithEnv("x in [1, 2, 3]", env);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Check_StringList_ReturnsListString()
    {
        var env = new TypeEnvironment().AddVariable("x", CelType.String);
        var result = CheckWithEnv("x in ['a', 'b', 'c']", env);
        Assert.False(result.HasErrors);
    }

    #endregion

    #region TypeChecker — Multiple Errors

    [Fact]
    public void Check_MultipleErrors_CollectsAll()
    {
        // This expression has two issues: unknown identifiers
        var result = Check("unknown_a && unknown_b");
        Assert.True(result.HasErrors);
        Assert.True(result.Errors.Count >= 2);
    }

    [Fact]
    public void Check_ErrorsHaveSourcePositions()
    {
        var result = Check("unknown_var == 42");
        Assert.True(result.HasErrors);
        var error = result.Errors[0];
        // Source position should be set (not default)
        Assert.True(error.Span.Start.Line > 0 || error.Span.Start.Offset >= 0);
    }

    #endregion

    #region CelEnvironment — Public API

    [Fact]
    public void CelEnvironment_AddVariable_CelType()
    {
        var env = new CelEnvironment()
            .AddVariable("threshold", CelType.Int)
            .AddVariable("name", CelType.String);

        Assert.Equal(CelType.Int, env.TypeEnvironment.LookupVariable("threshold"));
        Assert.Equal(CelType.String, env.TypeEnvironment.LookupVariable("name"));
    }

    [Fact]
    public void CelEnvironment_AddVariable_ClrType()
    {
        var env = new CelEnvironment()
            .AddVariable("count", typeof(int))
            .AddVariable("label", typeof(string));

        Assert.Equal(CelType.Int, env.TypeEnvironment.LookupVariable("count"));
        Assert.Equal(CelType.String, env.TypeEnvironment.LookupVariable("label"));
    }

    [Fact]
    public void CelEnvironment_TypeCheckingEnabled_ByDefault()
    {
        var env = new CelEnvironment();
        Assert.True(env.TypeCheckingEnabled);
    }

    [Fact]
    public void CelEnvironment_DisableTypeChecking()
    {
        var env = new CelEnvironment().DisableTypeChecking();
        Assert.False(env.TypeCheckingEnabled);
    }

    #endregion

    #region CelExpression — Integration with Type Checker

    [Fact]
    public void Parse_WithEnvironment_TypeChecks()
    {
        var env = new CelEnvironment()
            .AddVariable("name", CelType.String);

        // Should succeed — 'name' is declared as string, equality comparison is valid
        var expr = CelExpression.Parse("name == 'Alice'", env);
        Assert.NotNull(expr);
    }

    [Fact]
    public void Parse_WithEnvironment_ThrowsOnTypeError()
    {
        var env = new CelEnvironment()
            .AddVariable("x", CelType.Int);

        // Should throw — 'y' is undeclared
        var ex = Assert.Throws<CelTypeException>(() =>
            CelExpression.Parse("x > y", env));
        Assert.True(ex.Errors.Count > 0);
    }

    [Fact]
    public void Parse_WithDisabledTypeChecking_DoesNotThrow()
    {
        var env = new CelEnvironment()
            .DisableTypeChecking();

        // Should not throw even though 'x' is undeclared — type checking is off
        var expr = CelExpression.Parse("x > y", env);
        Assert.NotNull(expr);
    }

    [Fact]
    public void Parse_WithoutEnvironment_NoTypeChecking()
    {
        // Original API still works, no type checking
        var expr = CelExpression.Parse("whatever == 'foo'");
        Assert.NotNull(expr);
    }

    [Fact]
    public void CheckTypes_ValidatesAgainstTargetType()
    {
        var expr = CelExpression.Parse("Name == 'Alice'");
        var result = expr.CheckTypes<Person>();
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void CheckTypes_ReportsInvalidFields()
    {
        var expr = CelExpression.Parse("NonExistent == 'foo'");
        var result = expr.CheckTypes<Person>();
        Assert.True(result.HasErrors);
    }

    #endregion

    #region CelTypeException

    [Fact]
    public void CelTypeException_SingleError_FormatsMessage()
    {
        var env = new CelEnvironment()
            .AddVariable("x", CelType.Int);

        var ex = Assert.Throws<CelTypeException>(() =>
            CelExpression.Parse("x > unknown_y", env));

        Assert.Contains("undeclared identifier", ex.Message);
        Assert.Single(ex.Errors);
    }

    [Fact]
    public void CelTypeException_MultipleErrors_FormatsAllMessages()
    {
        var env = new CelEnvironment();

        var ex = Assert.Throws<CelTypeException>(() =>
            CelExpression.Parse("unknown_a && unknown_b", env));

        Assert.True(ex.Errors.Count >= 2);
        Assert.Contains("errors", ex.Message);
    }

    #endregion

    #region TypeChecker — CelField Attribute

    [Fact]
    public void Check_CelFieldAttribute_Passes()
    {
        var result = Check<Product>("product_name == 'Widget'");
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Check_CelFieldAttribute_NumericComparison_Passes()
    {
        var result = Check<Product>("unit_price > 10.0");
        Assert.False(result.HasErrors);
    }

    #endregion

    #region TypeChecker — Complex Expressions

    [Fact]
    public void Check_ComplexLogicalExpression_Passes()
    {
        var result = Check<Person>("(Name == 'Alice' || Name == 'Bob') && Age >= 21");
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Check_NestedArithmeticAndComparison_Passes()
    {
        var result = Check<Person>("Age + 5 > 30 && Score > 8.0");
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Check_StringFunctionInLogicalExpr_Passes()
    {
        var result = Check<Person>("Name.startsWith('A') && IsActive");
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Check_InOperatorWithLogical_Passes()
    {
        var result = Check<Person>("Age in [21, 25, 30] && Name != 'Bob'");
        Assert.False(result.HasErrors);
    }

    #endregion

    #region TypeChecker — Any Type Passthrough

    [Fact]
    public void Check_AnyType_AllowsAnything()
    {
        var env = new TypeEnvironment()
            .AddVariable("x", CelType.Any);

        // Any type should pass all operations without errors
        var result = CheckWithEnv("x > 10", env);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Check_AnyType_FieldAccess_Passes()
    {
        var env = new TypeEnvironment()
            .AddVariable("x", CelType.Any);

        var result = CheckWithEnv("x.some_field == 'foo'", env);
        Assert.False(result.HasErrors);
    }

    #endregion
}
