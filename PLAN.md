# CelDotNet — Project Plan

A CEL (Common Expression Language) implementation for .NET that produces `System.Linq.Expression` trees, enabling CEL filters to be used with EF Core and `IQueryable`.

**Unique value proposition**: No existing .NET CEL library produces `Expression<Func<T, bool>>` — they all evaluate directly. CelDotNet is the first to do CEL → Expression tree → SQL translation.

## Architecture

```
CEL string → Lexer → Token[] → Parser → Expr (AST) → [TypeChecker] → ExpressionCompiler → Expression<Func<T, bool>>
                                                                                          ↘ .Compile() → Func<T, bool>
```

## Project Structure

```
CelDotNet.sln
├── src/
│   ├── CelDotNet/                              # Core: zero dependencies
│   │   ├── Lexer/
│   │   │   ├── TokenKind.cs                    # enum of all CEL tokens
│   │   │   ├── Token.cs                        # record Token(TokenKind, string Lexeme, Position)
│   │   │   └── CelLexer.cs                     # hand-written scanner
│   │   ├── Ast/
│   │   │   ├── CelExpr.cs                      # sealed abstract record + derived types
│   │   │   ├── CelType.cs                      # CEL type system
│   │   │   └── Operator.cs                     # binary/unary op enums
│   │   ├── Parser/
│   │   │   └── CelParser.cs                    # recursive descent, full CEL grammar
│   │   ├── Checker/
│   │   │   ├── TypeChecker.cs                  # optional static type checking
│   │   │   └── TypeEnvironment.cs              # type bindings for variables
│   │   ├── Compiler/
│   │   │   ├── ExpressionCompiler.cs           # AST → System.Linq.Expressions
│   │   │   ├── CompilerContext.cs              # parameter tracking, variable scope
│   │   │   └── CelFunctions.cs                 # runtime helpers (size, matches, etc.)
│   │   ├── CelExpression.cs                    # main public API
│   │   ├── CelEnvironment.cs                   # env config (variables, functions, types)
│   │   ├── CelFieldAttribute.cs                # [CelField("snake_name")] for property mapping
│   │   └── CelException.cs
│   └── CelDotNet.EntityFrameworkCore/          # EF Core integration
│       ├── CelQueryableExtensions.cs           # .WhereCel("filter")
│       └── CelExpressionTranslator.cs          # EF-specific expression adjustments
├── tests/
│   ├── CelDotNet.Tests/                        # unit tests (xUnit v3)
│   ├── CelDotNet.EntityFrameworkCore.Tests/    # EF integration tests
│   └── CelDotNet.Conformance/                  # official cel-spec conformance
├── .github/workflows/ci.yml
├── .gitignore
├── .editorconfig
└── Directory.Build.props
```

## Public API

```csharp
// core usage
var expr = CelExpression.Parse("name == 'foo' && age > 21");
Expression<Func<Person, bool>> predicate = expr.ToExpression<Person>();
Func<Person, bool> compiled = expr.Compile<Person>();

// with environment (external variables, custom functions)
var env = new CelEnvironment()
    .AddVariable("threshold", typeof(int));
var expr = CelExpression.Parse("age > threshold", env);

// EF Core
var results = await db.People
    .WhereCel("name == 'foo' && age > 21")
    .ToListAsync();
```

## Key Expression Tree Mappings

| CEL                       | Expression Tree              | EF Core SQL                       |
| ------------------------- | ---------------------------- | --------------------------------- |
| `x.name == "foo"`         | `Expression.Equal(prop, c)`  | `WHERE Name = 'foo'`              |
| `x && y`                  | `Expression.AndAlso()`       | `AND`                             |
| `x \|\| y`               | `Expression.OrElse()`        | `OR`                              |
| `val in [1,2,3]`          | `Enumerable.Contains()`      | `WHERE val IN (1,2,3)`            |
| `name.contains("x")`      | `string.Contains()`          | `LIKE '%x%'`                      |
| `name.startsWith("x")`    | `string.StartsWith()`        | `LIKE 'x%'`                       |
| `items.exists(x, p)`      | `Enumerable.Any(lambda)`     | `EXISTS (subquery)`               |
| `items.all(x, p)`         | `Enumerable.All(lambda)`     | `NOT EXISTS (NOT subquery)`       |
| `items.filter(x, p)`      | `Enumerable.Where(lambda)`   | subquery                          |
| `has(x.field)`             | `x.Field != null`            | `IS NOT NULL`                     |
| `condition ? a : b`        | `Expression.Condition()`     | `CASE WHEN ... THEN ... ELSE`     |

## Field Name Resolution (priority order)

