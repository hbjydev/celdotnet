using CelDotNet.Ast;
using CelDotNet.Parser;
using Xunit;

namespace CelDotNet.Tests.Parser;

public class CelParserTests
{
    private static CelExpr Parse(string source) => CelParser.Parse(source);

    #region Literals

    [Fact]
    public void Parses_IntegerLiteral()
    {
        var expr = Parse("42");
        var lit = Assert.IsType<CelExpr.Literal>(expr);
        Assert.Equal(42L, lit.Value);
        Assert.Equal(CelTypeKind.Int, lit.TypeKind);
    }

    [Fact]
    public void Parses_UintLiteral()
    {
        var expr = Parse("42u");
        var lit = Assert.IsType<CelExpr.Literal>(expr);
        Assert.Equal(42UL, lit.Value);
        Assert.Equal(CelTypeKind.Uint, lit.TypeKind);
    }

    [Fact]
    public void Parses_DoubleLiteral()
    {
        var expr = Parse("3.14");
        var lit = Assert.IsType<CelExpr.Literal>(expr);
        Assert.Equal(3.14, lit.Value);
        Assert.Equal(CelTypeKind.Double, lit.TypeKind);
    }

    [Fact]
    public void Parses_StringLiteral()
    {
        var expr = Parse("'hello'");
        var lit = Assert.IsType<CelExpr.Literal>(expr);
        Assert.Equal("hello", lit.Value);
        Assert.Equal(CelTypeKind.String, lit.TypeKind);
    }

    [Fact]
    public void Parses_BoolLiterals()
    {
        var trueExpr = Parse("true");
        var falseExpr = Parse("false");
        Assert.Equal(true, Assert.IsType<CelExpr.Literal>(trueExpr).Value);
        Assert.Equal(false, Assert.IsType<CelExpr.Literal>(falseExpr).Value);
    }

    [Fact]
    public void Parses_NullLiteral()
    {
        var expr = Parse("null");
        var lit = Assert.IsType<CelExpr.Literal>(expr);
        Assert.Null(lit.Value);
        Assert.Equal(CelTypeKind.Null, lit.TypeKind);
    }

    #endregion

    #region Identifiers and Field Access

    [Fact]
    public void Parses_Identifier()
    {
        var expr = Parse("foo");
        var ident = Assert.IsType<CelExpr.Ident>(expr);
        Assert.Equal("foo", ident.Name);
    }

    [Fact]
    public void Parses_FieldAccess()
    {
        var expr = Parse("person.name");
        var select = Assert.IsType<CelExpr.Select>(expr);
        Assert.Equal("name", select.Field);
        var ident = Assert.IsType<CelExpr.Ident>(select.Operand);
        Assert.Equal("person", ident.Name);
    }

    [Fact]
    public void Parses_NestedFieldAccess()
    {
        var expr = Parse("person.address.city");
        var outer = Assert.IsType<CelExpr.Select>(expr);
        Assert.Equal("city", outer.Field);
        var inner = Assert.IsType<CelExpr.Select>(outer.Operand);
        Assert.Equal("address", inner.Field);
        var ident = Assert.IsType<CelExpr.Ident>(inner.Operand);
        Assert.Equal("person", ident.Name);
    }

    #endregion

    #region Binary Expressions

    [Theory]
    [InlineData("a + b", BinaryOp.Add)]
    [InlineData("a - b", BinaryOp.Subtract)]
    [InlineData("a * b", BinaryOp.Multiply)]
    [InlineData("a / b", BinaryOp.Divide)]
    [InlineData("a % b", BinaryOp.Modulo)]
    [InlineData("a == b", BinaryOp.Equal)]
    [InlineData("a != b", BinaryOp.NotEqual)]
    [InlineData("a < b", BinaryOp.LessThan)]
    [InlineData("a <= b", BinaryOp.LessThanOrEqual)]
    [InlineData("a > b", BinaryOp.GreaterThan)]
    [InlineData("a >= b", BinaryOp.GreaterThanOrEqual)]
    [InlineData("a && b", BinaryOp.And)]
    [InlineData("a || b", BinaryOp.Or)]
    [InlineData("a in b", BinaryOp.In)]
    public void Parses_BinaryOperators(string source, BinaryOp expectedOp)
    {
        var expr = Parse(source);
        var binary = Assert.IsType<CelExpr.Binary>(expr);
        Assert.Equal(expectedOp, binary.Op);
        Assert.IsType<CelExpr.Ident>(binary.Left);
        Assert.IsType<CelExpr.Ident>(binary.Right);
    }

    [Fact]
    public void Respects_Precedence_MulOverAdd()
    {
        // a + b * c should parse as a + (b * c)
        var expr = Parse("a + b * c");
        var add = Assert.IsType<CelExpr.Binary>(expr);
        Assert.Equal(BinaryOp.Add, add.Op);
        Assert.IsType<CelExpr.Ident>(add.Left); // a
        var mul = Assert.IsType<CelExpr.Binary>(add.Right);
        Assert.Equal(BinaryOp.Multiply, mul.Op);
    }

