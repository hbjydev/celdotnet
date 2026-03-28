using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace CelDotNet.Compiler;

/// <summary>
/// Tracks compilation state: the target parameter, variable scoping,
/// and field name resolution for a given target type.
/// </summary>
internal sealed class CompilerContext
{
    /// <summary>
    /// The ParameterExpression representing the root object (e.g. the 'x' in x => ...).
    /// </summary>
    public ParameterExpression Parameter { get; }

    /// <summary>
    /// The target .NET type being compiled against.
    /// </summary>
    public Type TargetType { get; }

    private readonly Dictionary<string, Expression> _variables = new();
    private readonly Dictionary<string, PropertyInfo> _fieldCache;

    public CompilerContext(Type targetType)
    {
        TargetType = targetType;
        Parameter = Expression.Parameter(targetType, "x");
        _fieldCache = BuildFieldCache(targetType);
    }

    /// <summary>
    /// Creates a child scope (for comprehension iteration variables etc.)
    /// that inherits existing variables but can shadow them.
    /// </summary>
    public CompilerContext CreateChildScope()
    {
        var child = new CompilerContext(TargetType, Parameter, new Dictionary<string, PropertyInfo>(_fieldCache));
        foreach (var kv in _variables)
            child._variables[kv.Key] = kv.Value;
        return child;
    }

    private CompilerContext(Type targetType, ParameterExpression parameter, Dictionary<string, PropertyInfo> fieldCache)
    {
        TargetType = targetType;
        Parameter = parameter;
        _fieldCache = fieldCache;
    }

    /// <summary>
    /// Registers a variable binding (e.g. loop iteration variable).
    /// </summary>
    public void SetVariable(string name, Expression expression)
    {
        _variables[name] = expression;
    }

    /// <summary>
    /// Tries to resolve a name — first as a variable, then as a property on the target type.
    /// </summary>
    public Expression? ResolveIdentifier(string name)
    {
        if (_variables.TryGetValue(name, out var varExpr))
            return varExpr;

        if (_fieldCache.TryGetValue(name, out var prop))
            return Expression.Property(Parameter, prop);

        return null;
    }

    /// <summary>
    /// Resolves a field/property on a given expression's type.
    /// Uses [CelField] attribute, exact match, then snake_case → PascalCase.
    /// </summary>
    public static PropertyInfo? ResolveProperty(Type type, string celFieldName)
    {
        var cache = BuildFieldCache(type);
        cache.TryGetValue(celFieldName, out var prop);
        return prop;
    }

    /// <summary>
    /// Builds a lookup of CEL field names → PropertyInfo for a type.
    /// Priority: [CelField] attribute > exact match > snake_case → PascalCase.
    /// </summary>
    private static Dictionary<string, PropertyInfo> BuildFieldCache(Type type)
    {
        var cache = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            // 1. [CelField("name")] attribute
            var attr = prop.GetCustomAttribute<CelFieldAttribute>();
            if (attr is not null)
            {
                cache.TryAdd(attr.Name, prop);
            }

            // 2. Exact match (property name as-is)
            cache.TryAdd(prop.Name, prop);

            // 3. Auto snake_case conversion: PascalCase → snake_case
            string snakeName = PascalToSnakeCase(prop.Name);
            cache.TryAdd(snakeName, prop);
        }

        return cache;
    }

    /// <summary>
    /// Converts PascalCase to snake_case.
    /// e.g. "FirstName" → "first_name", "HTTPSEnabled" → "https_enabled"
    /// </summary>
    internal static string PascalToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        var sb = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                {
                    // Insert underscore before uppercase if previous char is lowercase
                    // or if next char is lowercase (handles acronyms like "HTTP" → "http")
                    bool prevIsLower = char.IsLower(name[i - 1]);
                    bool nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                    if (prevIsLower || nextIsLower)
                        sb.Append('_');
                }
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
