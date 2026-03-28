namespace CelDotNet.Ast;

/// <summary>
/// The kinds of types in CEL's type system.
/// </summary>
public enum CelTypeKind
{
    Int,
    Uint,
    Double,
    Bool,
    String,
    Bytes,
    List,
    Map,
    Null,
    Timestamp,
    Duration,
    Type,
    Any,
    Error,
}

/// <summary>
/// Represents a type in CEL's type system. Supports primitive types, parameterised
/// collection types (list, map), and structured object types for .NET integration.
/// </summary>
public abstract record CelType
{
    // Prevent external subclassing
    private CelType() { }

    /// <summary>A primitive CEL type (int, bool, string, etc.).</summary>
    public sealed record PrimitiveType(CelTypeKind Kind) : CelType
    {
        public override string ToString() => Kind.ToString().ToLowerInvariant();
    }

    /// <summary>A parameterised list type: list(ElementType).</summary>
    public sealed record ListType(CelType ElementType) : CelType
    {
        public override string ToString() => $"list({ElementType})";
    }

    /// <summary>A parameterised map type: map(KeyType, ValueType).</summary>
    public sealed record MapType(CelType KeyType, CelType ValueType) : CelType
    {
        public override string ToString() => $"map({KeyType}, {ValueType})";
    }

    /// <summary>
    /// A structured type representing a .NET object with known fields.
    /// Used by the type checker to validate field access on target types.
    /// </summary>
    public sealed record ObjectType(System.Type ClrType, IReadOnlyDictionary<string, CelType> Fields) : CelType
    {
        public override string ToString() => ClrType.Name;
    }

    #region Singleton Instances

    /// <summary>CEL int (64-bit signed integer).</summary>
    public static readonly CelType Int = new PrimitiveType(CelTypeKind.Int);

    /// <summary>CEL uint (64-bit unsigned integer).</summary>
    public static readonly CelType Uint = new PrimitiveType(CelTypeKind.Uint);

    /// <summary>CEL double (64-bit IEEE 754 float).</summary>
    public static readonly CelType Double = new PrimitiveType(CelTypeKind.Double);

    /// <summary>CEL bool.</summary>
    public static readonly CelType Bool = new PrimitiveType(CelTypeKind.Bool);

    /// <summary>CEL string (UTF-8).</summary>
    public static readonly CelType String = new PrimitiveType(CelTypeKind.String);

    /// <summary>CEL bytes.</summary>
    public static readonly CelType Bytes = new PrimitiveType(CelTypeKind.Bytes);

    /// <summary>CEL null_type.</summary>
    public static readonly CelType Null = new PrimitiveType(CelTypeKind.Null);

    /// <summary>CEL timestamp (maps to DateTimeOffset).</summary>
    public static readonly CelType Timestamp = new PrimitiveType(CelTypeKind.Timestamp);

    /// <summary>CEL duration (maps to TimeSpan).</summary>
    public static readonly CelType Duration = new PrimitiveType(CelTypeKind.Duration);

    /// <summary>The dynamic/unknown type — anything goes.</summary>
    public static readonly CelType Any = new PrimitiveType(CelTypeKind.Any);

    /// <summary>Sentinel type representing a type error.</summary>
    public static readonly CelType Error = new PrimitiveType(CelTypeKind.Error);

    #endregion

    #region Factory Methods

    /// <summary>Creates a list type with the given element type.</summary>
    public static CelType List(CelType elementType) => new ListType(elementType);

    /// <summary>Creates a map type with the given key and value types.</summary>
    public static CelType Map(CelType keyType, CelType valueType) => new MapType(keyType, valueType);

    /// <summary>Creates an object type from a .NET type with the given field mappings.</summary>
    public static CelType Object(System.Type clrType, IReadOnlyDictionary<string, CelType> fields) =>
        new ObjectType(clrType, fields);

    #endregion

    #region Type Queries

    /// <summary>Returns true if this type is numeric (int, uint, or double).</summary>
    public bool IsNumeric => this is PrimitiveType p &&
        p.Kind is CelTypeKind.Int or CelTypeKind.Uint or CelTypeKind.Double;