    [Fact]
    public void Respects_Precedence_ComparisonOverLogical()
    {
        // a > 1 && b < 2 should parse as (a > 1) && (b < 2)
        var expr = Parse("a > 1 && b < 2");
        var and = Assert.IsType<CelExpr.Binary>(expr);
        Assert.Equal(BinaryOp.And, and.Op);
        Assert.Equal(BinaryOp.GreaterThan, Assert.IsType<CelExpr.Binary>(and.Left).Op);
        Assert.Equal(BinaryOp.LessThan, Assert.IsType<CelExpr.Binary>(and.Right).Op);
    }

    [Fact]
    public void Respects_Precedence_AndOverOr()
    {
        // a || b && c should parse as a || (b && c)
        var expr = Parse("a || b && c");
        var or = Assert.IsType<CelExpr.Binary>(expr);
        Assert.Equal(BinaryOp.Or, or.Op);
        Assert.IsType<CelExpr.Ident>(or.Left); // a
        Assert.Equal(BinaryOp.And, Assert.IsType<CelExpr.Binary>(or.Right).Op);
    }

    #endregion

    #region Unary Expressions

    [Fact]
    public void Parses_UnaryNegation()
    {
        var expr = Parse("-42");
        var unary = Assert.IsType<CelExpr.Unary>(expr);
        Assert.Equal(UnaryOp.Negate, unary.Op);
        Assert.IsType<CelExpr.Literal>(unary.Operand);
    }

    [Fact]
    public void Parses_UnaryNot()
    {
        var expr = Parse("!flag");
        var unary = Assert.IsType<CelExpr.Unary>(expr);
        Assert.Equal(UnaryOp.Not, unary.Op);
        Assert.IsType<CelExpr.Ident>(unary.Operand);
    }

    [Fact]
    public void Parses_DoubleNegation()
    {
        var expr = Parse("!!flag");
        var outer = Assert.IsType<CelExpr.Unary>(expr);
        Assert.Equal(UnaryOp.Not, outer.Op);
        var inner = Assert.IsType<CelExpr.Unary>(outer.Operand);
        Assert.Equal(UnaryOp.Not, inner.Op);
        Assert.IsType<CelExpr.Ident>(inner.Operand);
    }

    #endregion

    #region Ternary Expression

    [Fact]
    public void Parses_TernaryConditional()
    {
        var expr = Parse("x > 0 ? x : -x");
        var cond = Assert.IsType<CelExpr.Conditional>(expr);
        Assert.IsType<CelExpr.Binary>(cond.Condition);
        Assert.IsType<CelExpr.Ident>(cond.TrueExpr);
        Assert.IsType<CelExpr.Unary>(cond.FalseExpr);
    }

    #endregion

    #region Function Calls

    [Fact]
    public void Parses_GlobalFunctionCall()
    {
        var expr = Parse("size(name)");
        var call = Assert.IsType<CelExpr.Call>(expr);
        Assert.Null(call.Target);
        Assert.Equal("size", call.Function);
        Assert.Single(call.Args);
        Assert.IsType<CelExpr.Ident>(call.Args[0]);
    }

    [Fact]
    public void Parses_MethodCall()
    {
        var expr = Parse("name.startsWith('test')");
        var call = Assert.IsType<CelExpr.Call>(expr);
        Assert.NotNull(call.Target);
        Assert.IsType<CelExpr.Ident>(call.Target);
        Assert.Equal("startsWith", call.Function);
        Assert.Single(call.Args);
    }

    [Fact]
    public void Parses_FunctionCallWithMultipleArgs()
    {
        var expr = Parse("func(a, b, c)");
        var call = Assert.IsType<CelExpr.Call>(expr);
        Assert.Equal("func", call.Function);
        Assert.Equal(3, call.Args.Count);
    }

    [Fact]
    public void Parses_FunctionCallNoArgs()
    {
        var expr = Parse("timestamp()");
        var call = Assert.IsType<CelExpr.Call>(expr);
        Assert.Equal("timestamp", call.Function);
        Assert.Empty(call.Args);
    }

    #endregion

    #region Collection Literals

    [Fact]
    public void Parses_EmptyList()
    {
        var expr = Parse("[]");
        var list = Assert.IsType<CelExpr.CreateList>(expr);
        Assert.Empty(list.Elements);
    }

    [Fact]
    public void Parses_ListWithElements()
    {
        var expr = Parse("[1, 2, 3]");
        var list = Assert.IsType<CelExpr.CreateList>(expr);
        Assert.Equal(3, list.Elements.Count);
    }

    [Fact]
    public void Parses_ListWithTrailingComma()
    {
        var expr = Parse("[1, 2, 3,]");
        var list = Assert.IsType<CelExpr.CreateList>(expr);
        Assert.Equal(3, list.Elements.Count);
    }

    [Fact]
    public void Parses_EmptyMap()
    {
        var expr = Parse("{}");
        var map = Assert.IsType<CelExpr.CreateMap>(expr);
        Assert.Empty(map.Entries);
    }

    [Fact]
    public void Parses_MapWithEntries()
    {
        var expr = Parse("{'a': 1, 'b': 2}");
        var map = Assert.IsType<CelExpr.CreateMap>(expr);
        Assert.Equal(2, map.Entries.Count);
    }

