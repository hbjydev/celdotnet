using CelDotNet.Ast;
using CelDotNet.Checker;

namespace CelDotNet;

/// <summary>
/// Configuration for a CEL evaluation environment. Declares external variables,
/// their types, and controls optional type checking.
/// </summary>
/// <example>
/// <code>
/// var env = new CelEnvironment()
///     .AddVariable("threshold", CelType.Int)
///     .AddVariable("name", CelType.String);
///
/// var expr = CelExpression.Parse("age > threshold", env);
/// </code>
/// </example>
public sealed class CelEnvironment
{
    private readonly TypeEnvironment _typeEnv = new();
    private bool _typeCheckingEnabled = true;

    /// <summary>
    /// The underlying type environment used during type checking.
    /// </summary>
    internal TypeEnvironment TypeEnvironment => _typeEnv;

    /// <summary>
    /// Whether type checking is enabled. Defaults to true.
    /// When disabled, <see cref="CelExpression.Parse(string, CelEnvironment)"/>
    /// skips the type checking pass entirely.
    /// </summary>
    public bool TypeCheckingEnabled => _typeCheckingEnabled;

    /// <summary>
    /// Declares an external variable with the given name and CEL type.
    /// Variables declared here can be referenced in CEL expressions.
    /// </summary>
    /// <param name="name">The variable name as used in CEL expressions.</param>
    /// <param name="type">The CEL type of the variable.</param>
    /// <returns>This environment for fluent chaining.</returns>
    public CelEnvironment AddVariable(string name, CelType type)
    {
        _typeEnv.AddVariable(name, type);
        return this;
    }

    /// <summary>
    /// Declares an external variable with the given name, mapping the .NET type
    /// to the corresponding CEL type automatically.
    /// </summary>
    /// <param name="name">The variable name as used in CEL expressions.</param>
    /// <param name="clrType">The .NET type of the variable.</param>
    /// <returns>This environment for fluent chaining.</returns>
    public CelEnvironment AddVariable(string name, Type clrType)
    {
        _typeEnv.AddVariable(name, CelType.FromClrType(clrType));
        return this;
    }

    /// <summary>
    /// Disables type checking for this environment. When disabled, the type
    /// checker is not run during parsing and only runtime errors will surface
    /// during compilation/evaluation.
    /// </summary>
    /// <returns>This environment for fluent chaining.</returns>
    public CelEnvironment DisableTypeChecking()
    {
        _typeCheckingEnabled = false;
        return this;
    }

    /// <summary>
    /// Enables type checking for this environment (the default).
    /// </summary>
    /// <returns>This environment for fluent chaining.</returns>
    public CelEnvironment EnableTypeChecking()
    {
        _typeCheckingEnabled = true;
        return this;
    }
}