    /// <summary>Returns true if this is the error sentinel type.</summary>
    public bool IsError => this is PrimitiveType { Kind: CelTypeKind.Error };

    /// <summary>Returns true if this is the dynamic Any type.</summary>
    public bool IsAny => this is PrimitiveType { Kind: CelTypeKind.Any };

    /// <summary>
    /// Returns true if <paramref name="other"/> can be assigned to this type.
    /// Any is compatible with everything. Null is compatible with reference-like types.
    /// Error is compatible with everything (to avoid cascading errors).
    /// </summary>
    public bool IsAssignableFrom(CelType other)
    {
        if (this.IsAny || other.IsAny || this.IsError || other.IsError)
            return true;

        if (other is PrimitiveType { Kind: CelTypeKind.Null })
            return this is not PrimitiveType p || p.Kind is CelTypeKind.Null or CelTypeKind.Any;

        if (this == other)
            return true;

        // Numeric promotion: int is assignable from uint and vice versa in comparisons
        if (this.IsNumeric && other.IsNumeric)
            return true;

        // List covariance
        if (this is ListType thisList && other is ListType otherList)
            return thisList.ElementType.IsAssignableFrom(otherList.ElementType);

        // Map covariance
        if (this is MapType thisMap && other is MapType otherMap)
            return thisMap.KeyType.IsAssignableFrom(otherMap.KeyType) &&
                   thisMap.ValueType.IsAssignableFrom(otherMap.ValueType);

        return false;
    }

    #endregion

    #region .NET Type Mapping

    /// <summary>
    /// Maps a .NET <see cref="System.Type"/> to the corresponding <see cref="CelType"/>.
    /// </summary>
    public static CelType FromClrType(System.Type clrType)
    {
        // Unwrap nullable
        var underlying = System.Nullable.GetUnderlyingType(clrType);
        if (underlying is not null)
            clrType = underlying;

        // Primitives
        if (clrType == typeof(int) || clrType == typeof(long) || clrType == typeof(short) || clrType == typeof(sbyte))
            return Int;
        if (clrType == typeof(uint) || clrType == typeof(ulong) || clrType == typeof(ushort) || clrType == typeof(byte))
            return Uint;
        if (clrType == typeof(double) || clrType == typeof(float) || clrType == typeof(decimal))
            return Double;
        if (clrType == typeof(bool))
            return Bool;
        if (clrType == typeof(string))
            return String;
        if (clrType == typeof(byte[]))
            return Bytes;
        if (clrType == typeof(DateTimeOffset))
            return Timestamp;
        if (clrType == typeof(TimeSpan))
            return Duration;

        // Arrays
        if (clrType.IsArray)
            return List(FromClrType(clrType.GetElementType()!));

        // Generic collections
        if (clrType.IsGenericType)
        {
            var genDef = clrType.GetGenericTypeDefinition();

            // IDictionary<K,V> and Dictionary<K,V>
            if (genDef == typeof(IDictionary<,>) || genDef == typeof(Dictionary<,>))
            {
                var args = clrType.GetGenericArguments();
                return Map(FromClrType(args[0]), FromClrType(args[1]));
            }

            // Check interfaces for IDictionary<K,V>
            foreach (var iface in clrType.GetInterfaces())
            {
                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IDictionary<,>))
                {
                    var args = iface.GetGenericArguments();
                    return Map(FromClrType(args[0]), FromClrType(args[1]));
                }
            }

            // IEnumerable<T>, List<T>, IList<T>, etc.
            if (genDef == typeof(IEnumerable<>) || genDef == typeof(List<>) ||
                genDef == typeof(IList<>) || genDef == typeof(IReadOnlyList<>) ||
                genDef == typeof(ICollection<>) || genDef == typeof(IReadOnlyCollection<>))
            {
                return List(FromClrType(clrType.GetGenericArguments()[0]));
            }

            // Check interfaces for IEnumerable<T>
            foreach (var iface in clrType.GetInterfaces())
            {
                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    return List(FromClrType(iface.GetGenericArguments()[0]));
            }
        }

        // Fall back to Any for unknown types
        return Any;
    }

    #endregion
}