    [Fact]
    public void Parses_IndexAccess()
    {
        var expr = Parse("list[0]");
        var index = Assert.IsType<CelExpr.Index>(expr);
        Assert.IsType<CelExpr.Ident>(index.Operand);
        Assert.IsType<CelExpr.Literal>(index.Key);
    }

    #endregion

    #region In Operator

    [Fact]
    public void Parses_InOperator()
    {
        var expr = Parse("x in [1, 2, 3]");
        var binary = Assert.IsType<CelExpr.Binary>(expr);
        Assert.Equal(BinaryOp.In, binary.Op);
        Assert.IsType<CelExpr.Ident>(binary.Left);
        Assert.IsType<CelExpr.CreateList>(binary.Right);
    }

    #endregion

    #region Macros

    [Fact]
    public void Parses_HasMacro()
    {
        var expr = Parse("has(msg.field)");
        var call = Assert.IsType<CelExpr.Call>(expr);
        Assert.Equal("has", call.Function);
        Assert.Single(call.Args);
        Assert.IsType<CelExpr.Select>(call.Args[0]);
    }

    [Fact]
    public void Parses_AllMacro()
    {
        var expr = Parse("items.all(x, x > 0)");
        var comp = Assert.IsType<CelExpr.Comprehension>(expr);
        Assert.Equal("x", comp.IterVar);
        Assert.IsType<CelExpr.Ident>(comp.IterRange);
    }

    [Fact]
    public void Parses_ExistsMacro()
    {
        var expr = Parse("items.exists(x, x == 'foo')");
        var comp = Assert.IsType<CelExpr.Comprehension>(expr);
        Assert.Equal("x", comp.IterVar);
    }

    [Fact]
    public void Parses_ExistsOneMacro()
    {
        var expr = Parse("items.exists_one(x, x == 1)");
        var comp = Assert.IsType<CelExpr.Comprehension>(expr);
        Assert.Equal("x", comp.IterVar);
    }

    [Fact]
    public void Parses_FilterMacro()
    {
        var expr = Parse("items.filter(x, x > 10)");
        var comp = Assert.IsType<CelExpr.Comprehension>(expr);
        Assert.Equal("x", comp.IterVar);
    }

    [Fact]
    public void Parses_MapMacro_TwoArgs()
    {
        var expr = Parse("items.map(x, x * 2)");
        var comp = Assert.IsType<CelExpr.Comprehension>(expr);
        Assert.Equal("x", comp.IterVar);
    }

    [Fact]
    public void Parses_MapMacro_ThreeArgs()
    {
        var expr = Parse("items.map(x, x > 0, x * 2)");
        var comp = Assert.IsType<CelExpr.Comprehension>(expr);
        Assert.Equal("x", comp.IterVar);
    }

    #endregion

    #region Grouped Expressions

    [Fact]
    public void Parses_GroupedExpression()
    {
        // (a + b) * c should parse as (a + b) * c
        var expr = Parse("(a + b) * c");
        var mul = Assert.IsType<CelExpr.Binary>(expr);
        Assert.Equal(BinaryOp.Multiply, mul.Op);
        var add = Assert.IsType<CelExpr.Binary>(mul.Left);
        Assert.Equal(BinaryOp.Add, add.Op);
    }

    #endregion

    #region Complex Expressions

    [Fact]
    public void Parses_EfCoreStyleFilter()
    {
        var expr = Parse("name == 'foo' && age > 21 && active == true");
        var outerAnd = Assert.IsType<CelExpr.Binary>(expr);
        Assert.Equal(BinaryOp.And, outerAnd.Op);
    }

    [Fact]
    public void Parses_ContainsFilter()
    {
        var expr = Parse("name.contains('test') || description.startsWith('hello')");
        var or = Assert.IsType<CelExpr.Binary>(expr);
        Assert.Equal(BinaryOp.Or, or.Op);
        Assert.IsType<CelExpr.Call>(or.Left);
        Assert.IsType<CelExpr.Call>(or.Right);
    }

    [Fact]
    public void Parses_InWithList()
    {
        var expr = Parse("status in ['active', 'pending', 'review']");
        var inExpr = Assert.IsType<CelExpr.Binary>(expr);
        Assert.Equal(BinaryOp.In, inExpr.Op);
        var list = Assert.IsType<CelExpr.CreateList>(inExpr.Right);
        Assert.Equal(3, list.Elements.Count);
    }

    #endregion

    #region Error Handling

    [Fact]
    public void Throws_OnUnexpectedToken()
    {
        Assert.Throws<CelParseException>(() => Parse(")"));
    }

    [Fact]
    public void Throws_OnUnterminatedString()
    {
        Assert.Throws<CelParseException>(() => Parse("\"unterminated"));
    }

    [Fact]
    public void Throws_OnTrailingTokens()
    {
        Assert.Throws<CelParseException>(() => Parse("1 2"));
    }

    [Fact]
    public void Throws_OnHasWithNonSelect()
    {
        Assert.Throws<CelParseException>(() => Parse("has(42)"));
    }

    #endregion
}
