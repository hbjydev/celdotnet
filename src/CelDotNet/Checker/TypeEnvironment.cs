using System.Reflection;
using CelDotNet.Ast;

namespace CelDotNet.Checker;

/// <summary>
/// Holds type declarations for variables and structured types used during type checking.
/// Can be populated manually or built from a .NET type's properties.
/// </summary>
public sealed class TypeEnvironment
{
    private readonly Dictionary<string, CelType> _variables = new(StringComparer.Ordinal);
    private readonly TypeEnvironment? _parent;

    /// <summary>Creates an empty type environment.</summary>
    public TypeEnvironment() { }

    private TypeEnvironment(TypeEnvironment parent)
    {
        _parent = parent;
        // Copy parent variables into this scope
        foreach (var kv in parent._variables)
            _variables[kv.Key] = kv.Value;
    }

    /// <summary>
    /// Declares a variable with the given name and type.
    /// Returns this environment for fluent chaining.
    /// </summary>
    public TypeEnvironment AddVariable(string name, CelType type)
    {
        _variables[name] = type;
        return this;
    }

    /// <summary>
    /// Looks up a variable by name. Returns null if not declared.
    /// </summary>
    public CelType? LookupVariable(string name)
    {
        if (_variables.TryGetValue(name, out var type))
            return type;
        return _parent?.LookupVariable(name);
    }

    /// <summary>
    /// Creates a child scope that inherits all variables from this environment
    /// but can shadow them with new declarations.
    /// </summary>
    public TypeEnvironment CreateChildScope() => new(this);

    /// <summary>
    /// Populates the environment with declarations from the public properties of
    /// the given .NET type. Each property becomes a variable with the corresponding CEL type.
    /// Returns this environment for fluent chaining.
    /// </summary>
    public TypeEnvironment AddPropertiesFrom<T>() => AddPropertiesFrom(typeof(T));

    /// <summary>
    /// Populates the environment with declarations from the public properties of
    /// the given .NET type. Each property becomes a variable with the corresponding CEL type.
    /// Returns this environment for fluent chaining.
    /// </summary>
    public TypeEnvironment AddPropertiesFrom(Type clrType)
    {
        var objectType = BuildObjectType(clrType);

        // Register each field from the object type as a top-level variable
        if (objectType is CelType.ObjectType obj)
        {
            foreach (var (name, type) in obj.Fields)
            {
                _variables.TryAdd(name, type);
            }
        }

        return this;
    }

    /// <summary>
    /// Builds a <see cref="CelType.ObjectType"/> from a .NET type, including field name resolution
    /// via [CelField] attributes, exact match, and PascalCase → snake_case conversion.
    /// </summary>
    internal static CelType BuildObjectType(Type clrType)
    {
        var fields = new Dictionary<string, CelType>(StringComparer.Ordinal);
        var properties = clrType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            var celType = MapPropertyType(prop.PropertyType);

            // 1. [CelField("name")] attribute
            var attr = prop.GetCustomAttribute<CelFieldAttribute>();
            if (attr is not null)
            {
                // Skip non-visible fields
                if (!attr.Visible)
                    continue;

                if (attr.Name is not null)
                    fields.TryAdd(attr.Name, celType);
            }

            // 2. Exact match (property name as-is)
            fields.TryAdd(prop.Name, celType);

            // 3. Auto snake_case: PascalCase → snake_case
            string snakeName = Compiler.CompilerContext.PascalToSnakeCase(prop.Name);
            fields.TryAdd(snakeName, celType);
        }

        return CelType.Object(clrType, fields);
    }

    /// <summary>
    /// Maps a .NET property type to a CelType. For complex types (classes with properties),
    /// this produces an ObjectType rather than Any.
    /// </summary>
    private static CelType MapPropertyType(Type clrType)
    {
        // Unwrap nullable
        var underlying = Nullable.GetUnderlyingType(clrType);
        if (underlying is not null)
            clrType = underlying;

        // Check for primitives and well-known types first
        var celType = CelType.FromClrType(clrType);

        // If FromClrType returned Any and it's a class with properties, build an ObjectType
        if (celType.IsAny && clrType.IsClass && clrType != typeof(string) && clrType != typeof(object))
        {
            return BuildObjectType(clrType);
        }

        return celType;
    }

    /// <summary>
    /// Resolves what type a field selection returns on the given type.
    /// E.g., if parentType is an ObjectType with field "name" → string, returns string.
    /// </summary>
    internal static CelType? ResolveFieldType(CelType parentType, string fieldName)
    {
        if (parentType is CelType.ObjectType obj)
        {
            if (obj.Fields.TryGetValue(fieldName, out var fieldType))
                return fieldType;

            return null;
        }

        // Map index access
        if (parentType is CelType.MapType mapType)
        {
            return mapType.ValueType;
        }

        // Any type — field access returns Any
        if (parentType.IsAny)
            return CelType.Any;

        return null;
    }
}
