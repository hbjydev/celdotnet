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
    string name,
    bool visible = true
) : Attribute
{
    /// <summary>
    /// The CEL field name that maps to this property.
    /// </summary>
    public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));

    /// <summary>
    /// Indicates whether this field should be included in the set of fields that can be queried by CEL expressions.
    /// </summary>
    public bool Visible { get; } = visible;
}
