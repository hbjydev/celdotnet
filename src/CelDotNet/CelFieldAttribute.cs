namespace CelDotNet;

/// <summary>
/// Maps a CEL field name to a .NET property.
/// When the CEL expression references a field by its CEL name,
/// the compiler resolves it to the annotated property.
/// </summary>
/// <example>
/// <code>
/// public class Person
/// {
///     [CelField("first_name")]
///     public string FirstName { get; set; }
/// }
/// // CEL: "first_name == 'Alice'" → accesses Person.FirstName
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class CelFieldAttribute(
    string? name = null,
    bool visible = true
) : Attribute
{
    /// <summary>
    /// The CEL field name that maps to this property.
    /// If null, the property name will be auto-detected using exact match or snake_case conversion.
    /// </summary>
    public string? Name { get; } = name;

    /// <summary>
    /// Indicates whether this field should be included in the set of fields that can be queried by CEL expressions.
    /// </summary>
    public bool Visible { get; } = visible;
}
