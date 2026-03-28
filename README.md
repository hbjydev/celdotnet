# CelDotNet

[![CI](https://github.com/hbjydev/celdotnet/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/hbjydev/celdotnet/actions/workflows/ci.yml)
[![NuGet Version](https://img.shields.io/nuget/v/CelDotNet?logo=nuget&label=CelDotNet)](https://www.nuget.org/packages/CelDotNet)
[![NuGet Version](https://img.shields.io/nuget/v/CelDotNet.EntityFrameworkCore?logo=nuget&label=EntityFrameworkCore)](https://www.nuget.org/packages/CelDotNet.EntityFrameworkCore)

A [Common Expression Language (CEL)](https://cel.dev) implementation for .NET that compiles CEL expressions into `System.Linq.Expression` trees -- making it possible to use CEL filters directly with Entity Framework Core, `IQueryable`, or in-memory evaluation.

Unlike other .NET CEL libraries, CelDotNet doesn't just evaluate expressions -- it produces `Expression<Func<T, bool>>`, which means your CEL filters can be translated all the way down to SQL. Zero protobuf dependency. Zero runtime dependencies in the core package.

## Features

* **CEL -> Expression Trees** -- the first .NET CEL library to produce `Expression<Func<T, bool>>`
* **EF Core integration** -- CEL filters translate to SQL via the `CelDotNet.EntityFrameworkCore` package
* **Optional type checking** -- catch errors at parse time with `CelEnvironment`
* **Zero dependencies** -- the core package is entirely self-contained (hand-written lexer and parser)
* **Full CEL grammar** -- arithmetic, logic, string functions, comprehension macros, ternary, `in`, `has()`, timestamps, durations, and more
* **Field name resolution** -- automatic `snake_case` to `PascalCase` conversion, or explicit mapping via `[CelField]`

## Installation

Install the core package from NuGet:

```shell
dotnet add package CelDotNet
```

For EF Core integration:

```shell
dotnet add package CelDotNet.EntityFrameworkCore
```

## Quick Start

### Basic Usage

Parse a CEL expression, then compile it against a .NET type:

```csharp
using CelDotNet;

// Parse a CEL expression
var expr = CelExpression.Parse("name == 'Alice' && age > 21");

// Compile to an expression tree (for EF Core / IQueryable)
Expression<Func<Person, bool>> predicate = expr.ToExpression<Person>();

// Or compile to a delegate (for in-memory evaluation)
Func<Person, bool> compiled = expr.Compile<Person>();

var result = people.Where(compiled).ToList();
```

### With Type Checking

Declare external variables and enable static type checking before compilation:

```csharp
using CelDotNet;
using CelDotNet.Ast;

var env = new CelEnvironment()
    .AddVariable("threshold", CelType.Int)
    .AddVariable("name", CelType.String);

// Type errors are caught here, not at runtime
var expr = CelExpression.Parse("age > threshold", env);
```

You can also check types against a specific .NET type:

```csharp
var expr = CelExpression.Parse("name == 'foo'");
var result = expr.CheckTypes<Person>();

if (result.HasErrors)
{
    foreach (var error in result.Errors)
        Console.WriteLine(error);
}
```

Type checking can be disabled if you'd prefer to rely on runtime errors:

```csharp
var env = new CelEnvironment()
    .DisableTypeChecking()
    .AddVariable("threshold", CelType.Int);
```

### EF Core Integration

Apply CEL filters directly to your `IQueryable` sources -- the expression gets translated to SQL:

```csharp
using CelDotNet.EntityFrameworkCore;

var results = await db.People
    .WhereCel("name == 'Alice' && age > 21")
    .ToListAsync();
```

With an environment for type checking and external variables:

```csharp
var env = new CelEnvironment()
    .AddVariable("min_age", typeof(int));

var results = await db.People
    .WhereCel("age > min_age", env)
    .ToListAsync();
```

**Note:** Some CEL features (e.g. complex map operations, byte arrays) can't be translated to SQL. The EF Core package will throw a `CelTranslationException` for non-translatable expressions. The full CEL spec is supported for in-memory evaluation via `.Compile<T>()`.

### Field Name Mapping

CelDotNet resolves CEL field names to .NET properties in the following priority order:

1. `[CelField("name")]` attribute on the property
2. Exact property name match
3. Automatic `snake_case` to `PascalCase` conversion

```csharp
using CelDotNet;

public class Person
{
    [CelField("first_name")]
    public string FirstName { get; set; }

    public int Age { get; set; }
}

// Both of these work:
// "first_name == 'Alice'"  -> resolves via [CelField]
// "age > 21"               -> resolves via snake_case -> PascalCase
```

### Field Visibility Mapping

CelDotNet provides the ability to mark some fields on a class or record as
'visible' or not, which makes the field get skipped during expression
compilation.

```csharp
using CelDotNet;

public class Person
{
    // Included in .WhereCel()
    [CelField(visible: true)]
    public string Username { get; set; }

    // Excluded from .WhereCel()
    [CelField(visible: false)]
    public string PasswordHash { get; set; }
}
```

## Supported CEL Features

### Types

| CEL Type | .NET Type |
|----------|-----------|
| `int` | `long` / `int` |
| `uint` | `ulong` |
| `double` | `double` |
| `bool` | `bool` |
| `string` | `string` |
| `bytes` | `byte[]` |
| `list` | `IEnumerable<T>` |
| `map` | `IDictionary<K,V>` |
| `null_type` | `null` |
| `timestamp` | `DateTimeOffset` |
| `duration` | `TimeSpan` |

### Operators

Arithmetic (`+`, `-`, `*`, `/`, `%`), comparison (`==`, `!=`, `<`, `<=`, `>`, `>=`), logical (`&&`, `||`, `!`), ternary (`? :`), and membership (`in`).

### String Functions

`contains()`, `startsWith()`, `endsWith()`, `size()`, `matches()` (regex).

### Macros

`has()`, `all()`, `exists()`, `exists_one()`, `filter()`, `map()`.

### Type Conversions

`int()`, `uint()`, `double()`, `string()`, `bool()`.

### Expression Tree Mappings

| CEL | Expression Tree | EF Core SQL |
|-----|-----------------|-------------|
| `x.name == "foo"` | `Expression.Equal(prop, c)` | `WHERE Name = 'foo'` |
| `x && y` | `Expression.AndAlso()` | `AND` |
| `x \|\| y` | `Expression.OrElse()` | `OR` |
| `val in [1,2,3]` | `Enumerable.Contains()` | `WHERE val IN (1,2,3)` |
| `name.contains("x")` | `string.Contains()` | `LIKE '%x%'` |
| `name.startsWith("x")` | `string.StartsWith()` | `LIKE 'x%'` |
| `items.exists(x, p)` | `Enumerable.Any(lambda)` | `EXISTS (subquery)` |
| `items.all(x, p)` | `Enumerable.All(lambda)` | `NOT EXISTS (NOT subquery)` |
| `has(x.field)` | `x.Field != null` | `IS NOT NULL` |
| `condition ? a : b` | `Expression.Condition()` | `CASE WHEN ... THEN ... ELSE` |

## Architecture

```
CEL string
  -> Lexer -> Token[]
  -> Parser -> CelExpr (AST)
  -> [TypeChecker] (optional)
  -> ExpressionCompiler -> Expression<Func<T, bool>>
                        -> .Compile() -> Func<T, bool>
```

The pipeline is designed so that each stage is independent and testable. The type checker is optional and can be bypassed entirely if you prefer runtime-only error handling.

## Comparison with Other .NET CEL Libraries

| Library | Expression Trees? | Protobuf Required? | Maturity |
|---------|-------------------|-------------------|----------|
| [Cel (TELUS)](https://github.com/telus/cel-net) | No | Yes | Most mature |
| Cel.NET | No | Yes | Mature |
| Cel.Compiled | No (delegates) | No | Early |
| **CelDotNet** | **Yes** | **No** | In development |

## Licence

Apache-2.0. See [LICENSE](LICENSE) for details.
