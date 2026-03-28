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
public sealed class CelFieldAttribute : Attribute
{
    /// <summary>
    /// The CEL field name that maps to this property.
    /// </summary>
    public string Name { get; }

    public CelFieldAttribute(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }
}