1. `[CelField("snake_name")]` attribute
2. Exact match
3. Automatic `snake_case` → `PascalCase` conversion

## CEL Type Mapping

| CEL Type            | C# Type            |
| ------------------- | ------------------ |
| `int`               | `long` / `int`     |
| `uint`              | `ulong`            |
| `double`            | `double`           |
| `bool`              | `bool`             |
| `string`            | `string`           |
| `bytes`             | `byte[]`           |
| `list`              | `IEnumerable<T>`   |
| `map`               | `IDictionary<K,V>` |
| `null_type`         | `null`             |
| `Timestamp`         | `DateTimeOffset`   |
| `Duration`          | `TimeSpan`         |

## Implementation Phases

### Phase 1: Foundation ✅
- [x] Solution scaffolding, `Directory.Build.props`, CI pipeline
- [x] Lexer (full CEL token set)
- [x] AST record types
- [x] Recursive descent parser (full grammar incl. macros)
- [x] Unit tests for lexer + parser (131 passing)

### Phase 2: Expression Compilation (Core) ✅
- [x] `ExpressionCompiler` for comparisons, logical ops, arithmetic, field access
- [x] `CelExpression` public API (`.Parse()`, `.ToExpression<T>()`, `.Compile<T>()`)
- [x] String functions (`contains`, `startsWith`, `endsWith`, `size`)
- [x] `in` operator → `Contains()`
- [x] Field name resolution with `[CelField]`
- [x] `has()` macro (nested field selection → `!= null`)
- [x] Ternary `?:` operator
- [x] Numeric type harmonisation across int/long/double
- [x] Unit tests for compiler (42 new tests, 173 total passing)

### Phase 3: Advanced Features ✅
- [x] Macros: `has` (done in Phase 2)
- [x] Macros: `all`, `exists`, `exists_one`, `filter`, `map` (Comprehension AST → LINQ methods)
- [x] Timestamp/Duration support (`DateTimeOffset`/`TimeSpan` mapping, construction, member access, arithmetic)
- [x] Type conversion functions (`int()`, `uint()`, `double()`, `string()`, `bool()`)
- [x] `matches()` regex support (receiver-style and global)
- [x] Ternary `?:` operator (done in Phase 2)
- [x] Unit tests for all Phase 3 features (309 total passing)

### Phase 4: Type Checker ✅
- [x] Optional static type checking pass
- [x] `TypeEnvironment` for declaring variable types
- [x] `CelEnvironment` public API for environment configuration
- [x] Rich `CelType` hierarchy (primitives, lists, maps, object types)
- [x] .NET type → CEL type mapping (`CelType.FromClrType`)
- [x] Error reporting with source positions (collects all errors, not fail-fast)
- [x] `CelTypeException` with multiple error support
- [x] Integration: `CelExpression.Parse(expr, env)` and `CelExpression.CheckTypes<T>()`
- [x] Unit tests for type checker (93 new tests, 266 total passing)

### Phase 5: EF Core Integration ✅
- [x] `CelDotNet.EntityFrameworkCore` package
- [x] `IQueryable<T>.WhereCel()` extension methods (with and without `CelEnvironment`)
- [x] `CelExpressionTranslator` — `ExpressionVisitor` rewriting `CelFunctions.*` calls to EF-translatable property access
- [x] Clear exceptions for non-translatable features (`CelTranslationException`)
- [x] Integration tests with SQLite in-memory (43 new tests, 352 total passing)

### Phase 6: Conformance & Polish
- [ ] Conformance test runner against official `cel-spec` suite
- [x] Performance benchmarks
- [ ] NuGet packaging + publish config

## Design Notes

- **EF Core translatable vs in-memory**: full spec for `.Compile()`, but the EF Core package throws `CelTranslationException` for features that can't be expressed as SQL (e.g. complex map operations, bytes)
- **CEL's commutative short-circuit**: `false && error` = `false` in CEL (not in C#). For expression trees this is fine since SQL handles it, but `.Compile()` delegates need a wrapper
- **No protobuf dependency**: timestamps use `DateTimeOffset`, not `google.protobuf.Timestamp`
- **Zero dependencies** in the core package (hand-written parser)

## Existing .NET CEL Landscape

| Library          | Maturity           | Expression Trees? | Protobuf Required? |
| ---------------- | ------------------ | ----------------- | ------------------ |
| **Cel** (TELUS)  | most mature        | no                | yes                |
| **Cel.NET**      | mature             | no                | yes                |
| **Cel.Compiled** | early/experimental | no (delegates)    | no                 |
| **CelDotNet**    | in development     | **yes** ✅        | **no** ✅          |
