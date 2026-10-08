namespace ComposeSharp.Loader;

/// <summary>
/// Describes invalid Compose input with its source and YAML location.
/// The message is preformatted as
/// <c>Invalid Compose input in '{sourceFile}' at '{propertyPath}'[, service '{name}'][, line {line}[, column {column}]]: {message}</c>
/// so diagnostics stay stable and greppable while the structured properties remain machine-readable.
/// </summary>
public sealed class ComposeValidationException : InvalidOperationException
{
    /// <summary>
    /// Creates a validation failure for a property path inside a Compose document.
    /// </summary>
    /// <param name="sourceFile">Path of the Compose file that contributed the offending value.</param>
    /// <param name="propertyPath">Dotted Compose path such as <c>services.api.ports[0]</c>, or <c>$</c> for whole-document problems.</param>
    /// <param name="message">Human-readable reason, without location prefixes.</param>
    /// <param name="serviceName">Service key when the failure is inside a service definition.</param>
    /// <param name="line">YAML parser line of the failure, when known.</param>
    /// <param name="column">YAML parser column of the failure, when known.</param>
    /// <param name="innerException">Lower-level failure, typically a YAML or conversion error.</param>
    public ComposeValidationException(
        string sourceFile,
        string propertyPath,
        string message,
        string? serviceName = null,
        long? line = null,
        long? column = null,
        Exception? innerException = null)
        : base(FormatMessage(sourceFile, propertyPath, message, serviceName, line, column), innerException)
    {
        SourceFile = sourceFile;
        PropertyPath = propertyPath;
        ServiceName = serviceName;
        Line = line;
        Column = column;
    }

    /// <summary>
    /// Compose file the offending value came from. After a multi-file merge this is the
    /// overlay file when the overlay introduced or replaced the value, not necessarily the first file.
    /// </summary>
    public string SourceFile { get; }

    /// <summary>
    /// Dotted path of the failing property (<c>services.api.image</c>, list items as <c>[index]</c>),
    /// or <c>$</c> when the failure applies to the document as a whole.
    /// </summary>
    public string PropertyPath { get; }

    /// <summary>
    /// Service key the failure belongs to, or <c>null</c> for project-level problems.
    /// </summary>
    public string? ServiceName { get; }

    /// <summary>
    /// YAML line of the failure as reported by the parser, or <c>null</c> when the loader
    /// raised the failure without parser coordinates (for example semantic validation).
    /// </summary>
    public long? Line { get; }

    /// <summary>
    /// YAML column of the failure as reported by the parser; <c>null</c> under the same
    /// conditions as <see cref="Line"/>.
    /// </summary>
    public long? Column { get; }

    private static string FormatMessage(
        string sourceFile,
        string propertyPath,
        string message,
        string? serviceName,
        long? line,
        long? column)
    {
        // Optional segments are omitted entirely so single-file and pre-parse failures stay compact.
        var service = serviceName is null ? string.Empty : $", service '{serviceName}'";
        var location = line is null
            ? string.Empty
            : $", line {line}{(column is null ? string.Empty : $", column {column}")}";
        return $"Invalid Compose input in '{sourceFile}' at '{propertyPath}'{service}{location}: {message}";
    }
}
