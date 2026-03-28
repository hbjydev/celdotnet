using CelDotNet.Checker;
using CelDotNet.Lexer;

namespace CelDotNet;

/// <summary>
/// Base exception for all CelDotNet errors.
/// </summary>
public class CelException : Exception
{
    public CelException(string message) : base(message) { }
    public CelException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Thrown when the CEL source cannot be parsed.
/// </summary>
public class CelParseException : CelException
{
    public SourcePosition Position { get; }

    public CelParseException(string message, SourcePosition position)
        : base($"{message} at {position}")
    {
        Position = position;
    }
}

/// <summary>
/// Thrown when type checking fails. Contains all type errors found during checking.
/// </summary>
public class CelTypeException : CelException
{
    /// <summary>
    /// All type errors found during checking. May be empty if the exception was
    /// created with just a message string.
    /// </summary>
    public IReadOnlyList<CelTypeError> Errors { get; }

    public CelTypeException(string message) : base(message)
    {
        Errors = [];
    }

    /// <summary>
    /// Creates a type exception from a list of type errors.
    /// The message is built from all error messages with source positions.
    /// </summary>
    public CelTypeException(IReadOnlyList<CelTypeError> errors)
        : base(FormatErrors(errors))
    {
        Errors = errors;
    }

    private static string FormatErrors(IReadOnlyList<CelTypeError> errors)
    {
        if (errors.Count == 0)
            return "type checking failed";
        if (errors.Count == 1)
            return $"type error: {errors[0]}";
        return $"type checking found {errors.Count} errors:\n" +
               string.Join("\n", errors.Select(e => $"  - {e}"));
    }
}

/// <summary>
/// Thrown when an expression cannot be translated (e.g. to SQL via EF Core).
/// </summary>
public class CelTranslationException : CelException
{
    public CelTranslationException(string message) : base(message) { }
}
